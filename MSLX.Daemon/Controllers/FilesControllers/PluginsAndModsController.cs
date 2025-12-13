using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/pm")]
public class PluginsAndModsController: ControllerBase
{
    [HttpGet("instance/{id}/list")]
    public IActionResult GetPluginsAndModsList(uint id, [FromQuery] string? mode = "plugins")
    {
        try
        {
            var server = ConfigServices.ServerList.GetServer(id);
            if (server == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = "服务器不存在"
                });
            }
        
            string targetPath = mode == "plugins" ? Path.Combine(server.Base, "plugins") : Path.Combine(server.Base, "mods");
            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = $"未检测到{(mode == "plugins" ? "插件" : "模组")}目录,请检查当前服务端是否支持使用{(mode == "plugins" ? "插件" : "模组")}，或者尝试启动一次服务器。",
                });
            }
        
            // 获取这个文件夹所有模组/插件
            var jarFiles = Directory.GetFiles(targetPath, "*.jar").Select(Path.GetFileName).ToArray();
            var disableJarFiles = Directory.GetFiles(targetPath, "*.jar.disabled").Select(Path.GetFileName).ToArray();
            return Ok(new ApiResponse<object>()
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    totalCount = disableJarFiles.Length + jarFiles.Length,
                    activeCount = jarFiles.Length,
                    disabledCount = disableJarFiles.Length,
                    jarFiles,
                    disableJarFiles,
                }
            });
        }catch(Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 500,
                Message = "服务器内部错误" + e.Message,
            });
        }
    }
    
    [HttpPost("instance/{id}/set")]
    public IActionResult SetPluginOrModState(uint id, [FromBody] SetPluginModStateRequest request)
    {
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null)
            return NotFound(new ApiResponse<object> { Code = 404, Message = "服务器不存在" });
        
        string targetPath = request.Mode == "plugins" ? Path.Combine(server.Base, "plugins") : Path.Combine(server.Base, "mods");
        if (!Directory.Exists(targetPath))
            return NotFound(new ApiResponse<object> { Code = 404, Message = "目录不存在" });

        if (request.Targets == null || request.Targets.Count == 0)
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "请选择至少一个文件" });

        int successCount = 0;
        int failCount = 0;

        // 遍历处理文件
        foreach (var fileName in request.Targets)
        {
            string currentFilePath = Path.Combine(targetPath, Path.GetFileName(fileName));

            try
            {
                // 如果文件根本不存在，跳过
                if (!System.IO.File.Exists(currentFilePath))
                {
                    failCount++;
                    continue;
                }
                
                if (request.Action == "disable")
                {
                    // .jar 才操作
                    if (Path.GetFileName(fileName).EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        string newPath = currentFilePath + ".disabled";
                        // 如果目标文件存在，删了
                        if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
                        
                        System.IO.File.Move(currentFilePath, newPath);
                        successCount++;
                    }
                }
                else if (request.Action == "enable")
                {
                    // .disable 才操作
                    if (Path.GetFileName(fileName).EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        // 去掉 .disabled
                        string newName = Path.GetFileName(fileName).Substring(0, Path.GetFileName(fileName).Length - ".disabled".Length);
                        string newPath = Path.Combine(targetPath, newName);

                        if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);

                        System.IO.File.Move(currentFilePath, newPath);
                        successCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件 {Path.GetFileName(fileName)} 失败: {ex.Message}");
                failCount++;
            }
        }
        
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = $"操作完成。成功: {successCount}, 失败/忽略: {failCount}",
            Data = new { successCount, failCount }
        });
    }
}