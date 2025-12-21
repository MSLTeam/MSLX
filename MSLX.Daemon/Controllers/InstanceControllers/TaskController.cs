using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.InstanceControllers;
    [ApiController]
    [Route("api/instance/tasks")]
    public class TaskController : ControllerBase
    {
        private readonly ILogger<TaskController> _logger;

        public TaskController(ILogger<TaskController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 获取所有定时任务
        /// </summary>
        [HttpGet("list")]
        public IActionResult GetAllTasks()
        {
            try
            {
                var tasks = IConfigBase.TaskList.GetTaskList();
                return Ok(new ApiResponse<List<ScheduleTask>>
                {
                    Code = 200,
                    Message = "获取成功",
                    Data = tasks
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = e.Message
                });
            }
        }

        /// <summary>
        /// 获取指定服务器实例的任务
        /// </summary>
        [HttpGet("list/{instanceId}")]
        public IActionResult GetTasksByInstance(uint instanceId)
        {
            try
            {
                var tasks = IConfigBase.TaskList.GetTasksByInstanceId(instanceId);
                return Ok(new ApiResponse<List<ScheduleTask>>
                {
                    Code = 200,
                    Message = "获取成功",
                    Data = tasks
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = e.Message
                });
            }
        }

        /// <summary>
        /// 创建新任务
        /// </summary>
        [HttpPost("create")]
        public IActionResult CreateTask([FromBody] CreateTaskRequest request)
        {
            try
            {
                var newTask = new ScheduleTask
                {
                    InstanceId = request.InstanceId,
                    Name = request.Name,
                    Cron = request.Cron,
                    Type = request.Type,
                    Payload = request.Payload,
                    Enable = request.Enable,
                    LastRunTime = null
                };

                // 保存
                bool success = IConfigBase.TaskList.CreateTask(newTask);
                if (!success)
                {
                    throw new Exception("创建任务失败，可能是 ID 冲突");
                }

                return Ok(new ApiResponse<string>
                {
                    Code = 200,
                    Message = "任务创建成功",
                    Data = newTask.ID
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = e.Message
                });
            }
        }

        /// <summary>
        /// 更新任务
        /// </summary>
        [HttpPost("update")]
        public IActionResult UpdateTask([FromBody] UpdateTaskRequest request)
        {
            try
            {
                // 检查任务是否存在
                var existingTask = IConfigBase.TaskList.GetTask(request.ID);
                if (existingTask == null)
                {
                    throw new Exception("未找到指定的任务");
                }


                // 更新字段
                existingTask.Name = request.Name;
                existingTask.InstanceId = request.InstanceId;
                existingTask.Cron = request.Cron;
                existingTask.Type = request.Type;
                existingTask.Payload = request.Payload;
                existingTask.Enable = request.Enable;

                // 保存
                bool success = IConfigBase.TaskList.UpdateTask(existingTask);
                if (!success)
                {
                    throw new Exception("更新任务失败");
                }

                return Ok(new ApiResponse<object>
                {
                    Code = 200,
                    Message = "任务更新成功"
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = e.Message
                });
            }
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        [HttpPost("delete/{id}")]
        public IActionResult DeleteTask(string id)
        {
            try
            {
                bool success = IConfigBase.TaskList.DeleteTask(id);
                if (!success)
                {
                    throw new Exception("删除失败，未找到指定 ID 的任务");
                }

                return Ok(new ApiResponse<object>
                {
                    Code = 200,
                    Message = "任务已删除"
                });
            }
            catch (Exception e)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = e.Message
                });
            }
        }
    }