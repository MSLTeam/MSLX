<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';

import { getSettings, updateSettings } from '@/api/settings';
import type { SettingsModel } from '@/api/model/settings';
import { useUserStore } from '@/store';

const userStore = useUserStore();

const loading = ref(false);
const submitLoading = ref(false);

// 表单数据
const formData = reactive<SettingsModel>({
  user: '',
  avatar: '',
  fireWallBanLocalAddr: false,
  openWebConsoleOnLaunch: true,
  neoForgeInstallerMirrors: 'MSL Mirrors',
  listenHost: 'localhost',
  listenPort: 1027,
});

// 头像相关
const avatarMode = ref<'qq' | 'custom'>('qq');
const qqNumber = ref('');

// 安装镜像源
const mirrorOptions = [
  { label: '官方源 (较慢)', value: 'Official' },
  { label: 'MSL镜像源 (推荐)', value: 'MSL Mirrors' },
  { label: 'MSL镜像源 - 备用', value: 'MSL Mirrors Backup' },
];

// 开发者列表
const developers = [
  {
    name: 'xiaoyu',
    role: 'Core Developer',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=1791123970&spec=640&img_type=jpg',
    desc: '核心开发者',
  },
  {
    name: 'Weheal',
    role: 'Core Developer',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=2035582067&spec=640&img_type=jpg',
    desc: '核心开发者',
  },
];

// 初始化加载
const initData = async () => {
  loading.value = true;
  try {
    const res = await getSettings();
    // 赋值给表单
    Object.assign(formData, res);

    // 这头像是qq嘛？
    const qqMatch = res.avatar && res.avatar.match(/nk=(\d+)/);
    if (qqMatch && qqMatch[1]) {
      avatarMode.value = 'qq';
      qqNumber.value = qqMatch[1];
    } else {
      avatarMode.value = 'custom';
    }
  } catch (e) {
    MessagePlugin.error(e.message);
  } finally {
    loading.value = false;
  }
};

// 生成qq头像地址
watch(qqNumber, (val) => {
  if (avatarMode.value === 'qq' && val) {
    formData.avatar = `https://q.qlogo.cn/g?b=qq&nk=${val}&s=640`;
  }
});

// 监听模式切换
const handleModeChange = (val: any) => {
  if (val === 'qq' && qqNumber.value) {
    formData.avatar = `https://q.qlogo.cn/g?b=qq&nk=${qqNumber.value}&s=640`;
  } else if (val === 'custom') {
    // 不清空
  }
};

// 提交保存
const onSubmit = async () => {
  submitLoading.value = true;
  try {
    await updateSettings(formData);
    userStore.getUserInfo(); // 刷新一下用户信息
    MessagePlugin.success('设置保存成功');
  } catch (error) {
    MessagePlugin.error(error.message);
  } finally {
    submitLoading.value = false;
  }
};

onMounted(() => {
  initData();
});
</script>

<template>
  <div class="settings-page">
    <t-space direction="vertical" size="large" style="width: 100%">
      <t-card :bordered="false" title="系统设置" :loading="loading">
        <template #actions>
          <t-button theme="primary" variant="text" @click="initData">刷新</t-button>
        </template>

        <t-form ref="form" :data="formData" :label-width="120" label-align="left" @submit="onSubmit">
          <div class="section-title"><t-icon name="user" /> 用户设置</div>

          <t-form-item label="用户名" name="user">
            <t-input v-model="formData.user" placeholder="请输入显示的用户名" />
          </t-form-item>

          <t-form-item label="头像来源">
            <t-radio-group v-model="avatarMode" variant="default-filled" @change="handleModeChange">
              <t-radio-button value="qq">
                <template #default><t-icon name="logo-qq" /> QQ头像</template>
              </t-radio-button>
              <t-radio-button value="custom">
                <template #default><t-icon name="link" /> 自定义链接</template>
              </t-radio-button>
            </t-radio-group>
          </t-form-item>

          <t-form-item :label="avatarMode === 'qq' ? 'QQ号码' : '图片链接'">
            <t-input v-if="avatarMode === 'qq'" v-model="qqNumber" placeholder="请输入QQ号自动获取头像" type="number" />
            <t-input v-else v-model="formData.avatar" placeholder="请输入头像图片 URL" />
          </t-form-item>

          <t-form-item label="头像预览">
            <div class="avatar-preview">
              <t-avatar :image="formData.avatar" size="80px" shape="round">
                {{ formData.user ? formData.user.slice(0, 1).toUpperCase() : 'User' }}
              </t-avatar>
              <div v-if="avatarMode === 'qq' && !qqNumber" class="preview-tips">请输入QQ号以预览</div>
            </div>
          </t-form-item>

          <t-divider dashed />

          <div class="section-title"><t-icon name="desktop" /> MSLX 守护进程端设置</div>

          <t-form-item label="自动打开控制台" help="MSLX 守护进程启动成功后，是否自动登录网页端控制台。">
            <t-space align="center">
              <t-switch v-model="formData.openWebConsoleOnLaunch" />
              <span class="status-text">{{ formData.openWebConsoleOnLaunch ? '已开启' : '已关闭' }}</span>
            </t-space>
          </t-form-item>

          <t-form-item
            label="安装镜像源"
            style="margin-top: 15px"
            help="选择在自动安装 NeoForge / Forge 时所使用的镜像源。"
          >
            <t-select v-model="formData.neoForgeInstallerMirrors" :options="mirrorOptions" placeholder="请选择镜像源" />
          </t-form-item>

          <t-form-item>
            <t-button theme="primary" type="submit" :loading="submitLoading" block class="save-btn">
              <template #icon><t-icon name="save" /></template>
              保存设置
            </t-button>
          </t-form-item>

          <t-divider dashed />

          <div class="section-title"><t-icon name="secured" /> 安全设置</div>

          <t-form-item label="禁止本地访问" help="开启后将禁止本地回环地址访问，增强安全性。">
            <t-space align="center">
              <t-switch v-model="formData.fireWallBanLocalAddr" />
              <span class="status-text">{{ formData.fireWallBanLocalAddr ? '已开启' : '已关闭' }}</span>
            </t-space>
          </t-form-item>

          <t-form-item
            label="监听地址设置"
            style="margin-top: 15px"
            help="设置MSLX守护进程的监听地址。(需要重启守护进程生效,若不明白这是干什么的请一定不要修改！)"
          >
            <t-space break-line align="center">
              <t-space align="center">
                <span class="status-text">监听地址</span>
                <t-input v-model="formData.listenHost" />
              </t-space>
              <t-space align="center">
                <span class="status-text">监听端口</span>
                <t-input v-model="formData.listenPort" />
              </t-space>
            </t-space>
          </t-form-item>
        </t-form>
      </t-card>

      <t-card :bordered="false" title="关于项目">
        <div class="about-content">
          <p class="about-desc">感谢以下开发者对本项目的杰出贡献：</p>
          <t-row :gutter="[16, 16]">
            <t-col v-for="dev in developers" :key="dev.name" :span="6" :xs="12">
              <div class="dev-card">
                <t-avatar :image="dev.avatar" size="60px" shape="circle" />
                <div class="dev-info">
                  <div class="dev-name">{{ dev.name }}</div>
                  <div class="dev-role">{{ dev.role }}</div>
                </div>
              </div>
            </t-col>
          </t-row>
        </div>
      </t-card>
    </t-space>
  </div>
</template>

<style scoped lang="less">
.settings-page {
  margin: 0 auto;
}

.section-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  margin-bottom: 24px;
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 8px;

  .t-icon {
    font-size: 18px;
    color: var(--td-brand-color);
  }
}

.avatar-preview {
  display: flex;
  align-items: center;
  gap: 16px;

  .preview-tips {
    font-size: 12px;
    color: var(--td-text-color-placeholder);
  }
}

.status-text {
  font-size: 14px;
  color: var(--td-text-color-secondary);
}

.save-btn {
  margin-top: 16px;

  /* 移动端按钮加高 */
  @media screen and (max-width: 768px) {
    height: 44px;
  }
}

/* 关于模块样式 */
.about-content {
  .about-desc {
    color: var(--td-text-color-secondary);
    margin-bottom: 20px;
    font-size: 14px;
  }
}

.dev-card {
  display: flex;
  align-items: center;
  background-color: var(--td-bg-color-container-hover);
  padding: 16px;
  border-radius: var(--td-radius-medium);
  transition: all 0.3s;

  &:hover {
    transform: translateY(-2px);
    box-shadow: var(--td-shadow-1);
  }

  .dev-info {
    margin-left: 12px;
    display: flex;
    flex-direction: column;

    .dev-name {
      font-weight: 600;
      font-size: 16px;
      color: var(--td-text-color-primary);
    }

    .dev-role {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 2px;
    }
  }
}
</style>
