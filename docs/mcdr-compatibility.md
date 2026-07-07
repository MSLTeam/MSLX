# MSLX 兼容 MCDReforged (MCDR) 指南

本文档说明 MSLX 面板对 [MCDReforged](https://github.com/MCDReforged/MCDReforged)(下称 MCDR)的兼容方案,包含**一键创建(推荐)**与**手动接入**两种方式,以及底层实现与注意事项。

> 适用版本:MSLX ≥ 1.4.8(dev)。运行 MCDR 需要宿主机安装 **Python 3.8+**。

---

## 一、MCDR 是什么,为什么需要特殊处理

MCDR 是一个基于 Python 的**服务端进程包装器**:面板启动的不是 `java`,而是 `python -m mcdreforged start`;MCDR 再根据自身 `config.yml` 中的 `start_command` 拉起真正的 Minecraft 服务端,并在中间转发 stdin/stdout、提供插件系统。

因此 MCDR 实例的目录结构与普通实例不同:

```
实例根目录/
├── config.yml          # MCDR 主配置(start_command / handler / 编码等)
├── permission.yml      # MCDR 权限
├── plugins/            # MCDR 插件(.mcdr / .py),≠ 服务端插件
├── logs/               # MCDR 日志
└── server/             # ← 真实 MC 服务端工作目录(working_directory)
    ├── server.jar
    ├── server.properties
    ├── eula.txt
    ├── plugins/        # 服务端插件(Paper/Spigot)
    ├── mods/           # 服务端模组(Forge/Fabric)
    └── world/          # 存档
```

这带来两个关键差异,MSLX 已针对性适配:

1. **服务端相关文件都在 `server/` 子目录**,而非实例根目录。
2. **进程链多了一层 Python**:`cmd/bash → python(MCDR) → java`。

---

## 二、一键创建(推荐)

在 **创建服务端实例** 页面选择 **MCDR** 模式:

1. **实例名称 / 路径**:同普通实例。
2. **Python 环境**:面板会自动扫描本机 Python 并检测其是否已安装 MCDR。选择一个即可;若列表为空,切换到"自定义命令/路径"手动填写(如 `python3` 或绝对路径)。
3. **内部服务端核心**:与快速模式一致,可在线选择(Paper/Vanilla/Fabric/Forge…)、上传本地 jar 或填自定义文件名。核心会被部署进 `server/` 子目录。
4. **内部服务端 Java 环境**:为真实 MC 服务端选择 Java(在线下载 / 本机 / 环境变量 / 自定义路径)。
5. **内存**:真实服务端的 `-Xms/-Xmx`。
6. **高级选项**(可选):
   - **Handler**:默认按核心文件名自动推断(见附录),也可手动指定。
   - **额外 JVM 参数**。
   - **自动安装 MCDReforged**:默认开启,创建时执行 `pip install -U mcdreforged`。
   - **pip 镜像源**:默认清华源,可清空使用官方源。

点击提交后,后端会依次:检测 Python → (可选)安装 MCDR → 部署 Java → 部署核心到 `server/` → 生成 `config.yml` / `permission.yml`。

创建完成后进入实例即可启动。**首次启动**若未同意 EULA,面板会弹出 EULA 提示,同意后会在 `server/eula.txt` 写入并继续启动。

---

## 三、手动接入(适用于已有 MCDR 实例 / 自定义模式)

如果你已经有一个搭好的 MCDR 目录,或想用"自定义模式"接入,可按下述步骤:

### 1. 准备 MCDR

```bash
pip install mcdreforged          # 安装
cd <实例目录>
python -m mcdreforged init       # 生成 server/、config.yml、permission.yml、plugins/
# 将服务端核心放入 server/,配置好 config.yml 的 start_command / handler
```

### 2. 在 MSLX 用「自定义模式」创建实例,或修改现有实例设置

关键设置项如下:

| 设置项 | 值 | 说明 |
|---|---|---|
| Java | `none` | 走自定义启动通道 |
| 启动指令 (Args) | `python -m mcdreforged start` | 实例的实际启动命令 |
| 停止命令 (StopCommand) | `stop` | MCDR 会把 `stop` 转发给服务端,服务端退出后 MCDR 自身退出 |
| server.properties 路径 | `server/server.properties` | 指向 `server/` 子目录 |
| 插件目录 | `server/plugins` | 服务端插件(非 MCDR 插件) |
| 模组目录 | `server/mods` | |
| 地图目录 | `server/world` | |
| 输入编码 | `utf-8` | |
| 输出编码 | `utf-8` | 配合下方 Python UTF-8 环境变量 |

> MSLX 在自定义模式(Java=none)下会自动注入 `PYTHONIOENCODING=utf-8` 与 `PYTHONUTF8=1`,因此 Windows 上也无需担心 Python 默认 GBK 导致的乱码。

### 3. config.yml 关键项

```yaml
working_directory: server
start_command: java -Xms1G -Xmx2G -jar server.jar nogui
handler: bukkit_handler          # 按核心类型选择,见附录
encoding: utf8                   # MCDR -> 服务端
decoding: utf8                   # 服务端 -> MCDR
advanced_console: false          # ★ 必须为 false:MSLX 重定向了 stdio,开启会导致无法收发
```

> **`advanced_console: false` 是硬性要求。** MCDR 的高级控制台(prompt-toolkit)会独占终端,与 MSLX 的管道重定向冲突,必须关闭。一键创建模式已自动设为 `false`。

---

## 四、底层适配说明(实现细节)

面板侧为兼容 MCDR 做了以下改动:

1. **EULA 检查跟随 server.properties 目录**
   `eula.txt` 的读写位置不再写死为实例根目录,而是取 `server.properties` 所在目录(二者永远同级)。MCDR 布局下即 `server/eula.txt`。
   相关:`ServerPropertiesPathUtils.ResolveEulaPath`。

2. **进程监控递归穿透 Python 层**
   内存/CPU 监控与强制结束会从启动的 `cmd`/`bash` 逐层向下查找,穿过 `python(MCDR)` 找到真正的 `java` 进程(Windows 走 WMI,Linux 走 `pgrep -P`,均带递归与深度保护)。

3. **注入 Python UTF-8 环境变量**
   自定义模式(Java=none)启动时注入 `PYTHONIOENCODING=utf-8`、`PYTHONUTF8=1`,解决 Windows 控制台中文乱码。

4. **一键部署与 config.yml 生成**
   `ServerDeploymentService.DeployMcdrAsync` 负责搭建 `server/` 布局、安装 MCDR、部署核心并生成 `config.yml`;`McdrConfigGenerator` 负责 handler 推断与配置文本生成。

---

## 附录:Handler 与核心类型对照

| 核心类型 | handler |
|---|---|
| 原版 / Fabric / Quilt / Carpet | `vanilla_handler` |
| Paper / Spigot / Purpur / Folia / Leaves / Mohist | `bukkit_handler` |
| Bukkit / Spigot 1.14+ | `bukkit14_handler` |
| Forge / NeoForge | `forge_handler` |
| Arclight | `arclight_handler` |
| CatServer | `cat_server_handler` |
| BungeeCord | `bungeecord_handler` |
| Waterfall | `waterfall_handler` |
| Velocity | `velocity_handler` |

一键创建模式会根据核心文件名自动推断,推断失败时回退 `vanilla_handler`,可在高级选项手动覆盖。

---

## 常见问题

- **启动后控制台无输出 / 卡住**:检查 `config.yml` 的 `advanced_console` 是否为 `false`。
- **中文乱码**:确认输出编码为 `utf-8`(一键模式已默认);手动接入务必用自定义模式(Java=none)以触发 UTF-8 环境变量注入。
- **提示 EULA 但反复失败**:确认 `server.properties 路径` 指向 `server/server.properties`,面板会据此在 `server/eula.txt` 写入同意。
- **内存显示为 0 或偏小**:面板已递归定位到 java 进程;若仍异常,确认 MCDR 已成功拉起服务端。
- **MCDR 未安装**:进入实例目录执行 `pip install mcdreforged`,或重新以一键模式创建并开启"自动安装"。
