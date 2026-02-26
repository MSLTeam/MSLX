using fNbt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;
using System.Text.Json;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/players")]
[ApiController]
public class PlayerManagerController : ControllerBase
{
    private readonly MCServerService _mcServerService;

    // MC的日期时间格式
    private readonly string _mcDateTimeFormat = "yyyy-MM-dd HH:mm:ss zzz";

    // JSON 序列化
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PlayerManagerController(MCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }

    [HttpGet("online/{id}")]
    public IActionResult GetOnlinePlayers(uint id)
    {
        try
        {
            var players = _mcServerService.GetOnlinePlayers(id);
            return Success(players);
        }
        catch (Exception e)
        {
            return Error(e.Message);
        }
    }

    #region 历史玩家 (UserCache)

    [HttpGet("history/{id}")]
    public IActionResult GetHistoryPlayers(uint id)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var cache = ReadJsonFile<UserCacheItem>(Path.Combine(basePath, "usercache.json"));
            return Success(cache);
        }
        catch (Exception e) { return Error(e.Message); }
    }

    #endregion

    #region 白名单 (Whitelist)

    [HttpGet("whitelist/{id}")]
    public IActionResult GetWhitelist(uint id)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "whitelist.json");
            return Success(ReadJsonFile<WhitelistItem>(path));
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("whitelist/add/{id}")]
    public IActionResult AddWhitelist(uint id, [FromBody] AddPlayerRequest req)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var uuid = ResolveUuid(basePath, req.Name, req.Uuid);
            var path = Path.Combine(basePath, "whitelist.json");

            var list = ReadJsonFile<WhitelistItem>(path);
            if (list.Any(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
                return Error("该玩家已在白名单中");

            list.Add(new WhitelistItem { Name = req.Name, Uuid = uuid });
            WriteJsonFile(path, list);
            return Success("添加白名单成功");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("whitelist/remove/{id}")]
    public IActionResult RemoveWhitelist(uint id, [FromBody] RemovePlayerRequest req)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "whitelist.json");
            var list = ReadJsonFile<WhitelistItem>(path);
            var removed = list.RemoveAll(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase));

            if (removed > 0) WriteJsonFile(path, list);
            return Success(removed > 0 ? "移除白名单成功" : "玩家不在白名单中");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    #endregion

    #region 管理员 (Ops)

    [HttpGet("ops/{id}")]
    public IActionResult GetOps(uint id)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "ops.json");
            return Success(ReadJsonFile<OpItem>(path));
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("ops/add/{id}")]
    public IActionResult AddOp(uint id, [FromBody] AddPlayerRequest req)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var uuid = ResolveUuid(basePath, req.Name, req.Uuid);
            var path = Path.Combine(basePath, "ops.json");

            var list = ReadJsonFile<OpItem>(path);
            if (list.Any(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
                return Error("该玩家已经是管理员");

            list.Add(new OpItem { Name = req.Name, Uuid = uuid }); // level和bypass默认值已在Model中设置
            WriteJsonFile(path, list);
            return Success("设置管理员成功");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("ops/remove/{id}")]
    public IActionResult RemoveOp(uint id, [FromBody] RemovePlayerRequest req)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "ops.json");
            var list = ReadJsonFile<OpItem>(path);
            var removed = list.RemoveAll(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase));

            if (removed > 0) WriteJsonFile(path, list);
            return Success(removed > 0 ? "移除管理员成功" : "玩家不是管理员");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    #endregion

    #region 封禁 IP (Ban IP)

    [HttpGet("banip/{id}")]
    public IActionResult GetBannedIps(uint id)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "banned-ips.json");
            return Success(ReadJsonFile<BannedIpItem>(path));
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("banip/add/{id}")]
    public IActionResult AddBannedIp(uint id, [FromBody] AddBannedIpRequest req)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "banned-ips.json");
            var list = ReadJsonFile<BannedIpItem>(path);

            if (list.Any(x => x.Ip == req.Ip)) return Error("该IP已被封禁");

            var newItem = new BannedIpItem
            {
                Ip = req.Ip,
                Created = DateTimeOffset.Now.ToString(_mcDateTimeFormat)
            };
            if (!string.IsNullOrEmpty(req.Reason)) newItem.Reason = req.Reason;

            list.Add(newItem);
            WriteJsonFile(path, list);
            return Success("IP封禁成功");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("banip/remove/{id}")]
    public IActionResult RemoveBannedIp(uint id, [FromBody] RemoveBannedIpRequest req)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "banned-ips.json");
            var list = ReadJsonFile<BannedIpItem>(path);
            var removed = list.RemoveAll(x => x.Ip == req.Ip);

            if (removed > 0) WriteJsonFile(path, list);
            return Success(removed > 0 ? "IP解封成功" : "该IP未被封禁");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    #endregion

    #region 封禁玩家 (Ban Player)

    [HttpGet("banplayer/{id}")]
    public IActionResult GetBannedPlayers(uint id)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "banned-players.json");
            return Success(ReadJsonFile<BannedPlayerItem>(path));
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("banplayer/add/{id}")]
    public IActionResult AddBannedPlayer(uint id, [FromBody] AddBannedPlayerRequest req)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var uuid = ResolveUuid(basePath, req.Name, req.Uuid);
            var path = Path.Combine(basePath, "banned-players.json");

            var list = ReadJsonFile<BannedPlayerItem>(path);
            if (list.Any(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
                return Error("该玩家已被封禁");

            var newItem = new BannedPlayerItem
            {
                Name = req.Name,
                Uuid = uuid,
                Created = DateTimeOffset.Now.ToString(_mcDateTimeFormat)
            };
            if (!string.IsNullOrEmpty(req.Reason)) newItem.Reason = req.Reason;

            list.Add(newItem);
            WriteJsonFile(path, list);
            return Success("玩家封禁成功");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    [HttpPost("banplayer/remove/{id}")]
    public IActionResult RemoveBannedPlayer(uint id, [FromBody] RemovePlayerRequest req)
    {
        try
        {
            var path = Path.Combine(GetServerBasePath(id), "banned-players.json");
            var list = ReadJsonFile<BannedPlayerItem>(path);
            var removed = list.RemoveAll(x => x.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase));

            if (removed > 0) WriteJsonFile(path, list);
            return Success(removed > 0 ? "玩家解封成功" : "该玩家未被封禁");
        }
        catch (Exception e) { return Error(e.Message); }
    }

    #endregion

    #region 一些辅助方法

    private string GetServerBasePath(uint id)
    {
        var server = IConfigBase.ServerList.GetServer(id) ?? throw new Exception("实例不存在");
        return server.Base;
    }

    private List<T> ReadJsonFile<T>(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return new List<T>();
        var json = System.IO.File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>();
    }

    private void WriteJsonFile<T>(string filePath, List<T> data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        System.IO.File.WriteAllText(filePath, json);
    }

    private string ResolveUuid(string basePath, string name, string? providedUuid)
    {
        if (!string.IsNullOrWhiteSpace(providedUuid)) return providedUuid;

        // 如果没传 UUID，尝试去 usercache 找
        var cachePath = Path.Combine(basePath, "usercache.json");
        var cache = ReadJsonFile<UserCacheItem>(cachePath);
        var user = cache.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (user != null && !string.IsNullOrWhiteSpace(user.Uuid)) return user.Uuid;

        throw new Exception($"未提供UUID，且在 usercache 中未找到玩家 '{name}' 的历史记录。");
    }

    private IActionResult Success(object? data = null, string message = "操作成功")
    {
        return Ok(new ApiResponse<object> { Code = 200, Message = message, Data = data });
    }

    private IActionResult Error(string message, int code = 400)
    {
        return BadRequest(new ApiResponse<object> { Code = code, Message = message });
    }

    #endregion

    
}