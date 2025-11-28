import { MessagePlugin } from 'tdesign-vue-next';

export const copyText = (text: string, showMsg = true,msg: string = '复制成功') => {
  return new Promise<void>((resolve, reject) => {
    // 摩登！
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(() => {
        if (showMsg) MessagePlugin.success(msg);
        resolve();
      }).catch(() => {
        fallbackCopy(text, showMsg,msg, resolve, reject);
      });
    } else {
      fallbackCopy(text, showMsg,msg, resolve, reject);
    }
  });
};

function fallbackCopy(text: string, showMsg: boolean,msg: string, resolve: any, reject: any) {
  try {
    const textArea = document.createElement("textarea");
    textArea.value = text;

    // 避免滚动到底部
    textArea.style.top = "0";
    textArea.style.left = "0";
    textArea.style.position = "fixed";

    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    const successful = document.execCommand('copy');
    document.body.removeChild(textArea);

    if (successful) {
      if (showMsg) MessagePlugin.success(msg);
      resolve();
    } else {
      if (showMsg) MessagePlugin.error('复制失败');
      reject(new Error('execCommand returned false'));
    }
  } catch (err) {
    if (showMsg) MessagePlugin.error('复制出错');
    reject(err);
  }
}
