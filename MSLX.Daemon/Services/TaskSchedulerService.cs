using Cronos;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Services
{
    public class TaskSchedulerService : BackgroundService
    {
        private readonly ILogger<TaskSchedulerService> _logger;
        private readonly MCServerService _mcService;

        public TaskSchedulerService(
            ILogger<TaskSchedulerService> logger,
            MCServerService mcService)
        {
            _logger = logger;
            _mcService = mcService;
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

            try
            {
                switch (task.Type.ToLower())
                {
                    case "command":
                        _mcService.SendCommand(task.InstanceId, task.Payload);
                        break;

                    case "start":
                        _mcService.StartServer(task.InstanceId);
                        break;

                    case "stop":
                        _mcService.StopServer(task.InstanceId);
                        break;

                    case "restart":
                        await HandleRestartAsync(task.InstanceId, task.Payload);
                        break;
                    
                    case "backup":
                        _mcService.StartBackupServer(task.InstanceId);
                        break;
                    
                    default:
                        _logger.LogWarning($"未知的任务类型: {task.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"任务 [{task.Name}] 执行失败");
            }
        }

        private async Task HandleRestartAsync(uint instanceId, string payload)
        {
            // 如果服务器没开，直接开
            if (!_mcService.IsServerRunning(instanceId))
            {
                _mcService.StartServer(instanceId);
                return;
            }

            // 发送倒计时/提示信息
            if (!string.IsNullOrWhiteSpace(payload))
            {
                _mcService.SendCommand(instanceId, $"say [计划任务] {payload}");
            }
            _mcService.SendCommand(instanceId, "say 服务器即将执行计划重启...");

            // 执行停止
            bool stopped = _mcService.StopServer(instanceId);
            if (!stopped) return;

            // 等待进程完全退出
            int retry = 0;
            while (_mcService.IsServerRunning(instanceId) && retry < 30)
            {
                await Task.Delay(1000);
                retry++;
            }

            // 额外缓冲 2s
            await Task.Delay(2000);

            // 启动
            _mcService.StartServer(instanceId);
            _logger.LogInformation($"任务触发的重启已完成: Instance {instanceId}");
        }
    }
}