<script setup lang="ts">
import { computed } from 'vue';
import type { PropType } from 'vue';
import isObject from 'lodash/isObject';
import type { MenuRoute } from '@/types/interface';
import { getActive } from '@/router';

defineOptions({
  name: 'MenuContent',
});

const props = defineProps({
  navData: {
    type: Array as PropType<MenuRoute[]>,
    default: () => [],
  },
  isHorizontal: {
    type: Boolean,
    default: false,
  },
  maxItemCount: {
    type: Number,
    default: 3,
  }
});

const active = computed(() => getActive());

const list = computed(() => {
  const { navData } = props;
  return getMenuList(navData);
});

// 计算需要显示的列表
const renderList = computed(() => {
  if (!props.isHorizontal) return list.value;
  return list.value.slice(0, props.maxItemCount);
});

// 计算溢出的列表
const overflowList = computed(() => {
  if (!props.isHorizontal) return [];
  return list.value.slice(props.maxItemCount);
});

type ListItemType = MenuRoute & { icon?: string };

const getMenuList = (list: MenuRoute[], basePath?: string): ListItemType[] => {
  if (!list) {
    return [];
  }
  list.sort((a, b) => {
    return (a.meta?.orderNo || 0) - (b.meta?.orderNo || 0);
  });
  return list
    .map((item) => {
      const path = basePath && !item.path.includes(basePath) ? `${basePath}/${item.path}` : item.path;
      return {
        path,
        title: item.meta?.title,
        icon: item.meta?.icon || '',
        children: getMenuList(item.children, path),
        meta: item.meta,
        redirect: item.redirect,
      };
    })
    .filter((item) => item.meta && item.meta.hidden !== true);
};

const getHref = (item: MenuRoute) => {
  const { frameSrc, frameBlank } = item.meta;
  if (frameSrc && frameBlank) {
    return frameSrc.match(/(http|https):\/\/([\w.]+\/?)\S*/);
  }
  return null;
};

const getPath = (item) => {
  if (active.value.startsWith(item.path)) {
    return active.value;
  }
  return item.meta?.single ? item.redirect : item.path;
};

const beIcon = (item: MenuRoute) => {
  return item.icon && typeof item.icon === 'string';
};

const beRender = (item: MenuRoute) => {
  if (isObject(item.icon) && typeof item.icon.render === 'function') {
    return {
      can: true,
      render: item.icon.render,
    };
  }
  return {
    can: false,
    render: null,
  };
};

const openHref = (url: string) => {
  window.open(url);
};
</script>

<template>
  <div id="menu-wrapper" class="modern-menu-wrapper" :class="{ 'is-horizontal': isHorizontal }">
    <template v-for="item in renderList" :key="item.path">
      <template v-if="!item.children || !item.children.length || item.meta?.single">
        <t-menu-item
          v-if="getHref(item)"
          :name="item.path"
          :value="getPath(item)"
          class="modern-menu-item"
          @click="openHref(getHref(item)[0])"
        >
          <template #icon>
            <t-icon v-if="beIcon(item)" :name="item.icon" />
            <component :is="beRender(item).render" v-else-if="beRender(item).can" class="t-icon" />
          </template>
          <span class="menu-text">{{ item.title }}</span>
        </t-menu-item>

        <t-menu-item
          v-else
          :name="item.path"
          :value="getPath(item)"
          :to="item.path"
          class="modern-menu-item"
        >
          <template #icon>
            <t-icon v-if="beIcon(item)" :name="item.icon" />
            <component :is="beRender(item).render" v-else-if="beRender(item).can" class="t-icon" />
          </template>
          <span class="menu-text">{{ item.title }}</span>
        </t-menu-item>
      </template>

      <t-submenu
        v-else
        :name="item.path"
        :value="item.path"
        :title="item.title"
        class="modern-submenu"
      >
        <template #icon>
          <t-icon v-if="beIcon(item)" :name="item.icon" />
          <component :is="beRender(item).render" v-else-if="beRender(item).can" class="t-icon" />
        </template>
        <menu-content v-if="item.children" :nav-data="item.children" :is-horizontal="false" />
      </t-submenu>
    </template>

    <t-submenu
      v-if="isHorizontal && overflowList.length > 0"
      name="more-menu"
      title="更多"
      class="modern-submenu"
    >
      <template #icon>
        <t-icon name="ellipsis" />
      </template>
      <menu-content :nav-data="overflowList" :is-horizontal="false" />
    </t-submenu>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* ================== 清理 TDesign 默认结构 ================== */
:deep(.t-menu__item::after),
:deep(.t-menu__item::before),
:deep(.t-submenu__title::after),
:deep(.t-submenu__title::before) {
  display: none !important;
}

/* 剥离 TDesign 强加给子菜单的 padding 和 margin */
:deep(.t-menu__sub) {
  --padding-left: 0px !important;
  @apply !m-0 !p-0 !bg-transparent !border-none !overflow-hidden;
}

/* ================== 菜单盒子 ================== */
:deep(.t-menu__item),
:deep(.t-submenu__title) {
  @apply !relative !flex !items-center !gap-2.5 !w-auto !mx-3 !my-1 !rounded-xl !transition-all !duration-200 !border-none !bg-transparent !cursor-pointer;
}

/* ================== 垂直模式 ================== */
/* 父级菜单项排版 */
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-submenu__title),
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-menu__item:not(.t-submenu__item)) {
  @apply !h-[44px] !px-3 !text-[14.5px] !font-medium !text-[var(--td-text-color-primary)] dark:!text-zinc-400;
}

/* 子菜单排版 */
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-submenu__item) {
  @apply !h-[40px] !pl-[42px] !pr-3 !text-[13.5px] !font-normal !text-[var(--td-text-color-secondary)] dark:!text-zinc-400;
}

/* 统一图标样式 */
:deep(.t-icon) {
  @apply !text-[20px] !shrink-0 !opacity-70 !transition-colors !duration-200;
}

/* ================== 垂直模式交互 ================== */
/* 通用 Hover */
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-menu__item:hover:not(.t-is-active)),
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-submenu__title:hover:not(.t-is-opened)) {
  @apply !bg-zinc-100/80 dark:!bg-zinc-800/60 !text-zinc-900 dark:!text-zinc-100;
}

.modern-menu-wrapper:not(.is-horizontal) :deep(.t-menu__item:hover:not(.t-is-active)) .t-icon,
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-submenu__title:hover:not(.t-is-opened)) .t-icon {
  @apply !opacity-100 !text-zinc-800 dark:!text-zinc-200;
}

/* 通用 Active (选中的状态) */
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-menu__item.t-is-active) {
  @apply !bg-[var(--color-primary-light)]/20 dark:!bg-[var(--color-primary)]/15 !text-[var(--color-primary)] !font-semibold;
}

.modern-menu-wrapper:not(.is-horizontal) :deep(.t-menu__item.t-is-active) .t-icon {
  @apply !opacity-100 !text-[var(--color-primary)];
}

/* 子菜单选中时，文字加粗以强调层级 */
.modern-menu-wrapper:not(.is-horizontal) :deep(.t-submenu__item.t-is-active) {
  @apply !font-bold;
}

/* ================== 水平================== */
.modern-menu-wrapper.is-horizontal {
  @apply !flex !items-center;
}

/* 顶栏项解除垂直模式的强制 padding 和高度，保持紧凑 */
.modern-menu-wrapper.is-horizontal :deep(.t-menu__item),
.modern-menu-wrapper.is-horizontal :deep(.t-submenu__title) {
  @apply !h-[40px] !mx-1 !px-3 !rounded-lg !text-[14px] !font-medium;
}

/* 顶栏的 Hover */
.modern-menu-wrapper.is-horizontal :deep(.t-menu__item:hover:not(.t-is-active)),
.modern-menu-wrapper.is-horizontal :deep(.t-submenu__title:hover:not(.t-is-opened)) {
  @apply !bg-[var(--td-bg-color-secondarycontainer)]/50 !text-zinc-900 dark:!text-zinc-100;
}

/* 顶栏的 Active */
.modern-menu-wrapper.is-horizontal :deep(.t-menu__item.t-is-active) {
  @apply !bg-[var(--color-primary-light)]/20 dark:!bg-[var(--color-primary)]/15 !text-[var(--color-primary)] !font-bold;
}

.modern-menu-wrapper.is-horizontal :deep(.t-menu__item.t-is-active) .t-icon {
  @apply !opacity-100 !text-[var(--color-primary)];
}

/* ================== 暗黑模式兜底 ================== */
:global(html[theme-mode='dark']) :deep(.t-menu__item:not(.t-is-active)),
:global(html[theme-mode='dark']) :deep(.t-submenu__title:not(.t-is-opened)) {
  color: rgba(255, 255, 255, 0.65) !important;
}

:global(html[theme-mode='dark']) :deep(.t-menu__item:hover:not(.t-is-active)),
:global(html[theme-mode='dark']) :deep(.t-submenu__title:hover:not(.t-is-opened)) {
  color: #ffffff !important;
}
</style>
