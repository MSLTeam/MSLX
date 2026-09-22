using System.Text;

namespace MSLX.Daemon.Utils;

public static class CommandLineUtils
{
    /// <summary>
    /// 按空格拆分命令行参数，双引号内的内容（含空格）视为单个参数。
    /// 注意：空的引号对（""）会被丢弃，未闭合的引号将持续到行尾。
    /// </summary>
    public static string[] SplitCommandLineArgs(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return Array.Empty<string>();
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }
        return args.ToArray();
    }
}
