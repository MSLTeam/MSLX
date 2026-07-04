<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useUserStore } from '@/store';

import Banner from './components/Banner.vue';
import InfoCard from './components/InfoCard.vue';
import SystemStatus from './components/SystemStatus.vue';
import Announcement from './components/Announcement.vue';
import { changeUrl } from '@/router';
import PluginSlot from '@/components/PluginSlot.vue';

const userStore = useUserStore();

// 核心状态
const isOldBrowser = ref(false);
const isOldWindows = ref(false);
const isNotSecure = ref(false);

onMounted(() => {
  // 检测高级css支持情况
  if (typeof CSS !== 'undefined' && CSS.supports) {
    isOldBrowser.value = !CSS.supports('color: color-mix(in srgb, red, blue)');
  } else {
    isOldBrowser.value = true;
  }

  // 检测古董系统
  const ua = navigator.userAgent;
  if (/(Windows NT 6\.1|Windows NT 6\.2|Windows NT 6\.3)/i.test(ua)) {
    isOldWindows.value = true;
  }

  // 检测是否为非 HTTPS 且非本地/局域网环境
  const isHttps = window.location.protocol === 'https:';
  const hostname = window.location.hostname;

  // 正则匹配：localhost, 127.0.0.1, 10.x.x.x, 172.16-31.x.x, 192.168.x.x
  const isLocalOrLAN =
    /^(localhost|127\.0\.0\.1|10\.\d+\.\d+\.\d+|172\.(1[6-9]|2\d|3[01])\.\d+\.\d+|192\.168\.\d+\.\d+)$/i.test(hostname);

  if (!isHttps && !isLocalOrLAN) {
    isNotSecure.value = true;
  }
});

// 计算需要展示的浏览器/系统警告内容
const browserWarning = computed(() => {
  if (!isOldBrowser.value) return null;

  // 旧浏览器，且身处 Win7/8 系统
  if (isOldWindows.value) {
    return {
      title: '系统版本过旧 (Windows 7/8)',
      message:
        '受限于操作系统，您的 Chrome/Edge 浏览器已被官方永远停更在 109 版本，无法渲染本控制面板的现代 UI。请升级至 Windows 10/11，或改用受支持的 Firefox 浏览器！',
      btnText: '获取 Firefox',
      url: 'https://www.mozilla.org/zh-CN/firefox/new/',
    };
  }

  // 如果是旧浏览器，但在现代系统上
  return {
    title: '浏览器内核版本过低',
    message:
      '检测到您当前的浏览器不支持部分现代 Web 技术。为了保证 MSLX 面板的正常显示与完整功能，强烈建议您升级浏览器。',
    btnText: '获取新版 Chrome',
    url: 'https://www.google.cn/chrome/',
  };
});

// 判断是否是默认用户
const isDefaultUser = computed(() => {
  return userStore.userInfo?.username === 'mslx';
});

// 动态计算瀑布流动画延迟
const getDelay = (baseIndex: number) => {
  let offset = 0;
  if (browserWarning.value) offset += 1;
  if (isDefaultUser.value) offset += 1;
  if (isNotSecure.value) offset += 1;
  return `${(baseIndex + offset) * 0.05}s`;
};
</script>

<template>
  <div class="flex flex-col gap-6 mx-auto w-full min-h-screen pb-6">
    <t-alert
      v-if="browserWarning"
      theme="error"
      :title="browserWarning.title"
      :message="browserWarning.message"
      class="list-item-anim w-full shadow-sm"
      style="animation-delay: 0s"
    >
      <template #operation>
        <span
          class="cursor-pointer font-bold flex items-center gap-1 hover:opacity-80 transition-opacity"
          @click="changeUrl(browserWarning.url)"
        >
          {{ browserWarning.btnText }} <i class="fa-solid fa-arrow-right text-sm"></i>
        </span>
      </template>
    </t-alert>

    <t-alert
      v-if="isDefaultUser"
      theme="warning"
      title="安全风险提示"
      message="检测到您当前正在使用默认账号 (mslx)。为了保障系统安全，请务必尽快修改用户名和密码！"
      class="list-item-anim w-full shadow-sm"
      :style="{ animationDelay: browserWarning ? '0.05s' : '0s' }"
    >
      <template #operation>
        <span
          class="cursor-pointer font-bold flex items-center gap-1 hover:opacity-80 transition-opacity"
          @click="changeUrl('/settings/profile')"
        >
          去修改 <i class="fa-solid fa-arrow-right text-sm"></i>
        </span>
      </template>
    </t-alert>

    <t-alert
      v-if="isNotSecure"
      theme="error"
      title="连接未加密 (存在安全风险)"
      message="检测到当前正通过明文 HTTP 协议在公网环境下访问面板。为了防止你的密码和数据在传输中被窃听，强烈建议配置本地 SSL，或者自行通过反向代理启用 HTTPS！"
      class="list-item-anim w-full shadow-sm"
      :style="{ animationDelay: browserWarning ? '0.05s' : '0s' }"
    >
      <template #operation>
        <span
          class="cursor-pointer font-bold flex items-center gap-1 hover:opacity-80 transition-opacity"
          @click="changeUrl('/settings/daemon')"
        >
          配置 SSL <i class="fa-solid fa-arrow-right text-sm"></i>
        </span>
      </template>
    </t-alert>

    <banner class="list-item-anim" :style="{ animationDelay: getDelay(0) }" />
    <info-card class="list-item-anim" :style="{ animationDelay: getDelay(1) }" />
    <system-status class="list-item-anim" :style="{ animationDelay: getDelay(2) }" />
    <!--插件扩展区域 dashboard-index-after-system-status -->
    <plugin-slot class="list-item-anim" name="dashboard-index-after-system-status" />
    <announcement class="list-item-anim" :style="{ animationDelay: getDelay(3) }" />
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
