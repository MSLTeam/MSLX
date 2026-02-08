export function generateRandomString(length: number): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let result = '';

  for (let i = 0; i < length; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }

  return result;
}

// 格式化时间戳
export function formatTime(timestamp: number) {
  return new Date(timestamp * 1000).toLocaleString();
}

/**
 * 检测当前访问是否为内网地址
 */
export function isInternalNetwork(): boolean {
  const hostname = window.location.hostname;

  // localhost 和 IPv6 回环
  if (hostname === 'localhost' || hostname === '::1' || hostname === '[::1]') {
    return true;
  }

  // IPv4 范围
  // - 127.0.0.0/8 (回环)
  // - 10.0.0.0/8 (A类私有)
  // - 172.16.0.0/12 (B类私有: 172.16.0.0 - 172.31.255.255)
  // - 192.168.0.0/16 (C类私有)
  const ipv4Pattern = /^(?:127|10)\.\d+\.\d+\.\d+$|^(?:172\.(?:1[6-9]|2\d|3[0-1]))\.\d+\.\d+$|^(?:192\.168)\.\d+\.\d+$/;

  if (ipv4Pattern.test(hostname)) {
    return true;
  }

  // IPv6 局域网范围
  if (hostname.startsWith('fe80:') || hostname.startsWith('fc') || hostname.startsWith('fd')) {
    return true;
  }

  // .local 后缀 (mDNS / Bonjour 域名)
  if (hostname.endsWith('.local')) {
    return true;
  }

  return false;
}
