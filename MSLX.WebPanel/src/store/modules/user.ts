import { defineStore } from 'pinia';
import { TOKEN_NAME, BASE_URL_NAME } from '@/config/global';
import { store, usePermissionStore } from '@/store';
import { request } from '@/utils/request';
import router from '@/router';
import { MessagePlugin } from 'tdesign-vue-next';

// 存储记忆信息
const REMEMBER_URL_NAME = 'remembered_url';
const REMEMBER_KEY_NAME = 'remembered_key';

const InitUserInfo = {
  name: '',
  avatar: '',
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
    baseUrl: localStorage.getItem(BASE_URL_NAME) || '',
    userInfo: { ...InitUserInfo },
  }),
  getters: {
    roles: (state) => {
      // 在这里也加个兜底，确保 getters 总是返回数组
      return state.userInfo?.roles || [];
    },
  },
  actions: {
    async login(loginParams: Record<string, unknown>) {
      const { url, key, checked } = loginParams as { url: string; key: string; checked: boolean };

      // 1处理 URL
      let processedUrl = url;
      if (!/^(https?:)?\/\//.test(url)) {
        processedUrl = `http://${url}`;
      }

      try {
        // 将 baseURL 和 headers 作为 AxiosRequestConfig (第一个参数) 的一部分传入
        const resData = await request.get({
          url: '/api/status',
          baseURL: processedUrl,
          headers: {
            'x-api-key': key,
          },
        });

        // 登录成功，存储 token 和 baseUrl (当前会话)
        this.token = key;
        this.baseUrl = processedUrl;
        localStorage.setItem(TOKEN_NAME, key);
        localStorage.setItem(BASE_URL_NAME, processedUrl);

        // 记住
        if (checked) {
          localStorage.setItem(REMEMBER_URL_NAME, url);
          localStorage.setItem(REMEMBER_KEY_NAME, key);
        } else {
          localStorage.removeItem(REMEMBER_URL_NAME);
          localStorage.removeItem(REMEMBER_KEY_NAME);
        }

        // 存储用户信息
        this.userInfo = {
          name: resData.user,
          avatar: resData.avatar,
          roles: ['all'],
          ...resData,
        };

        // 初始化动态路由
        const permissionStore = usePermissionStore();
        await permissionStore.initRoutes(this.userInfo.roles);

      } catch (e) {
        console.error('Login failed:', e);
        throw e;
      }
    },

    async getUserInfo() {
      if (!this.token) {
        return;
      }

      try {
        const resData = await request.get({ url: '/api/status' });

        this.userInfo = {
          name: resData.user,
          avatar: resData.avatar,
          roles: ['all'],
          ...resData,
        };
      } catch (e) {
        // 获取不到用户信息 要求重新登录
        console.error('Get user info failed:', e);
        await this.logout(); // logout 会清除路由
        await router.push('/login');
        MessagePlugin.error('连接 MSLX 守护进程失败，请重新登录！');
      }
    },

    /**
     * +++ 修改 logout +++
     */
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
        const permissionStore = usePermissionStore();

        ctx.store.getUserInfo().then(() => {
          permissionStore.initRoutes(ctx.store.roles || []);
        }).catch(() => {
          // getUserInfo 失败, logout 逻辑已在内部执行
        });
      }
    },
  },
});

export function getUserStore() {
  return useUserStore(store);
}
