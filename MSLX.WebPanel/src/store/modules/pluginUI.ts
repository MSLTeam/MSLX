import { defineStore } from 'pinia';
import { shallowRef } from 'vue';

export const usePluginUIStore = defineStore('pluginUI', {
  state: () => ({
    extensions: shallowRef<Record<string, any[]>>({
      // 预埋插槽名字
      'instance-console-dropdown': [], // 实例控制台-更多功能下拉菜单
      'instance-console-overview-bottom': [], // 实例控制台-玩家列表下方
    }),
  }),
  actions: {
    registerExtension(slotName: string, extObj: any) {
      if (!this.extensions[slotName]) {
        this.extensions[slotName] = [];
      }
      this.extensions[slotName] = [...this.extensions[slotName], extObj];
    },
  },
});
