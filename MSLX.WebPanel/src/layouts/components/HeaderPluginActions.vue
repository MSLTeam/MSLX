<script setup lang="ts">
import { computed, inject, shallowRef, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useMediaQuery } from '@vueuse/core';
import { MessagePlugin } from 'tdesign-vue-next';
import { headerPluginActionsKey } from '@/hooks/useHeaderPluginActions';
import { nodeNavigationUrl } from '@/utils/nodeTarget';
import { isInternalHeaderPath } from '@/utils/pluginHeaderActions';
import type { RegisteredHeaderAction } from '@/utils/pluginHeaderActions';
import HeaderActionMenu from './HeaderActionMenu.vue';
import type { HeaderMenuItem } from './HeaderActionMenu.vue';

const props = defineProps<{ mobileItems: HeaderMenuItem[] }>();
const router = useRouter();
const desktop = useMediaQuery('(min-width: 1024px)');
const moreMenu = shallowRef<InstanceType<typeof HeaderActionMenu>>();
const directMenu = shallowRef<InstanceType<typeof HeaderActionMenu>>();
const { showPanel, pending, runAction, permitted, visibleEntries, leaves } = inject(headerPluginActionsKey)!;

watch([visibleEntries, desktop], () => {
  moreMenu.value?.close();
  directMenu.value?.close();
});

function menuItem(entry: RegisteredHeaderAction): HeaderMenuItem {
  const item: HeaderMenuItem = { key: entry.key, label: entry.label, icon: entry.icon };
  if (entry.type === 'dropdown') return { ...item, items: entry.items.filter(permitted).map(menuItem) };
  if (entry.type === 'link') return { ...item, href: entry.href, target: entry.target };
  if (entry.type === 'route') {
    try {
      const { fullPath } = router.resolve(entry.to, router.currentRoute.value);
      if (isInternalHeaderPath(fullPath)) {
        const target = nodeNavigationUrl(fullPath, entry.nodeId);
        const { href } = router.resolve(target);
        if (isInternalHeaderPath(href)) return { ...item, href, select(event) {
          if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.defaultPrevented) return;
          event.preventDefault();
          if (leaves.value.has(entry)) void router.push(target).catch(() => MessagePlugin.error(`无法打开「${entry.label}」`));
        } };
      }
    } catch {
      // A named route may be unavailable under the user's current permissions.
    }
    return { ...item, disabled: true };
  }
  if (entry.type === 'action') return { ...item, disabled: pending.has(entry), select: () => void runAction(entry) };
  return { ...item, select: (_event, trigger) => showPanel(entry, trigger) };
}

const directEntry = computed(() => desktop.value ? visibleEntries.value.find(entry => entry.placement !== 'more') : undefined);
const directItem = computed(() => directEntry.value && menuItem(directEntry.value));
const moreItems = computed(() => [
  ...(desktop.value ? [] : props.mobileItems),
  ...visibleEntries.value.filter(entry => entry !== directEntry.value).map(menuItem),
]);
</script>

<template>
  <div v-if="directItem" class="flex shrink-0" data-header-plugin-direct>
    <header-action-menu v-if="directItem.items" ref="directMenu" :label="directItem.label" :icon="directItem.icon || 'app'" :items="directItem.items" />
    <t-button
      v-else
      :tag="directItem.href ? 'a' : 'button'"
      :href="directItem.href"
      :target="directItem.target"
      :rel="directItem.target === '_blank' ? 'noopener noreferrer' : undefined"
      :disabled="directItem.disabled"
      :aria-label="directItem.label"
      :title="directItem.label"
      theme="default"
      shape="square"
      variant="text"
      class="header-btn"
      @click="directItem.select?.($event, $event.currentTarget as HTMLElement)"
    >
      <t-icon v-if="!directItem.icon || typeof directItem.icon === 'string'" :name="typeof directItem.icon === 'string' ? directItem.icon : 'app'" class="text-[20px]" />
      <component :is="directItem.icon" v-else class="text-[20px]" />
    </t-button>
  </div>
  <div v-if="moreItems.length" class="shrink-0" data-header-plugin-more>
    <header-action-menu ref="moreMenu" label="更多操作" icon="more" :items="moreItems" />
  </div>
</template>
