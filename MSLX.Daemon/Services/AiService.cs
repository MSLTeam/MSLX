using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;
using MSLX.SDK.Models.Tasks;

namespace MSLX.Daemon.Services;

public class AiService
{
    private readonly HttpClient _httpClient;
    private readonly IMCServerService _mcServerService;
    private readonly IBackgroundTaskQueue<CreateServerTask> _createTaskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AiService> _logger;

    public AiService(
        IMCServerService mcServerService,
        IBackgroundTaskQueue<CreateServerTask> createTaskQueue,
        IServiceScopeFactory scopeFactory,
        IMemoryCache memoryCache,
        ILogger<AiService> logger)
    {
        _httpClient = new HttpClient();
        _mcServerService = mcServerService;
        _createTaskQueue = createTaskQueue;
        _scopeFactory = scopeFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task ProcessChatAsync(
        JsonArray messages,
        Func<string, Task> onChunkReceived,
        Func<string, object, Task> onToolExecuted)
    {
        var config = IConfigBase.Config.ReadConfig();
        bool enabled = (bool?)(config["aiEnabled"]) ?? false;
        string apiKey = (string?)(config["aiApiKey"]) ?? "";
        string baseUrl = (string?)(config["aiBaseUrl"]) ?? "https://api.deepseek.com/v1";
        string modelName = (string?)(config["aiModelName"]) ?? "deepseek-chat";
        string systemPrompt = (string?)(config["aiSystemPrompt"]) ?? """
你是一个高效、果断的 Minecraft 服务器运维助手。

【敏感操作与前端 UI 二次确认规范 - 必须严格遵守】：
1. 当用户要求删除文件/目录(如删除存档 'world'、旧插件、模组或日志)或修改文件时，【绝对禁止在回复文本中反问用户“你确认要删除/修改吗”】！
2. 必须【立即、果断调用 delete_server_file 或 write_server_file 工具】！系统与前端 UI 界面会自动捕捉该 Tool Call，并在聊天卡片下方弹出漂亮的交互式黄色二次确认框与 [确认授权删除/写入] 按钮供用户点击！
3. 当用户要求“把 Java 切换为合适的版本”或修改设置时，绝对禁止反问用户或询问“你想用 Java 21 还是 25”，必须立即调用 update_instance_settings 工具！
4. 当服务器属于 NeoForge 26.x 或 MC 26.1+ 时，必须严格直接匹配 Java 25 (MSLX://Java/25)！
5. 在你每次回答的末尾，必须生成 3 个适合当前上下文的预设快捷回复选项：
<<<SUGGESTIONS:["启动服务器", "查看 server.properties", "把端口改成 25565"]>>>

【MC 与 Java 版本对应准则 - 必须严格遵守】：
- NeoForge 26.x / MC 26.1+ ➔ Java 25 (MSLX://Java/25)
- MC 1.20.5 - 1.21.11 ➔ Java 21 (MSLX://Java/21)
- MC 1.18 - 1.20.4 ➔ Java 17 (MSLX://Java/17)
- MC 1.17 / 1.17.1 ➔ Java 16 (MSLX://Java/16)
- MC 1.13 及更低 ➔ Java 8 (MSLX://Java/8)

当对话上下文中包含【当前用户界面上下文】且用户指令未显式指明服务器 ID 时，请默认直接作用于该当前服务器 ID！
你拥有 update_instance_settings, list_server_files, read_server_file, write_server_file, delete_server_file, control_server 等全套运维工具。
""";

        if (!enabled || string.IsNullOrWhiteSpace(apiKey))
        {
            await onChunkReceived("⚠️ AI 助手尚未启用或未设置 API Key，请先前往系统的【AI 助手设置】中配置。");
            return;
        }

        var requestUrl = $"{baseUrl.TrimEnd('/')}/chat/completions";

        var reqMessages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };

        foreach (var msg in messages)
        {
            if (msg != null)
            {
                reqMessages.Add(msg.DeepClone());
            }
        }

        var tools = GetToolsDefinition();

        int maxTurns = 5;
        while (maxTurns-- > 0)
        {
            var payload = new JsonObject
            {
                ["model"] = modelName,
                ["messages"] = reqMessages.DeepClone(),
                ["tools"] = tools.DeepClone(),
                ["stream"] = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("调用大模型失败 HTTP {Status}: {Body}", response.StatusCode, responseContent);
                    await onChunkReceived($"调用大模型失败 (HTTP {(int)response.StatusCode}): {responseContent}");
                    return;
                }

                var resJson = JsonNode.Parse(responseContent)?.AsObject();
                var messageObj = resJson?["choices"]?[0]?["message"]?.AsObject();

                if (messageObj == null)
                {
                    await onChunkReceived("大模型未返回有效的响应数据。");
                    return;
                }

                var toolCalls = messageObj["tool_calls"]?.AsArray();
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    reqMessages.Add(messageObj.DeepClone());

                    foreach (var call in toolCalls)
                    {
                        var toolCallId = call?["id"]?.ToString() ?? "";
                        var functionObj = call?["function"]?.AsObject();
                        var functionName = functionObj?["name"]?.ToString() ?? "";
                        var argumentsStr = functionObj?["arguments"]?.ToString() ?? "{}";

                        _logger.LogInformation("AI 触发 Tool Call: {Name}, Args: {Args}", functionName, argumentsStr);

                        var toolResult = await ExecuteToolAsync(functionName, argumentsStr, onToolExecuted);

                        reqMessages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = toolCallId,
                            ["content"] = toolResult
                        });
                    }

                    continue;
                }
                else
                {
                    var contentText = messageObj["content"]?.ToString() ?? "";

                    List<string>? suggestions = null;
                    var sugMatch = Regex.Match(contentText, @"<<<SUGGESTIONS:(?<json>\[.*?\])>>>", RegexOptions.Singleline);
                    if (sugMatch.Success)
                    {
                        try
                        {
                            suggestions = JsonSerializer.Deserialize<List<string>>(sugMatch.Groups["json"].Value);
                            contentText = Regex.Replace(contentText, @"<<<SUGGESTIONS:\[.*?\]>>>", "", RegexOptions.Singleline).Trim();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "解析建议回复 JSON 失败");
                        }
                    }

                    if (suggestions != null && suggestions.Count > 0)
                    {
                        await onToolExecuted("suggested_replies", suggestions);
                    }

                    var dsmlHandledResult = await TryHandleDsmlTextToolCallsAsync(contentText, onToolExecuted);
                    if (dsmlHandledResult != null)
                    {
                        await onChunkReceived(dsmlHandledResult);
                    }
                    else
                    {
                        await onChunkReceived(contentText);
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 AI 对话异常");
                await onChunkReceived($"处理请求时发生异常: {ex.Message}");
                break;
            }
        }
    }

    private string GetJavaMajorVersion(string versionStr)
    {
        if (string.IsNullOrWhiteSpace(versionStr)) return "17";
        var match = Regex.Match(versionStr, @"(?:1\.)?(?<ver>\d+)");
        return match.Success ? match.Groups["ver"].Value : "17";
    }

    private int GetRecommendedJavaMajor(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return 17;

        var match26 = Regex.Match(mcVersion, @"\b(2\d(?:\.\d+)*)\b");
        if (match26.Success)
        {
            if (float.TryParse(match26.Value, out float verNum) && verNum >= 26.1f)
                return 25;
        }

        var match = Regex.Match(mcVersion, @"\b(\d+(?:\.\d+)+)\b");
        string cleanVersion = match.Success ? match.Value : mcVersion.Trim();

        int CompareVersions(string v1, string v2)
        {
            var parts1 = v1.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
            var parts2 = v2.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
            int maxLen = Math.Max(parts1.Length, parts2.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int p1 = i < parts1.Length ? parts1[i] : 0;
                int p2 = i < parts2.Length ? parts2[i] : 0;
                if (p1 != p2) return p1 - p2;
            }
            return 0;
        }

        if (CompareVersions(cleanVersion, "26.1") >= 0) return 25;
        if (CompareVersions(cleanVersion, "1.20.5") >= 0) return 21;
        if (CompareVersions(cleanVersion, "1.18") >= 0) return 17;
        if (CompareVersions(cleanVersion, "1.17") >= 0) return 16;
        return 8;
    }

    private JsonNode? ConvertJTokenToJsonNode(object? data)
    {
        if (data is JToken token)
        {
            try
            {
                return JsonNode.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private async Task<string?> TryHandleDsmlTextToolCallsAsync(string contentText, Func<string, object, Task> onToolExecuted)
    {
        if (string.IsNullOrWhiteSpace(contentText) || !contentText.Contains("<｜DSML｜invoke"))
        {
            return null;
        }

        try
        {
            var invokeMatch = Regex.Match(contentText, @"<｜DSML｜invoke\s+name=""(?<name>[^""]+)""[^>]*>(?<body[\s\S]*?)</｜DSML｜invoke>");
            if (invokeMatch.Success)
            {
                string rawName = invokeMatch.Groups["name"].Value;
                string body = invokeMatch.Groups["body"].Value;

                string toolName = rawName switch
                {
                    "create_server" => "create_mc_server",
                    "create_mc_server" => "create_mc_server",
                    "query_cores" => "query_available_server_cores",
                    "query_java" => "query_java_environments",
                    "check_status" => "query_creation_status",
                    "update_instance" => "update_instance_settings",
                    _ => rawName
                };

                var argsObj = new JsonObject();
                var paramMatches = Regex.Matches(body, @"<｜DSML｜parameter\s+name=""(?<pname>[^""]+)""[^>]*>(?<pval>[^<]*)</｜DSML｜parameter>");
                foreach (Match pm in paramMatches)
                {
                    string pname = pm.Groups["pname"].Value;
                    string pval = pm.Groups["pval"].Value;

                    string mappedPName = pname switch
                    {
                        "core_name" => "server_type",
                        "version" => "mc_version",
                        "name" => "server_name",
                        _ => pname
                    };

                    argsObj[mappedPName] = pval;
                }

                _logger.LogInformation("解析并拦截文本中的 DSML Tool Call: {Name}, Args: {Args}", toolName, argsObj.ToJsonString());
                string toolResult = await ExecuteToolAsync(toolName, argsObj.ToJsonString(), onToolExecuted);

                string cleanText = Regex.Replace(contentText, @"<｜DSML｜tool_calls[\s\S]*?</｜DSML｜tool_calls>", "").Trim();
                return string.IsNullOrWhiteSpace(cleanText) ? toolResult : $"{cleanText}\n\n{toolResult}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析 DSML 文本标签失败");
        }

        return null;
    }

    private JsonArray GetToolsDefinition()
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "update_instance_settings",
                    ["description"] = "修改 MSLX 服务器基础实例属性（如切换绑定的 Java 环境，调整最大/最小内存 MB，修改运行核心 Core 文件名，附加 JVM 参数等系统级配置）。Java 参数传入 'suitable' 或 'auto' 时系统会自动根据规则精准推导（NeoForge 26.x / MC 26.1+ 强制绑定 Java 25；1.20.5~1.21 绑定 Java 21；1.18~1.20.4 绑定 Java 17；1.17 绑定 Java 16；1.13及更低绑定 Java 8）。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["java"] = new JsonObject { ["type"] = "string", ["description"] = "Java 环境标识，如 'MSLX://Java/25'、'suitable' (自动精准推导)、'MSLX://Java/21'、'MSLX://Java/17'、'MSLX://Java/8'" },
                            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "服务器实例名称" },
                            ["max_m"] = new JsonObject { ["type"] = "integer", ["description"] = "最大内存 (MB)，如 4096" },
                            ["min_m"] = new JsonObject { ["type"] = "integer", ["description"] = "最小内存 (MB)，如 1024" },
                            ["core"] = new JsonObject { ["type"] = "string", ["description"] = "核心 JAR 文件名，如 'neoforge-26.2.jar'" },
                            ["args"] = new JsonObject { ["type"] = "string", ["description"] = "附加 JVM 启动参数" },
                            ["auto_restart"] = new JsonObject { ["type"] = "boolean", ["description"] = "崩溃时是否自动重启" }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "list_server_files",
                    ["description"] = "列出指定服务器实例目录或子目录下的所有文件和文件夹列表。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["dir_path"] = new JsonObject { ["type"] = "string", ["description"] = "相对目录路径，如 '' (根目录), 'plugins', 'mods', 'logs'" }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "read_server_file",
                    ["description"] = "读取指定服务器实例目录下的任意文本文件（如 server.properties, eula.txt, spigot.yml, paper-global.yml, 插件/模组配置文件及运行日志等）。底层采用 FileShare.ReadWrite 共享模式，即使服务器正在运行中也可安全无冲突读取进行日志排查。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "相对文件路径，如 'server.properties', 'logs/latest.log', 'logs/debug.log', 'plugins/Essentials/config.yml'" }
                        },
                        ["required"] = new JsonArray { "file_path" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "write_server_file",
                    ["description"] = "创建或覆盖写入指定服务器实例目录下的文本文件（如修改 server.properties, eula.txt, 插件配置）。修改已有敏感文件请直接调用本工具，前端会自动弹出授权确认卡片供用户点击。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "相对文件路径，如 'server.properties', 'eula.txt', 'spigot.yml'" },
                            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "写入文件的完整文本内容" },
                            ["confirmed"] = new JsonObject { ["type"] = "boolean", ["description"] = "修改已有敏感文件时用户是否已在前端界面确认" }
                        },
                        ["required"] = new JsonArray { "file_path", "content" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "delete_server_file",
                    ["description"] = "删除指定服务器实例目录下的文件或文件夹（如删除存档 'world'、旧插件、模组或日志文件）。【请直接果断调用本工具，严禁在回复文本中反问用户，前端界面会自动拦截并展示交互式确认按钮卡片供用户点击授权】。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "相对文件或目录路径，如 'world', 'plugins/old_plugin.jar', 'mods/test.jar'" },
                            ["confirmed"] = new JsonObject { ["type"] = "boolean", ["description"] = "删除文件前用户是否已在前端界面确认" }
                        },
                        ["required"] = new JsonArray { "file_path" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "query_creation_status",
                    ["description"] = "查询指定服务器实例后台建服与安装的最新进度、状态消息及是否成功/失败。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID" }
                        },
                        ["required"] = new JsonArray { "server_id" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "query_java_environments",
                    ["description"] = "扫描并查询当前主机上已安装的所有 Java 环境列表（如 Java 8, 16, 17, 21, 25 等及对应的 MSLX 路径标识）。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "query_available_server_cores",
                    ["description"] = "向 MSL 镜像站实时查询当前支持的服务端核心类型（如 neoforge, forge, paper, fabric, purpur, mohist, arclight, vanilla 等）或指定核心所支持的所有 MC 游戏版本与构建号列表。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["core_name"] = new JsonObject { ["type"] = "string", ["description"] = "核心名称（如 'neoforge', 'paper', 'forge', 'fabric'）。留空时返回全量支持的核心分类与类型列表。" }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "create_mc_server",
                    ["description"] = "在 MSLX 面板中创建新的 Minecraft 基础服务器实例（支持 neoforge, forge, paper, fabric, spigot 等）。系统会自动根据 MC 版本推导并选择匹配的 Java 大版本。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_name"] = new JsonObject { ["type"] = "string", ["description"] = "服务器名称，如：我的 NeoForge 服务器" },
                            ["server_type"] = new JsonObject { ["type"] = "string", ["description"] = "核心类型，如 neoforge, forge, paper, fabric, spigot, vanilla, mohist, arclight" },
                            ["mc_version"] = new JsonObject { ["type"] = "string", ["description"] = "Minecraft 游戏版本号或 NeoForge 版本号，如 26.2, 1.20.2, 1.20.1" },
                            ["java"] = new JsonObject { ["type"] = "string", ["description"] = "可选 Java 环境标识（如 'MSLX://Java/21'），留空时系统按 MC 版本自动精确推导并绑定" }
                        },
                        ["required"] = new JsonArray { "server_type", "mc_version" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "update_server_config",
                    ["description"] = "更新服务器的核心属性（如修改 server.properties 中的端口 server_port、正版验证 online_mode、PVP pvp、最大玩家数 max_players、MOTD motd）。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["server_port"] = new JsonObject { ["type"] = "integer", ["description"] = "服务器通信端口，如 18889" },
                            ["online_mode"] = new JsonObject { ["type"] = "boolean", ["description"] = "正版验证设置" },
                            ["pvp"] = new JsonObject { ["type"] = "boolean", ["description"] = "PVP 设置" },
                            ["max_players"] = new JsonObject { ["type"] = "integer", ["description"] = "最大玩家数" },
                            ["motd"] = new JsonObject { ["type"] = "string", ["description"] = "服务器标语 (MOTD)" }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "control_server",
                    ["description"] = "控制服务器的启动、停止或重启。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "服务器 ID，未指定时结合【当前用户界面上下文】的主机 ID 或最新服务器" },
                            ["action"] = new JsonObject { ["type"] = "string", ["description"] = "动作，取值为 start, stop, restart" }
                        },
                        ["required"] = new JsonArray { "action" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "query_server_status",
                    ["description"] = "查询当前所有服务器或指定服务器的运行状态列表。",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["server_id"] = new JsonObject { ["type"] = "number", ["description"] = "可选服务器 ID" }
                        }
                    }
                }
            }
        };
    }

    private async Task<string> ExecuteToolAsync(string name, string argsJson, Func<string, object, Task> onToolExecuted)
    {
        try
        {
            var args = JsonNode.Parse(argsJson)?.AsObject();

            switch (name)
            {
                case "update_instance_settings":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    if (args != null && args.ContainsKey("java") && args["java"] != null)
                    {
                        string rawJava = args["java"].ToString().Trim();

                        if (rawJava.Equals("suitable", StringComparison.OrdinalIgnoreCase) ||
                            rawJava.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                            rawJava.Contains("合适") || rawJava.Contains("推荐"))
                        {
                            string versionHint = !string.IsNullOrEmpty(targetServer.Name) ? targetServer.Name : targetServer.Core;
                            int recMajor = GetRecommendedJavaMajor(versionHint);
                            rawJava = $"MSLX://Java/{recMajor}";
                        }
                        else
                        {
                            var match = Regex.Match(rawJava, @"\d+");
                            if (match.Success)
                            {
                                rawJava = $"MSLX://Java/{match.Value}";
                            }
                        }

                        targetServer.Java = rawJava;
                    }

                    if (args != null && args.ContainsKey("name") && args["name"] != null)
                    {
                        targetServer.Name = args["name"].ToString();
                    }
                    if (args != null && args.ContainsKey("max_m") && args["max_m"] != null)
                    {
                        if (int.TryParse(args["max_m"].ToString(), out int maxM))
                        {
                            targetServer.MaxM = maxM;
                        }
                    }
                    if (args != null && args.ContainsKey("min_m") && args["min_m"] != null)
                    {
                        if (int.TryParse(args["min_m"].ToString(), out int minM))
                        {
                            targetServer.MinM = minM;
                        }
                    }
                    if (args != null && args.ContainsKey("core") && args["core"] != null)
                    {
                        targetServer.Core = args["core"].ToString();
                    }
                    if (args != null && args.ContainsKey("args") && args["args"] != null)
                    {
                        targetServer.Args = args["args"].ToString();
                    }
                    if (args != null && args.ContainsKey("auto_restart") && args["auto_restart"] != null)
                    {
                        targetServer.AutoRestart = args["auto_restart"].GetValue<bool>();
                    }

                    IConfigBase.ServerList.UpdateServer(targetServer);

                    var resData = new
                    {
                        serverId = targetServer.ID,
                        name = targetServer.Name,
                        java = targetServer.Java,
                        maxM = targetServer.MaxM,
                        minM = targetServer.MinM,
                        core = targetServer.Core
                    };
                    await onToolExecuted("update_instance_settings", resData);
                    return $"[SUCCESS] 服务器 (ID: {targetServer.ID}, 名称: {targetServer.Name}) 的 MSLX 实例设置更新成功！绑定的 Java 已根据 MC 规则准确切换为 `{targetServer.Java}`，分配内存: {targetServer.MinM}M ~ {targetServer.MaxM}M！";
                }

                case "list_server_files":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }
                    string dirPath = args?["dir_path"]?.ToString() ?? "";

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    string serverDir = targetServer.Base;
                    if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
                    {
                        serverDir = Path.Combine(IConfigBase.GetAppDataPath(), "Servers", targetServer.ID.ToString());
                    }

                    string safeBaseDir = Path.GetFullPath(serverDir);
                    string targetDirPath = Path.GetFullPath(Path.Combine(safeBaseDir, dirPath));

                    if (!targetDirPath.StartsWith(safeBaseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return "[ERROR] 越权访问：不允许查看服务器实例目录之外的文件。";
                    }

                    if (!Directory.Exists(targetDirPath))
                    {
                        return $"[ERROR] 目录不存在: {dirPath}";
                    }

                    var entries = Directory.GetFileSystemEntries(targetDirPath)
                        .Select(p => new
                        {
                            name = Path.GetFileName(p),
                            isDir = Directory.Exists(p),
                            size = Directory.Exists(p) ? 0 : new FileInfo(p).Length,
                            lastModified = File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss")
                        }).ToList();

                    var listResult = new { serverId = targetServer.ID, dirPath = dirPath, count = entries.Count, items = entries };
                    await onToolExecuted("list_server_files", listResult);
                    return $"[SUCCESS] 目录 `{dirPath}` 文件列表:\n" + JsonSerializer.Serialize(entries);
                }

                case "query_creation_status":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }

                    if (targetId == 0) return "[ERROR] 请提供有效的 server_id";

                    if (_memoryCache.TryGetValue(targetId.ToString(), out CacheableStatus? status) && status != null)
                    {
                        bool isSuccess = status.Progress >= 100 || status.Message.Contains("完成");
                        bool isFailure = status.Progress < 0 || status.Message.Contains("失败") || status.Message.Contains("中断") || status.Message.Contains("错误");

                        await onToolExecuted("query_creation_status", status);

                        if (isFailure)
                        {
                            return $"[ERROR] 服务器 {targetId} 安装部署失败: {status.Message}";
                        }
                        else if (isSuccess)
                        {
                            return $"[SUCCESS] 服务器 {targetId} 建服全流程已成功完成！目前可以安全进行配置文件修改和启动控制。";
                        }
                        else
                        {
                            return $"[IN_PROGRESS] 服务器 {targetId} 正在后台安装中 (进度: {status.Progress:F1}%, 状态: {status.Message})。";
                        }
                    }

                    return $"[INFO] 未找到服务器 {targetId} 的实时安装缓存，可能已安装完成或实例不存在。";
                }

                case "query_java_environments":
                {
                    using var scope = _scopeFactory.CreateScope();
                    var javaScanner = scope.ServiceProvider.GetRequiredService<IJavaScannerService>();
                    var list = await javaScanner.ScanJavaAsync(false);

                    var resList = list.Select(j => {
                        string major = GetJavaMajorVersion(j.Version);
                        return new
                        {
                            version = j.Version,
                            majorVersion = major,
                            vendor = j.Vendor,
                            path = j.Path,
                            mslxIdentifier = $"MSLX://Java/{major}"
                        };
                    }).ToList();

                    await onToolExecuted("query_java_environments", resList);
                    return $"[SUCCESS] 扫描到的 Java 环境列表:\n" + JsonSerializer.Serialize(resList);
                }

                case "query_available_server_cores":
                {
                    string? coreName = args?["core_name"]?.ToString()?.Trim()?.ToLower();
                    if (string.IsNullOrEmpty(coreName))
                    {
                        var (succ, data, msg) = await MSLApi.GetDataAsync("/mirrors");
                        var cleanData = ConvertJTokenToJsonNode(data);
                        await onToolExecuted("query_available_server_cores", new { query = "all_cores", success = succ, data = cleanData });
                        return succ && cleanData != null 
                            ? $"[SUCCESS] MSL 镜像站全量支持的核心类型列表:\n{cleanData.ToJsonString()}" 
                            : $"[ERROR] 查询镜像核心分类失败: {msg}";
                    }
                    else
                    {
                        var (succ, data, msg) = await MSLApi.GetDataAsync($"/mirrors/{coreName}");
                        var cleanData = ConvertJTokenToJsonNode(data);
                        await onToolExecuted("query_available_server_cores", new { query = coreName, success = succ, data = cleanData });
                        return succ && cleanData != null 
                            ? $"[SUCCESS] 核心 [{coreName}] 支持的游戏版本列表:\n{cleanData.ToJsonString()}" 
                            : $"[ERROR] 查询核心 [{coreName}] 支持版本失败: {msg}";
                    }
                }

                case "create_mc_server":
                {
                    string type = args?["server_type"]?.ToString() ?? "paper";
                    string version = args?["mc_version"]?.ToString() ?? "1.20.2";
                    string serverName = args?["server_name"]?.ToString() ?? $"{type.ToUpper()}-{version}";
                    string specifiedJava = args?["java"]?.ToString() ?? "";

                    int recJavaMajor = GetRecommendedJavaMajor(version);

                    string selectedJava = specifiedJava;
                    if (string.IsNullOrWhiteSpace(selectedJava) || selectedJava.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var javaScanner = scope.ServiceProvider.GetRequiredService<IJavaScannerService>();
                        var scannedJavaList = await javaScanner.ScanJavaAsync(false);

                        if (scannedJavaList != null && scannedJavaList.Count > 0)
                        {
                            var parsedJavaList = scannedJavaList
                                .Select(j => new { Java = j, Major = int.TryParse(GetJavaMajorVersion(j.Version), out int m) ? m : 8 })
                                .ToList();

                            var exactMatch = parsedJavaList.FirstOrDefault(x => x.Major == recJavaMajor);
                            if (exactMatch != null)
                            {
                                selectedJava = $"MSLX://Java/{exactMatch.Major}";
                            }
                            else
                            {
                                var suitableJava = parsedJavaList
                                    .Where(x => x.Major >= recJavaMajor)
                                    .OrderBy(x => x.Major)
                                    .FirstOrDefault() ?? parsedJavaList.OrderByDescending(x => x.Major).FirstOrDefault();

                                if (suitableJava != null)
                                {
                                    selectedJava = $"MSLX://Java/{suitableJava.Major}";
                                }
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(selectedJava))
                    {
                        selectedJava = "auto";
                    }

                    string? coreUrl = null;
                    string? coreSha256 = null;
                    string coreFileName = $"{type}-{version}.jar";

                    try
                    {
                        var (apiSuccess, apiData, _) = await MSLApi.GetDataAsync($"/download/server/{type.ToLower()}/{version}?build=latest");
                        if (apiSuccess && apiData is JToken token)
                        {
                            coreUrl = token["url"]?.ToString();
                            coreSha256 = token["sha256"]?.ToString();
                            if (token["name"] != null && !string.IsNullOrEmpty(token["name"].ToString()))
                            {
                                coreFileName = token["name"].ToString();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "向镜像站查询核心下载地址失败: {Type} {Version}", type, version);
                    }

                    var serverId = IConfigBase.ServerList.GenerateServerId();
                    var createRequest = new CreateServerRequest
                    {
                        name = serverName,
                        core = coreFileName,
                        coreUrl = coreUrl,
                        coreSha256 = coreSha256,
                        java = selectedJava,
                        maxM = 2048,
                        minM = 1024,
                        DockerPorts = "25565:25565"
                    };

                    var task = new CreateServerTask
                    {
                        ServerId = serverId.ToString(),
                        Request = createRequest
                    };

                    await _createTaskQueue.QueueTaskAsync(task);

                    int maxWaitMs = 300000;
                    int intervalMs = 1000;
                    CacheableStatus? finalStatus = null;

                    while (maxWaitMs > 0)
                    {
                        await Task.Delay(intervalMs);
                        maxWaitMs -= intervalMs;

                        if (_memoryCache.TryGetValue(serverId.ToString(), out CacheableStatus? cacheStatus) && cacheStatus != null)
                        {
                            finalStatus = cacheStatus;
                            if (cacheStatus.Progress >= 100 || cacheStatus.Message.Contains("完成") || cacheStatus.Message.Contains("失败") || cacheStatus.Progress < 0)
                            {
                                break;
                            }
                        }
                    }

                    bool isSuccess = finalStatus != null && (finalStatus.Progress >= 100 || finalStatus.Message.Contains("完成"));
                    bool isFailure = finalStatus != null && (finalStatus.Progress < 0 || finalStatus.Message.Contains("失败") || finalStatus.Message.Contains("中断") || finalStatus.Message.Contains("错误"));

                    var resData = new
                    {
                        serverId = serverId,
                        name = serverName,
                        type = type,
                        version = version,
                        recommendedJavaMajor = recJavaMajor,
                        java = selectedJava,
                        status = finalStatus?.Message ?? "部署完成",
                        progress = finalStatus?.Progress ?? 100
                    };
                    await onToolExecuted("create_mc_server", resData);

                    if (isFailure)
                    {
                        return $"[ERROR] 服务器 (ID: {serverId}) 后台部署失败: {finalStatus?.Message ?? "部署过程发生错误"}";
                    }
                    else
                    {
                        return $"[SUCCESS] 服务器 (ID: {serverId}, 名称: {serverName}) 核心包部署安装已全量成功完成 (自动为 MC {version} 推荐并绑定 Java {recJavaMajor})！请立刻接着调用 write_server_file 或 update_server_config 为用户修改配置文件。";
                    }
                }

                case "read_server_file":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }
                    string filePath = args?["file_path"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        return "[ERROR] file_path 不能为空。";
                    }

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    string serverDir = targetServer.Base;
                    if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
                    {
                        serverDir = Path.Combine(IConfigBase.GetAppDataPath(), "Servers", targetServer.ID.ToString());
                    }

                    string safeBaseDir = Path.GetFullPath(serverDir);
                    string targetFilePath = Path.GetFullPath(Path.Combine(safeBaseDir, filePath));

                    if (!targetFilePath.StartsWith(safeBaseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return "[ERROR] 越权访问：不允许读取服务器实例目录之外的文件。";
                    }

                    if (!File.Exists(targetFilePath))
                    {
                        return $"[ERROR] 文件不存在: {filePath}";
                    }

                    string content;
                    using (var fs = new FileStream(targetFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        if (fs.Length > 200 * 1024)
                        {
                            fs.Seek(-200 * 1024, SeekOrigin.End);
                            sr.DiscardBufferedData();
                            content = "……(日志文件过大，已自动截取最新末尾日志)……\n" + await sr.ReadToEndAsync();
                        }
                        else
                        {
                            content = await sr.ReadToEndAsync();
                        }
                    }

                    var readResult = new { serverId = targetServer.ID, filePath = filePath, length = content.Length };
                    await onToolExecuted("read_server_file", readResult);
                    return $"[SUCCESS] 读取文件 {filePath} 成功：\n```\n{content}\n```";
                }

                case "write_server_file":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }
                    string filePath = args?["file_path"]?.ToString() ?? "";
                    string content = args?["content"]?.ToString() ?? "";
                    bool isConfirmed = args != null && args.ContainsKey("confirmed") && (bool?)(args["confirmed"]) == true;

                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        return "[ERROR] file_path 不能为空。";
                    }

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    string serverDir = targetServer.Base;
                    if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
                    {
                        serverDir = Path.Combine(IConfigBase.GetAppDataPath(), "Servers", targetServer.ID.ToString());
                        Directory.CreateDirectory(serverDir);
                    }

                    string safeBaseDir = Path.GetFullPath(serverDir);
                    string targetFilePath = Path.GetFullPath(Path.Combine(safeBaseDir, filePath));

                    if (!targetFilePath.StartsWith(safeBaseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return "[ERROR] 越权操作：不允许写文件到服务器实例目录之外。";
                    }

                    bool exists = File.Exists(targetFilePath);
                    if (exists && !isConfirmed)
                    {
                        var needConfirmData = new { serverId = targetServer.ID, filePath = filePath, action = "edit", requiresConfirmation = true, confirmed = false, content = content };
                        await onToolExecuted("write_server_file", needConfirmData);
                        return $"[REQUIRES_CONFIRMATION] 警告：准备覆盖编辑已有文件 `{filePath}`。已在前端弹出确认按钮，请提醒用户点击确认后再执行写文件。";
                    }

                    string? parentDir = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    await File.WriteAllTextAsync(targetFilePath, content);
                    var writeResult = new { serverId = targetServer.ID, filePath = filePath, contentLength = content.Length, confirmed = true };
                    await onToolExecuted("write_server_file", writeResult);
                    return $"[SUCCESS] 成功写入/更新服务器 {targetServer.ID} 的文件 `{filePath}`！";
                }

                case "delete_server_file":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }
                    string filePath = args?["file_path"]?.ToString() ?? "";
                    bool isConfirmed = args != null && args.ContainsKey("confirmed") && (bool?)(args["confirmed"]) == true;

                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        return "[ERROR] file_path 不能为空。";
                    }

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    string serverDir = targetServer.Base;
                    if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
                    {
                        serverDir = Path.Combine(IConfigBase.GetAppDataPath(), "Servers", targetServer.ID.ToString());
                    }

                    string safeBaseDir = Path.GetFullPath(serverDir);
                    string targetPath = Path.GetFullPath(Path.Combine(safeBaseDir, filePath));

                    if (!targetPath.StartsWith(safeBaseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return "[ERROR] 越权操作：不允许删除服务器实例目录之外的文件。";
                    }

                    if (!isConfirmed)
                    {
                        var confirmData = new { serverId = targetServer.ID, filePath = filePath, action = "delete", requiresConfirmation = true, confirmed = false };
                        await onToolExecuted("delete_server_file", confirmData);
                        return $"[REQUIRES_CONFIRMATION] 高风险警告：已申请删除服务器文件/目录 `{filePath}`！已在前端弹出 UI 按钮卡片，等待用户在界面点击确认授权。";
                    }

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, true);
                    }
                    else
                    {
                        return $"[ERROR] 要删除的文件/目录不存在: {filePath}";
                    }

                    var deleteResult = new { serverId = targetServer.ID, filePath = filePath, action = "delete", confirmed = true };
                    await onToolExecuted("delete_server_file", deleteResult);
                    return $"[SUCCESS] 已成功彻底删除服务器 {targetServer.ID} 的 `{filePath}`！";
                }

                case "update_server_config":
                {
                    int targetId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        int.TryParse(args["server_id"].ToString(), out targetId);
                    }

                    var serverList = IConfigBase.ServerList.GetServerList();
                    var targetServer = targetId > 0 
                        ? serverList.FirstOrDefault(s => s.ID == targetId) 
                        : serverList.OrderByDescending(s => s.ID).FirstOrDefault();

                    if (targetServer == null)
                    {
                        return "[ERROR] 未找到目标服务器实例。";
                    }

                    string serverDir = targetServer.Base;
                    if (string.IsNullOrEmpty(serverDir) || !Directory.Exists(serverDir))
                    {
                        serverDir = Path.Combine(IConfigBase.GetAppDataPath(), "Servers", targetServer.ID.ToString());
                        Directory.CreateDirectory(serverDir);
                    }

                    string propsPath = Path.Combine(serverDir, "server.properties");
                    var propsDict = new Dictionary<string, string>();
                    if (File.Exists(propsPath))
                    {
                        using (var fs = new FileStream(propsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            string? line;
                            while ((line = await sr.ReadLineAsync()) != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && line.Contains('='))
                                {
                                    var parts = line.Split('=', 2);
                                    propsDict[parts[0].Trim()] = parts[1].Trim();
                                }
                            }
                        }
                    }

                    if (args != null && args.ContainsKey("online_mode") && args["online_mode"] != null)
                    {
                        propsDict["online-mode"] = args["online_mode"].GetValue<bool>() ? "true" : "false";
                    }
                    if (args != null && args.ContainsKey("pvp") && args["pvp"] != null)
                    {
                        propsDict["pvp"] = args["pvp"].GetValue<bool>() ? "true" : "false";
                    }
                    if (args != null && args.ContainsKey("max_players") && args["max_players"] != null)
                    {
                        propsDict["max-players"] = args["max_players"].ToString();
                    }
                    if (args != null && args.ContainsKey("server_port") && args["server_port"] != null)
                    {
                        propsDict["server-port"] = args["server_port"].ToString();
                    }
                    if (args != null && args.ContainsKey("motd") && args["motd"] != null)
                    {
                        propsDict["motd"] = args["motd"].ToString();
                    }

                    var sb = new StringBuilder("#Minecraft server properties\n");
                    foreach (var kvp in propsDict)
                    {
                        sb.AppendLine($"{kvp.Key}={kvp.Value}");
                    }
                    File.WriteAllText(propsPath, sb.ToString());

                    var updateResult = new { serverId = targetServer.ID, serverName = targetServer.Name, properties = propsDict };
                    await onToolExecuted("update_server_config", updateResult);
                    return $"[SUCCESS] 服务器 (ID: {targetServer.ID}, 名称: {targetServer.Name}) 的 `server.properties` 配置更新成功！";
                }

                case "control_server":
                {
                    uint serverId = 0;
                    if (args != null && args.ContainsKey("server_id") && args["server_id"] != null)
                    {
                        uint.TryParse(args["server_id"].ToString(), out serverId);
                    }
                    string action = args?["action"]?.ToString()?.ToLower() ?? "start";

                    var serverList = IConfigBase.ServerList.GetServerList();
                    if (serverId == 0 && serverList.Count > 0)
                    {
                        serverId = (uint)serverList.OrderByDescending(s => s.ID).First().ID;
                    }

                    if (serverId == 0) return "[ERROR] 未指定有效的 server_id，且当前没有任何服务器实例。";

                    if (action == "start")
                    {
                        var (succ, msg) = _mcServerService.StartServer(serverId);
                        await onToolExecuted("control_server", new { serverId, action, succ, msg });
                        return succ ? $"[SUCCESS] 服务器 {serverId} 启动指令已发送。" : $"[ERROR] 启动服务器 {serverId} 失败: {msg}";
                    }
                    else if (action == "stop")
                    {
                        bool stopped = _mcServerService.StopServer(serverId);
                        await onToolExecuted("control_server", new { serverId, action, stopped });
                        return stopped ? $"[SUCCESS] 服务器 {serverId} 停止指令已发送。" : $"[ERROR] 停止服务器 {serverId} 失败。";
                    }
                    else if (action == "restart")
                    {
                        var (succ, msg) = await _mcServerService.RestartServer(serverId);
                        await onToolExecuted("control_server", new { serverId, action, succ, msg });
                        return succ ? $"[SUCCESS] 服务器 {serverId} 重启指令已完成。" : $"[ERROR] 重启服务器 {serverId} 失败: {msg}";
                    }

                    return "[ERROR] 未知的控制动作。";
                }

                case "query_server_status":
                {
                    var servers = IConfigBase.ServerList.GetServerList();
                    var list = new List<object>();
                    foreach (var s in servers)
                    {
                        var (status, desc) = _mcServerService.GetServerStatus((uint)s.ID);
                        list.Add(new
                        {
                            id = s.ID,
                            name = s.Name,
                            core = s.Core,
                            status = desc
                        });
                    }
                    await onToolExecuted("query_server_status", list);
                    return $"[SUCCESS] 当前服务器列表与状态:\n" + JsonSerializer.Serialize(list);
                }

                default:
                    return $"[ERROR] 未找到工具处理逻辑 {name}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行 Tool {Name} 异常", name);
            return $"[ERROR] 执行工具 {name} 失败: {ex.Message}";
        }
    }
}
