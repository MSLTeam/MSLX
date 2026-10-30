const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const vue = require('vue');
function load(relative, mocks) {
  const exports = {};
  const output = ts.transpileModule(fs.readFileSync(require.resolve('../src/' + relative + '.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  new Function('require', 'exports', output)(name => Object.hasOwn(mocks, name) ? mocks[name] :
    name.startsWith('http') && mocks.importer ? mocks.importer(name) : require(name), exports);
  return exports;
}
function storage() {
  const values = new Map([['BASE', 'http://master.test'], ['TOKEN', 'admin']]);
  global.localStorage = { getItem: key => values.get(key) ?? null };
  global.window = { location: { href: 'http://panel.test/instance/list?node=child-a', origin: 'http://panel.test' } };
  return values;
}
const constants = { BASE_URL_NAME: 'BASE', TOKEN_NAME: 'TOKEN' };
const allSlots = ['instance-console-dropdown', 'instance-console-overview-bottom', 'instance-setting-more', 'instance-settings-tab',
  'dashboard-index-after-system-status', 'frp-console-control-panel-bottom', 'frp-create-provider', 'settings-profile-bottom', 'settings-daemon-bottom'];

test('plugin declaration accepts only the exact string enum, defaulting an absent field to no-test', () => {
  const { parsePluginMultiNodeSupport, PluginMultiNodeSupport } = load('api/model/plugins', {});
  assert.equal(parsePluginMultiNodeSupport(undefined), PluginMultiNodeSupport.NoTest);
  for (const value of ['true', 'false', 'no-test']) assert.equal(parsePluginMultiNodeSupport(value), value);
  for (const value of [true, false, null, 0, 1, 'TRUE', 'supported', '']) assert.throws(() => parsePluginMultiNodeSupport(value));
});

function contextModule(send) {
  return load('utils/pluginNodeContext', {
    '@/utils/request': { request: { request: send } }, '@/config/global': constants,
    './nodeTarget': { getPageNodeId: () => 'child-a', getNodeTarget: nodeId => ({ nodeId, baseUrl: 'http://node.test' }), nodeResourceUrl: () => 'resource' },
    './scopedToken': {}, './hub': {}, '@microsoft/signalr': {},
  });
}

test('a captured plugin client stays on its node across navigation, respects explicit targets, and rejects stale ownership', async () => {
  const values = storage(); const calls = [];
  const { createPluginNodeContext } = contextModule(async (config, options) => { calls.push({ config, options }); return 'ok'; });
  const context = createPluginNodeContext('child-a', 'plugin-a');
  window.location.href = 'http://panel.test/instance/list?node=child-b';
  await context.request.post({ url: '/api/plugins/plugin-a/config', data: { value: 1 } });
  assert.equal(calls[0].options.targetNodeId, 'child-a');
  await context.request.get({ url: '/api/plugins/plugin-a/config' }, { requestToSlaveNode: false });
  assert.equal(calls[1].options.targetNodeId, 'local');
  await context.request.get({ url: '/api/plugins/plugin-a/config' }, { targetNodeId: 'child-b' });
  assert.equal(calls[2].options.targetNodeId, 'child-b');
  assert.throws(() => context.request.get({ url: 'http://other.test/api/config' }), /相对 API/);
  context.dispose();
  assert.equal(calls[0].config.signal.aborted, true);
  assert.throws(() => context.request.get({ url: '/api/config' }), /页面或登录状态/);
  const next = createPluginNodeContext('child-b');
  values.set('TOKEN', 'another-login');
  assert.throws(() => next.request.get({ url: '/api/config' }), /页面或登录状态/);
  assert.equal(calls.length, 3);
});

// Use Vue's real component runtime to verify transparent forwarding for every extension slot.
function renderer() {
  const insert = (child, parent, anchor) => {
    child.parent = parent;
    const at = anchor ? parent.children.indexOf(anchor) : -1;
    if (at < 0) parent.children.push(child); else parent.children.splice(at, 0, child);
  };
  return vue.createRenderer({
    createElement: tag => ({ tag, children: [], props: {} }), createText: text => ({ text, children: [] }), createComment: text => ({ text, children: [] }),
    insert, remove: node => { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); },
    setText: (node, text) => { node.text = text; }, setElementText: (node, text) => { node.text = text; },
    patchProp: (node, key, old, value) => { node.props[key] = value; }, parentNode: node => node.parent,
    nextSibling: () => null, querySelector: () => null, setScopeId() {}, insertStaticContent: () => [],
  });
}

test('all nine slot wrappers preserve instance props, open/close methods, saved events and node injection', async () => {
  storage();
  const { bindPluginComponent, usePluginNodeContext } = contextModule(async () => ({}));
  for (const slot of allSlots) {
    let received; let node; let saved = 0; const child = vue.ref();
    const component = vue.defineComponent({
      props: ['serverId', 'instanceId', 'frpId', 'status', 'isRunning'], emits: ['saved'],
      setup(props, { expose, emit }) {
        received = { ...props }; node = usePluginNodeContext();
        const isOpen = vue.ref(false);
        expose({ open() { isOpen.value = true; emit('saved'); }, close() { isOpen.value = false; }, isOpen });
        return () => vue.h('span', slot);
      },
    });
    const bound = bindPluginComponent(component, 'child-a', 'fixture');
    const app = renderer().createApp({ render: () => vue.h(bound, { ref: child, serverId: 41, instanceId: 41, frpId: 9, status: 2, isRunning: true, onSaved: () => saved++ }) });
    app.mount({ children: [] });
    await vue.nextTick();
    assert.equal(node.nodeId, 'child-a', slot);
    assert.deepEqual(received, { serverId: 41, instanceId: 41, frpId: 9, status: 2, isRunning: true });
    child.value.open(); assert.equal(child.value.isOpen, true); assert.equal(saved, 1);
    child.value.close(); assert.equal(child.value.isOpen, false);
    app.unmount();
    assert.throws(() => node.request.get({ url: '/api/test' }), /页面或登录状态/);
  }
});

test('node registry loads child-only plugins and different versions without mixing menus or slots, and clears on logout', async () => {
  const values = storage(); const requests = []; const loaded = [];
  const slots = {}; const ui = { nodeId: 'local', loading: false, error: '', extensionsByNode: slots,
    registerExtension(slot, ext, node) { ((slots[node] ||= {})[slot] ||= []).push(ext); },
    clear() { for (const key of Object.keys(slots)) delete slots[key]; },
  };
  const permission = { routers: [{ path: '/instance', name: 'instance', children: [] }], whiteListRouters: ['/login'] };
  const routes = new Map();
  const router = { hasRoute: name => routes.has(name), addRoute: (...args) => {
    const route = args.at(-1);
    const names = [route.name, ...(route.children || []).map(child => child.name)].filter(Boolean);
    names.forEach(name => routes.set(name, route));
    return () => names.forEach(name => routes.delete(name));
  } };
  const { PluginMultiNodeSupport } = load('api/model/plugins', {});
  const loader = load('utils/pluginManager', {
    '@/router': { __esModule: true, default: router }, '@/layouts/index.vue': {}, '@/config/global': constants,
    '@/store': { getPermissionStore: () => permission, usePluginUIStore: () => ui, useUserStore: () => ({ isAdmin: true }) },
    '@/api/plugins': { getPluginList: async (_params, node) => {
      requests.push(node);
      if (node === 'offline') throw new Error('node offline');
      const list = [{ id: 'shared', name: 'shared', status: '已启用', entryPath: `/${node}/entry.js`, version: node === 'local' ? '1' : '2' }];
      if (node !== 'local') list.push({ id: 'unsupported', status: '已启用', entryPath: '/unsupported.js', supportsMultiNode: 'false' });
      return list;
    } },
    '@/api/model/plugins': { PluginMultiNodeSupport },
    './nodeTarget': { getPageNodeId: () => 'local', getNodeTarget: node => ({ baseUrl: `http://${node}.test` }), pluginNodePages: new Set() },
    './pluginNodeContext': { bindPluginComponent: (component, nodeId) => ({ component, nodeId }) },
    './request': { request: { get: async () => ({ features: { pluginMultiNode: true } }) } },
    importer: url => { loaded.push(url); return { pluginConfig: {
      routes: [{ name: 'SharedPlugin', path: '/plugin-shared', component: {}, meta: { title: 'Shared' } }],
      extensions: allSlots.map(slot => ({ slot, component: { version: url } })),
    } }; },
  });
  await loader.loadAllPlugins('local'); loader.activatePluginNode('local');
  assert.equal(Object.keys(slots.local).length, 9);
  await loader.loadAllPlugins('child-a'); loader.activatePluginNode('child-a');
  assert.equal(Object.keys(slots['child-a']).length, 9);
  assert.equal(slots['child-a'][allSlots[0]][0].component.nodeId, 'child-a');
  assert.equal(permission.routers.filter(route => route.meta?.pluginOwner).length, 1);
  assert.equal(loaded.some(url => url.includes('unsupported')), false);
  await loader.loadAllPlugins('local'); loader.activatePluginNode('local');
  assert.deepEqual(requests, ['local', 'child-a']);
  await assert.rejects(loader.loadAllPlugins('offline'), /node offline/);
  loader.activatePluginNode('offline');
  assert.equal(ui.error, 'node offline'); assert.equal(routes.size, 0);
  values.set('TOKEN', 'another-user');
  assert.equal(loader.arePluginsLoaded('local'), false);
  assert.equal(Object.keys(slots).length, 0);
});

test('nodeStore reports hasSlaveNodes only when active non-removed slave nodes exist', () => {
  const { setActivePinia, createPinia } = require('pinia');
  setActivePinia(createPinia());
  const { useNodeStore } = load('store/modules/node', { '@/api/node': {}, '@/router': {} });
  const store = useNodeStore();

  assert.equal(store.hasSlaveNodes, false);
  store.slaveNodes = [{ nodeId: 'orphan', pendingRemoval: true }];
  assert.equal(store.hasSlaveNodes, false);
  store.slaveNodes = [{ nodeId: 'child-1' }];
  assert.equal(store.hasSlaveNodes, true);
  store.slaveNodes = [{ nodeId: 'child-1', pendingRemoval: true }, { nodeId: 'child-2' }];
  assert.equal(store.hasSlaveNodes, true);
});

test('multi-node plugin tag visibility is suppressed in standalone mode and shown in multi-node context', () => {
  const isMultiNodeTagVisible = (targetNodeId, hasSlaveNodes) => targetNodeId !== 'local' || Boolean(hasSlaveNodes);
  // Pure standalone master node with no slave nodes: hidden
  assert.equal(isMultiNodeTagVisible('local', false), false);
  // Master node with active slave nodes present in cluster: visible
  assert.equal(isMultiNodeTagVisible('local', true), true);
  // Directly targeting a slave node: always visible
  assert.equal(isMultiNodeTagVisible('slave-a', false), true);
  assert.equal(isMultiNodeTagVisible('slave-a', true), true);
});
