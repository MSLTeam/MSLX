/**
 * Java 版本推荐工具函数
 *
 * 根据 Minecraft 版本号推荐对应的 Java 版本：
 *   MC 26.1+       → Java 25
 *   MC 1.20.5~1.21 → Java 21
 *   MC 1.18~1.20.4 → Java 17
 *   MC 1.17~1.17.1 → Java 16
 *   MC 1.13 及更低  → Java 8
 */

/**
 * 比较两个版本字符串。
 * @returns 正数表示 v1 > v2，负数表示 v1 < v2，0 表示相等。
 */
export const compareVersions = (v1: string, v2: string): number => {
  const parts1 = v1.split('.').map(Number);
  const parts2 = v2.split('.').map(Number);
  const maxLen = Math.max(parts1.length, parts2.length);
  for (let i = 0; i < maxLen; i++) {
    const p1 = parts1[i] || 0;
    const p2 = parts2[i] || 0;
    if (p1 !== p2) return p1 - p2;
  }
  return 0;
};

/**
 * 从文件名或任意字符串中提取 MC 版本号字符串（如 "1.20.4"、"26.1"）。
 * 优先匹配 1.x.x 格式，其次匹配通用 x.x.x 格式。
 * 无法识别时返回 null。
 */
export const parseMcVersion = (input: string): string | null => {
  if (!input) return null;
  // 优先匹配 1.x 格式（标准 MC 版本）
  const mc1x = input.match(/\b(1\.\d+(?:\.\d+)*)\b/);
  if (mc1x) return mc1x[1];
  // 匹配通用 x.x 格式（用于快照/新版如 26.1）
  const general = input.match(/\b(\d+\.\d+(?:\.\d+)*)\b/);
  if (general) return general[1];
  return null;
};

/**
 * 根据 MC 版本字符串返回推荐的 Java 大版本号。
 * 无法识别时返回 null。
 */
export const getRecommendedJava = (version: string): number | null => {
  if (!version) return null;
  // 提取干净的版本号（例如 "26.3-snapshot-4" 转换为 "26.3"），防止 split 后出现 NaN 破坏比较
  const cleanVersion = parseMcVersion(version) || version;
  if (compareVersions(cleanVersion, '26.1') >= 0) return 25;
  if (compareVersions(cleanVersion, '1.20.5') >= 0) return 21;
  if (compareVersions(cleanVersion, '1.18') >= 0) return 17;
  if (compareVersions(cleanVersion, '1.17') >= 0) return 16;
  return 8;
};

/**
 * 综合 parseMcVersion + getRecommendedJava，直接从文件名/版本字符串
 * 返回推荐 Java 版本号字符串（如 "21"），无法识别时返回 null。
 */
export const getRecommendedJavaFromInput = (input: string): string | null => {
  const version = parseMcVersion(input);
  if (!version) return null;
  const rec = getRecommendedJava(version);
  return rec !== null ? String(rec) : null;
};
