using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public static class IConfigBase
    {
        public static IConfigService Config { get; private set; } = null!;
        public static ServerListConfig ServerList { get; set; } = null!;
        public static FrpListConfig FrpList { get; set; } = null!;
        public static TaskListConfig TaskList { get; set; } = null!;
        public static UserListConfig UserList { get; set; } = null!;
        public static string JwtSecret { get; private set; } = string.Empty;

        public static void Initialize(ILoggerFactory loggerFactory)
        {
            ApplicationLogging.LoggerFactory = loggerFactory;

            Config = new IConfigService();

            // 生成JWT密钥
            var secret = Config.ReadConfigKey("JwtSecret")?.ToString();
            if (string.IsNullOrEmpty(secret))
            {
                secret = StringServices.GenerateRandomString(32);
                Config.WriteConfigKey("JwtSecret", secret);
            }
            JwtSecret = secret;
        }

        public static string GetAppDataPath()
        {
            bool isMacAppBundle = PlatFormServices.GetOs() == "MacOS" && 
                                  AppContext.BaseDirectory.Contains(".app/Contents/MacOS", StringComparison.OrdinalIgnoreCase);

            if (isMacAppBundle)
            {
                // MacOS 且在 .app 包内
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSLX", "DaemonData");
            }
            // 数据文件放在程序同级目录下
            return Path.Combine(AppContext.BaseDirectory, "DaemonData");
        }

        public static string GetAppConfigPath()
        {
            return Path.Combine(GetAppDataPath(), "Configs");
        }

        public static T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        public static void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Formatting.Indented));
        }
    }
}
