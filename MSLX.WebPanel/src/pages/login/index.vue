<script lang="ts">
export default {
  name: 'LoginIndex',
};
</script>
<script setup lang="ts">

import Login from './components/Login.vue';
import LoginHeader from './components/Header.vue';
import TdesignSetting from '@/layouts/setting.vue';
</script>
<template>
  <div class="login-wrapper">
    <login-header />

    <div class="login-container">
      <div class="title-container">
        <h1 class="title margin-no">登录到</h1>
        <h1 class="title">MSLX 网页管理中心</h1>
      </div>

      <login />
      <tdesign-setting />
    </div>

    <footer class="copyright">Copyright @ 2021-{{ new Date().getFullYear() }} MSLTeam. All Rights Reserved</footer>
  </div>
</template>


<style lang="less" scoped>
@import url('./index.less');
</style>
