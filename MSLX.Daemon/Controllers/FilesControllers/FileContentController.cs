using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
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
    
    // 保存文件
    [HttpPost("instance/{id}/content")]
    public async Task<IActionResult> SaveFileContent(uint id, [FromBody] SaveFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "文件路径不能为空" });
        }
        
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到指定的实例" });
        }

        // check
        var check = FileUtils.GetSafePath(server.Base, request.Path);
        if (!check.IsSafe)
        {
            return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });
        }

        string targetPath = check.FullPath;

        /*
        if (!System.IO.File.Exists(targetPath))
        {
             return NotFound(new ApiResponse<object> { Code = 404, Message = "目标文件不存在，无法保存" });
        } */
        
        // 写入文件
        try
        {
            var utf8WithoutBom = new System.Text.UTF8Encoding(false);
            
            await System.IO.File.WriteAllTextAsync(targetPath, request.Content, utf8WithoutBom);

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "文件保存成功"
            });
        }
        catch (IOException ex)
        {
            // IO错误
            return StatusCode(500, new ApiResponse<object> 
            { 
                Code = 500, 
                Message = $"文件正如被占用或无法写入: {ex.Message}" 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object> 
            { 
                Code = 500, 
                Message = $"保存失败: {ex.Message}" 
            });
        }
    }
}