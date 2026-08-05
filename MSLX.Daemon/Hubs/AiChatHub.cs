using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Services;
using System.Text.Json.Nodes;

namespace MSLX.Daemon.Hubs;

[Authorize(Roles = "admin")]
public class AiChatHub : Hub
{
    private readonly AiService _aiService;
    private readonly ILogger<AiChatHub> _logger;

    public AiChatHub(AiService aiService, ILogger<AiChatHub> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task SendMessage(JsonArray messages)
    {
        _logger.LogInformation("收到 SignalR AI 对话请求，ConnectionId: {ConnectionId}", Context.ConnectionId);

        try
        {
            await _aiService.ProcessChatAsync(
                messages,
                async (chunk) =>
                {
                    await Clients.Caller.SendAsync("ChatChunk", chunk);
                },
                async (toolName, toolData) =>
                {
                    await Clients.Caller.SendAsync("ToolExecuted", toolName, toolData);
                }
            );

            await Clients.Caller.SendAsync("ChatComplete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR AI 对话处理异常");
            await Clients.Caller.SendAsync("ChatError", ex.Message);
        }
    }
}
