export type PropertyType = 'string' | 'number' | 'boolean' | 'select';

export interface PropertySchema {
  key: string;
  label: string; // 中文名
  desc?: string; // 详细解释
  type: PropertyType;
  options?: { label: string; value: string | number }[]; // 仅 select 类型需要
}

export const SERVER_PROPERTIES_SCHEMA: PropertySchema[] = [
  // ==========================================
  // 基础设置 (Basic)
  // ==========================================
  {
    key: 'motd',
    label: '服务器标语',
    desc: '显示在多人游戏列表中的服务器介绍信息。（支持中文 & 颜色代码，但是务必将文件编码修改为UTF-8并启用强制UTF8功能，否则会乱码）',
    type: 'string',
  },
  {
    key: 'server-port',
    label: '服务器端口',
    desc: '默认为 25565。如果要在一台机器运行多个服务器，必须修改此端口。',
    type: 'number',
  },
  {
    key: 'max-players',
    label: '最大玩家数',
    desc: '服务器同时允许在线的最大玩家数量。',
    type: 'number',
  },
  {
    key: 'online-mode',
    label: '正版验证',
    desc: '开启后将验证玩家的正版账号。如果使用离线模式/登录插件，请关闭此项。如果使用外置登录或者正版账户登录，请启用此项。',
    type: 'boolean',
  },
  {
    key: 'white-list',
    label: '启用白名单',
    desc: '开启后只有在白名单内的玩家才能进入服务器。',
    type: 'boolean',
  },
  {
    key: 'enforce-whitelist',
    label: '强制白名单',
    desc: '开启后，当玩家不在白名单时，即使已在线也会被踢出（通常用于维护时重新加载白名单）。',
    type: 'boolean',
  },
  {
    key: 'level-name',
    label: '存档文件夹名称',
    desc: '服务器读取的世界存档文件夹名称（默认为 world）。',
    type: 'string',
  },

  // ==========================================
  // 游戏规则 (Gameplay)
  // ==========================================
  {
    key: 'gamemode',
    label: '默认游戏模式',
    desc: '新玩家进入服务器时的默认模式。',
    type: 'select',
    options: [
      { label: '生存 (Survival)', value: 'survival' },
      { label: '创造 (Creative)', value: 'creative' },
      { label: '冒险 (Adventure)', value: 'adventure' },
      { label: '旁观 (Spectator)', value: 'spectator' },
    ],
  },
  {
    key: 'force-gamemode',
    label: '强制游戏模式',
    desc: '开启后，玩家每次加入服务器都会被重置为默认游戏模式。',
    type: 'boolean',
  },
  {
    key: 'difficulty',
    label: '难度',
    desc: '世界的游戏难度设置。',
    type: 'select',
    options: [
      { label: '和平 (Peaceful)', value: 'peaceful' },
      { label: '简单 (Easy)', value: 'easy' },
      { label: '普通 (Normal)', value: 'normal' },
      { label: '困难 (Hard)', value: 'hard' },
    ],
  },
  {
    key: 'hardcore',
    label: '极限模式',
    desc: '开启后，玩家死亡将被封禁。',
    type: 'boolean',
  },
  {
    key: 'allow-flight',
    label: '允许飞行',
    desc: '允许生存模式下的玩家飞行（防止被服务端反作弊踢出）。',
    type: 'boolean',
  },
  {
    key: 'spawn-protection',
    label: '出生点保护半径',
    desc: '出生点周围多少格内禁止非 OP 破坏。设为 0 可禁用。',
    type: 'number',
  },
  {
    key: 'player-idle-timeout',
    label: '挂机踢出时间',
    desc: '玩家闲置多少分钟后被自动踢出。0 为不限制。',
    type: 'number',
  },

  // ==========================================
  // 世界生成 (World Generation)
  // ==========================================
  {
    key: 'level-seed',
    label: '世界种子',
    desc: '生成世界使用的种子，留空则随机生成。',
    type: 'string',
  },
  {
    key: 'level-type',
    label: '世界类型',
    desc: '例如 minecraft:normal, minecraft:flat, minecraft:amplified。',
    type: 'string',
  },
  {
    key: 'generate-structures',
    label: '生成结构',
    desc: '是否生成村庄、地牢等结构。',
    type: 'boolean',
  },
  {
    key: 'generator-settings',
    label: '生成器设置',
    desc: '用于自定义超平坦或特定生成器的 JSON 参数。',
    type: 'string',
  },
  {
    key: 'max-world-size',
    label: '世界边界半径',
    desc: '设置世界边界（World Border）的最大半径。',
    type: 'number',
  },
  {
    key: 'simulation-distance',
    label: '模拟距离',
    desc: '服务器实际运算实体/作物生长的区块半径（3-32）。',
    type: 'number',
  },
  {
    key: 'view-distance',
    label: '视距',
    desc: '客户端可以看见的区块半径。数值过大显著增加内存和带宽消耗。',
    type: 'number',
  },
  {
    key: 'entity-broadcast-range-percentage',
    label: '实体广播范围百分比',
    desc: '控制客户端能看到实体的距离系数（100表示默认）。',
    type: 'number',
  },

  // ==========================================
  // 性能与网络 (Performance & Network)
  // ==========================================
  {
    key: 'max-tick-time',
    label: '最大刻耗时 (Watchdog)',
    desc: '一刻的最长处理毫秒数。超过此数值服务器将强制关闭以防卡死。设为 -1 可禁用。',
    type: 'number',
  },
  {
    key: 'network-compression-threshold',
    label: '网络压缩阈值',
    desc: '数据包超过此字节数时进行压缩。设为 -1 禁用压缩。推荐保持默认 256。',
    type: 'number',
  },
  {
    key: 'rate-limit',
    label: '数据包限制',
    desc: '如果玩家发送数据包过快，将被踢出。0 为禁用。',
    type: 'number',
  },
  {
    key: 'use-native-transport',
    label: '使用原生传输优化',
    desc: 'Linux 环境下是否启用 Epoll 优化网络性能。',
    type: 'boolean',
  },
  {
    key: 'prevent-proxy-connections',
    label: '防止代理连接',
    desc: '是否尝试阻止通过 VPN 或代理的连接（ISP/AS 号判断）。',
    type: 'boolean',
  },
  {
    key: 'sync-chunk-writes',
    label: '同步区块写入',
    desc: '开启后区块写入完成后才继续逻辑，数据更安全但可能掉帧；关闭可能提升性能。',
    type: 'boolean',
  },
  {
    key: 'region-file-compression',
    label: '区块文件压缩格式',
    desc: '用于保存区块的压缩算法。',
    type: 'select',
    options: [
      { label: 'Deflate (默认)', value: 'deflate' },
      { label: 'LZ4 (更快)', value: 'lz4' },
      { label: '不压缩 (None)', value: 'none' },
    ],
  },
  {
    key: 'max-chained-neighbor-updates',
    label: '最大连锁更新数',
    desc: '限制红石/方块连锁更新的数量以防止崩服。',
    type: 'number',
  },
  {
    key: 'log-ips',
    label: '控制台记录 IP',
    desc: '是否在控制台日志中显示玩家连接的 IP 地址。',
    type: 'boolean',
  },
  {
    key: 'hide-online-players',
    label: '隐藏在线玩家列表',
    desc: '开启后，服务器列表中将不显示具体的玩家名单。',
    type: 'boolean',
  },
  {
    key: 'enable-status',
    label: '启用状态查询',
    desc: '是否允许外部（如服务器列表网站）查询服务器状态。',
    type: 'boolean',
  },
  {
    key: 'accepts-transfers',
    label: '接受服务器传送',
    desc: '是否允许玩家从其他服务器无缝传送到此服务器。',
    type: 'boolean',
  },
  {
    key: 'pause-when-empty-seconds',
    label: '空载暂停时间',
    desc: '服务器内无玩家多少秒后暂停游戏循环（省资源）。-1 为不暂停。',
    type: 'number',
  },

  // ==========================================
  // 安全与权限 (Security)
  // ==========================================
  {
    key: 'op-permission-level',
    label: 'OP 权限等级',
    desc: '设置 OP 的默认权限级别。',
    type: 'select',
    options: [
      { label: '1 - 无视出生点保护', value: 1 },
      { label: '2 - 使用单机指令 (gamemode等)', value: 2 },
      { label: '3 - 多人管理 (kick/ban/op)', value: 3 },
      { label: '4 - 系统管理 (stop/save)', value: 4 },
    ],
  },
  {
    key: 'function-permission-level',
    label: '函数权限等级',
    desc: '数据包(Datapack)中函数的默认执行权限等级。',
    type: 'number',
  },
  {
    key: 'enforce-secure-profile',
    label: '强制安全配置 (签名)',
    desc: '强制玩家拥有官方签名的公钥（聊天报告相关）。离线服建议关闭。',
    type: 'boolean',
  },
  {
    key: 'enable-code-of-conduct',
    label: '启用行为准则提示',
    desc: '是否向玩家展示行为准则链接（遥测相关）。',
    type: 'boolean',
  },
  {
    key: 'bug-report-link',
    label: 'Bug 反馈链接',
    desc: '自定义玩家遇到错误时显示的反馈网址。',
    type: 'string',
  },

  // ==========================================
  // 资源包与数据包 (Resources & DataPacks)
  // ==========================================
  {
    key: 'resource-pack',
    label: '资源包下载地址',
    desc: '玩家进入服务器时提示下载的资源包直链 URL。',
    type: 'string',
  },
  {
    key: 'require-resource-pack',
    label: '强制资源包',
    desc: '开启后，拒绝下载资源包的玩家将被踢出。',
    type: 'boolean',
  },
  {
    key: 'resource-pack-sha1',
    label: '资源包 SHA1',
    desc: '资源包文件的 SHA-1 校验码，用于验证完整性和缓存。',
    type: 'string',
  },
  {
    key: 'resource-pack-prompt',
    label: '资源包提示语',
    desc: '下载资源包时向玩家显示的自定义消息（Json 格式）。',
    type: 'string',
  },
  {
    key: 'initial-enabled-packs',
    label: '初始启用数据包',
    desc: '世界生成时默认启用的数据包列表（逗号分隔）。',
    type: 'string',
  },
  {
    key: 'initial-disabled-packs',
    label: '初始禁用数据包',
    desc: '世界生成时默认禁用的数据包列表。',
    type: 'string',
  },

  // ==========================================
  // 远程管理 (RCON & Query)
  // ==========================================
  {
    key: 'enable-rcon',
    label: '启用 RCON',
    desc: '开启远程控制台协议，允许外部工具发送指令。',
    type: 'boolean',
  },
  {
    key: 'rcon.port',
    label: 'RCON 端口',
    desc: 'RCON 监听端口（默认为 25575）。',
    type: 'number',
  },
  {
    key: 'rcon.password',
    label: 'RCON 密码',
    desc: '连接 RCON 必须的密码。请设置复杂的密码。',
    type: 'string',
  },
  {
    key: 'broadcast-rcon-to-ops',
    label: '向 OP 广播 RCON',
    desc: '当 RCON 执行指令时，是否通知在线的 OP。',
    type: 'boolean',
  },
  {
    key: 'enable-query',
    label: '启用 Query',
    desc: '开启 GameSpy4 协议，用于获取服务器详细信息。',
    type: 'boolean',
  },
  {
    key: 'query.port',
    label: 'Query 端口',
    desc: 'Query 协议监听端口（默认为 25565）。',
    type: 'number',
  },
  {
    key: 'broadcast-console-to-ops',
    label: '向 OP 广播控制台',
    desc: '控制台执行的指令输出是否发给在线 OP。',
    type: 'boolean',
  },
  {
    key: 'enable-jmx-monitoring',
    label: '启用 JMX 监控',
    desc: '开启 Java JMX 性能监控（通常用于开发调试）。',
    type: 'boolean',
  },

  // ==========================================
  // 官方管理后台 (Management Server - 1.20.2+)
  // ==========================================
  {
    key: 'management-server-enabled',
    label: '启用管理后台',
    desc: '是否启用 Minecraft 官方定义的管理服务器接口。',
    type: 'boolean',
  },
  {
    key: 'management-server-port',
    label: '管理后台端口',
    desc: '管理接口监听的端口。',
    type: 'number',
  },
  {
    key: 'management-server-host',
    label: '管理后台主机',
    desc: '管理接口绑定的主机名/IP。',
    type: 'string',
  },
  {
    key: 'management-server-allowed-origins',
    label: '管理后台允许源',
    desc: '允许访问管理接口的 Origin 列表。',
    type: 'string',
  },

  // ==========================================
  // 杂项与调试 (Misc)
  // ==========================================
  {
    key: 'server-ip',
    label: '服务器绑定 IP',
    desc: '指定服务器绑定的本地网卡 IP。留空表示监听所有网卡（0.0.0.0）。',
    type: 'string',
  },
  {
    key: 'debug',
    label: '调试模式',
    desc: '开启后控制台将输出更多调试信息。',
    type: 'boolean',
  },
  {
    key: 'text-filtering-config',
    label: '文本过滤配置',
    desc: '用于文本过滤服务的 API 配置。',
    type: 'string',
  },
  {
    key: 'status-heartbeat-interval',
    label: '状态心跳间隔',
    desc: '服务器向客户端发送状态心跳的间隔（0 为默认）。',
    type: 'number',
  },
];
