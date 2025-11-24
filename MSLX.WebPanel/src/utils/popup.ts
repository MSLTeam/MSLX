/**
 * 打开一个居中的弹出窗口
 * @param url 目标地址
 * @param title 窗口标题
 * @param w 宽度
 * @param h 高度
 */
export const openLoginPopup = (url: string, title: string = 'MSL Login', w: number = 500, h: number = 600) => {
  const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);

  // 如果是移动端，直接打开新标签页，不通过弹窗
  if (isMobile) {
    window.open(url, '_blank');
    return null; // 移动端无法控制窗口实例，返回 null
  }

  const dualScreenLeft = window.screenLeft !== undefined ? window.screenLeft : window.screenX;
  const dualScreenTop = window.screenTop !== undefined ? window.screenTop : window.screenY;

  const width = window.innerWidth ? window.innerWidth : document.documentElement.clientWidth ? document.documentElement.clientWidth : screen.width;
  const height = window.innerHeight ? window.innerHeight : document.documentElement.clientHeight ? document.documentElement.clientHeight : screen.height;

  const systemZoom = width / window.screen.availWidth;
  const left = (width - w) / 2 / systemZoom + dualScreenLeft;
  const top = (height - h) / 2 / systemZoom + dualScreenTop;

  // 打开窗口配置参数
  const features = `
    scrollbars=yes,
    width=${w / systemZoom},
    height=${h / systemZoom},
    top=${top},
    left=${left}
  `;

  const newWindow = window.open(url, title, features);

  // 聚焦新窗口
  if (newWindow) newWindow.focus();

  return newWindow;
};
