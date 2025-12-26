using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.StaticFilesControllers;

[ApiController]
[Route("api/static/images")]
public class StaticImagesController: ControllerBase
{
    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult GetImage(string fileName)
    {
        try
        {
            string rootBase = Path.Combine(IConfigBase.GetAppDataPath(), "Public", "Images");
            
            var (isSafe, targetPath, message) = FileUtils.GetSafePath(rootBase, fileName);

            if (!isSafe)
            {
                return BadRequest(new ApiResponse<object>()
                {
                    Code = 400,
                    Message = message 
                });
            }

            // 扩展名检查
            string extension = Path.GetExtension(targetPath).ToLowerInvariant();
            string contentType;

            switch (extension)
            {
                case ".png":
                    contentType = "image/png";
                    break;
                case ".jpg":
                case ".jpeg":
                    contentType = "image/jpeg";
                    break;
                case ".webp":
                    contentType = "image/webp";
                    break;
                default:
                    return BadRequest(new ApiResponse<object>()
                    {
                        Code = 400,
                        Message = "不支持的文件类型，仅支持 .png, .jpg, .webp"
                    });
            }

            // 检查文件是否存在
            if (!System.IO.File.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object>()
                {
                    Code = 404,
                    Message = "文件不存在"
                });
            }
            
            Response.Headers.Append("Cache-Control", "public, max-age=10800");
            return PhysicalFile(targetPath, contentType);
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "获取图片失败: " + e.Message
            });
        }
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> UploadImage([FromBody] UploadStaticFilesRequest request) 
    {
        try
        {
            var finalPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", request.FileKey + ".tmp");
            if (!System.IO.File.Exists(finalPath))
            {
                return NotFound(new ApiResponse<string> { Code = 404, Message = "找不到指定的上传文件，可能已过期" });
            }

            if (!Directory.Exists(Path.Combine(IConfigBase.GetAppDataPath(), "Public", "Images")))
            {
                Directory.CreateDirectory(Path.Combine(IConfigBase.GetAppDataPath(), "Public", "Images"));
            }
            
            
            System.IO.File.Move(finalPath,Path.Combine(IConfigBase.GetAppDataPath(), "Public", "Images",request.FileName),true);
            return Ok(new ApiResponse<object>()
            {
                Code = 200,
                Message = "上传图片成功"
            });
        }catch(Exception e)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "上传图片失败: " + e.Message
            });
        }
    }
}