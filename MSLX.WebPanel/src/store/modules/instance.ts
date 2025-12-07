import { defineStore } from 'pinia';
import { InstanceListModel } from '@/api/model/instance';
import { ref } from 'vue';
import { getInstanceList } from '@/api/instance';
import { MessagePlugin } from 'tdesign-vue-next';

export const useInstanceListstore = defineStore('instanceList',()=> {
  const instanceList = ref<InstanceListModel[]>([]);

  async function refreshInstanceList() {
    try{
      instanceList.value = await getInstanceList();
    }catch (e){
      MessagePlugin.error('获取实例列表失败:'+e.message);
    }

  }

  return {
    instanceList,
    refreshInstanceList
  }
});

