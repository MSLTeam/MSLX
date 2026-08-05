using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;

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
        var formattedData = data.Replace("\r", "").Replace("\n", "\\n");
        await Response.WriteAsync($"event: {eventType}\ndata: {formattedData}\n\n");
        await Response.Body.FlushAsync();
    }
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
