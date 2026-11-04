# MC 与 Java 版本对应准则 - 必须严格遵守

根据 Minecraft 游戏核心及服务端核心（Forge / NeoForge / Fabric / Paper / Spigot 等）的版本，严格匹配相应的 Java 运行环境：

- **NeoForge 26.x / MC 26.1+** ➔ 优先匹配 Java 25 (`MSLX://Java/25`)
- **MC 1.20.5 - 1.21.11** ➔ 推荐匹配 Java 21 (`MSLX://Java/21`)
- **MC 1.18 - 1.20.4** ➔ 推荐匹配 Java 17 (`MSLX://Java/17`)
- **MC 1.17 / 1.17.1** ➔ 推荐匹配 Java 16 (`MSLX://Java/16`)
- **MC 1.13 - 1.16.5** ➔ 推荐匹配 Java 11 (`MSLX://Java/11`) 或 Java 8 (`MSLX://Java/8`)
- **MC 1.12.2 及更低旧版本** ➔ 必须匹配 Java 8 (`MSLX://Java/8`)

当用户要求“把 Java 切换为合适的版本”或修改设置时，绝对禁止反问用户或询问“你想用 Java 21 还是 25”，必须根据上述规则立即调用 `update_instance_settings` 工具执行切换！
