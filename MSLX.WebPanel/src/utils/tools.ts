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

// 检测当前访问是否本地回环
export function isLoopback(): boolean {
  const hostname = window.location.hostname;
  const ipv4Loopback = /^127\.\d+\.\d+\.\d+$/;

  return hostname === 'localhost' || hostname === '::1' || hostname === '[::1]' || ipv4Loopback.test(hostname);
}
