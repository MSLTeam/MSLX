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
    private static readonly Dictionary<string, Rgba32> BlockColorMap = new()
    {
        // ================= 💧 水与熔岩 =================
        { "minecraft:water", new Rgba32(35, 137, 218) },
        { "minecraft:lava", new Rgba32(230, 90, 20) },

        // ================= 🌍 基础地表与土壤 =================
        { "minecraft:grass_block", new Rgba32(121, 192, 90) },
        { "minecraft:dirt", new Rgba32(150, 108, 74) },
        { "minecraft:coarse_dirt", new Rgba32(119, 85, 59) },
        { "minecraft:podzol", new Rgba32(90, 60, 20) },
        { "minecraft:mycelium", new Rgba32(111, 99, 105) },
        { "minecraft:mud", new Rgba32(60, 57, 61) },
        { "minecraft:dirt_path", new Rgba32(150, 125, 90) },
        { "minecraft:farmland", new Rgba32(110, 70, 40) },   

        // ================= 🏜️ 沙漠与恶地 =================
        { "minecraft:sand", new Rgba32(238, 214, 175) },
        { "minecraft:red_sand", new Rgba32(210, 117, 52) },
        { "minecraft:sandstone", new Rgba32(223, 201, 138) },
        { "minecraft:cut_sandstone", new Rgba32(223, 201, 138) },
        { "minecraft:smooth_sandstone", new Rgba32(223, 201, 138) },
        { "minecraft:chiseled_sandstone", new Rgba32(223, 201, 138) },
        { "minecraft:terracotta", new Rgba32(152, 94, 67) },
        { "minecraft:cactus", new Rgba32(85, 125, 45) },
        { "minecraft:dead_bush", new Rgba32(143, 100, 48) },

        // ================= 🪨 岩石与矿石 =================
        { "minecraft:stone", new Rgba32(125, 125, 125) },
        { "minecraft:smooth_stone", new Rgba32(125, 125, 125) },
        { "minecraft:cobblestone", new Rgba32(100, 100, 100) },
        { "minecraft:mossy_cobblestone", new Rgba32(90, 110, 90) },
        { "minecraft:stone_bricks", new Rgba32(110, 110, 110) },
        { "minecraft:deepslate", new Rgba32(80, 80, 85) },
        { "minecraft:cobbled_deepslate", new Rgba32(70, 70, 75) },
        { "minecraft:tuff", new Rgba32(108, 113, 112) },
        { "minecraft:granite", new Rgba32(150, 110, 100) },
        { "minecraft:diorite", new Rgba32(180, 180, 185) },
        { "minecraft:andesite", new Rgba32(130, 130, 135) },
        { "minecraft:gravel", new Rgba32(130, 130, 130) },
        { "minecraft:clay", new Rgba32(160, 166, 179) },
        { "minecraft:obsidian", new Rgba32(20, 15, 30) },

        // ================= ❄️ 冰雪地貌 =================
        { "minecraft:snow", new Rgba32(255, 255, 255) },
        { "minecraft:snow_block", new Rgba32(255, 255, 255) },
        { "minecraft:powder_snow", new Rgba32(255, 255, 255) },
        { "minecraft:ice", new Rgba32(170, 210, 255) },
        { "minecraft:packed_ice", new Rgba32(140, 180, 255) },
        { "minecraft:blue_ice", new Rgba32(116, 168, 253) },

        // ================= 🍃 树叶与植物 =================
        { "minecraft:oak_leaves", new Rgba32(72, 181, 72) },
        { "minecraft:spruce_leaves", new Rgba32(97, 153, 97) },
        { "minecraft:birch_leaves", new Rgba32(104, 143, 104) },
        { "minecraft:jungle_leaves", new Rgba32(89, 168, 48) },
        { "minecraft:acacia_leaves", new Rgba32(96, 166, 73) },
        { "minecraft:dark_oak_leaves", new Rgba32(66, 140, 38) },
        { "minecraft:mangrove_leaves", new Rgba32(133, 165, 59) },
        { "minecraft:cherry_leaves", new Rgba32(246, 185, 206) },
        { "minecraft:azalea_leaves", new Rgba32(115, 168, 86) },
        { "minecraft:flowering_azalea_leaves", new Rgba32(125, 150, 100) },
        { "minecraft:short_grass", new Rgba32(100, 160, 50) },
        { "minecraft:tall_grass", new Rgba32(100, 160, 50) },
        { "minecraft:fern", new Rgba32(100, 160, 50) },
        { "minecraft:large_fern", new Rgba32(100, 160, 50) },
        { "minecraft:lily_pad", new Rgba32(32, 128, 48) },
        { "minecraft:sugar_cane", new Rgba32(148, 204, 72) },
        { "minecraft:wheat", new Rgba32(200, 180, 50) },
        { "minecraft:carrots", new Rgba32(230, 130, 30) },
        { "minecraft:potatoes", new Rgba32(160, 190, 80) },

        // ================= 🪵 木板 (基础建筑块) =================
        { "minecraft:oak_planks", new Rgba32(160, 130, 75) },
        { "minecraft:spruce_planks", new Rgba32(110, 80, 45) },
        { "minecraft:birch_planks", new Rgba32(195, 180, 130) },
        { "minecraft:jungle_planks", new Rgba32(150, 110, 75) },
        { "minecraft:acacia_planks", new Rgba32(160, 85, 55) },
        { "minecraft:dark_oak_planks", new Rgba32(65, 40, 20) },
        { "minecraft:mangrove_planks", new Rgba32(115, 50, 50) },
        { "minecraft:cherry_planks", new Rgba32(225, 175, 180) },
        { "minecraft:bamboo_planks", new Rgba32(210, 195, 100) },

        // ================= 🌲 原木与去皮原木 =================
        { "minecraft:oak_log", new Rgba32(115, 90, 60) },
        { "minecraft:stripped_oak_log", new Rgba32(160, 130, 75) },
        { "minecraft:spruce_log", new Rgba32(60, 40, 20) },
        { "minecraft:stripped_spruce_log", new Rgba32(110, 80, 45) },
        { "minecraft:birch_log", new Rgba32(215, 215, 210) },
        { "minecraft:stripped_birch_log", new Rgba32(195, 180, 130) },
        { "minecraft:jungle_log", new Rgba32(85, 65, 25) },
        { "minecraft:acacia_log", new Rgba32(105, 100, 90) },
        { "minecraft:dark_oak_log", new Rgba32(45, 30, 15) },
        { "minecraft:mangrove_log", new Rgba32(75, 40, 40) },
        { "minecraft:cherry_log", new Rgba32(50, 30, 40) },

        // ================= 🧱 其他人造/杂项方块 =================
        { "minecraft:bricks", new Rgba32(150, 90, 70) },
        { "minecraft:quartz_block", new Rgba32(235, 230, 225) },
        { "minecraft:glass", new Rgba32(200, 230, 255) },
        { "minecraft:hay_block", new Rgba32(200, 170, 30) },
        { "minecraft:bell", new Rgba32(240, 200, 80) },
        { "minecraft:white_wool", new Rgba32(230, 230, 230) },
        { "minecraft:iron_block", new Rgba32(200, 200, 200) }
    };

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
                return Ok(new ApiResponse<object> { Code = 200, Data = new { x = 0, z = 0 } });

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
            if (!System.IO.File.Exists(mcaFilePath)) return OutputImage(image);

            var parser = new McaParser(mcaFilePath);

            // 💡 初始化环境记忆色：默认给个草地绿，防止第一格就不认识
            Rgba32 lastKnownColor = new Rgba32(121, 192, 90);

            for (int cx = 0; cx < 32; cx++)
            {
                for (int cz = 0; cz < 32; cz++)
                {
                    var chunkTag = parser.GetChunkNbt(cx, cz);
                    if (chunkTag == null) continue;

                    var level = chunkTag.Get<NbtCompound>("Level") ?? chunkTag;

                    var heightmaps = level.Get<NbtCompound>("Heightmaps");
                    if (heightmaps == null) continue;

                    var motionData = heightmaps.Get<NbtLongArray>("MOTION_BLOCKING")?.Value;
                    var floorData = heightmaps.Get<NbtLongArray>("OCEAN_FLOOR")?.Value;
                    if (motionData == null || floorData == null) continue;

                    int[] topHeights = UnpackHeightmap(motionData);
                    int[] floorHeights = UnpackHeightmap(floorData);

                    var sections = level.Get<NbtList>("sections") ?? level.Get<NbtList>("Sections");
                    if (sections == null) continue;

                    for (int x = 0; x < 16; x++)
                    {
                        for (int z = 0; z < 16; z++)
                        {
                            int idx = z * 16 + x;
                            int topY = topHeights[idx] - 65;
                            int floorY = floorHeights[idx] - 65;

                            string blockId = GetBlockNameAtY(sections, x, topY, z);
                            Rgba32 baseColor;

                            // 🌊 核心 1：水深动态探测逻辑
                            if (blockId == "minecraft:water" || blockId == "minecraft:bubble_column")
                            {
                                int depth = topY - floorY;
                                if (depth > 25) baseColor = new Rgba32(10, 30, 80);
                                else if (depth > 12) baseColor = new Rgba32(15, 60, 150);
                                else if (depth > 5) baseColor = new Rgba32(35, 110, 200);
                                else if (depth > 2) baseColor = new Rgba32(60, 160, 230);
                                else baseColor = new Rgba32(100, 200, 230);
                            }
                            else
                            {
                                // 🌳 核心 2：智能后缀截断
                                string baseId = blockId;
                                if (baseId.EndsWith("_stairs")) baseId = baseId.Replace("_stairs", "");
                                else if (baseId.EndsWith("_slab")) baseId = baseId.Replace("_slab", "");
                                else if (baseId.EndsWith("_wall")) baseId = baseId.Replace("_wall", "");
                                else if (baseId.EndsWith("_fence")) baseId = baseId.Replace("_fence", "");
                                else if (baseId.EndsWith("_gate")) baseId = baseId.Replace("_gate", "");

                                // 🔎 去字典里智能匹配
                                if (BlockColorMap.TryGetValue(blockId, out var color))
                                    baseColor = color;
                                else if (BlockColorMap.TryGetValue(baseId, out color))
                                    baseColor = color;
                                else if (BlockColorMap.TryGetValue(baseId + "_planks", out color))
                                    baseColor = color;
                                else if (BlockColorMap.TryGetValue(baseId + "_block", out color))
                                    baseColor = color;
                                else
                                {
                                    // 🚨 核心改进：如果不认识这个方块，直接使用旁边方块的颜色伪装自己！
                                    baseColor = lastKnownColor;
                                }
                            }

                            // 更新最后一次成功匹配的环境色
                            lastKnownColor = baseColor;

                            // ⛰️ 核心 3：3D 阴影算法保留
                            int nwY = topY;
                            if (x > 0 && z > 0) nwY = topHeights[(z - 1) * 16 + (x - 1)] - 65;
                            else if (x > 0) nwY = topHeights[z * 16 + (x - 1)] - 65;
                            else if (z > 0) nwY = topHeights[(z - 1) * 16 + x] - 65;

                            Rgba32 finalColor = ApplyHillshading(baseColor, topY - nwY);

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

    private Rgba32 ApplyHillshading(Rgba32 color, int diff)
    {
        if (diff == 0) return color;
        float factor = 1.0f + Math.Clamp(diff * 0.05f, -0.2f, 0.2f);

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

    private string GetBlockNameAtY(NbtList sections, int x, int y, int z)
    {
        int sectionY = y >> 4;

        foreach (NbtCompound sec in sections)
        {
            var yTag = sec.Get<NbtByte>("Y")?.Value;
            if (yTag != sectionY) continue;

            var blockStates = sec.Get<NbtCompound>("block_states");
            if (blockStates == null) return "minecraft:air";

            var palette = blockStates.Get<NbtList>("palette");
            if (palette == null) return "minecraft:air";

            var data = blockStates.Get<NbtLongArray>("data")?.Value;

            if (data == null || data.Length == 0)
            {
                var singleBlock = palette[0] as NbtCompound;
                return singleBlock?.Get<NbtString>("Name")?.Value ?? "minecraft:air";
            }

            int localY = y & 15;
            int blockIndex = (localY * 256) + (z * 16) + x;

            int paletteSize = palette.Count;
            int bitsPerEntry = Math.Max(4, (int)Math.Ceiling(Math.Log2(paletteSize)));
            int entriesPerLong = 64 / bitsPerEntry;

            int longIndex = blockIndex / entriesPerLong;
            int startBit = (blockIndex % entriesPerLong) * bitsPerEntry;

            if (longIndex >= data.Length) return "minecraft:air";

            long val = data[longIndex];
            long mask = (1L << bitsPerEntry) - 1;
            int paletteIndex = (int)((val >> startBit) & mask);

            if (paletteIndex >= paletteSize) return "minecraft:air";

            var targetBlock = palette[paletteIndex] as NbtCompound;
            return targetBlock?.Get<NbtString>("Name")?.Value ?? "minecraft:air";
        }
        return "minecraft:air";
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