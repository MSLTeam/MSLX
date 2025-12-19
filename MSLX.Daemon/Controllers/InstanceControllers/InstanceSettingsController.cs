using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Utils.BackgroundTasks;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/settings")]
[ApiController]
public class InstanceSettingsController : ControllerBase
{
    private readonly MCServerService _mcServerService;
    private readonly IBackgroundTaskQueue<UpdateServerTask> _updateQueue;

    public InstanceSettingsController(MCServerService mcServerService,
        IBackgroundTaskQueue<UpdateServerTask> updateQueue)
    {
        _mcServerService = mcServerService;
        _updateQueue = updateQueue;
    }

    [HttpGet("general/{id}")]
    public IActionResult GetGeneralSettings(uint id)
    {
        try
        {
            McServerInfo.ServerInfo serverInfo =
                IConfigBase.ServerList.GetServer(id) ?? throw new Exception("未找到服务端实例配置");
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = serverInfo
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }

    [HttpPost("general/{id}")]
    public async Task<IActionResult> Update([FromRoute] uint id, [FromBody] UpdateServerRequest request)
    {
        // 校验ID
        if (id != request.ID) return BadRequest(new ApiResponse<object>
        {
            Code = 400,
            Message = "路由ID与请求体ID不一致",
        });
        // if (!ModelState.IsValid) return BadRequest(ModelState);

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound("服务器不存在");

        // 是否存在耗时操作
        bool needDownloadCore = !string.IsNullOrEmpty(request.CoreUrl) || !string.IsNullOrEmpty(request.CoreFileKey);
        bool needDownloadJava = IsJavaNeedDownload(request.Java);
        // 如果用户把neoforge/forge安装包传了进来 需要自动执行安装
        bool needInstallForge = (request.Core.Contains("forge") || request.Core.Contains("neoforge")) &&
                                (server.Core != request.Core || needDownloadCore) && request.Core.Contains(".jar") && !request.Core.Contains("arclight");

        // 判断是否需要进入后台队列
        bool needsBackgroundProcessing = needDownloadCore || needDownloadJava || needInstallForge;

        if (needsBackgroundProcessing)
        {
            if (_mcServerService.IsServerRunning(id))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "涉及修改运行核心内容，请先关闭服务器再操作！",
                });
            }
            
            // 丢后台
            await _updateQueue.QueueTaskAsync(new UpdateServerTask { Request = request });
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新任务已提交，正在后台处理...",
                Data = new { needListen = true } 
            });
        }
        else
        {
            // 直接保存参数
            
            server.Name = request.Name;
            server.MinM = request.MinM ?? server.MinM;
            server.MaxM = request.MaxM ?? server.MaxM;
            server.Args = request.Args ?? "";
            server.Java = request.Java;
            server.Core = request.Core;
            server.Base = request.Base;
            server.BackupDelay = request.BackupDelay;
            server.BackupMaxCount = request.BackupMaxCount;
            server.BackupPath = request.BackupPath;
            server.YggdrasilApiAddr = request.YggdrasilApiAddr;
            server.RunOnStartup = request.RunOnStartup;
            server.AutoRestart = request.AutoRestart;
            server.ForceAutoRestart = request.ForceAutoRestart;
            server.InputEncoding = request.InputEncoding;
            server.OutputEncoding = request.OutputEncoding;

            // 保存到磁盘
            IConfigBase.ServerList.UpdateServer(server);

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "配置更新成功！",
                Data = new { needListen = false }
            });
        }
    }


    // 下崽吗～
    private bool IsJavaNeedDownload(string javaConfig)
    {
        // 不是MSLX协议的 就不管了 写错了自己的问题嗷
        if (string.IsNullOrEmpty(javaConfig) || !javaConfig.StartsWith("MSLX://Java/"))
            return false;

        // 解析版本
        string version = javaConfig.Replace("MSLX://Java/", "");
        string javaBaseDir = Path.Combine(IConfigBase.GetAppDataPath(), "DaemonData", "Tools", "Java");
        string javaExec = PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java";

        // 检查文件是否存在
        string fullPath = Path.Combine(javaBaseDir, version, "bin", javaExec);

        // 如果文件已存在，就不需要下载
        return !System.IO.File.Exists(fullPath);
    }
}