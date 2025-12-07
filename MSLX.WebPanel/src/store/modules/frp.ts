import { defineStore } from 'pinia'
import { ref } from 'vue';
import type { FrpListModel } from '@/api/model/frp';
import { getFrpList } from '@/api/frp';
import { MessagePlugin } from 'tdesign-vue-next';

export const useTunnelsStore = defineStore('tunnels',()=> {
  const frpList = ref<FrpListModel[]>([]);

  async function getTunnels() {
    try{
      frpList.value = await getFrpList();
    }catch (e){
      MessagePlugin.error('获取Frp列表失败:'+e.message);
    }
  }

  return {
    frpList,
    getTunnels
  }
});
