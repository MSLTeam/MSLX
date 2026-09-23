using Cronos;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Instance;

namespace MSLX.Daemon.Services
{
    public class TaskSchedulerService : BackgroundService
    {
        private readonly ILogger<TaskSchedulerService> _logger;
        private readonly IInstanceLifecycleService _lifecycleService;
        private readonly IInstanceConsoleService _consoleService;
        private readonly IInstanceBackupService _backupService;
        private readonly IMSLXEvents _events;
        private DateTime _lastCheckDateUtc = DateTime.MinValue.Date;

        public TaskSchedulerService(
            ILogger<TaskSchedulerService> logger,
            IInstanceLifecycleService lifecycleService,
            IInstanceConsoleService consoleService,
            IInstanceBackupService backupService,
            IMSLXEvents events)
        {
            _logger = logger;
            _lifecycleService = lifecycleService;
            _consoleService = consoleService;
            _backupService = backupService;
            _events = events;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[MSLX-Scheduler] 定时任务调度器已启动");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessTasksAsync();
                    }
                    catch (Exception ex)
                    {
                        // 捕获业务逻辑异常，保证调度器不挂
                        _logger.LogError(ex, "调度器循环发生异常");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }

            _logger.LogInformation(">>> [MSLX-Scheduler] 定时任务调度器已停止");
        }

        private async Task ProcessTasksAsync()
        {
            // 使用 UTC 时间作为系统当前时间的基准
            var nowUtc = DateTime.UtcNow;
            
            var tasks = IConfigBase.TaskList.GetTaskList().Where(t => t.Enable).ToList();

            foreach (var task in tasks)
            {
                if (ShouldRun(task, nowUtc))
                {
                    // 更新最后运行时间
                    IConfigBase.TaskList.UpdateLastRunTime(task.ID, nowUtc);

                    // 丢到线程池执行
                    _ = Task.Run(async () => await ExecuteTaskLogic(task));
                }
            }

            // MSLX定时任务（按天）
            if (_lastCheckDateUtc != nowUtc.Date)
            {
                _lastCheckDateUtc = nowUtc.Date;
                _ = Task.Run(() =>
                {
                    _logger.LogInformation("[MSLX-Scheduler] 正在执行每日实例过期性安全巡检...");

                    var serverList = IConfigBase.ServerList.GetServerList();

                    foreach (var server in serverList)
                    {
                        uint instanceId = (uint)server.ID;

                        if (server.ExpireTime.HasValue && server.ExpireTime.Value <= nowUtc)
                        {
                            if (_lifecycleService.IsServerRunning(instanceId))
                            {
                                _logger.LogWarning($"[MSLX-Guard] 检测到运行中的实例 [{server.Name} (ID: {instanceId})] 已过期，正在执行下线...");
                                _consoleService.SendCommand(instanceId, "say [MSLX] 该服务器租约已到期，系统即将关闭此实例。");
                                _lifecycleService.StopServer(instanceId);
                            }
                        }
                    }
                });
            }


        }

        private bool ShouldRun(ScheduleTask task, DateTime nowUtc)
        {
            try
            {
                // 解析 Cron 表达式 支持秒级
                CronExpression expression = CronExpression.Parse(task.Cron, CronFormat.IncludeSeconds);

                // 处理 LastRunTime 的时区问题，确保它是 UTC
                DateTime fromTimeUtc = task.LastRunTime.HasValue 
                    ? task.LastRunTime.Value.ToUniversalTime() 
                    : nowUtc.AddMinutes(-1);

                // 获取下一次运行时间
                DateTime? nextRunUtc = expression.GetNextOccurrence(fromTimeUtc, TimeZoneInfo.Local);

                if (nextRunUtc.HasValue)
                {
                    // 判断是否到了运行时间 (全部用 UTC 比较)
                    if (nextRunUtc.Value <= nowUtc && nextRunUtc.Value > fromTimeUtc)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"任务 [{task.Name}] Cron 表达式解析失败: {ex.Message}");
            }
            return false;
        }

        private async Task ExecuteTaskLogic(ScheduleTask task)
        {
            _logger.LogInformation($"正在执行定时任务: [{task.Name}] ({task.Type}) -> Instance: {task.InstanceId}");

            var executingArgs = new TaskExecutingEventArgs
            {
                TaskId = task.ID,
                TaskName = task.Name,
                TaskType = task.Type,
                InstanceId = task.InstanceId,
                Timestamp = DateTime.Now
            };
            _events.PublishTaskExecuting(executingArgs);
            if (executingArgs.Cancel)
            {
                _logger.LogInformation($"[MSLX-Scheduler] 任务 [{task.Name}] 已被插件拦截取消执行。");
                return;
            }

            try
            {
                switch (task.Type.ToLower())
                {
                    case "command":
                        _consoleService.SendCommand(task.InstanceId, task.Payload);
                        break;

                    case "start":
                        _lifecycleService.StartServer(task.InstanceId);
                        break;

                    case "stop":
                        _lifecycleService.StopServer(task.InstanceId);
                        break;

                    case "restart":
                        await HandleRestartAsync(task.InstanceId, task.Payload);
                        break;
                    
                    case "backup":
                        _backupService.StartBackupServer(task.InstanceId);
                        break;
                    
                    case "shell":
                        if (!task.RunWhenOffline && !_lifecycleService.IsServerRunning(task.InstanceId))
                        {
                            _logger.LogInformation($"任务 [{task.Name}] 取消执行：实例 {task.InstanceId} 处于离线状态，且配置为未运行时不执行。");
                            break;
                        }
                        
                        var serverInfo = IConfigBase.ServerList.GetServer(task.InstanceId);
                        if (serverInfo != null && !string.IsNullOrWhiteSpace(serverInfo.Base))
                        {
                            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                            var startInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                                Arguments = isWindows ? $"/c \"{task.Payload}\"" : $"-c \"{task.Payload.Replace("\"", "\\\"")}\"",
                                WorkingDirectory = serverInfo.Base,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            
                            using var process = System.Diagnostics.Process.Start(startInfo);
                            _logger.LogInformation($"任务 [{task.Name}] 执行指令于: {serverInfo.Base}");
                        }
                        else
                        {
                            _logger.LogWarning($"任务 [{task.Name}] 执行失败: 实例 ID {task.InstanceId} 不存在或工作路径为空");
                        }
                        break;
                    
                    default:
                        _logger.LogWarning($"未知的任务类型: {task.Type}");
                        break;
                }

                _events.PublishTaskExecuted(new TaskExecutedEventArgs
                {
                    TaskId = task.ID,
                    TaskName = task.Name,
                    TaskType = task.Type,
                    InstanceId = task.InstanceId,
                    Success = true,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"任务 [{task.Name}] 执行失败");
                _events.PublishTaskExecuted(new TaskExecutedEventArgs
                {
                    TaskId = task.ID,
                    TaskName = task.Name,
                    TaskType = task.Type,
                    InstanceId = task.InstanceId,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.Now
                });
            }
        }

        private async Task HandleRestartAsync(uint instanceId, string payload)
        {
            // 如果服务器没开，直接开
            if (!_lifecycleService.IsServerRunning(instanceId))
            {
                _lifecycleService.StartServer(instanceId);
                return;
            }

            // 发送倒计时/提示信息
            if (!string.IsNullOrWhiteSpace(payload))
            {
                _consoleService.SendCommand(instanceId, $"say [计划任务] {payload}");
            }
            _consoleService.SendCommand(instanceId, "say 服务器即将执行计划重启...");

            // 执行停止
            bool stopped = _lifecycleService.StopServer(instanceId);
            if (!stopped) return;

            // 等待进程完全退出
            int retry = 0;
            while (_lifecycleService.IsServerRunning(instanceId) && retry < 30)
            {
                await Task.Delay(1000);
                retry++;
            }

            // 额外缓冲 2s
            await Task.Delay(2000);

            // 启动
            _lifecycleService.StartServer(instanceId);
            _logger.LogInformation($"任务触发的重启已完成: Instance {instanceId}");
        }
    }
}