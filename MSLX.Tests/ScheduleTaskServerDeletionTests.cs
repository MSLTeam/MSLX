using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;
using Newtonsoft.Json.Linq;

namespace MSLX.Tests;

/// <summary>
/// 验证删除服务器时联动删除定时任务（Issue #271）。
/// </summary>
[Collection("GlobalConfig")]
public class ScheduleTaskServerDeletionTests : IDisposable
{
    private readonly string _serverListFile =
        Path.Combine(IConfigBase.GetAppConfigPath(), "ServerList.json");
    private readonly string _taskListFile =
        Path.Combine(IConfigBase.GetAppConfigPath(), "TaskList.json");

    private ServerListConfig? _oldServerList;
    private TaskListConfig? _oldTaskList;
    private string? _originalServerListJson;
    private string? _originalTaskListJson;

    public ScheduleTaskServerDeletionTests()
    {
        _oldServerList = IConfigBase.ServerList;
        _oldTaskList = IConfigBase.TaskList;

        _originalServerListJson = File.Exists(_serverListFile) ? File.ReadAllText(_serverListFile) : null;
        _originalTaskListJson = File.Exists(_taskListFile) ? File.ReadAllText(_taskListFile) : null;

        // 初始化独立的配置实例
        File.WriteAllText(_serverListFile, "[]");
        File.WriteAllText(_taskListFile, "[]");

        IConfigBase.ServerList = new ServerListConfig();
        IConfigBase.TaskList = new TaskListConfig();
    }

    [Fact]
    public void DeleteTasksByInstanceId_RemovesOnlyMatchingTasks()
    {
        var taskList = IConfigBase.TaskList;

        var task1 = new ScheduleTask { ID = "task-1", InstanceId = 1, Name = "Task 1", Type = "command", Cron = "0 * * * *" };
        var task2 = new ScheduleTask { ID = "task-2", InstanceId = 1, Name = "Task 2", Type = "command", Cron = "0 * * * *" };
        var task3 = new ScheduleTask { ID = "task-3", InstanceId = 2, Name = "Task 3", Type = "command", Cron = "0 * * * *" };

        taskList.CreateTask(task1);
        taskList.CreateTask(task2);
        taskList.CreateTask(task3);

        Assert.Equal(3, taskList.GetTaskList().Count);

        // 删除 InstanceId 为 1 的任务
        taskList.DeleteTasksByInstanceId(1);

        var remainingTasks = taskList.GetTaskList();
        Assert.Single(remainingTasks);
        Assert.Equal("task-3", remainingTasks[0].ID);
        Assert.Equal(2u, remainingTasks[0].InstanceId);
    }

    [Fact]
    public void DeleteServer_CascadesToDeleteScheduledTasks()
    {
        var serverList = IConfigBase.ServerList;
        var taskList = IConfigBase.TaskList;

        var server = new McServerInfo.ServerInfo
        {
            ID = 1,
            Name = "Server A",
            Core = "Paper",
            Base = "/tmp/fake-server-a",
            Java = "java"
        };
        serverList.CreateServer(server);

        var taskA = new ScheduleTask { ID = "task-a", InstanceId = 1, Name = "Server A Task", Type = "command", Cron = "0 * * * *" };
        taskList.CreateTask(taskA);

        Assert.NotNull(serverList.GetServer(1));
        Assert.Single(taskList.GetTasksByInstanceId(1));

        // 删除服务器 1
        bool deleted = serverList.DeleteServer(1, deleteFiles: false);
        Assert.True(deleted);

        // 验证服务器已被删除
        Assert.Null(serverList.GetServer(1));

        // 验证对应的定时任务已被联动删除
        Assert.Empty(taskList.GetTasksByInstanceId(1));
        Assert.Empty(taskList.GetTaskList());
    }

    public void Dispose()
    {
        IConfigBase.ServerList?.Dispose();
        IConfigBase.TaskList?.Dispose();

        IConfigBase.ServerList = _oldServerList!;
        IConfigBase.TaskList = _oldTaskList!;

        if (_originalServerListJson != null)
            File.WriteAllText(_serverListFile, _originalServerListJson);
        else if (File.Exists(_serverListFile))
            File.Delete(_serverListFile);

        if (_originalTaskListJson != null)
            File.WriteAllText(_taskListFile, _originalTaskListJson);
        else if (File.Exists(_taskListFile))
            File.Delete(_taskListFile);
    }
}
