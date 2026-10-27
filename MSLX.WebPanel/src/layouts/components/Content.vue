<template>
  <Result v-if="nodePage && route.query.node && !node"
    :type="nodeStore.fetchError ? 'wifi' : '403'"
    :title="nodeStore.fetchError ? '节点列表加载失败' : '节点不可用'"
    :tip="nodeStore.fetchError || '节点不存在、已解除链接或无访问权限'">
    <t-button :loading="nodeStore.loading" @click="nodeStore.fetchNodes(true)">重试</t-button>
  </Result>
  <Result v-else-if="pluginUI.error" type="wifi" title="目标节点插件加载失败" :tip="pluginUI.error">
    <t-button :loading="pluginUI.loading" @click="retryPlugins">重试</t-button>
  </Result>
  <t-loading v-else-if="pluginUI.loading" text="正在加载目标节点插件" />
  <router-view v-if="(!nodePage || !route.query.node || node) && !pluginUI.error && !pluginUI.loading" v-slot="{ Component }">
    <transition name="fade" mode="out-in">
      <keep-alive v-if="!nodePage">
        <component :is="Component" />
      </keep-alive>
      <component v-else :is="Component" :key="`${route.path}:${route.query.node || 'local'}`" />
    </transition>
  </router-view>

</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { usePluginUIStore } from '@/store/modules/pluginUI';
import { loadAllPlugins, activatePluginNode } from '@/utils/pluginManager';
import { isNodePage } from '@/utils/nodeTarget';
import { useNodeStore } from '@/store/modules/node';
import Result from '@/components/result/index.vue';
const route = useRoute();
const router = useRouter();
const pluginUI = usePluginUIStore();
const retryPlugins = async () => {
  const nodeId = String(route.query.node || 'local');
  try {
    await nodeStore.fetchNodes(true);
    await loadAllPlugins(nodeId, true);
  } catch (error) {
    pluginUI.error = error instanceof Error ? error.message : '插件加载失败';
  } finally { activatePluginNode(nodeId); }
  await router.replace(route.fullPath);
};
const nodeStore = useNodeStore();
const nodePage = computed(() => isNodePage(route.path));
const node = computed(() => nodeStore.slaveNodes.find(n => n.nodeId === route.query.node && !n.pendingRemoval));

</script>

<style lang="less" scoped>
.fade-leave-active,
.fade-enter-active {
  transition: opacity @anim-duration-slow @anim-time-fn-easing;
}
.fade-enter,
.fade-leave-to {
  opacity: 0;
}
</style>
