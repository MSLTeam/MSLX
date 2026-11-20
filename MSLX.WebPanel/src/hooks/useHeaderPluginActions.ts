import { computed, nextTick, onBeforeUnmount, shallowReactive, shallowRef, watch } from 'vue';
import type { InjectionKey } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { usePluginUIStore, useUserStore } from '@/store';
import { createPluginNodeContext } from '@/utils/pluginNodeContext';
import { HEADER_ACTION_SLOT } from '@/utils/pluginHeaderActions';
import type { RegisteredHeaderAction, RegisteredHeaderLeaf, RegisteredHeaderPanel } from '@/utils/pluginHeaderActions';

export const headerPluginActionsKey: InjectionKey<ReturnType<typeof useHeaderPluginActions>> = Symbol('header-plugin-actions');

export function useHeaderPluginActions() {
  const user = useUserStore();
  const pluginUI = usePluginUIStore();
  const openPanel = shallowRef<RegisteredHeaderPanel>();
  const panels = shallowReactive(new Set<RegisteredHeaderPanel>());
  const pending = shallowReactive(new Map<RegisteredHeaderLeaf, ReturnType<typeof createPluginNodeContext>>());
  let panelTrigger: HTMLElement | null = null;

  const permitted = (entry: RegisteredHeaderAction) => !entry.roleCode || entry.roleCode.includes(user.isAdmin ? 'admin' : 'user');
  const registered = computed<RegisteredHeaderAction[]>(() => user.token ? pluginUI.extensions[HEADER_ACTION_SLOT] ?? [] : []);
  const visibleEntries = computed(() => registered.value.filter(entry => permitted(entry) &&
    (entry.type !== 'dropdown' || entry.items.some(permitted))));
  const leaves = computed(() => new Set(visibleEntries.value.flatMap(entry => entry.type === 'dropdown'
    ? entry.items.filter(permitted) : [entry])));

  watch(leaves, allowed => {
    for (const panel of panels) if (!allowed.has(panel)) panels.delete(panel);
    if (openPanel.value && !allowed.has(openPanel.value)) {
      openPanel.value = undefined;
      panelTrigger = null;
    }
    for (const [entry, context] of pending) {
      if (allowed.has(entry)) continue;
      pending.delete(entry);
      context.dispose();
    }
  });

  onBeforeUnmount(() => {
    for (const context of pending.values()) context.dispose();
    pending.clear();
  });

  function showPanel(panel: RegisteredHeaderPanel, trigger: HTMLElement | null) {
    if (!leaves.value.has(panel)) return;
    panels.add(panel);
    panelTrigger = trigger;
    openPanel.value = panel;
  }

  function closePanel(panel: RegisteredHeaderPanel) {
    if (openPanel.value !== panel) return;
    const target = panelTrigger;
    openPanel.value = undefined;
    panelTrigger = null;
    void nextTick(() => {
      if (openPanel.value || !leaves.value.has(panel)) return;
      if (target?.getClientRects().length) target.focus();
      else (document.querySelector<HTMLElement>('[data-header-plugin-more] button') ??
        document.querySelector<HTMLElement>('[data-header-plugin-direct] .header-btn'))?.focus();
    });
  }

  async function runAction(entry: Extract<RegisteredHeaderLeaf, { type: 'action' }>) {
    if (pending.has(entry) || !leaves.value.has(entry)) return;
    const context = createPluginNodeContext(entry.nodeId, entry.pluginId);
    pending.set(entry, context);
    try {
      await entry.run(context);
    } catch (error) {
      if (leaves.value.has(entry) && pending.get(entry) === context) {
        console.error(`[MSLX Plugin] 顶部操作失败 (${entry.pluginId}/${entry.id})`, error);
        MessagePlugin.error(`执行「${entry.label}」失败`);
      }
    } finally {
      if (pending.get(entry) === context) {
        pending.delete(entry);
        context.dispose();
      }
    }
  }

  return { panels, openPanel, showPanel, closePanel, pending, runAction, permitted, visibleEntries, leaves };
}
