const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
function loadTs(relative, mocks = {}) {
  const exports = {};
  const source = fs.readFileSync(require.resolve('../src/' + relative + '.ts'), 'utf8');
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  new Function('require', 'exports', output)(name => Object.hasOwn(mocks, name) ? mocks[name] : require(name), exports);
  return exports;
}


// Exercise the actual helper with isolated storage, node inventory and transport.
function setup(post = async () => ({ token: token('slave-a'), expiresAt: Date.now() / 1000 + 600 }), inventory) {
  const values = new Map([['BASE_URL', 'http://master.test'], ['TOKEN', 'parent-session']]);
  global.localStorage = { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
  const session = new Map();
  global.sessionStorage = { getItem: key => session.get(key) ?? null, setItem: (key, value) => session.set(key, value), removeItem: key => session.delete(key) };
  global.window = { location: { href: 'http://panel.test/instance/console/1', origin: 'http://panel.test' } };
  const exports = {};
  const nodes = [{ nodeId: 'slave-a', nodeUrl: 'http://slave.test' }, { nodeId: 'removed', nodeUrl: 'http://removed.test', pendingRemoval: true }];
  const nodeStore = inventory || { slaveNodes: nodes, fetchError: '' };
  const source = fs.readFileSync(require.resolve('../src/utils/nodeTarget.ts'), 'utf8');
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  new Function('require', 'exports', output)(name => {
    if (name === '@/utils/request') return { request: { post } };
    if (name === '@/store/modules/node') return { useNodeStore: () => nodeStore };
    if (name === '@/config/global') return { BASE_URL_NAME: 'BASE_URL', TOKEN_NAME: 'TOKEN' };
    return require(name);
  }, exports);
  return { ...exports, values };
}
function token(nodeId, scope = 'download') {
  return `header.${Buffer.from(JSON.stringify({ ...(nodeId ? { NodeId: nodeId } : {}), UserId: 'test-user', Scope: scope, exp: Math.floor(Date.now() / 1000) + 600 })).toString('base64url')}.signature`;
}

test('ordinary URLs stay local regardless of legacy global selection', () => {
  const helper = setup();
  helper.values.set('ACTIVE_NODE_ID', 'slave-a');
  assert.equal(helper.getPageNodeId(), 'local');
  window.location.href += '?node=slave-a';
  assert.equal(helper.getNodeTarget().baseUrl, 'http://slave.test');
  assert.throws(() => helper.getNodeTarget('missing'));
  assert.throws(() => helper.getNodeTarget('removed'));
});

test('async download credentials retain their node after navigation', () => {
  const helper = setup();
  const url = helper.nodeResourceUrl('/api/files/instance/1/download', token('slave-a'), 'download_token');
  assert.equal(url.origin, 'http://slave.test');
  assert.equal(url.searchParams.get('x-node-id'), 'slave-a');
  window.location.href += '?node=slave-a';
  const local = helper.nodeResourceUrl('/api/files/instance/1/download', token(), 'download_token');
  assert.equal(local.origin, 'http://master.test');
  assert.equal(local.searchParams.has('x-node-id'), false);
  assert.throws(() => helper.nodeResourceUrl('/download', token('missing'), 'download_token'));
  assert.throws(() => helper.nodeResourceUrl('/download', '', 'download_token'));
});

test('node credential cache deduplicates requests and separates purposes', async () => {
  const calls = [];
  const helper = setup(async (...args) => { calls.push(args); return { token: token('slave-a'), expiresAt: Date.now() / 1000 + 600 }; });
  await Promise.all([helper.getNodeToken('slave-a'), helper.getNodeToken('slave-a')]);
  assert.equal(calls.length, 1);
  assert.equal(calls[0][1].requestToSlaveNode, false);
  await helper.getNodeToken('slave-a', 'media');
  assert.equal(calls.length, 2);
  assert.equal(calls[1][0].data.scope, 'media');
});

test('in-flight credentials cannot be cached or returned after account change', async () => {
  let finish;
  const helper = setup(() => new Promise(resolve => { finish = resolve; }));
  const pending = helper.getNodeToken('slave-a', 'download');
  helper.values.set('TOKEN', 'different-account');
  finish({ token: token('slave-a'), expiresAt: Date.now() / 1000 + 600 });
  await assert.rejects(pending, /登录状态已变化/);
  assert.equal(helper.getCachedNodeToken('slave-a', 'download'), '');
});

test('queued Hub connections retain their node and never reuse another node with the same instance ID', async () => {
  let currentNode = 'slave-a';
  const urls = [];
  class Builder {
    withUrl(url) { this.url = url; return this; }
    configureLogging() { return this; }
    withAutomaticReconnect() { return this; }
    build() {
      urls.push(this.url);
      return {
        state: 'Disconnected', on() {}, onreconnecting() {}, onreconnected() {}, onclose() {},
        async start() { this.state = 'Connected'; }, async stop() { this.state = 'Disconnected'; }, async invoke(method) { if (method === 'ResumeConsole') return { log: null, pty: null, status: { isPty: false, isRunning: false } }; },
      };
    }
  }
  const exports = {};
  const source = fs.readFileSync(require.resolve('../src/store/modules/instanceHub.ts'), 'utf8');
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  new Function('require', 'exports', output)(name => {
    if (name === 'pinia') return { defineStore: (_name, setup) => setup };
    if (name === '@/utils/consoleStream') return loadTs('utils/consoleStream');
    if (name === '@microsoft/signalr') return { HubConnectionBuilder: Builder, LogLevel: { Warning: 3 } };
    if (name === '@/utils/hub') return { getHubUrl: (_path, node) => node, getHubOptions: () => ({}) };
    if (name === '@/utils/nodeTarget') return { getPageNodeId: () => currentNode };
    return require(name);
  }, exports);
  const store = exports.useInstanceHubStore();
  const queued = store.connect(1);
  currentNode = 'slave-b'; // A navigation occurs before the serialized connection starts.
  await queued;
  assert.deepEqual(urls, ['slave-a']);
  await store.connect(1);
  assert.deepEqual(urls, ['slave-a', 'slave-b']);
});


test('malformed resource credentials fail without a local fallback', () => {
  const helper = setup();
  for (const value of ['broken', 'a.!.c', 'a.bnVsbA.c', 'a.W10.c', 'a.eyJOb2RlSWQiOjQyfQ.c', 'a.eyJOb2RlSWQiOiIifQ.c']) {
    assert.throws(() => helper.nodeResourceUrl('/download', value, 'download_token'), /凭据格式无效/);
  }
});

test('console recovery deduplicates overlap and orders live output without clearing the screen', () => {
  const { ConsoleStream } = loadTs('utils/consoleStream');
  const written = [];
  const stream = new ConsoleStream(text => written.push(text));
  stream.replay({ streamId: 'a', sequence: 2, gap: false, frames: [{ sequence: 1, text: 'one' }, { sequence: 2, text: 'two' }] });
  stream.receive('four', { streamId: 'a', sequence: 4 });
  stream.replay({ streamId: 'a', sequence: 4, gap: false, frames: [{ sequence: 3, text: 'three' }, { sequence: 4, text: 'four' }] });
  stream.receive('two', { streamId: 'a', sequence: 2 });
  assert.deepEqual(written, ['one', 'two', 'three', 'four']);
  assert.deepEqual(stream.cursor, { streamId: 'a', sequence: 4 });
  stream.replay({ streamId: 'b', sequence: 1, gap: true, frames: [{ sequence: 1, text: 'new process' }] });
  assert.equal(written.at(-1), 'new process');
});

function hubFixture() {
  const hubs = [];
  class Builder {
    withUrl() { return this; } configureLogging() { return this; } withAutomaticReconnect() { return this; }
    build() {
      const events = {}, callbacks = {}, calls = [];
      const hub = {
        events, callbacks, calls, state: 'Disconnected', result: null,
        on(name, handler) { events[name] = handler; },
        onreconnecting(handler) { callbacks.reconnecting = handler; },
        onreconnected(handler) { callbacks.reconnected = handler; },
        onclose(handler) { callbacks.close = handler; },
        async start() { this.state = 'Connected'; }, async stop() { this.state = 'Disconnected'; },
        async invoke(method, ...args) {
          calls.push([method, ...args]);
          if (method === 'ResumeConsole') return this.result ?? { log: null, pty: null, status: { isPty: true, isRunning: true } };
        },
      };
      hubs.push(hub); return hub;
    }
  }
  const exports = {};
  const output = ts.transpileModule(fs.readFileSync(require.resolve('../src/store/modules/instanceHub.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  new Function('require', 'exports', output)(name => {
    if (name === 'pinia') return { defineStore: (_name, setup) => setup };
    if (name === '@microsoft/signalr') return { HubConnectionBuilder: Builder, LogLevel: { Warning: 3 } };
    if (name === '@/utils/hub') return { getHubUrl: () => 'slave-a', getHubOptions: () => ({}) };
    if (name === '@/utils/nodeTarget') return { getPageNodeId: () => 'slave-a' };
    if (name === '@/utils/consoleStream') return loadTs('utils/consoleStream');
    return require(name);
  }, exports);
  return { store: exports.useInstanceHubStore(), hubs };
}

test('reconnect restores PTY size, merges in-flight output once, and blocks disconnected input', async () => {
  const { store, hubs } = hubFixture();
  const written = [], notices = [];
  store.onPtyData(text => written.push(text)); store.onSystemMessage(text => notices.push(text));
  let reportedSize = { cols: 80, rows: 24 };
  store.onPtyStatus(() => { void store.resizePty(reportedSize.cols, reportedSize.rows); });
  await store.connect(1); const hub = hubs[0];
  hub.result = { log: null, pty: { streamId: 'pty', sequence: 1, gap: false, frames: [{ sequence: 1, text: 'old' }] }, status: { isPty: true, isRunning: true } };
  await store.joinPtyGroup(80, 24);
  hub.state = 'Reconnecting'; hub.callbacks.reconnecting();
  reportedSize = { cols: 120, rows: 35 };
  await store.resizePty(120, 35);
  await store.sendPtyInput('never queued');
  assert.equal(hub.calls.some(call => call[0] === 'SendPtyInput'), false);
  let complete;
  hub.result = new Promise(resolve => { complete = resolve; });
  hub.state = 'Connected'; const recovered = hub.callbacks.reconnected();
  reportedSize = { cols: 140, rows: 40 };
  await store.resizePty(140, 40);
  hub.events.ReceivePtyData('three', { streamId: 'pty', sequence: 3 });
  complete({ log: null, pty: { streamId: 'pty', sequence: 3, gap: false, frames: [{ sequence: 2, text: 'two' }, { sequence: 3, text: 'three' }] }, status: { isPty: true, isRunning: true } });
  await recovered;
  assert.deepEqual(written, ['old', 'two', 'three']);
  assert.deepEqual(hub.calls.filter(call => call[0] === 'ResumeConsole').at(-1).slice(-3), [true, 140, 40]);
  assert.equal(store.isConnected.value, true);
  assert.equal(store.connectionNotice.value, '');
  assert.deepEqual(notices, []);
  await store.disconnect();
});

test('switching to log view while disconnected does not restore a PTY subscription', async () => {
  const { store, hubs } = hubFixture();
  await store.connect(1); const hub = hubs[0]; await store.joinPtyGroup(80, 24);
  hub.state = 'Reconnecting'; hub.callbacks.reconnecting(); await store.leavePtyGroup();
  hub.state = 'Connected'; await hub.callbacks.reconnected();
  assert.equal(hub.calls.filter(call => call[0] === 'ResumeConsole').at(-1)[4], false);
  hub.state = 'Reconnecting'; hub.callbacks.reconnecting();
  hub.state = 'Disconnected'; await hub.callbacks.close();
  assert.equal(store.connectionNotice.value, '连接已断开，请刷新页面');
  await store.disconnect();
});


test('server-initiated close renews the connection without restarting the terminal', async () => {
  const { store, hubs } = hubFixture();
  await store.connect(1); const hub = hubs[0]; await store.joinPtyGroup(90, 30);
  hub.state = 'Disconnected'; await hub.callbacks.close();
  assert.equal(store.isConnected.value, true);
  assert.equal(store.connectionNotice.value, '');
  assert.equal(hub.calls.filter(call => call[0] === 'ResumeConsole').at(-1)[4], true);
  await store.disconnect();
});

test('inventory failures keep their cause, preserve known targets, and clear after retry', async () => {
  const { createPinia, setActivePinia } = require('pinia');
  setActivePinia(createPinia());
  let unavailable = true;
  const { useNodeStore } = loadTs('store/modules/node', {
    '@/api/node': { getSlaveNodes: async () => {
      if (unavailable) throw new Error('主节点暂时不可用');
      return [{ nodeId: 'new-child', nodeUrl: 'http://new-child.test' }];
    } }, '@/router': {},
  });
  const store = useNodeStore();
  store.slaveNodes = [{ nodeId: 'cached-child', nodeUrl: 'http://cached-child.test' }];
  const helper = setup(undefined, store);
  const original = console.error; console.error = () => {};
  try { await store.fetchNodes(true); } finally { console.error = original; }
  assert.match(store.fetchError, /节点列表加载失败.*主节点暂时不可用/);
  assert.throws(() => helper.getNodeTarget('new-child'), /节点列表加载失败/);
  assert.equal(helper.getNodeTarget('cached-child').baseUrl, 'http://cached-child.test');
  assert.equal(helper.getNodeTarget('local').baseUrl, 'http://master.test');
  unavailable = false; await store.fetchNodes(true);
  assert.equal(store.fetchError, '');
  assert.equal(helper.getNodeTarget('new-child').baseUrl, 'http://new-child.test');
  assert.throws(() => helper.getNodeTarget('unknown'), /节点不存在、已解除链接或无访问权限/);
});

test('cold local media credentials and logout update thumbnail bindings without a login-token fallback', async () => {
  const vue = require('vue'); const helper = setup(); let finish;
  const scoped = loadTs('utils/scopedToken', {
    '@/utils/request': { request: { get: () => new Promise(resolve => { finish = resolve; }) } },
    './nodeTarget': helper,
  });
  const files = loadTs('api/files', {
    '@/utils/nodeTarget': helper, '@/utils/request': { request: {} }, '@/utils/scopedToken': scoped,
  });
  const rendered = [];
  const effect = vue.effect(() => { rendered.push(files.getFileThumbnailUrl(1, 'test.png')); });
  try {
    assert.deepEqual(rendered, ['']);
    const mediaToken = token(undefined, 'media');
    const pending = scoped.getMediaToken(); finish({ token: mediaToken, expiresIn: 600 });
    await pending;
    assert.equal(rendered.length, 2);
    const url = new URL(rendered.at(-1));
    assert.equal(url.origin, 'http://master.test');
    assert.equal(url.searchParams.has('x-user-token'), false);
    assert.equal(url.searchParams.has('x-node-id'), false);
    assert.equal(url.searchParams.get('media_token'), mediaToken);
    scoped.clearMediaToken();
    assert.equal(rendered.at(-1), '');
  } finally { vue.stop(effect); }
});

// Compile the real SFC setup so the queue test exercises component behavior, not a copy.
function loadGrid(getDownloadToken, getVideoStreamUrl) {
  const { parse, compileScript } = require('@vue/compiler-sfc');
  const vue = require('vue');
  const { descriptor } = parse(fs.readFileSync(require.resolve('../src/pages/instance/files/components/FileGridView.vue'), 'utf8'));
  const script = compileScript(descriptor, { id: 'grid-recovery-test' });
  const output = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  const exports = {};
  new Function('require', 'exports', output)(name => {
    if (name === 'vue') return { ...vue, onMounted() {}, onUnmounted() {} };
    if (name === 'tdesign-icons-vue-next') return {};
    if (name === '@/api/files') return { getVideoStreamUrl };
    if (name === '@/utils/scopedToken') return { getDownloadToken, getSyncMediaToken: () => '' };
    return require(name);
  }, exports);
  const props = vue.reactive({ instanceId: 1, currentPath: '', fileList: [], selectedRowKeys: [] });
  return { ...exports.default.setup(props, { expose() {}, emit() {} }), testProps: props };
}

test('failed video credentials and URL construction release queue slots for subsequent videos', async () => {
  const helper = setup(); let attempts = 0;
  const files = loadTs('api/files', {
    '@/utils/nodeTarget': helper, '@/utils/request': { request: {} },
    '@/utils/scopedToken': { getSyncDownloadToken: () => '' },
  });
  const originalDocument = global.document; const originalWarn = console.warn;
  let createdVideos = 0;
  global.document = { createElement: type => {
    if (type === 'canvas') return { getContext: () => ({ drawImage() {} }), toDataURL: () => 'data:image/jpeg;base64,ok' };
    createdVideos++;
    const handlers = {};
    return {
      duration: 3, videoWidth: 320, videoHeight: 180,
      addEventListener: (name, handler) => { handlers[name] = handler; },
      set src(value) { queueMicrotask(() => handlers.loadedmetadata()); },
      set currentTime(value) { queueMicrotask(() => handlers.seeked()); },
      removeAttribute() {}, load() {},
    };
  } };
  console.warn = () => {};
  try {
    const grid = loadGrid(async () => {
      attempts++;
      if (attempts === 1) throw new Error('credential request failed');
      if (attempts === 2) return '';
      if (attempts === 3) return 'broken-jwt';
      return token();
    }, files.getVideoStreamUrl);
    for (const name of ['request-failure.mp4', 'empty-token.mp4', 'bad-token.mp4', 'working.mp4']) grid.enqueueVideoThumbnail(name, name);
    for (let i = 0; i < 20 && grid.videoStatuses.value['working.mp4'] !== 'done'; i++) await new Promise(resolve => setImmediate(resolve));
    assert.equal(attempts, 4);
    assert.equal(createdVideos, 1);
    for (const name of ['request-failure.mp4', 'empty-token.mp4', 'bad-token.mp4']) assert.equal(grid.videoStatuses.value[name], 'error');
    assert.equal(grid.videoStatuses.value['working.mp4'], 'done');
    assert.match(grid.videoThumbnails.value['working.mp4'], /^data:image\/jpeg/);
    grid.testProps.fileList = [{ name: 'request-failure.mp4', type: 'file' }];
    await require('vue').nextTick();
    assert.equal(grid.videoStatuses.value['request-failure.mp4'], undefined);
    grid.enqueueVideoThumbnail('request-failure.mp4', 'request-failure.mp4');
    for (let i = 0; i < 20 && grid.videoStatuses.value['request-failure.mp4'] !== 'done'; i++) await new Promise(resolve => setImmediate(resolve));
    assert.equal(grid.videoStatuses.value['request-failure.mp4'], 'done');
  } finally { global.document = originalDocument; console.warn = originalWarn; }
});
