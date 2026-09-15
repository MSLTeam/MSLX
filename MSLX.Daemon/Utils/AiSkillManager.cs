using System.Reflection;
using System.Text;

namespace MSLX.Daemon.Utils;

public static class AiSkillManager
{
    private static readonly string[] SkillDiskPaths = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "Skills"),
        Path.Combine(Directory.GetCurrentDirectory(), "Skills"),
        Path.Combine(Directory.GetCurrentDirectory(), "MSLX.Daemon", "Skills")
    };

    public static string GetSystemPrompt()
    {
        var sb = new StringBuilder();
        var loadedCount = 0;

        // 1. 优先从 Assembly 程序集内嵌资源中读取 (支持单文件 PublishSingleFile 打包)
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(r => r.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && r.Contains("Skills", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r)
                .ToList();

            foreach (var resourceName in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var content = reader.ReadToEnd().Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine(content);
                        sb.AppendLine();
                        loadedCount++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiSkillManager] 读取程序集内嵌 Skill 资源异常: {ex.Message}");
        }

        // 2. 如果内嵌资源未读取到，降级尝试从磁盘 Skills 目录读取
        if (loadedCount == 0)
        {
            foreach (var dirPath in SkillDiskPaths)
            {
                if (!Directory.Exists(dirPath)) continue;

                var skillFiles = Directory.GetFiles(dirPath, "*.md", SearchOption.TopDirectoryOnly);
                Array.Sort(skillFiles);

                foreach (var file in skillFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(file, Encoding.UTF8).Trim();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            sb.AppendLine(content);
                            sb.AppendLine();
                            loadedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AiSkillManager] 读取磁盘 Skill 文件 {file} 异常: {ex.Message}");
                    }
                }

                if (loadedCount > 0) break;
            }
        }

        if (loadedCount == 0 || sb.Length == 0)
        {
            return """
你是一个高效、果断的 Minecraft 服务器运维助手。当用户提出需求时，优先调用工具函数(Function Calling)完成操作。
""";
        }

        return sb.ToString().Trim();
    }
}
