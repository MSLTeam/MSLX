<script setup lang="ts">
import { ref, computed } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';

// 催更计数器
const urgeCount = ref(0);
// 控制图片抖动动画
const isShaking = ref(false);

const phrases = [
  '开发者收到了你的怨念！(╯°□°）╯︵ ┻━┻',
  '正在疯狂敲击键盘中... 🔥',
  '生产队的驴都不敢这么歇！🐴',
  '别催了别催了，头发已经掉光了！👴',
  '再催就把 Bug 变成 Feature！🐛',
  '服务器正在冒烟... 💥',
  '正在与 Bug 进行殊死搏斗！⚔️',
  '不要急，我在试图理解我昨晚写的代码... 🤔',
  '进度条：99%... (卡住了) 🚫',
  '键盘冒火星子了！灭火器准备！🧯',
  '新建文件夹 (2) - 最终版 - 绝对不改版.zip 📁',
  '404 Developer Not Found 🤖',
  '已读不回 (假的，正在改) 📱',
  '正在向虚空终端请求算力... 🧠',
  '画饼中，请稍后... 🥞',
  '再催我就去提瓦特大陆摸鱼了！🎣',
  '纳西妲说她想吃枣椰蜜糖，没空写代码！🍬',
  '正在虚空终端检索：《如何 1 秒写完代码》... 🧠',
  '知识与你分享，但 Bug 不行！📖',
  '再催？再催就把你关进净善宫陪我一起加班！🏰',
  '陷入了改 Bug 的花神诞祭轮回... 第168次尝试... 🔄',
  '别急，代码还在梦境里生长呢 💤',
  '所有的代码，皆是智慧的结晶（大概）✨',
  '我要去上个厕所，把写不完的焦虑通通冲走！🚽',
  '兰那罗说这行代码它不想修，它想去唱歌 🎶',
  '或许... 我们可以用罐装知识把功能直接灌进去？🤔',
  '这虽然是痛痛，但也是成长的过程... (指修Bug) 🩹',
];

// 根据点击次数显示的文案
const dynamicText = computed(() => {
  if (urgeCount.value === 0) return '这里正在进行一项神秘的大工程...';
  if (urgeCount.value < 5) return '工期正在加急！';
  if (urgeCount.value < 10) return '好痛！别点啦！';
  return '呜呜呜，错啦，这就去写代码！😭';
});

// 催更按钮点击事件
const handleUrge = () => {
  urgeCount.value++;
  isShaking.value = true;

  // 500ms 后重置动画状态，以便下次触发
  setTimeout(() => {
    isShaking.value = false;
  }, 500);

  // 随机提示消息
  let msg = phrases[Math.floor(Math.random() * phrases.length)];

  // 点击次数过多时的特殊反馈
  if (urgeCount.value > 20) {
    msg = '键盘已经被敲烂了！求放过！🆘';
    MessagePlugin.error(msg);
  } else {
    MessagePlugin.success(msg);
  }
};
</script>

<template>
  <div class="construction-container">
    <div class="content-wrapper">
      <div class="image-box" :class="{ 'shake-anim': isShaking }">
        <img src="@/assets/others/aowu.jpg" alt="施工中" class="mascot-img" />
        <div class="halo"></div>
      </div>

      <h1 class="main-title">嗷呜！Ｏ(≧口≦)Ｏ</h1>

      <p class="description">{{ dynamicText }}</p>

      <div class="progress-decoration">
        <t-progress theme="plump" :percentage="30 + (urgeCount % 70)" :label="false" status="active" />
      </div>

      <div class="action-area">
        <t-button theme="primary" size="large" shape="round" variant="base" class="urge-btn" @click="handleUrge">
          <template #icon>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" style="margin-right: 4px">
              <path d="M8.5 1L2 9h5v6l6.5-8h-5V1z" />
            </svg>
          </template>
          催更 ({{ urgeCount }})
        </t-button>
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
.construction-container {
  width: 100%;
  height: 100%;
  display: flex;
  justify-content: center;
  align-items: center;
  color: var(--td-text-color-primary);
  padding: 20px;
  box-sizing: border-box;
  overflow: hidden;
}

.content-wrapper {
  text-align: center;
  max-width: 500px;
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 24px;
}

.image-box {
  position: relative;
  width: 200px;
  height: 200px;
  display: flex;
  justify-content: center;
  align-items: center;

  .mascot-img {
    width: 100%;
    height: 100%;
    object-fit: contain;
    border-radius: 50%;
    z-index: 2;
    // 给图片加一点阴影，更有层次感
    filter: drop-shadow(0 4px 12px rgba(0, 0, 0, 0.1));
  }

  .halo {
    position: absolute;
    width: 180px;
    height: 180px;
    background: var(--td-brand-color-focus);
    opacity: 0.2;
    border-radius: 50%;
    z-index: 1;
    filter: blur(20px);
    animation: breathe 3s infinite ease-in-out;
  }
}

.main-title {
  font-size: 28px;
  font-weight: bold;
  margin: 0;
  color: var(--td-brand-color);
  font-family: 'Comic Sans MS', 'Chalkboard SE', sans-serif;
}

.description {
  font-size: 16px;
  color: var(--td-text-color-secondary);
  margin: 0;
  line-height: 1.5;
  min-height: 24px;
}

.progress-decoration {
  width: 80%;
  opacity: 0.8;
}

.action-area {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  justify-content: center;
  margin-top: 10px;
}

.urge-btn {
  transition: all 0.2s ease;

  &:active {
    transform: scale(0.95);
  }
}

// 动画定义
@keyframes breathe {
  0%,
  100% {
    transform: scale(1);
    opacity: 0.2;
  }
  50% {
    transform: scale(1.2);
    opacity: 0.3;
  }
}

// 抖动动画类
.shake-anim {
  animation: shake 0.5s cubic-bezier(0.36, 0.07, 0.19, 0.97) both;
}

@keyframes shake {
  10%,
  90% {
    transform: translate3d(-1px, 0, 0) rotate(-1deg);
  }
  20%,
  80% {
    transform: translate3d(2px, 0, 0) rotate(2deg);
  }
  30%,
  50%,
  70% {
    transform: translate3d(-4px, 0, 0) rotate(-4deg);
  }
  40%,
  60% {
    transform: translate3d(4px, 0, 0) rotate(4deg);
  }
}

// 移动端适配微调
@media (max-width: 768px) {
  .image-box {
    width: 150px;
    height: 150px;
  }

  .main-title {
    font-size: 24px;
  }
}
</style>
