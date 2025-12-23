using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public static class IConfigBase
    {
        public static IConfigService Config { get; private set; } = null!;
        public static ServerListConfig ServerList { get; private set; } = null!;
        public static FrpListConfig FrpList { get; private set; } = null!;
        public static TaskListConfig TaskList { get; private set; } = null!;
        public static UserListConfig UserList { get; private set; } = null!;
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

            ServerList = new ServerListConfig();
            FrpList = new FrpListConfig();
            TaskList = new TaskListConfig();
            UserList = new UserListConfig();
        }

        public static string GetAppDataPath()
        {
            if (PlatFormServices.GetOs() == "MacOS")
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSLX", "DaemonData");
            }
            else
            {
                return Path.Combine(AppContext.BaseDirectory,"DaemonData");
            }
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
