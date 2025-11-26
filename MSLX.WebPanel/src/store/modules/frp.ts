import { defineStore } from 'pinia'
import { ref } from 'vue';
import type { FrpListModel } from '@/api/model/frp';
import { getFrpList } from '@/api/frp';

export const useTunnelsStore = defineStore('tunnels',()=> {
  const frpList = ref<FrpListModel[]>([]);

  async function getTunnels() {
    frpList.value = await getFrpList();
  }

  return {
    frpList,
    getTunnels
  }
});
