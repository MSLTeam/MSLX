const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const ts = require('typescript');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/utils/passkey.ts'), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const b64 = (value) => Buffer.from(value).toString('base64url');
const bytes = (value) => new Uint8Array(value).buffer;
function load(credentials = {}) {
  const exports = {};
  const context = {
    exports,
    URL,
    ArrayBuffer,
    Uint8Array,
    TextEncoder,
    atob,
    btoa,
    PublicKeyCredential: {},
    navigator: { credentials },
    window: {
      isSecureContext: true,
      PublicKeyCredential: {},
      location: { origin: 'https://panel.example', protocol: 'https:', hostname: 'panel.example' },
    },
  };
  vm.runInNewContext(source, context);
  return { api: exports, context };
}

test('passkey controls require supported same-origin secure domain', () => {
  const { api, context } = load();
  assert.equal(api.passkeySupported(), true);
  assert.equal(api.passkeySupported('https://other.example'), false);
  context.window.isSecureContext = false;
  assert.equal(api.passkeySupported(), false);
  context.window.isSecureContext = true;
  context.window.location = { origin: 'http://192.168.0.1', protocol: 'http:', hostname: '192.168.0.1' };
  assert.equal(api.passkeySupported(), false);
});

test('creation fallback decodes challenge, stable user handle and excluded credential IDs', async () => {
  let received;
  const { api } = load({
    create: async (options) => {
      received = options.publicKey;
      return {
        id: b64([255, 0, 254]),
        rawId: bytes([255, 0, 254]),
        type: 'public-key',
        authenticatorAttachment: 'platform',
        getClientExtensionResults: () => ({}),
        response: {
          clientDataJSON: bytes([1, 2, 3]),
          attestationObject: bytes([3, 4, 5]),
          getTransports: () => ['internal'],
        },
      };
    },
  });
  const result = await api.createPasskey(
    { challenge: b64([254, 0, 255]), user: { id: b64([0, 1, 255]) }, excludeCredentials: [{ id: b64([255, 254, 0]) }] },
    new AbortController().signal,
  );
  assert.deepEqual([...received.challenge], [254, 0, 255]);
  assert.deepEqual([...received.user.id], [0, 1, 255]);
  assert.deepEqual([...received.excludeCredentials[0].id], [255, 254, 0]);
  assert.deepEqual(Buffer.from(result.response.attestationObject, 'base64url'), Buffer.from([3, 4, 5]));
  assert.equal(result.rawId, '_wD-');
  assert.deepEqual([...result.response.transports], ['internal']);
});

test('assertion fallback serializes authentic signatures, user handles and extension results', async () => {
  let received;
  const { api } = load({
    get: async (options) => {
      received = options;
      return {
        id: 'id',
        rawId: bytes([9, 8, 7]),
        type: 'public-key',
        getClientExtensionResults: () => ({ appid: false }),
        response: {
          clientDataJSON: bytes([1]),
          authenticatorData: bytes([2]),
          signature: bytes([255, 254, 253]),
          userHandle: bytes([3, 4]),
        },
      };
    },
  });
  const signal = new AbortController().signal;
  const result = await api.getPasskey({ challenge: b64([0, 255]), allowCredentials: [{ id: b64([1, 254]) }] }, signal);
  assert.equal(received.signal, signal);
  assert.deepEqual([...received.publicKey.allowCredentials[0].id], [1, 254]);
  assert.deepEqual(Buffer.from(result.response.signature, 'base64url'), Buffer.from([255, 254, 253]));
  assert.deepEqual(Buffer.from(result.response.userHandle, 'base64url'), Buffer.from([3, 4]));
  assert.equal(result.clientExtensionResults.appid, false);
});

test('cancelled prompts remain distinguishable from server validation errors', () => {
  const { api } = load();
  assert.equal(api.passkeyCancelled({ name: 'NotAllowedError' }), true);
  assert.equal(api.passkeyCancelled({ name: 'AbortError' }), true);
  assert.equal(api.passkeyCancelled(new Error('signature rejected')), false);
});
