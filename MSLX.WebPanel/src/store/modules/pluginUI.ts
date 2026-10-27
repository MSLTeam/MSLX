import { defineStore } from 'pinia';

export const usePluginUIStore = defineStore('pluginUI', {
  state: () => ({
    nodeId: 'local', loading: false, error: '',
    extensionsByNode: {} as Record<string, Record<string, any[]>>,
  }),
  getters: {
    extensions: state => state.loading || state.error ? {} as Record<string, any[]> : state.extensionsByNode[state.nodeId] || {},
  },
  actions: {
    registerExtension(slotName: string, extObj: any, nodeId = this.nodeId) {
      const slots = this.extensionsByNode[nodeId] ||= {};
      const current = slots[slotName] ||= [];
      if (!current.some(item => item === extObj || item.component === extObj.component)) slots[slotName] = [...current, extObj];
    },
    clear() { this.extensionsByNode = {}; this.error = ''; this.loading = false; },
  },
});
