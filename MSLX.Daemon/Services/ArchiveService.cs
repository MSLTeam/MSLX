using System.IO.Compression;
using System.Text;
using CliWrap;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace MSLX.Daemon.Services;

public class ArchiveService
{
    private readonly ILogger<ArchiveService> _logger;

    public ArchiveService(ILogger<ArchiveService> logger)
    {
        _logger = logger;
    }

    #region 解压 (Decompress)

    /// <summary>
    /// 解压归档文件，支持 .zip, .jar, .tar, .tar.gz, .tgz, .tar.xz, .txz, .tar.bz2, .tbz2, .7z, .rar 等
    /// 可选传入 password 解密加密压缩包。
    /// </summary>
    public Task DecompressAsync(
        string archiveFullPath,
        string extractRootPath,
        string? encodingName,
        CancellationToken ct,
        Action<int, string>? onProgress = null,
        Func<string, bool>? isPathSafe = null)
        => DecompressAsync(archiveFullPath, extractRootPath, encodingName, null, ct, onProgress, isPathSafe);

    public async Task DecompressAsync(
        string archiveFullPath,
        string extractRootPath,
        string? encodingName,
        string? password = null,
        CancellationToken ct = default,
        Action<int, string>? onProgress = null,
        Func<string, bool>? isPathSafe = null)
    {
        if (!File.Exists(archiveFullPath))
            throw new FileNotFoundException("压缩文件不存在", archiveFullPath);

        if (!Directory.Exists(extractRootPath))
            Directory.CreateDirectory(extractRootPath);

        var encoding = ResolveEncoding(encodingName, archiveFullPath);
        var lowerName = Path.GetFileName(archiveFullPath).ToLowerInvariant();

        // 解压根路径
        var normalizedExtractRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractRootPath))
                                    + Path.DirectorySeparatorChar;

        string passPrompt = string.IsNullOrEmpty(password) ? "无密码" : "带密码";
        _logger.LogInformation("开始解压文件: {Archive} 到 {Destination}, 编码: {Encoding}, {PassPrompt}",
            archiveFullPath, extractRootPath, encoding.EncodingName, passPrompt);

        onProgress?.Invoke(0, "正在分析归档结构...");

        var readerOptions = new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = encoding },
            Password = string.IsNullOrWhiteSpace(password) ? null : password
        };

        try
        {
            // 针对 .7z, .zip, .jar, .rar 等基于索引头/随机访问的格式，优先使用 ArchiveFactory
            if (lowerName.EndsWith(".7z") || lowerName.EndsWith(".zip") || lowerName.EndsWith(".jar") || lowerName.EndsWith(".rar"))
            {
                try
                {
                    await DecompressWithArchiveFactoryAsync(archiveFullPath, normalizedExtractRoot, readerOptions, ct, onProgress, isPathSafe);
                    return;
                }
                catch (Exception ex) when (!IsPasswordRelatedException(ex, password))
                {
                    // 注意：BCL ZipArchive 绝对不支持解压密码。若已提供密码，绝不应回退到 BCL，避免产生误导性的 unsupported compression method 报错
                    if (string.IsNullOrWhiteSpace(readerOptions.Password) && (lowerName.EndsWith(".zip") || lowerName.EndsWith(".jar")))
                    {
                        _logger.LogWarning(ex, "ArchiveFactory 解压遇到异常，尝试回退处理: {Archive}", archiveFullPath);
                        await DecompressWithBclZipArchiveAsync(archiveFullPath, normalizedExtractRoot, encoding, ct, onProgress, isPathSafe);
                        return;
                    }

                    throw;
                }
            }

            // 针对 .tar, .tar.gz, .tgz, .tar.xz, .txz, .tar.bz2, .tbz2 等流式打包格式，使用 ReaderFactory
            try
            {
                await DecompressWithReaderFactoryAsync(archiveFullPath, normalizedExtractRoot, readerOptions, ct, onProgress, isPathSafe);
            }
            catch (Exception ex) when (!IsPasswordRelatedException(ex, password))
            {
                _logger.LogWarning(ex, "ReaderFactory 解压遇到异常，尝试 ArchiveFactory 回退: {Archive}", archiveFullPath);
                await DecompressWithArchiveFactoryAsync(archiveFullPath, normalizedExtractRoot, readerOptions, ct, onProgress, isPathSafe);
            }
        }
        catch (Exception ex) when (IsPasswordRelatedException(ex, password))
        {
            throw new InvalidOperationException("密码错误或当前文件已加密需要提供解压密码", ex);
        }
    }

    private static bool IsPasswordRelatedException(Exception? ex, string? password = null)
    {
        while (ex != null)
        {
            if (ex is System.Security.Cryptography.CryptographicException ||
                ex is SharpCompress.Common.CryptographicException)
            {
                return true;
            }

            string msg = ex.Message.ToLowerInvariant();
            if (msg.Contains("password") ||
                msg.Contains("encrypt") ||
                msg.Contains("crypt") ||
                msg.Contains("密码") ||
                msg.Contains("aes") ||
                msg.Contains("verification") ||
                msg.Contains("checksum") ||
                msg.Contains("crc") ||
                msg.Contains("bad key") ||
                msg.Contains("invalid key") ||
                msg.Contains("mac mismatch") ||
                msg.Contains("unsupported compression method") ||
                msg.Contains("compressed using an unsupported compression method"))
            {
                return true;
            }

            // 用户提供了密码，但 SharpCompress 报无法确定压缩流类型时，说明因密码错误导致格式探测/校验未通过
            if (!string.IsNullOrWhiteSpace(password) &&
                (ex is SharpCompress.Common.ArchiveOperationException || msg.Contains("cannot determine compressed stream type")))
            {
                return true;
            }

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (IsPasswordRelatedException(inner, password))
                        return true;
                }
            }

            ex = ex.InnerException;
        }

        return false;
    }

    private async Task DecompressWithArchiveFactoryAsync(
        string archiveFullPath,
        string normalizedExtractRoot,
        ReaderOptions readerOptions,
        CancellationToken ct,
        Action<int, string>? onProgress,
        Func<string, bool>? isPathSafe)
    {
        using var archive = ArchiveFactory.OpenArchive(archiveFullPath, readerOptions);
        var entries = archive.Entries.ToList();

        // 检查是否存在需要密码的加密条目，如果未提供密码则提前抛出明确提示
        bool hasEncrypted = entries.Any(e => !e.IsDirectory && e.IsEncrypted);
        if (hasEncrypted && string.IsNullOrWhiteSpace(readerOptions.Password))
        {
            throw new InvalidOperationException("当前压缩包已加密，请提供解压密码");
        }
        int total = entries.Count;
        int current = 0;
        int lastReportedPercent = -1;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            current++;

            if (total > 0)
            {
                int percent = (int)((double)current / total * 100);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    onProgress?.Invoke(percent, $"正在解压: {entry.Key}");
                }
            }

            if (string.IsNullOrEmpty(entry.Key)) continue;

            string normalizedRelPath = entry.Key
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            string destinationPath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, normalizedRelPath));

            // Zip Slip 防御
            if (!destinationPath.StartsWith(normalizedExtractRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isPathSafe != null && !isPathSafe(destinationPath))
                continue;

            if (entry.IsDirectory || normalizedRelPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
            }
            else
            {
                string? parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                await using var entryStream = entry.OpenEntryStream();
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await entryStream.CopyToAsync(fileStream, ct);
            }
        }
    }

    private async Task DecompressWithReaderFactoryAsync(
        string archiveFullPath,
        string normalizedExtractRoot,
        ReaderOptions readerOptions,
        CancellationToken ct,
        Action<int, string>? onProgress,
        Func<string, bool>? isPathSafe)
    {
        await using var fs = File.OpenRead(archiveFullPath);
        using var reader = ReaderFactory.OpenReader(fs, readerOptions);

        int count = 0;
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

            count++;
            if (count % 5 == 0)
            {
                onProgress?.Invoke(-1, $"正在解压: {entry.Key}");
            }

            string normalizedRelPath = entry.Key
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            string destinationPath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, normalizedRelPath));

            // Zip Slip 防御
            if (!destinationPath.StartsWith(normalizedExtractRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isPathSafe != null && !isPathSafe(destinationPath))
                continue;

            if (entry.IsDirectory || normalizedRelPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
            }
            else
            {
                string? parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                await using var entryStream = reader.OpenEntryStream();
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await entryStream.CopyToAsync(fileStream, ct);
            }
        }
    }

    private async Task DecompressWithBclZipArchiveAsync(
        string archiveFullPath,
        string normalizedExtractRoot,
        Encoding encoding,
        CancellationToken ct,
        Action<int, string>? onProgress,
        Func<string, bool>? isPathSafe)
    {
        await using var fs = File.OpenRead(archiveFullPath);
        using var archive = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: encoding);

        int total = archive.Entries.Count;
        int current = 0;
        int lastReportedPercent = -1;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            current++;

            if (total > 0)
            {
                int percent = (int)((double)current / total * 100);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    onProgress?.Invoke(percent, $"正在解压: {entry.Name}");
                }
            }

            string normalizedRelPath = entry.FullName
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            string destinationPath = Path.GetFullPath(Path.Combine(normalizedExtractRoot, normalizedRelPath));

            if (!destinationPath.StartsWith(normalizedExtractRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isPathSafe != null && !isPathSafe(destinationPath))
                continue;

            if (string.IsNullOrEmpty(entry.Name) || normalizedRelPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
            }
            else
            {
                string? parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
    }

    #endregion

    #region 压缩 (Compress)

    /// <summary>
    /// 压缩文件/文件夹，根据目标文件名后缀选择格式 (.zip, .tar, .tar.gz, .tgz, .tar.bz2, .tbz2, .7z)
    /// 可选传入 password 设置密码保护（.zip 采用纯 C# AES-256，.7z 采用 7z CLI）。
    /// </summary>
    public Task CompressAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        CancellationToken ct,
        Action<int, string>? onProgress = null)
        => CompressAsync(filesToCompress, targetFilePath, null, ct, onProgress);

    public async Task CompressAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        string? password = null,
        CancellationToken ct = default,
        Action<int, string>? onProgress = null)
    {
        if (filesToCompress == null || filesToCompress.Count == 0)
            throw new ArgumentException("没有可压缩的文件列表", nameof(filesToCompress));

        string? targetDir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        if (File.Exists(targetFilePath))
            File.Delete(targetFilePath);

        string lowerTarget = Path.GetFileName(targetFilePath).ToLowerInvariant();
        bool hasPassword = !string.IsNullOrWhiteSpace(password);

        // Tar 家族不支持包内密码
        if (hasPassword && (lowerTarget.EndsWith(".tar.gz") || lowerTarget.EndsWith(".tgz") ||
                            lowerTarget.EndsWith(".tar.bz2") || lowerTarget.EndsWith(".tbz2") ||
                            lowerTarget.EndsWith(".tar.xz") || lowerTarget.EndsWith(".txz") ||
                            lowerTarget.EndsWith(".tar")))
        {
            throw new NotSupportedException("tar 类打包格式在标准中不支持原生密码保护，请选用 .zip 或 .7z 格式进行加密。");
        }

        if (lowerTarget.EndsWith(".7z"))
        {
            await Compress7zAsync(filesToCompress, targetFilePath, password, ct, onProgress);
            return;
        }

        if (lowerTarget.EndsWith(".tar.gz") || lowerTarget.EndsWith(".tgz"))
        {
            await CompressTarGzAsync(filesToCompress, targetFilePath, ct, onProgress);
            return;
        }

        if (lowerTarget.EndsWith(".tar.bz2") || lowerTarget.EndsWith(".tbz2"))
        {
            await CompressTarBz2Async(filesToCompress, targetFilePath, ct, onProgress);
            return;
        }

        if (lowerTarget.EndsWith(".tar"))
        {
            await CompressTarAsync(filesToCompress, targetFilePath, ct, onProgress);
            return;
        }

        // 默认作为 .zip 处理
        if (hasPassword)
        {
            // 纯 C# AES-256 加密 ZIP (通过 SharpZipLib)
            await CompressZipEncryptedAsync(filesToCompress, targetFilePath, password!, ct, onProgress);
        }
        else
        {
            // 普通标准 ZIP
            await CompressZipAsync(filesToCompress, targetFilePath, ct, onProgress);
        }
    }

    private async Task CompressZipAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        int total = filesToCompress.Count;
        int current = 0;

        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new System.IO.Compression.ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);

        foreach (var kvp in filesToCompress)
        {
            ct.ThrowIfCancellationRequested();
            string sourcePath = kvp.Key;
            string entryName = kvp.Value.Replace('\\', '/');

            current++;
            if (current % 5 == 0 || current == total)
            {
                int percent = (int)((double)current / total * 100);
                onProgress?.Invoke(percent, $"正在压缩: {entryName}");
            }

            archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        }
    }

    /// <summary>
    /// 加密 ZIP 压缩
    /// </summary>
    private async Task CompressZipEncryptedAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        string password,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        int total = filesToCompress.Count;
        int current = 0;

        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var zipStream = new ZipOutputStream(fileStream);
        zipStream.Password = password;
        zipStream.SetLevel(6); // 默认最优平衡压缩级别

        foreach (var kvp in filesToCompress)
        {
            ct.ThrowIfCancellationRequested();
            string sourcePath = kvp.Key;
            string entryName = ZipEntry.CleanName(kvp.Value);

            current++;
            if (current % 5 == 0 || current == total)
            {
                int percent = (int)((double)current / total * 100);
                onProgress?.Invoke(percent, $"正在加密压缩: {entryName}");
            }

            var entry = new ZipEntry(entryName)
            {
                DateTime = File.GetLastWriteTime(sourcePath),
                Size = new FileInfo(sourcePath).Length,
                AESKeySize = 256 // AES-256加密
            };

            zipStream.PutNextEntry(entry);
            await using var sourceStream = File.OpenRead(sourcePath);
            await sourceStream.CopyToAsync(zipStream, ct);
            zipStream.CloseEntry();
        }

        zipStream.IsStreamOwner = false;
        zipStream.Finish();
    }

    private async Task CompressTarAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        int total = filesToCompress.Count;
        int current = 0;

        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var writer = WriterFactory.OpenWriter(fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.None));

        foreach (var kvp in filesToCompress)
        {
            ct.ThrowIfCancellationRequested();
            string sourcePath = kvp.Key;
            string entryName = kvp.Value.Replace('\\', '/');

            current++;
            if (current % 5 == 0 || current == total)
            {
                int percent = (int)((double)current / total * 100);
                onProgress?.Invoke(percent, $"正在打包: {entryName}");
            }

            await using var entrySource = File.OpenRead(sourcePath);
            writer.Write(entryName, entrySource, File.GetLastWriteTimeUtc(sourcePath));
        }
    }

    private async Task CompressTarGzAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        int total = filesToCompress.Count;
        int current = 0;

        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var writer = WriterFactory.OpenWriter(fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.GZip));

        foreach (var kvp in filesToCompress)
        {
            ct.ThrowIfCancellationRequested();
            string sourcePath = kvp.Key;
            string entryName = kvp.Value.Replace('\\', '/');

            current++;
            if (current % 5 == 0 || current == total)
            {
                int percent = (int)((double)current / total * 100);
                onProgress?.Invoke(percent, $"正在压缩: {entryName}");
            }

            await using var entrySource = File.OpenRead(sourcePath);
            writer.Write(entryName, entrySource, File.GetLastWriteTimeUtc(sourcePath));
        }
    }

    private async Task CompressTarBz2Async(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        int total = filesToCompress.Count;
        int current = 0;

        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var writer = WriterFactory.OpenWriter(fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2));

        foreach (var kvp in filesToCompress)
        {
            ct.ThrowIfCancellationRequested();
            string sourcePath = kvp.Key;
            string entryName = kvp.Value.Replace('\\', '/');

            current++;
            if (current % 5 == 0 || current == total)
            {
                int percent = (int)((double)current / total * 100);
                onProgress?.Invoke(percent, $"正在压缩: {entryName}");
            }

            await using var entrySource = File.OpenRead(sourcePath);
            writer.Write(entryName, entrySource, File.GetLastWriteTimeUtc(sourcePath));
        }
    }

    private async Task Compress7zAsync(
        Dictionary<string, string> filesToCompress,
        string targetFilePath,
        string? password,
        CancellationToken ct,
        Action<int, string>? onProgress)
    {
        string? sevenZipExe = Find7zExecutable();
        if (string.IsNullOrEmpty(sevenZipExe))
        {
            throw new NotSupportedException(
                "当前环境未安装 7z 命令行工具（7z/7za/7zz），无法创建 .7z 格式。推荐选择 .zip 或 .tar.gz 格式进行压缩。");
        }

        onProgress?.Invoke(10, "正在调用 7z 引擎压缩...");

        // 收集所有需要压缩的源文件路径，写入列表文件以防命令行参数溢出
        string listFile = Path.Combine(Path.GetTempPath(), $"7z_{Guid.NewGuid():N}.txt");
        try
        {
            var lines = filesToCompress.Keys.Distinct().ToList();
            await File.WriteAllLinesAsync(listFile, lines, Encoding.UTF8, ct);

            var args = new List<string> { "a", "-t7z", "-mx=7" };
            if (!string.IsNullOrWhiteSpace(password))
            {
                args.Add($"-p{password}");
                args.Add("-mhe=on"); // 加密文件名
            }
            args.Add(targetFilePath);
            args.Add($"@{listFile}");

            var cmd = Cli.Wrap(sevenZipExe).WithArguments(args);
            var result = await cmd.ExecuteAsync(ct);
            if (result.ExitCode != 0)
            {
                throw new Exception($"7z 压缩进程退出代码异常: {result.ExitCode}");
            }
        }
        finally
        {
            if (File.Exists(listFile))
            {
                try { File.Delete(listFile); } catch { }
            }
        }
    }

    private static string? Find7zExecutable()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { "7z.exe", "7za.exe", @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" }
            : new[] { "7z", "7za", "7zz", "/usr/bin/7z", "/usr/local/bin/7z", "/usr/bin/7za", "/usr/bin/7zz" };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // 检查 PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        string[] binNames = OperatingSystem.IsWindows() ? new[] { "7z.exe", "7za.exe" } : new[] { "7z", "7za", "7zz" };

        foreach (var dir in paths)
        {
            foreach (var bin in binNames)
            {
                var full = Path.Combine(dir, bin);
                if (File.Exists(full)) return full;
            }
        }

        return null;
    }

    #endregion

    #region 编码解析与辅助

    public Encoding ResolveEncoding(string? userChoice, string archivePath)
    {
        if (!string.IsNullOrEmpty(userChoice) && !userChoice.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Encoding.GetEncoding(userChoice);
            }
            catch
            {
                // 回退到自动探测
            }
        }

        // 默认 UTF-8，自动检测常见 GBK 乱码
        var utf8 = Encoding.UTF8;
        try
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(archivePath);
                using var archive = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: utf8);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.Contains('\uFFFD'))
                    {
                        return Encoding.GetEncoding("GBK");
                    }
                }
            }
            return utf8;
        }
        catch
        {
            try { return Encoding.GetEncoding("GBK"); } catch { return Encoding.UTF8; }
        }
    }

    #endregion
}
