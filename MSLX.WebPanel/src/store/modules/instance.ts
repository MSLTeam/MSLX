import { defineStore } from 'pinia';
import { InstanceListModel } from '@/api/model/instance';
import { ref } from 'vue';
import { getInstanceList } from '@/api/instance';
import { MessagePlugin } from 'tdesign-vue-next';

export const useInstanceListstore = defineStore('instanceList',()=> {
  const instanceList = ref<InstanceListModel[]>([]);
  const totalInstanceCount = ref(0);
  const onlineInstanceCount = ref(0);

  async function refreshInstanceList() {
    try{
      instanceList.value = await getInstanceList();
      totalInstanceCount.value = instanceList.value.length;
      onlineInstanceCount.value = instanceList.value.filter(item=>item.status).length;
    }catch (e){
      MessagePlugin.error('获取实例列表失败:'+e.message);
    }

  }

  return {
    instanceList,
    refreshInstanceList,
    totalInstanceCount,
    onlineInstanceCount
  }
});

