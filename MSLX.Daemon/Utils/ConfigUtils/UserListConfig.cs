using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils;

public class UserListConfig : IDisposable
{
    private readonly string _userListPath = Path.Combine(IConfigBase.GetAppConfigPath(), "UserList.json");
    private JArray _userListCache;
    private readonly ReaderWriterLockSlim _userListLock = new();
    private readonly ILogger _logger;
    private static bool _hasInitialized = false;

    public UserListConfig()
    {
        _logger = ApplicationLogging.CreateLogger<UserListConfig>();
        InitializeFile(_userListPath, "[]");
        _userListCache = IConfigBase.LoadJson<JArray>(_userListPath);

        // 如果没有用户，创建一个默认管理员
        if (!_userListCache.HasValues)
        {
            var defaultPassword = StringServices.GenerateRandomString(16);
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
            _logger.LogInformation($"账号: mslx \n密码: {defaultPassword}");
            ExportDefaultCredentialsFile("mslx", defaultPassword);

            // 这里打开带初始化信息提示的登录页面
            OpenWebBrowser(true);
        }
        else
        {
            OpenWebBrowser();
        }

        _hasInitialized = true;
    }

    private void OpenWebBrowser(bool isInitialize = false)
    {
        if (_hasInitialized) return;
        var config = IConfigBase.Config.ReadConfig();

        var openOnLaunch = (bool?)config["openWebConsoleOnLaunch"] ?? true;
        var rawHost = (config["listenHost"] ?? "localhost").ToString().Trim();
        var port = config["listenPort"] ?? 1027;

        if (openOnLaunch)
        {
            var isWildcard = rawHost == "*" || rawHost == "0.0.0.0" || rawHost == "[::]" || rawHost == "+";
            var targetHost = isWildcard ? "localhost" : rawHost;
            var suffix = isInitialize ? "/login?initialize=true" : string.Empty;
            var url = $"http://{targetHost}:{port}{suffix}";

            Console.WriteLine($"[WebConsole] 自动检测到监听地址为 {rawHost}，正在通过 {targetHost} 打开控制台...");
            PlatFormServices.OpenBrowser(url);
        }
    }

    private void InitializeFile(string path, string defaultContent)
    {
        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
        if (!File.Exists(path)) File.WriteAllText(path, defaultContent);
    }
    
    private void ExportDefaultCredentialsFile(string username, string password)
    {
        try
        {
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            string slaveKeyStr = "";
            if (isSlaveMode)
            {
                string linkKey = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "";
                slaveKeyStr = $"\n\n【子节点连接秘钥信息】\n节点秘钥: {linkKey}\n(主控节点在链接此子节点时，需输入该秘钥进行通讯校验)";
            }

            var txtPath = Path.Combine(IConfigBase.GetAppDataPath(), "默认账户信息.txt");
            var content = $@"==================================================
MSLX-Daemon 初始化成功 - 默认管理员凭据
==================================================
生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

【安全提示】
请务必在首次登录控制台后，前往用户管理修改默认的用户名、密码！
以防止您的守护进程面板被恶意扫描并控制。

【账户信息】
登录账号: {username}
默认密码: {password}{slaveKeyStr}

【控制台地址】
如果浏览器未自动打开，请手动访问配置的端口（默认 http://localhost:1027）
==================================================";

            File.WriteAllText(txtPath, content, System.Text.Encoding.UTF8);
            _logger.LogInformation($"已将初始账户密码导出至: {txtPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"导出默认账户密码文件失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 更新从主节点同步的影子用户信息（仅用于子节点鉴权）
    /// </summary>
    public void UpdateShadowUser(string userId, string role, JArray resources)
    {
        _userListLock.EnterWriteLock();
        try
        {
            var userToken = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (userToken != null)
            {
                userToken["Role"] = role;
                userToken["Resources"] = resources;
            }
            else
            {
                var shadowUser = new JObject
                {
                    ["Id"] = userId,
                    ["Role"] = role,
                    ["Resources"] = resources,
                    ["Username"] = $"shadow_{userId}",
                    ["Name"] = "Shadow User"
                };
                _userListCache.Add(shadowUser);
            }
            // 立即保存到磁盘，以便其他组件读取
            IConfigBase.SaveJson(_userListPath, _userListCache);
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
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
        finally
        {
            _userListLock.ExitReadLock();
        }
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
        finally
        {
            _userListLock.ExitReadLock();
        }
    }

    public UserInfo? GetUserByOpenId(string openId)
    {
        _userListLock.EnterReadLock();
        try
        {
            return _userListCache
                .FirstOrDefault(u => u["OAuthMSLOpenID"]?.ToString() == openId)
                ?.ToObject<UserInfo>();
        }
        finally
        {
            _userListLock.ExitReadLock();
        }
    }

    public bool BindUserOpenId(string userId, string openId)
    {
        _userListLock.EnterWriteLock();
        try
        {
            // 检查用户是否存在
            var targetUser = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (targetUser == null) return false;

            // 检查该 OpenID 是否已经被其用户绑定了
            var existingBind = _userListCache.FirstOrDefault(u => u["OAuthMSLOpenID"]?.ToString() == openId);
            if (existingBind != null && existingBind["Id"]?.ToString() != userId) return false;

            targetUser["OAuthMSLOpenID"] = openId;
            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }

    public bool UnbindUserOpenId(string userId)
    {
        _userListLock.EnterWriteLock();
        try
        {
            var targetUser = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (targetUser == null) return false;

            targetUser["OAuthMSLOpenID"] = null;

            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }


    public bool ValidateUser(string username, string rawPassword)
    {
        _userListLock.EnterReadLock();
        try
        {
            // 先找到用户
            var userToken = _userListCache.FirstOrDefault(u => u["Username"]?.ToString() == username);

            if (userToken == null) return false;

            var storedHash = userToken["PasswordHash"]?.ToString() ?? "";

            // 验证
            return BCrypt.Net.BCrypt.Verify(rawPassword, storedHash);
        }
        catch
        {
            return false;
        }
        finally
        {
            _userListLock.ExitReadLock();
        }
    }

    public bool CreateUser(UserInfo user)
    {
        _userListLock.EnterWriteLock();
        try
        {
            if (_userListCache.Any(u => u["Username"]?.ToString() == user.Username)) return false;

            // 确保存进去的是 Hash 过的
            if (!user.PasswordHash.StartsWith("$2"))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            _userListCache.Add(JObject.FromObject(user));
            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
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

                IConfigBase.SaveJson(_userListPath, _userListCache);
            }
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }

    public UserInfo? GetUserById(string id)
    {
        if (id == "system-admin")
        {
            return new UserInfo
            {
                Id = "system-admin",
                Username = "MSLX Manager",
                Name = "MSLX Manager",
                Role = "admin",
                Resources = new List<string>() 
            };
        }

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

    public List<UserInfo?> GetAllUsers()
    {
        _userListLock.EnterReadLock();
        try
        {
            return _userListCache.Select(u => u.ToObject<UserInfo>()).ToList();
        }
        finally
        {
            _userListLock.ExitReadLock();
        }
    }

    public bool UpdateUser(UserInfo updatedUser)
    {
        _userListLock.EnterWriteLock();
        try
        {
            var target = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == updatedUser.Id);
            if (target == null) return false;

            target.Replace(JObject.FromObject(updatedUser));
            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }

    public bool DeleteUser(string userId)
    {
        _userListLock.EnterWriteLock();
        try
        {
            var target = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (target == null) return false;

            _userListCache.Remove(target);
            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _userListLock?.Dispose();
    }

    /// <summary>
    /// 全量覆盖用户的资源列表
    /// </summary>
    public bool UpdateUserResources(string userId, List<string> newResources)
    {
        _userListLock.EnterWriteLock();
        try
        {
            var target = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (target == null) return false;

            target["Resources"] = JArray.FromObject(newResources ?? new List<string>());
            IConfigBase.SaveJson(_userListPath, _userListCache);
            return true;
        }
        finally
        {
            _userListLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 验证用户是否拥有某项资源权限
    /// </summary>
    public bool HasResourcePermission(string userId,string type, int id)
    {
        if(userId == "system-admin") return true; // 全局APIKey
        _userListLock.EnterReadLock();
        try
        {
            string resource = $"{type}:{id}";
            var userToken = _userListCache.FirstOrDefault(u => u["Id"]?.ToString() == userId);
            if (userToken == null) return false;

            // 管理员默认放行
            if (userToken["Role"]?.ToString() == "admin") return true;

            var resourcesArray = userToken["Resources"] as JArray;
            if (resourcesArray == null) return false;

            return resourcesArray.Any(r => r.ToString() == resource);
        }
        finally
        {
            _userListLock.ExitReadLock();
        }
    }
}