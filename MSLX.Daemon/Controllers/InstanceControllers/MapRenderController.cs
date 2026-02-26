using fNbt;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils.ConfigUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/map")]
[ApiController]
public class MapRenderController : ControllerBase
{
    private string GetServerBasePath(uint id)
    {
        var server = IConfigBase.ServerList.GetServer(id) ?? throw new Exception("实例不存在");
        return server.Base;
    }

    [HttpGet("spawn/{id}")]
    public IActionResult GetWorldSpawn(uint id)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var levelDatPath = Path.Combine(basePath, "world", "level.dat");

            if (!System.IO.File.Exists(levelDatPath))
            {
                return Ok(new ApiResponse<object> { Code = 200, Data = new { x = 0, z = 0 } });
            }

            using var fs = new FileStream(levelDatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var nbtFile = new NbtFile();
            nbtFile.LoadFromStream(fs, NbtCompression.AutoDetect);

            var dataTag = nbtFile.RootTag.Get<NbtCompound>("Data");
            int spawnX = dataTag?.Get<NbtInt>("SpawnX")?.Value ?? 0;
            int spawnZ = dataTag?.Get<NbtInt>("SpawnZ")?.Value ?? 0;

            return Ok(new ApiResponse<object> { Code = 200, Data = new { x = spawnX, z = spawnZ } });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = e.Message });
        }
    }

    [HttpGet("{id}/{regionX}/{regionZ}")]
    public IActionResult GetRegionMap(uint id, int regionX = 0, int regionZ = 0)
    {
        try
        {
            var basePath = GetServerBasePath(id);
            var mcaFilePath = Path.Combine(basePath, "world", "region", $"r.{regionX}.{regionZ}.mca");

            using var image = new Image<Rgba32>(512, 512);

            if (!System.IO.File.Exists(mcaFilePath))
            {
                return OutputImage(image);
            }

            var parser = new McaParser(mcaFilePath);

            for (int cx = 0; cx < 32; cx++)
            {
                for (int cz = 0; cz < 32; cz++)
                {
                    var chunkTag = parser.GetChunkNbt(cx, cz);
                    if (chunkTag == null) continue;

                    var level = chunkTag.Get<NbtCompound>("Level") ?? chunkTag;
                    var heightmaps = level.Get<NbtCompound>("Heightmaps");
                    if (heightmaps == null) continue;

                    // 🚨 终极强化：三图齐下！
                    var surfaceData = heightmaps.Get<NbtLongArray>("WORLD_SURFACE")?.Value;
                    var motionData = heightmaps.Get<NbtLongArray>("MOTION_BLOCKING")?.Value;
                    var floorData = heightmaps.Get<NbtLongArray>("OCEAN_FLOOR")?.Value;

                    // 如果任何一个核心数据丢失，说明区块生成异常，跳过
                    if (surfaceData == null || motionData == null || floorData == null) continue;

                    int[] worldSurface = UnpackHeightmap(surfaceData);
                    int[] motionBlocking = UnpackHeightmap(motionData);
                    int[] oceanFloor = UnpackHeightmap(floorData);

                    for (int x = 0; x < 16; x++)
                    {
                        for (int z = 0; z < 16; z++)
                        {
                            int idx = z * 16 + x;

                            // 获取真实的三层高度 (-65 是修正 1.18+ 的最低点偏移)
                            int wsY = worldSurface[idx] - 65;
                            int mbY = motionBlocking[idx] - 65;
                            int ofY = oceanFloor[idx] - 65;

                            // 1. 三层叠加渲染：算出地表、植被和水深的复合色彩
                            Rgba32 baseColor = GetUltraAdvancedColor(wsY, mbY, ofY);

                            // 2. 3D 阴影算法 (基于物理碰撞层 MOTION_BLOCKING 算阴影最平滑)
                            int nwMbY = mbY;
                            if (x > 0 && z > 0) nwMbY = motionBlocking[(z - 1) * 16 + (x - 1)] - 65;
                            else if (x > 0) nwMbY = motionBlocking[z * 16 + (x - 1)] - 65;
                            else if (z > 0) nwMbY = motionBlocking[(z - 1) * 16 + x] - 65;

                            int heightDiff = mbY - nwMbY;
                            Rgba32 finalColor = ApplyHillshading(baseColor, heightDiff);

                            // 写入图片像素
                            int pixelX = cx * 16 + x;
                            int pixelZ = cz * 16 + z;
                            image[pixelX, pixelZ] = finalColor;
                        }
                    }
                }
            }

            return OutputImage(image);
        }
        catch (Exception e)
        {
            return BadRequest(new { Code = 400, Message = e.Message });
        }
    }

    private IActionResult OutputImage(Image image)
    {
        var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return File(ms.ToArray(), "image/png");
    }

    // 🚀 终极地貌渲染引擎：结合了底表、水深与植被的点阵渲染
    private Rgba32 GetUltraAdvancedColor(int wsY, int mbY, int ofY)
    {
        Rgba32 baseColor;

        // 【判断一】：这里是水域还是陆地？
        if (mbY > ofY)
        {
            // 是水域！计算水深
            int depth = mbY - ofY;
            if (depth > 25) baseColor = new Rgba32(10, 30, 80);    // 深海沟
            else if (depth > 12) baseColor = new Rgba32(15, 60, 150);   // 远洋深海
            else if (depth > 5) baseColor = new Rgba32(35, 110, 200);   // 近海
            else if (depth > 2) baseColor = new Rgba32(60, 160, 230);   // 浅水
            else baseColor = new Rgba32(100, 200, 230);                 // 浅滩透底
        }
        else
        {
            // 是陆地！根据海拔生成底色
            if (ofY <= 64) baseColor = new Rgba32(230, 210, 160);       // 海岸沙滩
            else if (ofY < 85) baseColor = new Rgba32(95, 175, 75);     // 低海拔平原
            else if (ofY < 110) baseColor = new Rgba32(65, 130, 55);    // 中海拔森林/丘陵
            else if (ofY < 140) baseColor = new Rgba32(120, 125, 120);  // 高海拔岩石
            else if (ofY < 180) baseColor = new Rgba32(180, 185, 190);  // 极高海拔雪山
            else baseColor = new Rgba32(245, 250, 255);                 // 雪峰顶端
        }

        // 【判断二】：陆地上有没有附着物（植被、花草、积雪层）？
        // 如果 WORLD_SURFACE > MOTION_BLOCKING 且这里是陆地，说明有花草或雪！
        if (wsY > mbY && mbY == ofY)
        {
            if (ofY > 100)
            {
                // 高海拔的附着物，大概率是积雪，我们将底色向白色稍微混合，形成“斑驳的白雪点点”
                baseColor = BlendColor(baseColor, new Rgba32(255, 255, 255), 0.4f);
            }
            else
            {
                // 低海拔的附着物，大概率是草丛/花朵，我们将底色向亮黄绿色混合，形成“茂盛的草丛质感”
                baseColor = BlendColor(baseColor, new Rgba32(160, 220, 60), 0.35f);
            }
        }

        return baseColor;
    }

    // 颜色混合器（用于生成植被和积雪的半透明叠加效果）
    private Rgba32 BlendColor(Rgba32 bottom, Rgba32 top, float ratio)
    {
        byte r = (byte)(bottom.R * (1 - ratio) + top.R * ratio);
        byte g = (byte)(bottom.G * (1 - ratio) + top.G * ratio);
        byte b = (byte)(bottom.B * (1 - ratio) + top.B * ratio);
        return new Rgba32(r, g, b, bottom.A);
    }

    // 3D 光影遮蔽算法 (Hillshading)
    private Rgba32 ApplyHillshading(Rgba32 color, int diff)
    {
        if (diff == 0) return color;

        float factor = 1.0f;
        if (diff > 0)
        {
            // 迎光坡，提亮
            factor = 1.0f + (Math.Min(diff, 5) * 0.06f);
        }
        else if (diff < 0)
        {
            // 背光坡，压暗
            factor = 1.0f - (Math.Min(-diff, 5) * 0.06f);
        }

        byte r = (byte)Math.Clamp(color.R * factor, 0, 255);
        byte g = (byte)Math.Clamp(color.G * factor, 0, 255);
        byte b = (byte)Math.Clamp(color.B * factor, 0, 255);

        return new Rgba32(r, g, b, color.A);
    }

    private int[] UnpackHeightmap(long[] data)
    {
        int[] heights = new int[256];
        int bitsPerEntry = 9;
        int entriesPerLong = 64 / bitsPerEntry;
        long mask = (1L << bitsPerEntry) - 1;

        int index = 0;
        foreach (long value in data)
        {
            long temp = value;
            for (int j = 0; j < entriesPerLong && index < 256; j++)
            {
                heights[index++] = (int)(temp & mask);
                temp >>= bitsPerEntry;
            }
        }
        return heights;
    }

    private class McaParser
    {
        private readonly byte[] _fileData;
        public McaParser(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            _fileData = ms.ToArray();
        }

        public NbtCompound? GetChunkNbt(int localX, int localZ)
        {
            if (_fileData == null || _fileData.Length < 8192) return null;

            int offsetLocation = 4 * ((localX & 31) + (localZ & 31) * 32);
            if (offsetLocation + 3 >= _fileData.Length) return null;

            int offset = (_fileData[offsetLocation] << 16) | (_fileData[offsetLocation + 1] << 8) | _fileData[offsetLocation + 2];
            int sectors = _fileData[offsetLocation + 3];

            if (offset == 0 && sectors == 0) return null;

            int dataOffset = offset * 4096;
            if (dataOffset + 4 >= _fileData.Length) return null;

            int length = (_fileData[dataOffset] << 24) | (_fileData[dataOffset + 1] << 16) | (_fileData[dataOffset + 2] << 8) | _fileData[dataOffset + 3];
            byte compressionType = _fileData[dataOffset + 4];

            if (dataOffset + 5 + length - 1 > _fileData.Length) return null;

            using var ms = new MemoryStream(_fileData, dataOffset + 5, length - 1);
            Stream decompressedStream;

            if (compressionType == 1) decompressedStream = new GZipStream(ms, CompressionMode.Decompress);
            else if (compressionType == 2) decompressedStream = new ZLibStream(ms, CompressionMode.Decompress);
            else decompressedStream = ms;

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromStream(decompressedStream, NbtCompression.None);
                return nbtFile.RootTag;
            }
            catch
            {
                return null;
            }
        }
    }
}