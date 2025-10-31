<template>
  <router-view v-slot="{ Component }">
    <transition name="fade" mode="out-in">
      <keep-alive>
        <component :is="Component" />
      </keep-alive>
    </transition>
  </router-view>

</template>

<script setup lang="ts">

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
