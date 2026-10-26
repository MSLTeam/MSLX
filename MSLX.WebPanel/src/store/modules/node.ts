import { defineStore } from 'pinia';
import { getSlaveNodes } from '@/api/node';
import router from '@/router';

export const useNodeStore = defineStore('node', {
  state: () => ({
    slaveNodes: [] as any[],
    loading: false,
    fetchError: '',
    lastFetchTime: 0,
    activeNodeId: new URL(window.location.href).searchParams.get('node') || 'local'
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
        this.fetchError = '';
      } catch (error) {
        this.fetchError = `节点列表加载失败：${error instanceof Error ? error.message : '请稍后重试'}`;
        console.error('Failed to fetch slave nodes:', error);
      } finally {
        this.loading = false;
      }
      return this.slaveNodes;
    },
    async setActiveNode(nodeId: string) {
      if (nodeId !== 'local' && !this.slaveNodes.some(n => n.nodeId === nodeId && !n.pendingRemoval))
        throw new Error('节点不存在或已解除链接');
      await router.replace({ query: { ...router.currentRoute.value.query, node: nodeId === 'local' ? undefined : nodeId } });
      sessionStorage.setItem('MSLX_DEFAULT_NODE', nodeId);
    }
  }
});
