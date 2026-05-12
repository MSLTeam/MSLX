// src/hooks/usePluginSlot.ts
import { ref } from 'vue';

export function usePluginSlot() {
  const pluginRefs = ref<Record<number, any>>({});
  const setPluginRef = (el: any, index: number) => {
    if (el) {
      pluginRefs.value[index] = el;
    } else {
      delete pluginRefs.value[index];
    }
  };

  const openPluginDialog = (index: number, config?: any) => {
    const instance = pluginRefs.value[index];
    if (instance && typeof instance.open === 'function') {
      instance.open(config);
    } else {
      console.warn(`[MSLX Plugin] 警告: 插件未挂载或未暴露 open 方法 (index: ${index})`);
    }
  };

  // 强制关闭弹窗
  const closePluginDialog = (index: number) => {
    const instance = pluginRefs.value[index];
    if (instance && typeof instance.close === 'function') {
      instance.close();
    }
  };

  // 重置插件数据
  const resetPluginDialog = (index: number) => {
    const instance = pluginRefs.value[index];
    if (instance && typeof instance.reset === 'function') {
      instance.reset();
    }
  };

  // 获取插件的打开状态
  const isPluginOpen = (index: number): boolean => {
    const instance = pluginRefs.value[index];
    if (!instance) return false;
    // 如果插件暴露的是 ref(false)，取 .value；如果是普通布尔值，直接取
    return instance.isOpen?.value ?? instance.isOpen ?? false;
  };

  // 获取插件是否正在处理任务
  const isPluginBusy = (index: number): boolean => {
    const instance = pluginRefs.value[index];
    if (!instance) return false;
    return instance.isBusy?.value ?? instance.isBusy ?? false;
  };

  const closeAllPlugins = () => {
    Object.keys(pluginRefs.value).forEach((key) => {
      closePluginDialog(Number(key));
    });
  };

  return {
    pluginRefs,
    setPluginRef,

    // 动作方法
    openPluginDialog,
    closePluginDialog,
    resetPluginDialog,
    closeAllPlugins,

    // 状态查询
    isPluginOpen,
    isPluginBusy,
  };
}
