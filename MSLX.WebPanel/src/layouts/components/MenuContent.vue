<template>
  <div class="modern-menu-wrapper">
    <template v-for="item in list" :key="item.path">
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
        <menu-content v-if="item.children" :nav-data="item.children" />
      </t-submenu>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { PropType } from 'vue';
import isObject from 'lodash/isObject';
import type { MenuRoute } from '@/types/interface';
import { getActive } from '@/router';

const props = defineProps({
  navData: {
    type: Array as PropType<MenuRoute[]>,
    default: () => [],
  },
});

const active = computed(() => getActive());
const list = computed(() => {
  const { navData } = props;
  return getMenuList(navData);
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

<style lang="less" scoped>
@ease-smooth: cubic-bezier(0.4, 0, 0.2, 1);

:deep(.t-menu__item),
:deep(.t-submenu__title) {
  position: relative;
  margin: 4px 10px;
  border-radius: 8px !important;
  height: 40px;
  line-height: 40px;

  color: var(--td-text-color-primary);
  font-size: 14px;

  transition: background-color 0.2s, color 0.2s, transform 0.2s;

  display: flex;
  align-items: center;

  overflow: hidden;
}

:deep(.t-icon) {
  font-size: 18px;
  margin-right: 10px;
  flex-shrink: 0;
  opacity: 0.8;
  transition: all 0.2s;
}


:deep(.t-menu__item:hover:not(.t-is-active)),
:deep(.t-submenu__title:hover) {
  background-color: var(--td-bg-color-container-hover);
  transform: translateX(4px);

  .t-icon {
    opacity: 1;
    transform: scale(1.1);
  }
}

:deep(.t-menu__item.t-is-active) {
  background-color: color-mix(in srgb, var(--td-brand-color), transparent 85%) !important;
  color: var(--td-brand-color);
  font-weight: 600;

  .t-icon {
    opacity: 1;
    color: var(--td-brand-color);
  }

  &::before {
    content: '';
    position: absolute;
    left: 0;
    top: 50%;
    transform: translateY(-50%);
    width: 3px;
    height: 14px;
    background-color: var(--td-brand-color);
    border-radius: 0 4px 4px 0;
  }
}

:deep(.t-submenu) .t-menu__item {
  margin: 4px 10px;
}

:deep(.t-submenu) .t-menu__item.t-is-active::before {
  display: none;
}

</style>
