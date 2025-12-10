using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class FileContentController: ControllerBase
{
    // 获取文件内容
    [HttpGet("instance/{id}/content")]
    public async Task<IActionResult> GetFileContent(uint id, [FromQuery] string path)
    {
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null)
            return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        // 安全检查
        var check = FileUtils.GetSafePath(server.Base, path);
        if (!check.IsSafe)
            return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });

        string targetPath = check.FullPath;

        // 检查文件是否存在
        if (!System.IO.File.Exists(targetPath))
            return NotFound(new ApiResponse<object> { Code = 404, Message = "文件不存在" });

        // 检查文件大小
        long fileSize = new FileInfo(targetPath).Length;
        long maxEditSize = 1024 * 1024 * 2; // 最大2MB

        if (fileSize > maxEditSize)
        {
            return BadRequest(new ApiResponse<object> 
            { 
                Code = 400, 
                Message = $"文件过大 ({fileSize / 1024}KB)，不支持在线编辑。请下载后查看。" 
            });
        }

        try
        {
            // 文件流读取文件内容
            using (var fileStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var streamReader = new StreamReader(fileStream, System.Text.Encoding.UTF8))
            {
                string content = await streamReader.ReadToEndAsync();
                
                return Ok(new ApiResponse<string>
                {
                    Code = 200,
                    Message = "读取成功",
                    Data = content
                });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object> 
            { 
                Code = 500, 
                Message = $"读取文件失败: {ex.Message}" 
            });
        }
    }
}