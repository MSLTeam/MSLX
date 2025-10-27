import { defineStore } from 'pinia';
import { TOKEN_NAME, BASE_URL_NAME } from '@/config/global';
import { store, usePermissionStore } from '@/store';
import { request } from '@/utils/request';

const InitUserInfo = {
  name: '',
  avatar: '',
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
      const { url, key } = loginParams as { url: string; key: string };

      // 1. 处理 URL
      let processedUrl = url;
      if (!/^(https?:)?\/\//.test(url)) {
        processedUrl = `http://${url}`;
      }

      try {
        // 将 baseURL 和 headers 作为 AxiosRequestConfig (第一个参数) 的一部分传入
        const resData = await request.get({
          url: '/status',
          baseURL: processedUrl,
          headers: {
            'x-api-key': key,
          },
        });

        // 4. 登录成功，存储 token 和 baseUrl
        this.token = key;
        this.baseUrl = processedUrl;
        localStorage.setItem(TOKEN_NAME, key);
        localStorage.setItem(BASE_URL_NAME, processedUrl);

        // 5. 存储用户信息 (resData 已经是 data.data)
        this.userInfo = {
          name: resData.user,
          avatar: resData.avatar,
          roles: ['all'], // 假设登录成功就是 'all' 权限
          ...resData,
        };

        // 6. 初始化动态路由
        const permissionStore = usePermissionStore();
        permissionStore.initRoutes(this.userInfo.roles);

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
        const resData = await request.get({ url: '/status' });

        this.userInfo = {
          name: resData.user,
          avatar: resData.avatar,
          roles: ['all'],
          ...resData,
        };
      } catch (e) {
        console.error('Get user info failed:', e);
        await this.logout();
        throw new Error('获取用户信息失败，请重新登录');
      }
    },

    async logout() {
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
      // 页面加载、持久化数据恢复后
      if (ctx.store.token) {
        const permissionStore = usePermissionStore();

        // --- 修正点: 'this' 在这里是 undefined, 必须用 ctx.store ---
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
