import { getPageNodeId } from '@/utils/nodeTarget';
import { defineStore } from 'pinia'
import { ref } from 'vue';
import type { FrpListModel } from '@/api/model/frp';
import { getFrpList } from '@/api/frp';
import { MessagePlugin } from 'tdesign-vue-next';

export const useTunnelsStore = defineStore('tunnels',()=> {
  let requestSequence = 0;
  let loadedNode = '';
  const frpList = ref<FrpListModel[]>([]);

  async function getTunnels() {
    const nodeId = getPageNodeId();
    const sequence = ++requestSequence;
    if (loadedNode !== nodeId) frpList.value = [];
    loadedNode = nodeId;
    try{
      const result = await getFrpList();
      if (getPageNodeId() !== nodeId || sequence !== requestSequence) return;
      frpList.value = result;
    }catch (e){
      MessagePlugin.error('获取Frp列表失败:'+e.message);
    }
  }

  return {
    frpList,
    getTunnels
  }
});
