<template>
  <div :class="layoutCls">
    <t-head-menu :class="menuCls" :theme="theme" expand-type="popup" :value="active">
      <template #logo>
        <span v-if="showLogo" :class="`${prefix}-side-nav-logo-wrapper`" @click="handleNav('/dashboard/base')">
          <img style="width: 32px;" :src="CustomLogo" :class="`${prefix}-side-nav-logo-img`" alt="logo" />
          <span  style="margin-left: 8px;font-size: 18px;font-weight: bold;text-overflow: ellipsis;overflow: hidden;white-space: nowrap;" :class="`${prefix}-side-nav-logo-text hide-on-mobile`"> MSLX 管理中心 </span>
        </span>

        <div v-if="layout == 'side'" class="header-operate-left show-on-mobile">
          <t-button theme="default" shape="square" variant="text" @click="changeCollapsed">
            <t-icon class="collapsed-icon" name="view-list" />
          </t-button>
        </div>
      </template>

      <template v-if="layout !== 'side'" #default>
        <menu-content class="header-menu hide-on-mobile" :nav-data="menu" />

        <t-popup
          placement="bottom-left"
          class="show-on-mobile header-menu-mobile-popup"
          :overlay-style="{ padding: '0', width: '100%', marginTop: '2px', boxShadow: 'none', borderTop: '1px solid var(--td-border-level-1-color)' }"
        >
          <t-button style="margin-left: -16px;" theme="default" shape="square" variant="text">
            <t-icon name="app" />
          </t-button>
          <template #content>
            <t-menu :value="active" :theme="theme" expand-mutex @change="handleMenuChange" style="width: 100vw;">
              <menu-content :nav-data="menu" />
            </t-menu>
          </template>
        </t-popup>
      </template>
      <template #operations>
        <div class="operations-container">
          <t-tooltip placement="bottom" content="代码仓库" class="hide-on-mobile">
            <t-button theme="default" shape="square" variant="text" @click="navToGitHub">
              <t-icon name="logo-github" />
            </t-button>
          </t-tooltip>

          <t-tooltip placement="bottom" content="帮助文档" class="hide-on-mobile">
            <t-button theme="default" shape="square" variant="text" @click="navToHelper">
              <t-icon name="help-circle" />
            </t-button>
          </t-tooltip>

          <t-dropdown :min-column-width="135" trigger="click">
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item class="operations-dropdown-container-item" @click="handleNav('/settings')">
                  <t-icon name="user-circle"></t-icon>个人中心
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-container-item" @click="handleLogout">
                  <t-icon name="poweroff"></t-icon>退出登录
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
            <t-button class="header-user-btn" theme="default" variant="text">
              <template #icon>
                <t-avatar :image="userStore.userInfo.avatar" />
              </template>
              <div class="header-user-account" style="margin-left: 6px;">{{ userStore.userInfo.name }}</div>
              <template #suffix><t-icon name="chevron-down" /></template>
            </t-button>
          </t-dropdown>

          <t-dropdown :min-column-width="135" trigger="click" class="show-on-mobile">
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item class="operations-dropdown-container-item" @click="navToGitHub">
                  <t-icon name="logo-github"></t-icon>代码仓库
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-container-item" @click="navToHelper">
                  <t-icon name="help-circle"></t-icon>帮助文档
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-container-item" @click="toggleSettingPanel">
                  <t-icon name="setting"></t-icon>系统设置
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
            <t-button theme="default" shape="square" variant="text">
              <t-icon name="more" />
            </t-button>
          </t-dropdown>

          <t-tooltip placement="bottom" content="系统设置" class="hide-on-mobile">
            <t-button theme="default" shape="square" variant="text" @click="toggleSettingPanel">
              <t-icon name="setting" />
            </t-button>
          </t-tooltip>
        </div>
      </template>
    </t-head-menu>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { PropType } from 'vue';
import { useRouter } from 'vue-router';
import { useSettingStore,useUserStore } from '@/store';
import { getActive } from '@/router';
import { prefix } from '@/config/global';
import type { MenuRoute } from '@/types/interface';

const userStore = useUserStore();


import MenuContent from './MenuContent.vue';
import CustomLogo from "@/assets/logo.png";

const props = defineProps({
  theme: {
    type: String as PropType<'light' | 'dark'>,
    default: 'light',
  },
  layout: {
    type: String,
    default: 'top',
  },
  showLogo: {
    type: Boolean,
    default: true,
  },
  menu: {
    type: Array as PropType<MenuRoute[]>,
    default: () => [],
  },
  isFixed: {
    type: Boolean,
    default: false,
  },
  isCompact: {
    type: Boolean,
    default: false,
  },
  maxLevel: {
    type: Number,
    default: 3,
  },
});

const router = useRouter();
const settingStore = useSettingStore();

const toggleSettingPanel = () => {
  settingStore.updateConfig({
    showSettingPanel: true,
  });
};

const active = computed(() => getActive());

const layoutCls = computed(() => [`${prefix}-header-layout`]);

const menuCls = computed(() => {
  const { isFixed, layout, isCompact } = props;
  return [
    {
      [`${prefix}-header-menu`]: !isFixed,
      [`${prefix}-header-menu-fixed`]: isFixed,
      [`${prefix}-header-menu-fixed-side`]: layout === 'side' && isFixed,
      [`${prefix}-header-menu-fixed-side-compact`]: layout === 'side' && isFixed && isCompact,
    },
  ];
});

const changeCollapsed = () => {
  settingStore.updateConfig({
    isSidebarCompact: !settingStore.isSidebarCompact,
  });
};

const handleNav = (url) => {
  router.push(url);
};

const handleMenuChange = (value: string) => {
  // value = 路由 path
  if (value) {
    router.push(value);
  }
};
const handleLogout = () => {
  router.push({
    path: '/login',
    query: { redirect: encodeURIComponent(router.currentRoute.value.fullPath) },
  });
};

const navToGitHub = () => {
  window.open('https://github.com/MSLTeam/MSLX');
};

const navToHelper = () => {
  window.open('https://www.mslmc.cn');
};
</script>
<style lang="less" scoped>
.@{starter-prefix}-header {
  &-menu-fixed {
    position: fixed;
    top: 0;
    z-index: 1001;

    &-side {
      left: 232px;
      right: 0;
      z-index: 10;
      width: auto;
      transition: all 0.3s;
      &-compact {
        left: 64px;
      }
    }
  }

  &-logo-container {
    cursor: pointer;
    display: inline-flex;
  }
}
.header-menu {
  flex: 1 1 auto;
  display: inline-flex;

  :deep(.t-menu__item) {
    min-width: unset;
    padding: 0px 16px;
  }
}

.header-menu-mobile-popup {
  display: inline-flex;
  align-items: center;
}

.operations-container {
  display: flex;
  align-items: center;

  .t-popup__reference {
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .t-button {
    margin: 0 8px;
    &.header-user-btn {
      margin: 0;
    }
  }

  .t-icon {
    font-size: 20px;
    &.general {
      margin-right: 16px;
    }
  }
}

.header-operate-left {
  display: flex;
  margin-left: 20px;
  align-items: normal;
  line-height: 0;

  .collapsed-icon {
    font-size: 20px;
  }
}

.header-logo-container {
  width: 184px;
  height: 26px;
  display: flex;
  margin-left: 24px;
  color: var(--td-text-color-primary);

  .t-logo {
    width: 100%;
    height: 100%;
    &:hover {
      cursor: pointer;
    }
  }

  &:hover {
    cursor: pointer;
  }
}

.header-user-account {
  display: inline-flex;
  align-items: center;
  color: var(--td-text-color-primary);
  .t-icon {
    margin-left: 4px;
    font-size: 16px;
  }
}

:deep(.t-head-menu__inner) {
  border-bottom: 1px solid var(--td-border-level-1-color);
}

.t-menu--light {
  .header-user-account {
    color: var(--td-text-color-primary);
  }
}
.t-menu--dark {
  .t-head-menu__inner {
    border-bottom: 1px solid var(--td-gray-color-10);
  }
  .header-user-account {
    color: rgba(255, 255, 255, 0.55);
  }
  .t-button {
    --ripple-color: var(--td-gray-color-10) !important;
    &:hover {
      background: var(--td-gray-color-12) !important;
    }
  }
}

.operations-dropdown-container-item {
  width: 100%;
  display: flex;
  align-items: center;

  .t-icon {
    margin-right: 3px;
  }

  :deep(.t-dropdown__item) {
    .t-dropdown__item__content {
      display: flex;
      justify-content: center;
    }
    .t-dropdown__item__content__text {
      display: flex;
      align-items: center;
      font-size: 14px;
    }
  }

  :deep(.t-dropdown__item) {
    width: 100%;
    margin-bottom: 0px;
  }
  &:last-child {
    :deep(.t-dropdown__item) {
      margin-bottom: 8px;
    }
  }
}

.show-on-mobile {
  display: none !important;
}

// 低于一定宽度菜单显示不了 那就算移动端咯
@media (max-width: 1012px) {

  .hide-on-mobile {
    display: none !important;
  }

  .header-operate-left.show-on-mobile {
    display: flex !important;
  }

  .header-menu-mobile-popup.show-on-mobile {
    display: inline-flex !important;
    align-items: center;
  }

  .operations-container .show-on-mobile {
    display: inline-flex !important;
    align-items: center;
  }

  :deep(.t-head-menu__inner) {
    display: flex;
    justify-content: space-between;
  }
  :deep(.t-head-menu__logo) {
    flex-shrink: 0; // 防止 logo 被压缩
  }
  :deep(.t-head-menu__operations) {
    flex-shrink: 0; // 防止操作按钮被压缩
  }
  :deep(.t-head-menu__default) {
    flex: 0 1 auto !important;
    margin-left: 16px;
  }
}


</style>
