using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/ai")]
public class AiAssistantController : ControllerBase
{
    private readonly AiService _aiService;

    public AiAssistantController(AiService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("chat")]
    [Authorize(Roles = "admin")]
    public async Task Chat([FromBody] ChatRequest request)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        if (request.Messages == null || request.Messages.Count == 0)
        {
            await SendSseEventAsync("message", "请输入有效的对话内容。");
            await SendSseEventAsync("done", "[DONE]");
            return;
        }

        try
        {
            var jsonMessages = new JsonArray();
            foreach (var m in request.Messages)
            {
                jsonMessages.Add(new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                });
            }

            await _aiService.ProcessChatAsync(
                jsonMessages,
                async (chunkText) =>
                {
                    await SendSseEventAsync("message", chunkText);
                },
                async (toolName, toolData) =>
                {
                    var toolPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        tool = toolName,
                        data = toolData
                    });
                    await SendSseEventAsync("tool_executed", toolPayload);
                }
            );
        }
        catch (Exception ex)
        {
            await SendSseEventAsync("error", ex.Message);
        }
        finally
        {
            await SendSseEventAsync("done", "[DONE]");
        }
    }

    private async Task SendSseEventAsync(string eventType, string data)
    {
        try
        {
            var formattedData = data.Replace("\r", "").Replace("\n", "\\n");
            await Response.WriteAsync($"event: {eventType}\ndata: {formattedData}\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            // 客户端已断开（如用户中断对话），忽略写入异常
            Console.WriteLine($"SSE 写入失败 (客户端可能已断开): {ex.Message}");
        }
    }

    [HttpPost("confirm-tool")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ConfirmTool([FromBody] ConfirmToolRequest request)
    {
        string? toolName = null;
        object? toolData = null;

        var (success, message, _) = await _aiService.ConfirmPendingToolAsync(
            request.ConfirmationId,
            request.Approved,
            async (name, data) =>
            {
                toolName = name;
                toolData = data;
                await Task.CompletedTask;
            });

        return Ok(new ApiResponse<object>
        {
            Code = success ? 200 : 400,
            Message = message,
            Data = new
            {
                success,
                message,
                tool = toolName,
                data = toolData
            }
        });
    }
}

public class ConfirmToolRequest
{
    public string ConfirmationId { get; set; } = "";
    public bool Approved { get; set; }
}

public class ChatRequest
{
    public List<ChatMessageDto>? Messages { get; set; }
}

public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
