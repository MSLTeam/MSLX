using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class FileContentController : ControllerBase
{
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

        // 根据后缀快速拦截
        var ext = Path.GetExtension(targetPath).ToLower();
        var binaryExtensions = new HashSet<string>
        {
            ".jar", ".zip", ".gz", ".tar", ".rar", ".7z", // 压缩包
            ".exe", ".dll", ".so", ".bin", // 可执行程序
            ".png", ".jpg", ".jpeg", ".gif", ".ico", ".webp", ".bmp", // 图片
            ".db", ".db-wal", ".db-shm", ".dat", ".level", // 数据库/二进制数据
            ".mp3", ".wav", ".ogg", ".mp4",".pdf" // 媒体
        };

        if (binaryExtensions.Contains(ext))
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "该文件类型不支持在线编辑，请下载后查看。"
            });
        }

        // 现在文件大小
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

        // 内容采样
        if (FileUtils.IsBinaryFile(targetPath))
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "检测到二进制内容，不支持在线编辑。"
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
    
    [HttpPost("instance/{id}/directory")]
    public IActionResult CreateDirectory(uint id, [FromBody] CreateDirectoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "文件夹名字不能为空" });
        }
        
        // 合法？
        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (request.Name.IndexOfAny(invalidChars) >= 0)
        {
             return BadRequest(new ApiResponse<object> { Code = 400, Message = "文件夹名字包含非法字符" });
        }
        
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });
        }

        // 组合完整路径：用户当前路径 + 新文件夹名
        string relativePath = Path.Combine(request.Path ?? "", request.Name);
        
        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe)
        {
            return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });
        }

        string finalPath = check.FullPath;

        // 检查冲突
        if (Directory.Exists(finalPath))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "该文件夹已存在" });
        }
        if (System.IO.File.Exists(finalPath))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "存在同名文件" });
        }

        try
        {
            // 创建文件夹
            Directory.CreateDirectory(finalPath);

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "文件夹创建成功"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Code = 500,
                Message = $"创建失败: {ex.Message}"
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