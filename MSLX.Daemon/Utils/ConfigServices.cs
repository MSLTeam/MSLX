using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils;

public static class ConfigServices
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
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSLX");
        }
        else
        {
            return AppContext.BaseDirectory;
        }
    }

    public class IConfigService : IDisposable
    {
        private readonly string _configPath = Path.Combine(GetAppDataPath(),"DaemonData", "Configs", "Config.json");
        private JObject _configCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public IConfigService()
        {
            _logger = ApplicationLogging.CreateLogger<IConfigService>();
            InitializeFile(_configPath);
            _configCache = LoadJson<JObject>(_configPath);
        }

        private void InitializeFile(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                string apikey = StringServices.GenerateRandomString(64);

                _logger.LogInformation("正在初始化配置文件...");
                _logger.LogInformation("您的默认 API Key 是: {ApiKey}", apikey);

                File.WriteAllText(path, $"{{\n    \"apiKey\":\"{apikey}\",\n    \"user\":\"MSLX用户\",\n    \"avatar\":\"https://www.mslmc.cn/logo.png\"\n}}");
            }
        }

        public JObject ReadConfig()
        {
            _configLock.EnterReadLock();
            try
            {
                return (JObject)_configCache.DeepClone();
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        public JToken? ReadConfigKey(string key)
        {
            _configLock.EnterReadLock();
            try
            {
                return _configCache.TryGetValue(key, out var value) ? value : null;
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        public void WriteConfig(JObject content)
        {
            _configLock.EnterWriteLock();
            try
            {
                _configCache = (JObject)content.DeepClone();
                SaveJson(_configPath, _configCache);
            }
            finally
            {
                _configLock.ExitWriteLock();
            }
        }

        public void WriteConfigKey(string key, JToken value)
        {
            _configLock.EnterWriteLock();
            try
            {
                _configCache[key] = value;
                SaveJson(_configPath, _configCache);
            }
            finally
            {
                _configLock.ExitWriteLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _configLock?.Dispose();
        }
    }

    public class ServerListConfig : IDisposable
    {
        private readonly string _serverListPath = Path.Combine(GetAppDataPath(),"DaemonData", "Configs", "ServerList.json");
        private JArray _serverListCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _serverListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public ServerListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<ServerListConfig>();
            InitializeFile(_serverListPath, "[]");
            _serverListCache = LoadJson<JArray>(_serverListPath);
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
        }

        public JArray ReadServerList()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return (JArray)_serverListCache.DeepClone();
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public List<McServerInfo.ServerInfo> GetServerList()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return [.. _serverListCache.Select(s => s.ToObject<McServerInfo.ServerInfo>()!)];
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public void WriteServerList(JArray content)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                _serverListCache = (JArray)content.DeepClone();
                SaveJson(_serverListPath, _serverListCache);
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool CreateServer(McServerInfo.ServerInfo server)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                if (_serverListCache.Any(s => s["ID"]?.Value<uint>() == server.ID))
                {
                    return false;
                }

                var serverObject = JObject.FromObject(server);
                _serverListCache.Add(serverObject);

                SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool DeleteServer(uint serverId,bool deleteFiles = false)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                var target = _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == serverId);

                if (target == null) return false;

                _serverListCache.Remove(target);
                SaveJson(_serverListPath, _serverListCache);
                if (deleteFiles)
                {
                    if (Directory.Exists(target["Base"]?.Value<string>()))
                    {
                        try
                        {
                            Directory.Delete(target["Base"]?.Value<string>(), true);
                        }
                        catch{}
                    }
                }
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool UpdateServer(McServerInfo.ServerInfo updatedServer)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                var target = _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == updatedServer.ID);

                if (target == null) return false;

                target.Replace(JObject.FromObject(updatedServer));
                SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public McServerInfo.ServerInfo? GetServer(uint serverId)
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == serverId)
                    ?.ToObject<McServerInfo.ServerInfo>();
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public uint GenerateServerId()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache.Any()
                    ? _serverListCache.Max(s => s["ID"]!.Value<uint>()) + 1
                    : 1;
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _configLock?.Dispose();
            _serverListLock?.Dispose();
        }
    }

    public class FrpListConfig : IDisposable
    {
        private readonly string _frpListPath = Path.Combine(GetAppDataPath(),"DaemonData", "Configs", "FrpList.json");
        private JArray _frpListCache;
        private readonly ReaderWriterLockSlim _frpListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public FrpListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<FrpListConfig>();
            InitializeFile(_frpListPath, "[]");
            _frpListCache = LoadJson<JArray>(_frpListPath);
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
        }

        public JArray ReadFrpList()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return (JArray)_frpListCache.DeepClone();
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public List<JToken> GetFrpList()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.ToList();
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public bool CreateFrpConfig(string name, string server, string configType, string config)
        {
            int id = StringServices.GetRandomNumber(10000000, 99999999);
            _frpListLock.EnterWriteLock();
            try
            {
                if (_frpListCache.Any(s => s["ID"]?.Value<int>() == id))
                    return false;

                string folderPath = Path.Combine(GetAppDataPath(),"DaemonData", "Configs", "Frpc", id.ToString());
                string filePath = Path.Combine(folderPath, $"frpc.{configType}");
                try
                {
                    Directory.CreateDirectory(folderPath);
                    File.WriteAllText(filePath, config);
                }
                catch
                {
                    return false;
                }

                var newItem = new JObject
                {
                    ["ID"] = id,
                    ["Name"] = name,
                    ["ConfigType"] = configType,
                    ["Service"] = server
                };
                _frpListCache.Add(newItem);
                SaveJson(_frpListPath, _frpListCache);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public bool DeleteFrpConfig(int id)
        {
            _frpListLock.EnterWriteLock();
            try
            {
                var target = _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id);
                if (target == null) return false;

                _frpListCache.Remove(target);
                SaveJson(_frpListPath, _frpListCache);
                Directory.Delete(Path.Combine(GetAppDataPath(),"DaemonData", "Configs", "Frpc", id.ToString()), true);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public bool UpdateFrpConfig(int id, string name, string server, string configType)
        {
            _frpListLock.EnterWriteLock();
            try
            {
                var target = _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id);
                if (target == null) return false;

                target["Name"] = name;
                target["Service"] = server;
                target["ConfigType"] = configType;
                SaveJson(_frpListPath, _frpListCache);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public JObject? GetFrpConfig(int id)
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id) as JObject;
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public int GenerateFrpId()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.Any()
                    ? _frpListCache.Max(s => s["ID"]!.Value<int>()) + 1
                    : 1;
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _frpListLock?.Dispose();
        }
    }
    
    public class TaskListConfig : IDisposable
    {
        // 配置文件路径: DaemonData/Configs/TaskList.json
        private readonly string _taskListPath = Path.Combine(GetAppDataPath(), "DaemonData", "Configs", "TaskList.json");
        private JArray _taskListCache;
        
        // 读写锁，保证并发安全
        private readonly ReaderWriterLockSlim _taskListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public TaskListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<TaskListConfig>();
            InitializeFile(_taskListPath, "[]");
            _taskListCache = LoadJson<JArray>(_taskListPath);
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
        }

        /// <summary>
        /// 获取所有任务列表（强类型）
        /// </summary>
        public List<ScheduleTask> GetTaskList()
        {
            _taskListLock.EnterReadLock();
            try
            {
                // 将 JArray 转换为 List<ScheduleTask>
                return [.. _taskListCache.Select(s => s.ToObject<ScheduleTask>()!)];
            }
            finally
            {
                _taskListLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取特定服务器的所有任务
        /// </summary>
        public List<ScheduleTask> GetTasksByInstanceId(uint instanceId)
        {
            _taskListLock.EnterReadLock();
            try
            {
                return _taskListCache
                    .Select(s => s.ToObject<ScheduleTask>()!)
                    .Where(t => t.InstanceId == instanceId)
                    .ToList();
            }
            finally
            {
                _taskListLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 创建新任务
        /// </summary>
        public bool CreateTask(ScheduleTask task)
        {
            _taskListLock.EnterWriteLock();
            try
            {
                // 检查ID是否冲突（虽然GUID极小概率冲突）
                if (_taskListCache.Any(s => s["ID"]?.Value<string>() == task.ID))
                {
                    return false;
                }

                var taskObject = JObject.FromObject(task);
                _taskListCache.Add(taskObject);

                SaveJson(_taskListPath, _taskListCache);
                return true;
            }
            finally
            {
                _taskListLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        public bool DeleteTask(string taskId)
        {
            _taskListLock.EnterWriteLock();
            try
            {
                var target = _taskListCache
                    .FirstOrDefault(s => s["ID"]?.Value<string>() == taskId);

                if (target == null) return false;

                _taskListCache.Remove(target);
                SaveJson(_taskListPath, _taskListCache);
                return true;
            }
            finally
            {
                _taskListLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 更新任务信息
        /// </summary>
        public bool UpdateTask(ScheduleTask updatedTask)
        {
            _taskListLock.EnterWriteLock();
            try
            {
                var target = _taskListCache
                    .FirstOrDefault(s => s["ID"]?.Value<string>() == updatedTask.ID);

                if (target == null) return false;

                target.Replace(JObject.FromObject(updatedTask));
                SaveJson(_taskListPath, _taskListCache);
                return true;
            }
            finally
            {
                _taskListLock.ExitWriteLock();
            }
        }
        
        /// <summary>
        /// 更新任务的最后运行时间（这是一个高频操作，单独拿出来）
        /// </summary>
        public void UpdateLastRunTime(string taskId, DateTime runTime)
        {
            _taskListLock.EnterWriteLock();
            try
            {
                var target = _taskListCache.FirstOrDefault(s => s["ID"]?.Value<string>() == taskId);
                if (target != null)
                {
                    target["LastRunTime"] = runTime;
                    SaveJson(_taskListPath, _taskListCache);
                }
            }
            finally
            {
                _taskListLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 获取单个任务
        /// </summary>
        public ScheduleTask? GetTask(string taskId)
        {
            _taskListLock.EnterReadLock();
            try
            {
                return _taskListCache
                    .FirstOrDefault(s => s["ID"]?.Value<string>() == taskId)
                    ?.ToObject<ScheduleTask>();
            }
            finally
            {
                _taskListLock.ExitReadLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _taskListLock?.Dispose();
        }
    }
    
    public class UserListConfig : IDisposable
    {
        private readonly string _userListPath = Path.Combine(GetAppDataPath(), "DaemonData", "Configs", "UserList.json");
        private JArray _userListCache;
        private readonly ReaderWriterLockSlim _userListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public UserListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<UserListConfig>();
            InitializeFile(_userListPath, "[]");
            _userListCache = LoadJson<JArray>(_userListPath);
            
            // 如果没有用户，创建一个默认管理员
            if (!_userListCache.HasValues)
            {
                string defaultPassword = StringServices.GenerateRandomString(16);
                CreateUser(new UserInfo
                {
                    Username = "mslx",
                    Name = "MSLX 用户",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword),
                    Role = "admin",
                    ApiKey = StringServices.GenerateRandomString(32),
                    Avatar = "https://www.mslmc.cn/logo.png"
                });
                _logger.LogInformation($"已初始化默认管理员用户: mslx / {defaultPassword}");
                _logger.LogInformation($"账号: mslx \n 密码: {defaultPassword}");
            }
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
            if (!File.Exists(path)) File.WriteAllText(path, defaultContent);
        }

        public UserInfo? GetUserByApiKey(string apiKey)
        {
            _userListLock.EnterReadLock();
            try
            {
                return _userListCache
                    .FirstOrDefault(u => u["ApiKey"]?.ToString() == apiKey)
                    ?.ToObject<UserInfo>();
            }
            finally { _userListLock.ExitReadLock(); }
        }

        public UserInfo? GetUserByUsername(string username)
        {
            _userListLock.EnterReadLock();
            try
            {
                return _userListCache
                    .FirstOrDefault(u => u["Username"]?.ToString() == username)
                    ?.ToObject<UserInfo>();
            }
            finally { _userListLock.ExitReadLock(); }
        }

        public bool ValidateUser(string username, string rawPassword)
        {
            _userListLock.EnterReadLock();
            try
            {
                // 先找到用户
                var userToken = _userListCache.FirstOrDefault(u => u["Username"]?.ToString() == username);
            
                if (userToken == null) return false;

                string storedHash = userToken["PasswordHash"]?.ToString() ?? "";

                // 验证
                return BCrypt.Net.BCrypt.Verify(rawPassword, storedHash);
            }
            catch 
            {
                return false;
            }
            finally { _userListLock.ExitReadLock(); }
        }

        public bool CreateUser(UserInfo user)
        {
            _userListLock.EnterWriteLock();
            try
            {
                if (_userListCache.Any(u => u["Username"]?.ToString() == user.Username)) return false;
            
                // 确保存进去的是 Hash 过的
                if (!user.PasswordHash.StartsWith("$2")) 
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
                }

                _userListCache.Add(JObject.FromObject(user));
                SaveJson(_userListPath, _userListCache);
                return true;
            }
            finally { _userListLock.ExitWriteLock(); }
        }
        
        public void UpdateLastLoginTime(string username)
        {
            _userListLock.EnterWriteLock();
            try
            {
                var userToken = _userListCache.FirstOrDefault(u => u["Username"]?.ToString() == username);
                if (userToken != null)
                {
                    // 更新时间
                    userToken["LastLoginTime"] = DateTime.Now;

                    SaveJson(_userListPath, _userListCache);
                }
            }
            finally
            {
                _userListLock.ExitWriteLock();
            }
        }
        
        public UserInfo? GetUserById(string id)
        {
            _userListLock.EnterReadLock();
            try
            {
                return _userListCache
                    .FirstOrDefault(u => u["Id"]?.ToString() == id)
                    ?.ToObject<UserInfo>();
            }
            finally
            {
                _userListLock.ExitReadLock();
            }
        }
        
        public List<UserInfo> GetAllUsers()
        {
            _userListLock.EnterReadLock();
            try
            {
                return _userListCache.Select(u => u.ToObject<UserInfo>()).ToList();
            }
            finally { _userListLock.ExitReadLock(); }
        }
        
        public bool UpdateUser(UserInfo updatedUser)
        {
            _userListLock.EnterWriteLock();
            try
            {
                var target = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == updatedUser.Id);
                if (target == null) return false;

                target.Replace(JObject.FromObject(updatedUser));
                SaveJson(_userListPath, _userListCache);
                return true;
            }
            finally { _userListLock.ExitWriteLock(); }
        }
        
        public bool DeleteUser(string userId)
        {
            _userListLock.EnterWriteLock();
            try
            {
                var target = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
                if (target == null) return false;

                _userListCache.Remove(target);
                SaveJson(_userListPath, _userListCache);
                return true;
            }
            finally { _userListLock.ExitWriteLock(); }
        }
        
        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON");
        }
        private void SaveJson<T>(string path, T data) where T : JToken 
            => File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        public void Dispose() => _userListLock?.Dispose();
    }
}