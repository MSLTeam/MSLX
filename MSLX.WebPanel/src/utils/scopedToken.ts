import { request } from '@/utils/request';

const MEDIA_TOKEN_KEY = 'MSLX_MEDIA_TOKEN';
const MEDIA_TOKEN_EXP_KEY = 'MSLX_MEDIA_TOKEN_EXP';
const DOWNLOAD_TOKEN_KEY = 'MSLX_DOWNLOAD_TOKEN';
const DOWNLOAD_TOKEN_EXP_KEY = 'MSLX_DOWNLOAD_TOKEN_EXP';

let mediaEpoch = 0;
let downloadEpoch = 0;
let mediaTokenPromise: Promise<string> | null = null;
let downloadTokenPromise: Promise<string> | null = null;

/**
 * 获取媒体资源专用降权 Token（存储于 localStorage，与 24h 登录会话生命周期对齐，页面刷新不丢失）
 */
export async function getMediaToken(): Promise<string> {
  const stored = localStorage.getItem(MEDIA_TOKEN_KEY);
  const expStr = localStorage.getItem(MEDIA_TOKEN_EXP_KEY);
  const exp = expStr ? parseInt(expStr, 10) : 0;

  if (stored && (!exp || Date.now() < exp)) {
    return stored;
  }

  if (mediaTokenPromise) return mediaTokenPromise;

  const currentEpoch = mediaEpoch;
  mediaTokenPromise = (async () => {
    try {
      const res = await request.get<{ token: string; expiresIn?: number }>({
        url: '/api/auth/media-token',
      });
      const token = res.token || '';
      // 防在途竞态：若在请求期间发生换号或清理，禁止将旧账号 Token 写回存储
      if (token && currentEpoch === mediaEpoch) {
        localStorage.setItem(MEDIA_TOKEN_KEY, token);
        if (res.expiresIn && res.expiresIn > 0) {
          localStorage.setItem(MEDIA_TOKEN_EXP_KEY, (Date.now() + res.expiresIn * 1000).toString());
        }
      }
      return currentEpoch === mediaEpoch ? token : '';
    } catch (e) {
      console.error('Failed to get media token', e);
      return '';
    } finally {
      if (currentEpoch === mediaEpoch) {
        mediaTokenPromise = null;
      }
    }
  })();

  return mediaTokenPromise;
}

/**
 * 同步获取当前缓存的媒体 Token（从 localStorage 读取，刷新页面立即生效，确保地图切片/图标稳定命中强缓存）
 */
export function getSyncMediaToken(): string {
  const stored = localStorage.getItem(MEDIA_TOKEN_KEY);
  const expStr = localStorage.getItem(MEDIA_TOKEN_EXP_KEY);
  const exp = expStr ? parseInt(expStr, 10) : 0;
  if (stored && (!exp || Date.now() < exp)) {
    return stored;
  }
  return '';
}

/**
 * 获取文件下载专用降权 Token（存储于 sessionStorage，有效期 2h，支持断点续传）
 * 无论当前目录下有多少视频，在当前标签页 2 小时内只向后端请求 1 次签名。
 */
export async function getDownloadToken(): Promise<string> {
  const stored = sessionStorage.getItem(DOWNLOAD_TOKEN_KEY);
  const expStr = sessionStorage.getItem(DOWNLOAD_TOKEN_EXP_KEY);
  const exp = expStr ? parseInt(expStr, 10) : 0;

  if (stored && Date.now() < exp) {
    return stored;
  }

  if (downloadTokenPromise) return downloadTokenPromise;

  const currentEpoch = downloadEpoch;
  downloadTokenPromise = (async () => {
    try {
      const res = await request.post<{ token: string; expiresIn?: number }>({
        url: '/api/auth/download-token',
      });
      const token = res.token || '';
      // 防在途竞态：若在请求期间发生换号或清理，禁止将旧账号 Token 写回存储
      if (token && currentEpoch === downloadEpoch) {
        const totalExp = res.expiresIn ?? 7200;
        // 动态缓冲计算：如果寿命大于 15 分钟，提前 10 分钟过期；如果不足 15 分钟，提前 30 秒；极端情况不足 30 秒则不缓存
        let validSeconds = 0;
        if (totalExp > 900) {
          validSeconds = totalExp - 600;
        } else if (totalExp > 60) {
          validSeconds = totalExp - 30;
        } else if (totalExp > 10) {
          validSeconds = totalExp - 5;
        }

        if (validSeconds > 0) {
          sessionStorage.setItem(DOWNLOAD_TOKEN_KEY, token);
          sessionStorage.setItem(DOWNLOAD_TOKEN_EXP_KEY, (Date.now() + validSeconds * 1000).toString());
        }
      }
      return currentEpoch === downloadEpoch ? token : '';
    } catch (e) {
      console.error('Failed to get download token', e);
      return '';
    } finally {
      if (currentEpoch === downloadEpoch) {
        downloadTokenPromise = null;
      }
    }
  })();

  return downloadTokenPromise;
}

/**
 * 同步获取当前缓存的下载 Token（若未初始化或已过期返回空字符串）
 */
export function getSyncDownloadToken(): string {
  const stored = sessionStorage.getItem(DOWNLOAD_TOKEN_KEY);
  const expStr = sessionStorage.getItem(DOWNLOAD_TOKEN_EXP_KEY);
  const exp = expStr ? parseInt(expStr, 10) : 0;
  if (stored && Date.now() < exp) {
    return stored;
  }
  return '';
}

/**
 * 清除所有降权 Token 缓存（登出或重新登录时调用）
 */
export function clearMediaToken(): void {
  mediaEpoch++;
  downloadEpoch++;
  localStorage.removeItem(MEDIA_TOKEN_KEY);
  localStorage.removeItem(MEDIA_TOKEN_EXP_KEY);
  sessionStorage.removeItem(DOWNLOAD_TOKEN_KEY);
  sessionStorage.removeItem(DOWNLOAD_TOKEN_EXP_KEY);
  mediaTokenPromise = null;
  downloadTokenPromise = null;
}
