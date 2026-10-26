import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';
import { getPageNodeId } from './nodeTarget';

// 防止操作的时候切换了节点的带提示封装弹窗
export function confirmNodeAction(options: Parameters<typeof DialogPlugin.confirm>[0]) {
  const nodeId = getPageNodeId();
  const path = window.location.pathname + window.location.search;
  const dialog = DialogPlugin.confirm({
    ...options,
    onConfirm: (...args) => {
      if (getPageNodeId() !== nodeId || window.location.pathname + window.location.search !== path) {
        dialog.destroy();
        MessagePlugin.warning('页面目标已变化，请在当前页面重新操作');
        return;
      }
      return options.onConfirm?.(...args);
    },
  });
  return dialog;
}
