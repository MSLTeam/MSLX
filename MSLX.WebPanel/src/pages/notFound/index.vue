<script setup lang="ts">
import { computed } from 'vue';
import { useSettingStore } from '@/store';
import { changeUrl } from '@/router';

const settingStore = useSettingStore();

const themeMode = computed(() => settingStore.displayMode);

</script>

<template>
  <div class="result-wrapper" :class="themeMode">
    <div class="result-content">
      <div class="result-container">

        <div class="result-info">
          <h1 class="code">404</h1>
          <h2 class="title">掉出了这个世界......</h2>
          <p class="desc">越过基岩的边界，只剩无尽的黑夜，<br>请点击重生，寻回原本的世界。</p>
        </div>

        <div class="action-box">
          <t-button
            theme="primary"
            size="large"
            block
            class="home-btn"
            @click="changeUrl('/')"
          >
            返回主页
          </t-button>
        </div>

        <footer class="copyright">Copyright @ 2021-{{ new Date().getFullYear() }} MSLTeam</footer>
      </div>
    </div>
  </div>
</template>

<style lang="less" scoped>
.result-wrapper {
  width: 100vw;
  min-height: 100vh;
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;
  transition: all 0.3s ease;
  background-image: url('@/assets/bg_light_new.jpg');
}

.dark.result-wrapper {
  background-image: url('@/assets/bg_night_new.jpg');
}

.result-container {
  width: 420px;
  max-width: 90%;
  padding: 50px 40px;
  border-radius: 16px;
  box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.2);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  display: flex;
  flex-direction: column;
  align-items: center;
  z-index: 10;
  text-align: center;
  transition: transform 0.3s ease, background 0.3s ease;
}

.result-info {
  margin-bottom: 40px;

  .code {
    font-size: 96px;
    font-weight: 700;
    line-height: 1.1;
    margin: 0;
    background: linear-gradient(135deg, #40a9ff 0%, #1890ff 100%);
    -webkit-background-clip: text;
    background-clip: text;
    color: transparent;
    opacity: 0.9;
  }

  .title {
    font-size: 24px;
    font-weight: 600;
    margin-top: 10px;
    margin-bottom: 16px;
    letter-spacing: 1px;
  }

  .desc {
    font-size: 14px;
    opacity: 0.7;
    margin: 0;
    line-height: 1.6;
    padding: 0 20px;
  }
}

.action-box {
  width: 100%;
  .home-btn {
    border-radius: 8px;
    height: 48px;
    font-size: 16px;
  }
}

.copyright {
  text-align: center;
  font-size: 12px;
  margin-top: 32px;
  opacity: 0.6;
}

.light.result-wrapper {
  background-color: rgba(255, 255, 255, 0.2);

  .result-container {
    background: rgba(255, 255, 255, 0.65);
    border: 1px solid rgba(255, 255, 255, 0.4);

    .title, .desc, .copyright {
      color: #333;
    }
  }
}

.dark.result-wrapper {
  background-color: rgba(0, 0, 0, 0.2);
  background-blend-mode: overlay;

  .result-container {
    background: rgba(30, 30, 40, 0.5);
    border: 1px solid rgba(255, 255, 255, 0.15);

    .title, .desc, .copyright {
      color: #fff;
    }

    .code {
      background: linear-gradient(135deg, #69c0ff 0%, #40a9ff 100%);
      -webkit-background-clip: text;
      color: transparent;
    }
  }
}

@media (max-width: 768px) {
  .result-container {
    width: 100%;
    margin: 20px;
    padding: 40px 24px;
  }

  .result-info .code {
    font-size: 72px;
  }
}
</style>
