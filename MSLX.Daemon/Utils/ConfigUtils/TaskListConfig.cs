using MSLX.Daemon.Models.Instance;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class TaskListConfig : IDisposable
    {
        // 配置文件路径: DaemonData/Configs/TaskList.json
        private readonly string _taskListPath = Path.Combine(IConfigBase.GetAppConfigPath(), "TaskList.json");
        private JArray _taskListCache;

        // 读写锁，保证并发安全
        private readonly ReaderWriterLockSlim _taskListLock = new ReaderWriterLockSlim();
        // private readonly ILogger _logger;

        public TaskListConfig()
        {
            // _logger = ApplicationLogging.CreateLogger<TaskListConfig>();
            InitializeFile(_taskListPath, "[]");
            _taskListCache = IConfigBase.LoadJson<JArray>(_taskListPath);
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

                IConfigBase.SaveJson(_taskListPath, _taskListCache);
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
                IConfigBase.SaveJson(_taskListPath, _taskListCache);
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
                IConfigBase.SaveJson(_taskListPath, _taskListCache);
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
                    IConfigBase.SaveJson(_taskListPath, _taskListCache);
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

        public void Dispose()
        {
            _taskListLock?.Dispose();
        }
    }
}
