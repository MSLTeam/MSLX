import { defineStore } from 'pinia';
import { TOKEN_NAME, BASE_URL_NAME } from '@/config/global';
import { store, usePermissionStore } from '@/store';
import { request } from '@/utils/request';
import { changeUrl } from '@/router';

// 存储记忆信息
const REMEMBER_URL_NAME = 'remembered_url';
const REMEMBER_USER_NAME = 'remembered_username';

const InitUserInfo = {
  name: '',
  username: '',
  avatar: '',
  id: '',
  version: '',
  systemInfo:{
    netVersion: "",
    osType: "",
    osVersion: "",
    osArchitecture: "",
    hostname: ""
  },
  targetFrontendVersion:{
    desktop:"",
    panel: ""
  },
  roles: [],
};

export const useUserStore = defineStore('user', {
  state: () => ({
    token: localStorage.getItem(TOKEN_NAME) || '',
    baseUrl: localStorage.getItem(BASE_URL_NAME) || window.location.origin,
    userInfo: { ...InitUserInfo },
  }),

  getters: {
    roles: (state) => state.userInfo?.roles || [],
  },

  actions: {
    // 判断后端是否同源
    async checkConnection(baseUrl: string) {
      let processedUrl = baseUrl;
      if (baseUrl && !/^(https?:)?\/\//.test(baseUrl)) {
        processedUrl = `http://${baseUrl}`;
      }
      try {
        await request.get({
          url: '/api/ping',
          baseURL: processedUrl || undefined,
          timeout: 3000,
        });
        return true;
      } catch {
        return false;
      }
    },

    // 登录
    async login(loginParams: Record<string, unknown>) {
      const { url, username, password, checked } = loginParams;

      // 处理 URL
      let processedUrl = url as string;
      if (processedUrl && !/^(https?:)?\/\//.test(processedUrl)) {
        processedUrl = `http://${processedUrl}`;
      }
      const requestBaseUrl = processedUrl || '';

      try {
        // 请求登录接口拿到 Token
        const res = await request.post({
          url: '/api/auth/login',
          baseURL: requestBaseUrl,
          data: { username, password },
        });

        // 保存 Token 和 BaseURL
        this.token = res.token;
        this.baseUrl = requestBaseUrl;

        localStorage.setItem(TOKEN_NAME, res.token);
        localStorage.setItem(BASE_URL_NAME, requestBaseUrl);

        // 处理记住账号
        if (checked) {
          localStorage.setItem(REMEMBER_URL_NAME, (url as string) || '');
          localStorage.setItem(REMEMBER_USER_NAME, username as string);
        } else {
          localStorage.removeItem(REMEMBER_URL_NAME);
          localStorage.removeItem(REMEMBER_USER_NAME);
        }

        await this.getUserInfo();

      } catch (e) {
        console.error('Login failed:', e);
        throw e;
      }
    },

    // oauth 登录
    async loginByOAuth(data: { token: string; userInfo?: any }) {
      try {
        this.token = data.token;
        localStorage.setItem(TOKEN_NAME, data.token);

        if (!this.baseUrl) {
          this.baseUrl = window.location.origin;
          localStorage.setItem(BASE_URL_NAME, this.baseUrl);
        }

        await this.getUserInfo();

      } catch (e) {
        console.error('OAuth登录失败:', e);
        throw e;
      }
    },


    // 用户信息
    async getUserInfo() {
      if (!this.token) return;

      try {
        const resData = await request.get({
          url: '/api/status',
        });

        // 存储用户信息
        this.userInfo = {
          ...InitUserInfo,
          ...resData,
          name: resData.user || resData.username, // 兼容字段
          roles: ['all'],
        };

        // 初始化路由权限
        const permissionStore = usePermissionStore();
        await permissionStore.initRoutes(this.userInfo.roles);

      } catch (e) {
        console.error('Get user info failed:', e);
        await this.logout();
        changeUrl('/login');
      }
    },

    // 登出
    async logout() {
      const permissionStore = usePermissionStore();
      await permissionStore.clearRoutes();

      localStorage.removeItem(TOKEN_NAME);
      localStorage.removeItem(BASE_URL_NAME);
      this.token = '';
      this.baseUrl = '';
      this.userInfo = { ...InitUserInfo };
    },

    async removeToken() {
      this.token = '';
    },
  },

  persist: {
    afterRestore: (ctx) => {
      if (ctx.store.token) {
        // 页面刷新后，利用 Token 重新拉取 Status 信息
        ctx.store.getUserInfo();
      }
    },
  },
});

export function getUserStore() {
  return useUserStore(store);
}
