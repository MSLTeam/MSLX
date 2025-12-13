using System.ComponentModel.DataAnnotations;
using Cronos;

namespace MSLX.Daemon.Models.Instance;

public class ScheduleTask
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N"); // 唯一ID
    public uint InstanceId { get; set; }     // 关联的服务器ID
    public string Name { get; set; } = "";   // 任务名称
    public string Type { get; set; } = "Command"; // 类型
    public string Cron { get; set; } = "";   // Cron 表达式
    public string Payload { get; set; } = "";// 负载
    public bool Enable { get; set; } = true; // 是否启用
    public DateTime? LastRunTime { get; set; } // 最后一次运行时间
}

public class CreateTaskRequest
{
    [Required]
    public uint InstanceId { get; set; }
        
    [Required(ErrorMessage = "任务名称不能为空")]
    public string Name { get; set; } = "";
        
    [Required(ErrorMessage = "Cron 表达式不能为空")]
    [CronExpression(ErrorMessage = "无效的 Cron 表达式，请检查格式")]
    public string Cron { get; set; } = ""; 
    
    [Required]
    [AllowedValues("command", "start", "stop", "restart", ErrorMessage = "不支持的任务类型，仅支持: command, start, stop, restart")]
    public string Type { get; set; } = "Command"; 
        
    public string Payload { get; set; } = "";
        
    public bool Enable { get; set; } = true;
    
    public class CronExpressionAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var str = value as string;
            
            // 如果是空的，交给 [Required] 去处理，这里只验证格式
            if (string.IsNullOrEmpty(str))
            {
                return ValidationResult.Success; 
            }

            try
            {
                // 尝试用 Cronos 解析（支持秒级）
                CronExpression.Parse(str, CronFormat.IncludeSeconds);
                return ValidationResult.Success;
            }
            catch (Exception)
            {
                return new ValidationResult(ErrorMessage ?? "Cron 表达式格式无效 (示例: '0 0 12 * * ?')");
            }
        }
    }
}

public class UpdateTaskRequest : CreateTaskRequest
{
    [Required(ErrorMessage = "任务 ID 不能为空")]
    public string ID { get; set; } = "";
}