const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const ts = require('typescript');

function loadCreateUtils() {
  const exports = {};
  const filePath = path.resolve(__dirname, '../src/pages/frp/createFrp/utils/create.ts');
  const source = fs.readFileSync(filePath, 'utf8');
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;

  const mocks = {
    'vue': { h: () => {}, ref: (v) => ({ value: v }) },
    '@/api/frp': { postCreateFrpTunnel: async () => {} },
    '@/router': { changeUrl: () => {} },
    '@/store/modules/frp': { useTunnelsStore: () => ({ frpList: [], getTunnels: async () => {} }) },
    'tdesign-vue-next': {
      DialogPlugin: () => ({ hide: () => {} }),
      Input: {},
      Select: {},
      MessagePlugin: { warning: () => {}, success: () => {}, error: () => {}, info: () => {} },
    },
  };

  new Function('require', 'exports', output)(
    (name) => (Object.hasOwn(mocks, name) ? mocks[name] : require(name)),
    exports,
  );
  return exports;
}

const { convertIniToToml } = loadCreateUtils();

test('converts HTTP proxy with single custom_domains and disabled encryption/compression to valid TOML', () => {
  const ini = `
[common]
server_addr = 111.170.158.49
server_port = 8000
tls_enable = true
user = testuser
token = testtoken

[web_tunnel]
type = HTTP
local_ip = 127.0.0.1
local_port = 8080
custom_domains = test.example.com
use_encryption = false
use_compression = false
`;

  const toml = convertIniToToml(ini);

  // customDomains must be formatted as string array with brackets
  assert.match(toml, /customDomains = \["test\.example\.com"\]/);
  // transport encryption & compression must have transport. prefix
  assert.match(toml, /transport\.useEncryption = false/);
  assert.match(toml, /transport\.useCompression = false/);
  // top-level unknown fields should not exist
  assert.doesNotMatch(toml, /^useEncryption\s*=/m);
  assert.doesNotMatch(toml, /^useCompression\s*=/m);
  // type should be lowercase "http"
  assert.match(toml, /type = "http"/);
});

test('converts multiple custom_domains and locations to array', () => {
  const ini = `
[common]
server_addr = 127.0.0.1
server_port = 7000

[web]
type = http
local_ip = 127.0.0.1
local_port = 80
custom_domains = a.com, b.com
locations = /news, /about
use_encryption = true
use_compression = true
`;

  const toml = convertIniToToml(ini);

  assert.match(toml, /customDomains = \["a\.com", "b\.com"\]/);
  assert.match(toml, /locations = \["\/news", "\/about"\]/);
  assert.match(toml, /transport\.useEncryption = true/);
  assert.match(toml, /transport\.useCompression = true/);
});

test('handles domains alias and already bracketed custom_domains', () => {
  const ini = `
[common]
server_addr = 127.0.0.1
server_port = 7000

[web1]
type = http
local_ip = 127.0.0.1
local_port = 80
domains = alias.example.com

[web2]
type = http
local_ip = 127.0.0.1
local_port = 80
custom_domains = ["bracket.example.com"]
`;

  const toml = convertIniToToml(ini);

  assert.match(toml, /customDomains = \["alias\.example\.com"\]/);
  assert.match(toml, /customDomains = \["bracket\.example\.com"\]/);
});

test('verifies generated TOML against frpc binary syntax validation', () => {
  const frpcPath = path.resolve(__dirname, '../../MSLX.Daemon/bin/Debug/net10.0/DaemonData/Tools/frpc');
  if (!fs.existsSync(frpcPath)) {
    return; // Skip binary check if frpc binary is not present in build output
  }

  const ini = `
[common]
server_addr = 111.170.158.49
server_port = 8000
tls_enable = true
user = testuser
token = testtoken

[my_http_tunnel]
type = HTTP
local_ip = 127.0.0.1
local_port = 80
custom_domains = http.example.com
locations = /api, /web
use_encryption = false
use_compression = false
`;

  const toml = convertIniToToml(ini);
  const tempFile = path.resolve(__dirname, `temp_test_${Date.now()}.toml`);

  try {
    fs.writeFileSync(tempFile, toml, 'utf8');
    const result = execFileSync(frpcPath, ['verify', '-c', tempFile], { encoding: 'utf8' });
    assert.match(result, /syntax is ok/);
  } finally {
    if (fs.existsSync(tempFile)) {
      fs.unlinkSync(tempFile);
    }
  }
});
