import { defineStore } from 'pinia';
import { InstanceListModel } from '@/api/model/instance';
import { ref } from 'vue';
import { getInstanceList } from '@/api/instance';

export const useInstanceListstore = defineStore('instanceList',()=> {
  const instanceList = ref<InstanceListModel[]>([]);

  async function refreshInstanceList() {
    instanceList.value = await getInstanceList();
  }

  return {
    instanceList,
    refreshInstanceList
  }
});

