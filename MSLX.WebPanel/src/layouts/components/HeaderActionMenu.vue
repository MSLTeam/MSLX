<script lang="ts">
import type { HeaderActionIcon } from '@/utils/pluginHeaderActions';

export interface HeaderMenuItem {
  key: string;
  label: string;
  icon?: HeaderActionIcon;
  disabled?: boolean;
  badge?: boolean;
  href?: string;
  target?: '_blank' | '_self';
  items?: HeaderMenuItem[];
  select?: (event: MouseEvent, trigger: HTMLElement | null) => void;
}
</script>

<script setup lang="ts">
import { computed, nextTick, ref, useId, watch } from 'vue';

const props = defineProps<{ label: string; icon: HeaderActionIcon; items: HeaderMenuItem[] }>();
const id = useId();
const visible = ref(false);
const groupKey = ref('');
const trigger = ref<InstanceType<typeof import('tdesign-vue-next').Button>>();
const menu = ref<HTMLElement>();
const group = computed(() => props.items.find(item => item.key === groupKey.value && item.items?.length));
const rows = computed(() => group.value ? [
  { key: '', label: '返回上一级', icon: 'chevron-left' }, ...group.value.items!,
] : props.items);

function triggerElement(): HTMLElement | null {
  return trigger.value?.$el ?? null;
}

function itemsInView() {
  return Array.from(menu.value?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])') ?? [])
    .filter(item => item.getClientRects().length);
}

function setVisible(open: boolean) {
  if (visible.value === open) return;
  visible.value = open;
  groupKey.value = '';
}

watch(menu, (element, _previous, cleanup) => {
  if (!element) return;
  let attempts = 4;
  const focusFirst = () => {
    const first = itemsInView()[0];
    if (!visible.value || !first) return;
    // TDesign hides the popup during the first frames of its enter transition.
    if (getComputedStyle(first).visibility === 'hidden' && attempts-- > 0) {
      frame = requestAnimationFrame(focusFirst);
    } else {
      first.focus();
    }
  };
  let frame = requestAnimationFrame(focusFirst);
  cleanup(() => cancelAnimationFrame(frame));
}, { flush: 'post' });

function focusMenu() {
  if (visible.value) itemsInView()[0]?.focus();
  else setVisible(true);
}

function back() {
  const previous = groupKey.value;
  groupKey.value = '';
  void nextTick(() => itemsInView().find(item => item.dataset.entryKey === previous)?.focus());
}

function select(item: HeaderMenuItem, event: MouseEvent) {
  if (!item.key) {
    back();
    return;
  }
  if (item.items?.length) {
    groupKey.value = item.key;
    void nextTick(() => itemsInView()[1]?.focus());
    return;
  }
  const opener = triggerElement();
  setVisible(false);
  opener?.focus();
  item.select?.(event, opener);
}

function handleKey(event: KeyboardEvent) {
  if (event.key === 'Tab') {
    setVisible(false);
    triggerElement()?.focus();
    return;
  }
  if (!['Escape', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
  event.preventDefault();
  event.stopPropagation();
  if (event.key === 'Escape') {
    if (group.value) back();
    else {
      setVisible(false);
      triggerElement()?.focus();
    }
    return;
  }
  const items = itemsInView();
  if (!items.length) return;
  const index = items.indexOf(document.activeElement as HTMLElement);
  const last = items.length - 1;
  if (event.key === 'Home' || (event.key === 'ArrowDown' && index === last)) items[0]?.focus();
  else if (event.key === 'End' || (event.key === 'ArrowUp' && index <= 0)) items[last]?.focus();
  else items[index + (event.key === 'ArrowDown' ? 1 : -1)]?.focus();
}

defineExpose({ close: () => setVisible(false) });
</script>

<template>
  <t-popup :visible="visible" trigger="click" placement="bottom-right" attach="body" @visible-change="setVisible">
    <t-button
      ref="trigger"
      theme="default"
      shape="square"
      variant="text"
      class="header-btn"
      :title="label"
      :aria-label="label"
      aria-haspopup="menu"
      :aria-expanded="visible"
      :aria-controls="visible ? id : undefined"
      @keydown.down.prevent="focusMenu"
      @keydown.esc="setVisible(false)"
    >
      <t-icon v-if="typeof icon === 'string'" :name="icon" class="text-[20px]" />
      <component :is="icon" v-else class="text-[20px]" />
    </t-button>
    <template #content>
      <div v-if="visible" :id="id" ref="menu" class="header-action-menu" role="menu" :aria-label="group?.label ?? label" @keydown="handleKey">
        <component
          :is="item.href ? 'a' : 'button'"
          v-for="item in rows"
          :key="item.key"
          :data-entry-key="item.key"
          class="header-action-row"
          :type="item.href ? undefined : 'button'"
          :disabled="item.disabled || undefined"
          :href="item.href"
          :target="item.target"
          :rel="item.target === '_blank' ? 'noopener noreferrer' : undefined"
          :aria-haspopup="item.items ? 'menu' : undefined"
          role="menuitem"
          tabindex="-1"
          @click="select(item, $event)"
        >
          <t-icon v-if="!item.icon || typeof item.icon === 'string'" :name="typeof item.icon === 'string' ? item.icon : 'app'" aria-hidden="true" />
          <component :is="item.icon" v-else aria-hidden="true" />
          <span class="min-w-0 break-words">{{ item.label }}</span>
          <t-icon v-if="item.items" name="chevron-right" class="ml-auto shrink-0" aria-hidden="true" />
          <t-badge v-if="item.badge" dot class="ml-auto" />
        </component>
      </div>
    </template>
  </t-popup>
</template>

<style scoped>
.header-action-menu {
  min-inline-size: min(160px, 85vw);
  max-inline-size: min(320px, 85vw);
  max-block-size: min(24rem, 65dvh);
  overflow-y: auto;
}

.header-action-row {
  display: flex;
  align-items: center;
  gap: 8px;
  inline-size: 100%;
  min-block-size: 36px;
  padding: 6px 10px;
  border-radius: var(--td-radius-default);
  text-align: start;
  color: var(--td-text-color-primary);
}

.header-action-row:not(:disabled):is(:hover, :focus-visible) {
  background: var(--td-bg-color-container-hover);
}

.header-action-row:focus-visible {
  outline: 2px solid var(--td-brand-color);
  outline-offset: -2px;
}

.header-action-row:disabled {
  color: var(--td-text-color-disabled);
}
</style>
