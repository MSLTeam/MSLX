import { defineStore } from 'pinia';

export const usePluginUIStore = defineStore('pluginUI', {
  state: () => ({
    extensions: {
      // 预埋插槽名字
      'instance-console-dropdown': [], // 实例控制台-更多功能下拉菜单
      'instance-console-overview-bottom': [], // 实例控制台-玩家列表下方
      'instance-settings-tab': [], // 实例配置-扩展选项卡
    } as Record<string, any[]>,
  }),
  actions: {
    registerExtension(slotName: string, extObj: any) {
      if (!this.extensions[slotName]) {
        this.extensions[slotName] = [];
      }
      const current = this.extensions[slotName];
      const isDuplicate = current.some(
        (item: any) => item === extObj || (item.component && item.component === extObj.component),
      );
      if (!isDuplicate) {
        this.extensions[slotName] = [...current, extObj];
      }
    },
  },
});

