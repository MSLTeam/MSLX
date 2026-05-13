using MSLX.SDK.Interfaces;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;

namespace MSLX.Daemon.Adapters;

/// <summary>
/// 将 SDK 接口调用转发至 IConfigBase
/// </summary>
public class DaemonConfigProvider : IMSLXConfig
{
    public string GetAppDataPath() => IConfigBase.GetAppDataPath();
    public string GetAppConfigPath() => IConfigBase.GetAppConfigPath();
    public T LoadJson<T>(string path) where T : JToken => IConfigBase.LoadJson<T>(path);
    public void SaveJson<T>(string path, T data) where T : JToken => IConfigBase.SaveJson(path, data);

    public IMainConfigBridge Main { get; } = new MainConfigBridge();
    public IServerConfigBridge Servers { get; } = new ServerConfigBridge();
    public IFrpConfigBridge Frp { get; } = new FrpConfigBridge();
    public ITaskConfigBridge Tasks { get; } = new TaskConfigBridge();
    public IUserConfigBridge Users { get; } = new UserConfigBridge();
    
    public IPluginConfigBridge GetPluginConfig(string pluginId) 
    {
        return new PluginConfigBridge(pluginId);
    }
}


public class PluginConfigBridge : IPluginConfigBridge
{
    private readonly string _pluginId;
    public PluginConfigBridge(string pluginId) => _pluginId = pluginId;
    
    private PluginConfigService Service => IConfigBase.GetPluginConfigService(_pluginId);

    public string GetDataPath() => Service.GetDataPath();
    public JObject ReadConfig() => Service.ReadConfig();
    public JToken? ReadConfigKey(string key) => Service.ReadConfigKey(key);
    public void WriteConfig(JObject content) => Service.WriteConfig(content);
    public void WriteConfigKey(string key, JToken value) => Service.WriteConfigKey(key, value);
}

public class MainConfigBridge : IMainConfigBridge
{
    public JObject ReadConfig() => IConfigBase.Config.ReadConfig();
    public JToken? ReadConfigKey(string key) => IConfigBase.Config.ReadConfigKey(key);
    public void WriteConfig(JObject content) => IConfigBase.Config.WriteConfig(content);
    public void WriteConfigKey(string key, JToken value) => IConfigBase.Config.WriteConfigKey(key, value);
}

public class ServerConfigBridge : IServerConfigBridge
{
    public JArray ReadServerList() => IConfigBase.ServerList.ReadServerList();
    public List<McServerInfo.ServerInfo> GetServerList() => IConfigBase.ServerList.GetServerList();
    public void WriteServerList(JArray content) => IConfigBase.ServerList.WriteServerList(content);
    public bool CreateServer(McServerInfo.ServerInfo server) => IConfigBase.ServerList.CreateServer(server);
    public bool DeleteServer(uint serverId, bool deleteFiles = false) => IConfigBase.ServerList.DeleteServer(serverId, deleteFiles);
    public bool UpdateServer(McServerInfo.ServerInfo updatedServer) => IConfigBase.ServerList.UpdateServer(updatedServer);
    public McServerInfo.ServerInfo? GetServer(uint serverId) => IConfigBase.ServerList.GetServer(serverId);
    public uint GenerateServerId() => IConfigBase.ServerList.GenerateServerId();
}

public class FrpConfigBridge : IFrpConfigBridge
{
    public JArray ReadFrpList() => IConfigBase.FrpList.ReadFrpList();
    public List<JToken> GetFrpList() => IConfigBase.FrpList.GetFrpList();
    public bool CreateFrpConfig(string name, string server, string configType, string config) => IConfigBase.FrpList.CreateFrpConfig(name, server, configType, config);
    public bool DeleteFrpConfig(int id) => IConfigBase.FrpList.DeleteFrpConfig(id);
    public bool UpdateFrpConfig(int id, string name, string server, string configType) => IConfigBase.FrpList.UpdateFrpConfig(id, name, server, configType);
    public JObject? GetFrpConfig(int id) => IConfigBase.FrpList.GetFrpConfig(id);
    public bool IsFrpIdValid(int id) => IConfigBase.FrpList.IsFrpIdValid(id);
    public int GenerateFrpId() => IConfigBase.FrpList.GenerateFrpId();
}

public class TaskConfigBridge : ITaskConfigBridge
{
    public List<ScheduleTask> GetTaskList() => IConfigBase.TaskList.GetTaskList();
    public List<ScheduleTask> GetTasksByInstanceId(uint instanceId) => IConfigBase.TaskList.GetTasksByInstanceId(instanceId);
    public bool CreateTask(ScheduleTask task) => IConfigBase.TaskList.CreateTask(task);
    public bool DeleteTask(string taskId) => IConfigBase.TaskList.DeleteTask(taskId);
    public bool UpdateTask(ScheduleTask updatedTask) => IConfigBase.TaskList.UpdateTask(updatedTask);
    public void UpdateLastRunTime(string taskId, DateTime runTime) => IConfigBase.TaskList.UpdateLastRunTime(taskId, runTime);
    public ScheduleTask? GetTask(string taskId) => IConfigBase.TaskList.GetTask(taskId);
}

public class UserConfigBridge : IUserConfigBridge
{
    public UserInfo? GetUserByApiKey(string apiKey) => IConfigBase.UserList.GetUserByApiKey(apiKey);
    public UserInfo? GetUserByUsername(string username) => IConfigBase.UserList.GetUserByUsername(username);
    public UserInfo? GetUserByOpenId(string openId) => IConfigBase.UserList.GetUserByOpenId(openId);
    public bool BindUserOpenId(string userId, string openId) => IConfigBase.UserList.BindUserOpenId(userId, openId);
    public bool UnbindUserOpenId(string userId) => IConfigBase.UserList.UnbindUserOpenId(userId);
    public bool ValidateUser(string username, string rawPassword) => IConfigBase.UserList.ValidateUser(username, rawPassword);
    public bool CreateUser(UserInfo user) => IConfigBase.UserList.CreateUser(user);
    public void UpdateLastLoginTime(string username) => IConfigBase.UserList.UpdateLastLoginTime(username);
    public UserInfo? GetUserById(string id) => IConfigBase.UserList.GetUserById(id);
    public List<UserInfo?> GetAllUsers() => IConfigBase.UserList.GetAllUsers();
    public bool UpdateUser(UserInfo updatedUser) => IConfigBase.UserList.UpdateUser(updatedUser);
    public bool DeleteUser(string userId) => IConfigBase.UserList.DeleteUser(userId);
    public bool UpdateUserResources(string userId, List<string> newResources) => IConfigBase.UserList.UpdateUserResources(userId, newResources);
    public bool HasResourcePermission(string userId, string type, int id) => IConfigBase.UserList.HasResourcePermission(userId, type, id);
}