import { createPinia } from 'pinia';
import { createPersistedState } from 'pinia-plugin-persistedstate';

const store = createPinia();
store.use(createPersistedState());

export { store };

export * from './modules/permission';
export * from './modules/setting';
export * from './modules/update';
export * from './modules/webpanel';
export * from './modules/instance';
export * from './modules/frp';
export * from './modules/user';

export default store;
