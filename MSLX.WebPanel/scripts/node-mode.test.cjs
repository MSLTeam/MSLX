const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const vue = require('vue');
const { parse, compileScript } = require('@vue/compiler-sfc');

function settingsForm(initial, target = 'local') {
  let settings = { isSlaveMode: false, slaveLinkKey: '', canManageNodeMode: true,
    modeLocked: false, linkKeyLocked: false, ...initial };
  const requests = [];
  const file = require.resolve('../src/pages/settings/base/components/SystemSettingsCard.vue');
  const { descriptor } = parse(fs.readFileSync(file, 'utf8'));
  const script = compileScript(descriptor, { id: 'node-mode-form' });
  const code = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const exports = {};
  new Function('require', 'exports', code)(name => {
    if (name === 'vue') return { ...vue, onMounted() {} };
    if (name === '@/utils/nodeTarget') return { getPageNodeId: () => target };
    if (name === '@/store') return { useUpdateStore: () => ({}) };
    if (name === 'tdesign-vue-next') return { MessagePlugin: { success() {}, error: assert.fail } };
    if (name === '@/api/settings') return {
      getSettings: async node => { assert.equal(node, target); return { ...settings }; },
      updateSettings: async (data, node) => {
        requests.push({ data, node });
        settings = { ...settings, ...data };
        if (data.isSlaveMode && !settings.slaveLinkKey) settings.slaveLinkKey = 'auto-generated-key';
      },
    };
    return {};
  }, exports);
  return { form: exports.default.setup({}, { expose() {} }), requests };
}

test('local mode form submits only changed mode/key and reads the generated key after saving', async () => {
  const { form, requests } = settingsForm();
  await form.initData();
  await form.onSysSubmit();
  assert.equal(Object.hasOwn(requests[0].data, 'isSlaveMode'), false);
  assert.equal(Object.hasOwn(requests[0].data, 'slaveLinkKey'), false);
  form.sysData.isSlaveMode = true;
  await form.onSysSubmit();
  assert.equal(requests[1].data.isSlaveMode, true);
  assert.equal(Object.hasOwn(requests[1].data, 'slaveLinkKey'), false);
  assert.equal(form.sysData.slaveLinkKey, 'auto-generated-key');
  form.sysData.slaveLinkKey = 'replacement-key';
  await form.onSysSubmit();
  assert.equal(requests[2].data.slaveLinkKey, 'replacement-key');
  assert.equal(Object.hasOwn(requests[2].data, 'isSlaveMode'), false);
});

test('delegated and CLI-controlled forms omit mode and pairing secrets from writes', async () => {
  for (const options of [
    { canManageNodeMode: false },
    { modeLocked: true, linkKeyLocked: true },
  ]) {
    const { form, requests } = settingsForm({ isSlaveMode: true, slaveLinkKey: 'original', ...options }, 'child');
    await form.initData();
    form.sysData.isSlaveMode = false;
    form.sysData.slaveLinkKey = 'changed';
    await form.onSysSubmit();
    assert.equal(Object.hasOwn(requests[0].data, 'isSlaveMode'), false);
    assert.equal(Object.hasOwn(requests[0].data, 'slaveLinkKey'), false);
    assert.equal(Object.hasOwn(requests[0].data, 'canManageNodeMode'), false);
    assert.equal(requests[0].node, 'child');
  }
});
