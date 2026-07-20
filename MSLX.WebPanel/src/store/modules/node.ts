import { defineStore } from 'pinia';
import { getSlaveNodes } from '@/api/node';

export const useNodeStore = defineStore('node', {
  state: () => ({
    slaveNodes: [] as any[],
    loading: false,
    lastFetchTime: 0,
    activeNodeId: localStorage.getItem('ACTIVE_NODE_ID') || 'local'
  }),
  actions: {
    async fetchNodes(force = false) {
      if (!force && Date.now() - this.lastFetchTime < 5000) {
        return this.slaveNodes;
      }
      this.loading = true;
      try {
        const res = await getSlaveNodes();
        this.slaveNodes = res || [];
        this.lastFetchTime = Date.now();
      } catch (error) {
        console.error('Failed to fetch slave nodes:', error);
      } finally {
        this.loading = false;
      }
      return this.slaveNodes;
    },
    setActiveNode(nodeId: string) {
      this.activeNodeId = nodeId;
      if (nodeId === 'local') {
        localStorage.removeItem('ACTIVE_NODE_ID');
        localStorage.removeItem('ACTIVE_NODE_URL');
      } else {
        const node = this.slaveNodes.find(n => n.nodeId === nodeId);
        if (node) {
          localStorage.setItem('ACTIVE_NODE_ID', node.nodeId);
          localStorage.setItem('ACTIVE_NODE_URL', node.nodeUrl);
        }
      }
    }
  }
});
