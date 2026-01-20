using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

public class NeoForgeInstallerService
{
    private readonly ILogger<NeoForgeInstallerService> _logger;

    public class InstallLogEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public double? Progress { get; set; }
    }

    public event EventHandler<InstallLogEventArgs>? OnLog;
    public string InstallName = "NeoForge";
    public string InstallBasePath = string.Empty;
    public string InstallerPath = string.Empty;
    public uint InstallVersionType = 5;
    public string InstallMcVersion = "";
    public string InstallMirrorsName = "MSL Mirrors";

    public NeoForgeInstallerService(ILogger<NeoForgeInstallerService> logger)
    {
        _logger = logger;
    }

    public async Task<(bool IsSuccess, string? McVersion)> InstallNeoForge(string basePath, string installerPath, string javaEnvPath)
    {
        // 编译输出日志的缓存变量
        string logTemp = "";
        int counter = 100;

        string classPathConnectChar = PlatFormServices.GetOs() == "Windows" ? ";" : ":";

        try
        {
            // 传递变量
            InstallBasePath = basePath;
            InstallerPath = Path.Combine(basePath, installerPath);
            // 输出一些信息
            if (!installerPath.Contains("neo")) InstallName = "Forge";
            ReportLog($"开始执行{InstallName}安装进程，安装路径：{basePath}，安装器路径：{installerPath}，Java环境路径：{javaEnvPath}。");
            InstallMirrorsName = IConfigBase.Config.ReadConfig()["neoForgeInstallerMirrors"]?.ToString() ??
                                 "MSL Mirrors";
            switch (InstallMirrorsName)
            {
                case "MSL Mirrors":
                    InstallMirrorsName = "MSL镜像源";
                    break;
                case "MSL Mirrors Backup":
                    InstallMirrorsName = "MSL镜像源 - 备用";
                    break;
                default:
                    InstallMirrorsName = "官方源";
                    break;
            }

            ReportLog($"使用镜像源 {InstallMirrorsName} 进行安装。若需要切换镜像源，请在下一次安装前前往MSLX设置进行修改哦！");
            await Task.Delay(3000); // 给3秒用户阅读信息

            // 查询过往的安装资源 进行清除
            if (Directory.Exists(Path.Combine(basePath, "libraries")))
            {
                ReportLog("已删除 libraries 文件夹。");
                Directory.Delete(Path.Combine(basePath, "libraries"), true);
            }

            DirectoryInfo directoryInfo = new DirectoryInfo(basePath);
            FileInfo[] fileInfo = directoryInfo.GetFiles();
            foreach (FileInfo file in fileInfo)
            {
                if (file.Name != Path.GetFileName(installerPath) && file.Name.Contains("forge") &&
                    file.Name.Contains(".jar"))
                {
                    ReportLog("检测到" + file.Name + "文件，尝试将其删除……");
                    try
                    {
                        file.Delete();
                    }
                    finally
                    {
                        await Task.Delay(100);
                    }
                }
            }

            // ———— 正式安装流程 ————
            if (!Directory.Exists(Path.Combine(basePath, "temp")))
            {
                Directory.CreateDirectory(Path.Combine(basePath, "temp"));
                ReportLog("已创建临时文件夹。");
            }

            // 解压安装jar
            ReportLog($"正在解压 {InstallName} 安装器文件...");
            bool unzip = ExtractJar(Path.Combine(InstallBasePath, installerPath), Path.Combine(basePath, "temp"));
            if (!unzip)
            {
                ReportLog($"解压 {InstallName} 安装器失败，安装失败！");
                return (false,InstallMcVersion);
            }

            ReportLog($"{InstallName} 安装器解压成功！");

            // 读取安装信息
            JObject installJobj = GetJsonObj(Path.Combine(basePath, "temp", "install_profile.json"));

            // 获取MC版本
            InstallMcVersion = installJobj["minecraft"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(InstallMcVersion))
            {
                if (CompareMinecraftVersions(InstallMcVersion, "1.20.3") != -1)
                {
                    //1.20.3-Latest
                    InstallVersionType = 1;
                }
                else if (CompareMinecraftVersions(InstallMcVersion, "1.18") >= 0 &&
                         CompareMinecraftVersions(InstallMcVersion, "1.20.3") < 0)
                {
                    //1.18-1.20.2
                    InstallVersionType = 2;
                }
                else if (CompareMinecraftVersions(InstallMcVersion, "1.17.1") == 0)
                {
                    //1.17.1
                    InstallVersionType = 3;
                }
                else if (CompareMinecraftVersions(InstallMcVersion, "1.12") >= 0 &&
                         CompareMinecraftVersions(InstallMcVersion, "1.17.1") < 0)
                {
                    //1.12-1.16.5
                    InstallVersionType = 4;
                }
                else //if(CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.7") >= 0 && CompareMinecraftVersions(installJobj["minecraft"].ToString(), "1.12") < 0)
                {
                    //剩下版本应该都是1.11.2以下了 json格式大变（
                    InstallVersionType = 5;
                }
            }
            else
            {
                InstallMcVersion = installJobj["install"]?["minecraft"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(InstallMcVersion))
                {
                    ReportLog($"无法读取 {InstallName} 安装器对应的MC版本号，安装失败！");
                    return (false, InstallMcVersion);
                }
            }

            // 下载原版核心文件
            string serverJarPath;
            string vanillaUrl = "";

            if (InstallVersionType <= 3)
            {
                serverJarPath = ReplaceStr(installJobj["serverJarPath"]?.ToString() ?? "");
            }
            else
            {
                serverJarPath = Path.Combine(InstallBasePath, $"minecraft_server.{InstallMcVersion}.jar");
            }

            HttpService.HttpResponse response =
                await MSLApi.GetAsync(
                    $"/download/server/vanilla{(InstallMcVersion.Contains("snapshot") ? "-snapshot" : "")}/{InstallMcVersion}",
                    null);
            if (response.IsSuccessStatusCode)
            {
                JObject vanillaJobj = JObject.Parse(response.Content ?? "{}");
                vanillaUrl = vanillaJobj["data"]?["url"]?.ToString() ?? "";
            }

            if (string.IsNullOrEmpty(vanillaUrl))
            {
                ReportLog("获取原版服务端失败，安装失败！");
                return (false, InstallMcVersion);
            }

            if (InstallMirrorsName == "Official") // 是否使用镜像源 不用镜像就替换回原版
            {
                vanillaUrl = vanillaUrl.Replace("file.mslmc.cn/mirrors/vanilla/", "piston-data.mojang.com/v1/objects/");
            }

            ReportLog($"开始下载 {InstallMcVersion} 原版服务端核心···");
            var downloader = new ParallelDownloader(parallelCount: 1);
            var (success, errorMsg) = await downloader.DownloadFileAsync(vanillaUrl, serverJarPath,
                // 进度回调
                async (progress, speed) =>
                {
                    ReportLog($"正在下载 {InstallMcVersion} 原版服务端 进度: {progress:0.00}% | 下载速度: {speed}");
                }
            );

            if (success)
            {
                ReportLog($"{InstallMcVersion} 原版服务端核心下载成功！");
            }
            else
            {
                ReportLog($"{InstallMcVersion} 原版服务端核心下载失败！");
                return (false, InstallMcVersion);
            }

            // 解压原版服务端核心
            if (InstallVersionType <= 2)
            {
                ReportLog("正在解压原版核心资源···");
                bool result = ExtractJar(serverJarPath, Path.Combine(InstallBasePath, "temp", "vanilla"));
                if (result)
                {
                    try
                    {
                        // 指定源文件夹和目标文件夹
                        string sourceDirectory =
                            Path.Combine(InstallBasePath, "temp", "vanilla", "META-INF", "libraries");
                        string targetDirectory = InstallBasePath;

                        // 确保目标文件夹存在
                        Directory.CreateDirectory(targetDirectory);

                        // 获取源文件夹中的所有文件
                        string[] files = Directory.GetFiles(sourceDirectory);

                        // 复制所有文件到目标文件夹
                        ReportLog("正在复制原版核心资源···");
                        foreach (string file in files)
                        {
                            string name = Path.GetFileName(file);
                            string dest = Path.Combine(targetDirectory, name);
                            File.Copy(file, dest);
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportLog("原版服务端核心资源解压失败！" + ex.Message);
                        await Task.Delay(1000);
                        return (false, InstallMcVersion);
                    }
                }
            }

            // 处理库文件下载
            ReportLog($"正在下载 {InstallName} 所需要的运行库文件···");

            var tasks = new List<Task<(bool Success, string ErrorMessage)>>(); // 存储下载任务列表

            /* 下载用法
            var taskCore = downloader.DownloadFileAsync(
                "http://url/core.jar",
                "path/core.jar",
                (p, s) => Console.WriteLine($"Core: {p}%") // 简单日志
            );
            tasks.Add(taskCore); */

            if (InstallVersionType != 5)
            {
                // 1.13以上高版本处理逻辑
                var versionlJobj = GetJsonObj(Path.Combine(InstallBasePath, "temp", "version.json"));

                // 安全获取 libraries 数组
                JArray? libraries2 = installJobj["libraries"] as JArray;
                JArray? libraries = versionlJobj["libraries"] as JArray;

                if (libraries2 == null || libraries == null)
                {
                    ReportLog($"[ {InstallName} ]错误：无法获取运行库列表");
                    throw new Exception("Err: 无法获取运行库列表");
                }

                // 比较器存储 用于查重
                var addedDownloadPaths = new HashSet<string>();

                foreach (JObject lib in libraries.Cast<JObject>()) // 遍历数组，进行文件下载
                {
                    // 安全获取嵌套属性
                    var artifactToken = lib["downloads"]?["artifact"];
                    if (artifactToken == null)
                        continue;

                    string? _dlurl = artifactToken["url"]?.ToString();
                    if (string.IsNullOrEmpty(_dlurl))
                        continue;

                    _dlurl = ReplaceStr(_dlurl);

                    string? path = artifactToken["path"]?.ToString();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    // 把文件路径扔进去查重
                    if (!addedDownloadPaths.Add(path))
                        continue;

                    string? _sha1 = artifactToken["sha1"]?.ToString();
                    ReportLog($"[ {InstallName} 运行库]下载：" + path);

                    // 添加下载项
                    var taskLib = downloader.DownloadFileAsync(
                        _dlurl,
                        Path.Combine(InstallBasePath, "libraries", path),
                        async (progress, speed) => { ReportLog($"正在下载 {path} 进度: {progress:0.00}% | 下载速度: {speed}"); }
                    );
                    tasks.Add(taskLib);
                }

                // 来自MSL的注释 - 2024.02.27 下午11：25 写的时候bmclapi炸了，导致被迫暂停，望周知（
                foreach (JObject lib in libraries2.Cast<JObject>()) //遍历数组，进行文件下载
                {
                    // 安全获取嵌套属性
                    var artifactToken = lib["downloads"]?["artifact"];
                    if (artifactToken == null)
                        continue;

                    string? _dlurl = artifactToken["url"]?.ToString();
                    if (string.IsNullOrEmpty(_dlurl))
                        continue;

                    _dlurl = ReplaceStr(_dlurl);

                    string? path = artifactToken["path"]?.ToString();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    // 查重
                    if (!addedDownloadPaths.Add(path))
                        continue;

                    string? _sha1 = artifactToken["sha1"]?.ToString();
                    ReportLog($"[ {InstallName} 运行库]下载：" + path);

                    // 添加下载项
                    var taskLib = downloader.DownloadFileAsync(
                        _dlurl,
                        Path.Combine(InstallBasePath, "libraries", path),
                        async (progress, speed) => { ReportLog($"正在下载 {path} 进度: {progress:0.00}% | 下载速度: {speed}"); }
                    );
                    tasks.Add(taskLib);
                }
            }
            else
            {
                // 这里是1.12-版本的处理逻辑
                if (installJobj["versionInfo"]?["libraries"] is not JArray libraries2)
                {
                    ReportLog($"[ {InstallName} ]错误：无法获取运行库列表");
                    throw new Exception("Err: 无法获取运行库列表");
                }

                int libALLCount = libraries2.Count; //总数
                int libCount = 0; // 用于计数

                foreach (JObject lib in libraries2.Cast<JObject>()) // 遍历数组，进行文件下载
                {
                    libCount++;

                    string? libName = lib["name"]?.ToString();
                    if (string.IsNullOrEmpty(libName))
                        continue;

                    string libPath = NameToPath(libName);

                    string _dlurl;
                    string? libUrl = lib["url"]?.ToString();

                    if (string.IsNullOrEmpty(libUrl))
                    {
                        _dlurl = ReplaceStr("https://maven.minecraftforge.net/" + libPath);
                    }
                    else
                    {
                        //_dlurl = ReplaceStr(lib["url"]?.ToString() ?? "" + NameToPath(lib["name"]?.ToString() ?? ""));

                        _dlurl = ReplaceStr(libUrl.TrimEnd('/') + "/" + libPath);
                    }

                    if (string.IsNullOrEmpty(_dlurl))
                        continue;

                    ReportLog($"[ {InstallName} 运行库]下载：" + libPath);

                    // 添加下载项
                    var taskLib = downloader.DownloadFileAsync(
                        _dlurl,
                        Path.Combine(InstallBasePath, "libraries", libPath),
                        async (progress, speed) =>
                        {
                            ReportLog($"正在下载 {libPath} 进度: {progress:0.00}% | 下载速度: {speed}");
                        }
                    );
                    tasks.Add(taskLib);
                }
            }

            // 所有文件成功添加到下载 等待下崽崽完成～
            ReportLog($"正在等待所有库文件下载完成···");

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                if (!result.Success)
                {
                    ReportLog($"检测到有文件下载失败，原因: {result.ErrorMessage}");
                }
            }

            // 检查是否全部成功
            if (results.All(x => x.Success))
            {
                ReportLog("所有文件下载完成！");
            }

            // 复制一些文件
            if (InstallVersionType == 1 && !InstallerPath.Contains("neoforge"))
            {
                try
                {
                    ReportLog("正在复制shim.jar···");
                    string src = Path.Combine(InstallBasePath,
                        Path.GetFileName(Path.Combine(InstallBasePath, "libraries",
                            NameToPath(installJobj["path"]?.ToString() ?? ""))));
                    // 复制shim jar（鬼知道什么版本加进来的哦！）
                    if (!File.Exists(src))
                    {
                        File.Copy(
                            Path.Combine(InstallBasePath, "temp", "maven",
                                NameToPath(installJobj["path"]?.ToString() ?? "")), src);
                    }
                }
                catch
                {
                    ReportLog("复制Shim.jar失败！");
                    return (false, InstallMcVersion);
                }
            }
            else if (InstallVersionType == 4)
            {
                ReportLog("正在合并一些库文件···");
                MergeDirectories(Path.Combine(InstallBasePath, "temp", "maven/net"),
                    Path.Combine(InstallBasePath, "libraries", "net"));
                CopyJarFiles(Path.Combine(InstallBasePath, "temp", "maven/net"), InstallBasePath);
            }
            else if (InstallVersionType == 5)
            {
                ReportLog("正在合并一些库文件···");
                CopyJarFiles(Path.Combine(InstallBasePath, "temp"), InstallBasePath, 2);
            }

            ReportLog($"正在处理 {InstallName} 编译参数···");
            List<string> cmdLines = [];

            if (InstallVersionType != 5) // 低版本不需要运行编译构建
            {
                // 获取 processors 数组
                if (installJobj["processors"] is not JArray processors)
                {
                    ReportLog($"[ {InstallName} ]错误：无法获取 processors 数组");
                    return (false, InstallMcVersion);
                }

                foreach (JObject processor in processors.Cast<JObject>())
                {
                    // 获取 sides 数组
                    // 如果 sides 为 null 或包含 "server"，则处理
                    if (processor["sides"] is not JArray sides || sides.Values<string>().Contains("server"))
                    {
                        string buildarg = @"-Djavax.net.ssl.trustStoreType=Windows-ROOT -cp """;

                        // 安全获取 jar 路径
                        string? jarName = processor["jar"]?.ToString();
                        if (string.IsNullOrEmpty(jarName))
                        {
                            ReportLog("警告：processor 缺少 jar 属性，跳过此项");
                            continue;
                        }

                        string jarPath = NameToPath(jarName);

                        // 处理 classpath
                        buildarg += Path.Combine(InstallBasePath, "libraries", jarPath) + classPathConnectChar;

                        // 获取 classpath 数组
                        if (processor["classpath"] is not JArray classpath)
                        {
                            ReportLog("警告：processor 缺少 classpath 属性，跳过此项");
                            continue;
                        }

                        string entryjar = Path.Combine(InstallBasePath, "libraries", jarPath);
                        ReportLog("捕获到执行的入口文件：" + entryjar);

                        // 获取主类
                        string? mainclass = GetJarMainClass(entryjar);
                        if (string.IsNullOrEmpty(mainclass))
                        {
                            ReportLog("未能捕获到入口文件的主类，安装失败！");
                            return (false, InstallMcVersion);
                        }

                        ReportLog("捕获到入口文件的主类：" + mainclass);

                        // 处理 classpath 路径
                        foreach (string? path in classpath.Values<string>())
                        {
                            if (string.IsNullOrEmpty(path))
                                continue;

                            buildarg += Path.Combine(InstallBasePath, "libraries", NameToPath(path)) +
                                        classPathConnectChar;
                        }

                        buildarg += @""" "; // 结束 cp 处理

                        // 添加主类
                        buildarg += $"{mainclass} ";

                        // 安全获取并处理 args 数组
                        JArray? args = processor["args"] as JArray;
                        if (args == null)
                        {
                            ReportLog("警告：processor 缺少 args 属性，跳过此项");
                            continue;
                        }

                        foreach (string? arg in args.Values<string>())
                        {
                            if (string.IsNullOrEmpty(arg))
                                continue;

                            if (arg.StartsWith("[") && arg.EndsWith("]")) // 在 [] 中，表明要转换
                            {
                                buildarg += @"""" +
                                            Path.Combine(InstallBasePath, "libraries", ReplaceStr(NameToPath(arg))) +
                                            @""" ";
                            }
                            else
                            {
                                buildarg += @"""" + ReplaceStr(arg) + @""" ";
                            }
                        }

                        // 检查并添加命令行
                        if (!buildarg.Contains("DOWNLOAD_MOJMAPS"))
                        {
                            cmdLines.Add(buildarg);
                            ReportLog("启动参数：" + buildarg);
                        }
                        else
                        {
                            ReportLog("DOWNLOAD_MOJMAPS 任务跳过！");
                        }
                    }
                }
            }

            // 额外自动处理下载混淆代码映射表（好心的mj在新版本去除了这个代码混淆 这个后续估计也不需要咯）
            if (InstallVersionType < 4 && CompareMinecraftVersions(InstallMcVersion,"1.21.11") < 1) // 低版本和26.1+版本不下载映射表
            {
                // 自动DOWNLOAD_MOJMAPS
                ReportLog("正在下载MC映射表，请耐心等待……");
                string mappings_file_path = ReplaceStr("{MOJMAPS}".Replace("/", "\\"));
                try
                {
                    HttpService.HttpResponse res_metadata =
                        await GeneralApi.GetAsync(
                            ReplaceStr("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
                    if (res_metadata.IsSuccessStatusCode)
                    {
                        // 查找对应版本的元信息文件URL
                        JObject metadata_jobj = JObject.Parse(res_metadata.Content ??
                                                              throw new Exception("err: null res_metadata.Content"));
                        var foundVersion = metadata_jobj["versions"]?
                            .FirstOrDefault(v => v["id"]?.ToString() == InstallMcVersion);
                        string? versionUrl = foundVersion?["url"]?.ToString();
                        if (string.IsNullOrEmpty(versionUrl))
                        {
                            ReportLog($"错误：未能在版本清单中找到版本号为 '{InstallMcVersion}' 的详细信息。请检查版本号是否正确。");
                            return (false, InstallMcVersion);
                        }
                        else
                        {
                            ReportLog($"成功找到版本 {InstallMcVersion} 的元信息文件URL: {versionUrl}");
                        }

                        // 替换下镜像源
                        versionUrl = ReplaceStr(versionUrl);
                        HttpService.HttpResponse res_version_metadata = await GeneralApi.GetAsync(versionUrl);
                        if (res_version_metadata.IsSuccessStatusCode)
                        {
                            JObject version_metadata_jobj = JObject.Parse(res_version_metadata.Content ??
                                                                          throw new Exception(
                                                                              "err: null res_version_metadata.Content"));
                            string mappingsUrl = version_metadata_jobj["downloads"]?["server_mappings"]?["url"]
                                ?.ToString() ?? "";
                            if (string.IsNullOrEmpty(mappingsUrl))
                            {
                                throw new Exception("错误：未能在版本元信息中找到映射表的下载URL。请检查该版本是否包含映射表。");
                            }

                            // 下载到指定位置
                            ReportLog($"映射表文件下载到: {mappings_file_path}");
                            var (suc_mojmap, err_mojmap) = await downloader.DownloadFileAsync(
                                ReplaceStr(mappingsUrl),
                                mappings_file_path,
                                // 进度回调
                                async (progress, speed) =>
                                {
                                    // lazy~
                                }
                            );
                            if (suc_mojmap)
                            {
                                ReportLog("映射表文件下载成功！");
                            }
                            else
                            {
                                throw new Exception("下载映射表失败！" + err_mojmap);
                            }
                        }
                        else
                        {
                            ReportLog("无法获取MC元信息，请重试，或改用命令行安装。");
                            return (false, InstallMcVersion);
                        }
                    }
                    else
                    {
                        ReportLog("无法获取MC元信息，请重试，或改用命令行安装。");
                        ReportLog(res_metadata.Content ?? "");
                        return (false, InstallMcVersion);
                    }
                }
                catch (Exception ex)
                {
                    ReportLog("自动下载MOJMAPS失败！" + ex.Message);
                    ReportLog("无法获取MC元信息，请重试，或改用命令行安装。");
                    return (false, InstallMcVersion);
                }
            }

            // 开始执行编译构建
            foreach (string cmdLine in cmdLines)
            {
                ReportLog($"执行任务: {cmdLine}\n");
                Process process = new Process();
                process.StartInfo.WorkingDirectory = InstallBasePath;
                process.StartInfo.FileName = javaEnvPath;
                process.StartInfo.Arguments = cmdLine;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.ErrorDataReceived += ProcessOutputDataReceived;
                process.OutputDataReceived += ProcessOutputDataReceived;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(process.WaitForExit);
                process.CancelOutputRead();
                process.CancelErrorRead();
                process.ErrorDataReceived -= ProcessOutputDataReceived;
                process.OutputDataReceived -= ProcessOutputDataReceived;
            }

            // 编译完成 安装结束
            ReportLog($"{InstallName} 安装完成！");

            // 清理临时文件
            ReportLog("正在清理安装产生的临时文件···");
            try
            {
                Directory.Delete(Path.Combine(InstallBasePath, "temp"), true);
                File.Delete(InstallerPath);
            }
            catch
            {
            }

            return (true, InstallMcVersion);
        }
        catch (Exception ex)
        {
            ReportLog($"安装失败：{ex.Message}");
            _logger.LogError("安装失败 {0}", ex.ToString());
            return (false, InstallMcVersion);
        }

        // 回报日志的方法
        void ReportLog(string message, double? progress = null)
        {
            OnLog?.Invoke(this, new InstallLogEventArgs { Message = message, Progress = progress });
            _logger.LogInformation(message);
        }

        // 接收编译日志回调
        void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                if (counter == 100)
                {
                    counter = 0;
                    ReportLog(logTemp);
                    logTemp = "";
                }

                logTemp += e.Data + "\n";
                counter++;
            }
        }
    }

    // 替换镜像源
    private string ReplaceStr(string str)
    {
        var installJobj = GetJsonObj(Path.Combine(InstallBasePath, "temp", "install_profile.json"));
        str = str.Replace("{LIBRARY_DIR}", Path.Combine(InstallBasePath, "libraries"));
        str = str.Replace("{MINECRAFT_VERSION}", InstallMcVersion);
        //是否使用镜像源
        if (InstallMirrorsName == "MSL镜像源")
        {
            str = str.Replace("https://maven.neoforged.net", "https://neoforge.mirrors.mslmc.cn");
            str = str.Replace("https://maven.minecraftforge.net", "https://forge-maven.mirrors.mslmc.cn");
            str = str.Replace("https://files.minecraftforge.net", "https://forge-files.mirrors.mslmc.cn");
            str = str.Replace("https://libraries.minecraft.net", "https://mclibs.mirrors.mslmc.cn");
            str = str.Replace("https://piston-meta.mojang.com", "https://mc-meta.mirrors.mslmc.cn");
            str = str.Replace("piston-data.mojang.com/v1/objects/", "file.mslmc.cn/mirrors/vanilla/");
        }

        // 备用镜像源
        if (InstallMirrorsName == "MSL镜像源 - 备用")
        {
            //改成镜像源的部分
            str = str.Replace("https://maven.neoforged.net", "https://neoforge.mc-mirrors.aino.cyou");
            str = str.Replace("https://maven.minecraftforge.net", "https://forge-maven.mc-mirrors.aino.cyou");
            str = str.Replace("https://files.minecraftforge.net", "https://forge-files.mc-mirrors.aino.cyou");
            str = str.Replace("https://libraries.minecraft.net", "https://mclibs.mc-mirrors.aino.cyou");
            str = str.Replace("https://piston-meta.mojang.com", "https://mcmeta.mc-mirrors.aino.cyou");
            str = str.Replace("piston-data.mojang.com/v1/objects/", "file.mslmc.cn/mirrors/vanilla/");
        }

        //构建时候的变量
        str = str.Replace("{INSTALLER}", InstallerPath);
        str = str.Replace("{ROOT}", InstallBasePath);
        if (InstallVersionType <= 3)
        {
            str = str.Replace("{MINECRAFT_JAR}",
                installJobj["serverJarPath"]?.ToString()
                    .Replace("{LIBRARY_DIR}", Path.Combine(InstallBasePath, "libraries"))
                    .Replace("{MINECRAFT_VERSION}", InstallMcVersion));
        }
        else
        {
            str = str.Replace("{MINECRAFT_JAR}",
                Path.Combine(InstallBasePath, $"minecraft_server.{InstallMcVersion}.jar"));
        }

        str = str.Replace("{MAPPINGS}",
            Path.Combine(InstallBasePath, "libraries",
                NameToPath(installJobj["data"]?["MAPPINGS"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{MC_UNPACKED}",
            Path.Combine(InstallBasePath, "libraries",
                NameToPath(installJobj["data"]?["MC_UNPACKED"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{SIDE}", "server");
        str = str.Replace("{MOJMAPS}",
            Path.Combine(InstallBasePath, "libraries",
                NameToPath(installJobj["data"]?["MOJMAPS"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{MERGED_MAPPINGS}",
            Path.Combine(InstallBasePath, "libraries",
                NameToPath(installJobj["data"]?["MERGED_MAPPINGS"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{MC_SRG}", Path.Combine(InstallBasePath, "libraries",
            NameToPath(installJobj["data"]?["MC_SRG"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{PATCHED}", Path.Combine(InstallBasePath, "libraries",
            NameToPath(installJobj["data"]?["PATCHED"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{BINPATCH}",
            Path.Combine(InstallBasePath, "temp",
                (installJobj["data"]?["BINPATCH"]?["server"]?.ToString() ?? "").TrimStart('/'))); // 这个是改掉路径
        str = str.Replace("{MC_SLIM}", Path.Combine(InstallBasePath, "libraries",
            NameToPath(installJobj["data"]?["MC_SLIM"]?["server"]?.ToString() ?? "")));
        str = str.Replace("{MC_EXTRA}", Path.Combine(InstallBasePath, "libraries",
            NameToPath(installJobj["data"]?["MC_EXTRA"]?["server"]?.ToString() ?? "")));

        return str;
    }


    // 解压jar文件方法
    private bool ExtractJar(string jarPath, string extractPath)
    {
        try
        {
            if (!Directory.Exists(extractPath))
            {
                Directory.CreateDirectory(extractPath);
            }

            ZipFile.ExtractToDirectory(jarPath, extractPath, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解压Jar文件时出错：{0}", ex.Message);
            return false;
        }
    }

    // 获取json对象
    private JObject GetJsonObj(string file)
    {
        var json = File.ReadAllText(file);
        var jsonObj = JObject.Parse(json);
        return jsonObj;
    }

    private int CompareMinecraftVersions(string version1, string version2)
    {
        // 预处理：提取版本号中的“核心数字序列”
        var v1Parts = ExtractVersionNumbers(version1);
        var v2Parts = ExtractVersionNumbers(version2);

        int maxLength = Math.Max(v1Parts.Count, v2Parts.Count);

        for (int i = 0; i < maxLength; i++)
        {
            int part1 = i < v1Parts.Count ? v1Parts[i] : 0;
            int part2 = i < v2Parts.Count ? v2Parts[i] : 0;

            if (part1 > part2) return 1;
            if (part1 < part2) return -1;
        }

        // 核心数字完全一致时的特殊处理
        bool v1IsSnapshot = version1.Contains("-") || Regex.IsMatch(version1, "[a-zA-Z]");
        bool v2IsSnapshot = version2.Contains("-") || Regex.IsMatch(version2, "[a-zA-Z]");

        if (!v1IsSnapshot && v2IsSnapshot) return 1; // 正式版 > 测试版
        if (v1IsSnapshot && !v2IsSnapshot) return -1; // 测试版 < 正式版

        return 0;
    }

    // 辅助方法：使用正则提取纯数字部分
    private List<int> ExtractVersionNumbers(string version)
    {
        var numbers = new List<int>();
        // 这个正则会匹配所有连续的数字段
        // "26.1-snapshot-1" -> 匹配出 [26, 1, 1]
        // "1.20.3" -> 匹配出 [1, 20, 3]
        var matches = Regex.Matches(version, @"\d+");

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Value, out int num))
            {
                numbers.Add(num);
            }
        }

        return numbers;
    }

    // 路径转换函数，参考：https://rechalow.gitee.io/lmaml/FirstChapter/GetCpLibraries.html 非常感谢！
    private string NameToPath(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        string extentTag = "";

        if (name.StartsWith("[") && name.EndsWith("]")) //部分包含在[]中，干掉
        {
            name = name.Substring(1, name.Length - 2);
        }

        if (name.Contains("@"))
        {
            string[] parts = name.Split('@');

            name = parts[0]; //第一部分，按照原版处理
            extentTag = parts[1]; //这里等下添加后缀
        }

        List<string> c1 = new List<string>();
        List<string> c2 = new List<string>();
        List<string> all = new List<string>();
        StringBuilder sb = new StringBuilder();

        try
        {
            string n1 = name.Substring(0, name.IndexOf(":"));
            string n2 = name.Substring(name.IndexOf(":") + 1);

            c1.AddRange(n1.Split('.'));
            foreach (var i in c1)
            {
                all.Add(i + "/");
            }

            c2.AddRange(n2.Split(':'));
            for (int i = 0; i < c2.Count; i++)
            {
                if (c2.Count >= 3)
                {
                    if (i < c2.Count - 1)
                    {
                        all.Add(c2[i] + "/");
                    }
                }
                else
                {
                    all.Add(c2[i] + "/");
                }
            }

            for (int i = 0; i < c2.Count; i++)
            {
                if (i < c2.Count - 1)
                {
                    all.Add(c2[i] + "-");
                }
                else
                {
                    all.Add(c2[i] + ".jar");
                }
            }

            foreach (var i in all)
            {
                sb.Append(i);
            }

            if (extentTag != "")
            {
                return sb.ToString().Replace(".jar", "") + "." + extentTag;
            }

            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    // 合并目录 低版本
    private void MergeDirectories(string source, string target)
    {
        foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dirPath.Replace(source, target));

        foreach (string newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            File.Copy(newPath, newPath.Replace(source, target), true);
    }

    // 复制jar 用于低版本
    private void CopyJarFiles(string source, string target, int mode = 1)
    {
        if (mode == 1)
        {
            foreach (string filePath in Directory.GetFiles(source, "*.jar", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                File.Copy(filePath, Path.Combine(target, fileName), true);
            }
        }
        else
        {
            // 遍历所有jar文件
            foreach (var file in Directory.GetFiles(source, "*.jar"))
            {
                // 获取文件名
                var fileName = Path.GetFileName(file);
                // 复制文件到目标目录
                File.Copy(file, Path.Combine(target, fileName), true);
            }
        }
    }

    // 获取jar主类
    private static string? GetJarMainClass(string? jarFilePath)
    {
        // 1. 检查输入参数
        if (string.IsNullOrWhiteSpace(jarFilePath))
        {
            return null;
        }

        if (!File.Exists(jarFilePath))
        {
            return null;
        }

        try
        {
            // 使用 ZipFile.OpenRead 打开 ZIP (JAR) 文件
            using (ZipArchive archive = ZipFile.OpenRead(jarFilePath))
            {
                // 查找 MANIFEST.MF 文件
                ZipArchiveEntry? manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
                if (manifestEntry == null)
                {
                    return null;
                }

                // 打开 entry 的流
                using (Stream stream = manifestEntry.Open())
                using (StreamReader reader = new StreamReader(stream))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // 读取主类
                        if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                        {
                            // 安全地提取主类名称
                            string mainClass = line.Substring("Main-Class:".Length).Trim();

                            // 返回前验证不为空
                            return string.IsNullOrWhiteSpace(mainClass) ? null : mainClass;
                        }
                    }
                }
            }
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"读取 JAR 文件失败 (IO错误): {ioEx.Message}");
            return null;
        }
        catch (InvalidDataException dataEx)
        {
            Console.WriteLine($"读取 JAR 文件失败 (无效的ZIP格式): {dataEx.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取 JAR 文件失败: {ex}");
            return null;
        }

        // 遍历完成仍未找到
        return null;
    }
}