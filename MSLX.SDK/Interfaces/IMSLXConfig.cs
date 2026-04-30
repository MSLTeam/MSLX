using Newtonsoft.Json.Linq;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance; 

namespace MSLX.SDK.Interfaces;


public interface IMSLXConfig
{
    string GetAppDataPath();
    string GetAppConfigPath();
    T LoadJson<T>(string path) where T : JToken;
    void SaveJson<T>(string path, T data) where T : JToken;

    /// <summary>主配置 (Config.json) </summary>
    IMainConfigBridge Main { get; }

    /// <summary>服务器列表 (ServerList.json) </summary>
    IServerConfigBridge Servers { get; }

    /// <summary>Frp 隧道列表 (FrpList.json) </summary>
    IFrpConfigBridge Frp { get; }

    /// <summary>计划任务列表 (TaskList.json) </summary>
    ITaskConfigBridge Tasks { get; }

    /// <summary>用户列表 (UserList.json) </summary>
    IUserConfigBridge Users { get; }
}

public interface IMainConfigBridge
{
    JObject ReadConfig();
    JToken? ReadConfigKey(string key);
    void WriteConfig(JObject content);
    void WriteConfigKey(string key, JToken value);
}

public interface IServerConfigBridge
{
    JArray ReadServerList();
    List<McServerInfo.ServerInfo> GetServerList();
    void WriteServerList(JArray content);
    bool CreateServer(McServerInfo.ServerInfo server);
    bool DeleteServer(uint serverId, bool deleteFiles = false);
    bool UpdateServer(McServerInfo.ServerInfo updatedServer);
    McServerInfo.ServerInfo? GetServer(uint serverId);
    uint GenerateServerId();
}

public interface IFrpConfigBridge
{
    JArray ReadFrpList();
    List<JToken> GetFrpList();
    bool CreateFrpConfig(string name, string server, string configType, string config);
    bool DeleteFrpConfig(int id);
    bool UpdateFrpConfig(int id, string name, string server, string configType);
    JObject? GetFrpConfig(int id);
    bool IsFrpIdValid(int id);
    int GenerateFrpId();
}

public interface ITaskConfigBridge
{
    List<ScheduleTask> GetTaskList();
    List<ScheduleTask> GetTasksByInstanceId(uint instanceId);
    bool CreateTask(ScheduleTask task);
    bool DeleteTask(string taskId);
    bool UpdateTask(ScheduleTask updatedTask);
    void UpdateLastRunTime(string taskId, DateTime runTime);
    ScheduleTask? GetTask(string taskId);
}

public interface IUserConfigBridge
{
    UserInfo? GetUserByApiKey(string apiKey);
    UserInfo? GetUserByUsername(string username);
    UserInfo? GetUserByOpenId(string openId);
    bool BindUserOpenId(string userId, string openId);
    bool UnbindUserOpenId(string userId);
    bool ValidateUser(string username, string rawPassword);
    bool CreateUser(UserInfo user);
    void UpdateLastLoginTime(string username);
    UserInfo? GetUserById(string id);
    List<UserInfo?> GetAllUsers();
    bool UpdateUser(UserInfo updatedUser);
    bool DeleteUser(string userId);
    bool UpdateUserResources(string userId, List<string> newResources);
    bool HasResourcePermission(string userId, string type, int id);
}