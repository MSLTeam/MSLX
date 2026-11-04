const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

function readSource(relative) {
  return fs.readFileSync(path.resolve(__dirname, '../src', relative), 'utf8');
}

function evaluate(source, mocks = {}, bindings = {}) {
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const exports = {};
  new Function('require', 'exports', ...Object.keys(bindings), output)(
    name => Object.hasOwn(mocks, name) ? mocks[name] : require(name), exports, ...Object.values(bindings),
  );
  return exports;
}

function scriptStatements(relative, names) {
  const script = readSource(relative).match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1];
  const ast = ts.createSourceFile(relative, script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  return ast.statements.filter(statement => {
    if (ts.isFunctionDeclaration(statement)) return names.includes(statement.name?.text);
    return ts.isVariableStatement(statement) && statement.declarationList.declarations.some(
      declaration => names.includes(declaration.name.getText(ast)),
    );
  }).map(statement => statement.getText(ast)).join('\n');
}

function deferred() {
  let resolve;
  const promise = new Promise(r => { resolve = r; });
  return { promise, resolve };
}

const immediateTimer = callback => { queueMicrotask(callback); return 0; };

function setupChunkedUpload(api = {}, resume = {}) {
  return evaluate(readSource('utils/chunkedUpload.ts'), {
    '@/api/files': {
      initUpload: async () => ({ uploadId: 'test-upload' }),
      rememberUploadTarget: () => {},
      uploadChunk: async () => {},
      getUploadStatus: async () => ({ receivedChunks: [] }),
      finishUpload: async () => ({ uploadId: 'test-upload' }),
      ...api,
    },
    '@/utils/uploadResume': {
      computeFastFileFingerprint: async () => 'fingerprint',
      computeFileBlake3WithOptions: async () => 'hash',
      computeChunkCrc32c: async () => '00000000',
      getUploadResumeRecords: async () => [],
      saveUploadResumeRecord: async () => {},
      deleteUploadResumeRecord: async () => {},
      ...resume,
    },
  }, {
    window: { setTimeout, clearTimeout },
    setTimeout: immediateTimer,
  });
}

function setupUpload(api = {}, resume = {}) {
  return evaluate(readSource('hooks/useFileUpload.ts'), {
    vue: { ref: value => ({ value }), onUnmounted: () => {} },
    '@/utils/nodeTarget': { getPageNodeId: () => 'local' },
    '@/api/files': api,
    '@/utils/uploadResume': { saveUploadResumeRecord: async () => {}, ...resume },
    '@/utils/chunkedUpload': setupChunkedUpload(api, resume),
  }).useFileUpload();
}

test('chunk failures retain HTTP status and cause after stopping other workers', async () => {
  for (const status of [413, 403, 503]) {
    let requests = 0;
    const hook = setupUpload({
      uploadChunk: async () => {
        requests++;
        throw Object.assign(new Error('original upload failure'), { status });
      },
    });
    await assert.rejects(hook.startUpload(new File(['data'], 'core.jar')), error => {
      assert.match(error.message, new RegExp(`HTTP ${status}`));
      assert.match(error.message, /original upload failure/);
      return true;
    });
    assert.equal(requests, status === 503 ? 5 : 1);
    assert.equal(hook.isUploading.value, false);
  }
});

test('a sibling worker aborted by failure cannot replace the first error', async () => {
  const bothStarted = deferred();
  let requests = 0;
  const hook = setupUpload({
    uploadChunk: async (_id, index, _blob, _progress, signal) => {
      if (++requests === 2) bothStarted.resolve();
      await bothStarted.promise;
      if (index === 0) throw Object.assign(new Error('too large'), { status: 413 });
      await new Promise((_resolve, reject) => {
        signal.addEventListener('abort', () => reject(new Error('canceled')), { once: true });
      });
    },
  });
  await assert.rejects(hook.startUpload(new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'core.jar')),
    /HTTP 413.*too large/);
});

test('user cancellation remains distinguishable from failure cleanup', async () => {
  const started = deferred();
  const hook = setupUpload({
    uploadChunk: async (_id, _index, _blob, _progress, signal) => new Promise((_resolve, reject) => {
      signal.addEventListener('abort', () => reject(new Error('canceled')), { once: true });
      started.resolve();
    }),
  });
  const upload = hook.startUpload(new File(['data'], 'core.jar'));
  const rejection = assert.rejects(upload, /^Error: 已取消$/);
  await started.promise;
  await hook.cancelUpload();
  await rejection;
  assert.equal(hook.isUploading.value, false);
});

test('missing and invalid chunks reuse the repair queue with validated, deduplicated indices', async () => {
  for (const [status, field] of [[400, 'missingChunks'], [422, 'invalidChunks']]) {
    let chunks = 0;
    let finishes = 0;
    const hook = setupUpload({
      uploadChunk: async () => { chunks++; },
      finishUpload: async () => {
        if (++finishes === 1) throw Object.assign(new Error('repair needed'), {
          responseData: { code: status, data: { [field]: [0, 0, -1, 9, 'bad'] } },
        });
        return { uploadId: 'repaired' };
      },
    });
    assert.equal(await hook.startUpload(new File(['data'], 'core.jar')), 'repaired');
    assert.equal(chunks, 2);
    assert.equal(finishes, 2);
    assert.equal(hook.uploadProgress.value, 100);
  }
});

test('repair stops at the merge retry limit and preserves the last server error', async () => {
  let chunks = 0;
  let finishes = 0;
  const error = Object.assign(new Error('server still reports missing chunks'), {
    responseData: { code: 400, data: { missingChunks: [0] } },
  });
  const hook = setupUpload({
    uploadChunk: async () => { chunks++; },
    finishUpload: async () => { finishes++; throw error; },
  });
  await assert.rejects(hook.startUpload(new File(['data'], 'core.jar')), value => value === error);
  assert.equal(finishes, 3);
  assert.equal(chunks, 3);
});

test('ordinary bad requests are not retried', async () => {
  let finishes = 0;
  const error = Object.assign(new Error('invalid metadata'), { status: 400 });
  const hook = setupUpload({ finishUpload: async () => { finishes++; throw error; } });
  await assert.rejects(hook.startUpload(new File(['data'], 'core.jar')), value => value === error);
  assert.equal(finishes, 1);
});

test('empty files finish with zero chunks and no empty chunk requests', async () => {
  const hook = setupUpload({
    uploadChunk: async () => assert.fail('empty files must not upload chunks'),
    finishUpload: async (_id, chunks, _node, size) => {
      assert.equal(chunks, 0);
      assert.equal(size, 0);
      return { uploadId: 'empty-file' };
    },
  });
  assert.equal(await hook.startUpload(new File([], 'empty.txt')), 'empty-file');
});

function fileUploader(api = {}) {
  const source = scriptStatements('pages/instance/files/components/FileUploader.vue',
    ['uploadSingleFile', 'calcConcurrency', 'backoffDelay']);
  const bindings = {
    ...setupChunkedUpload(api),
    targetNodeId: 'local', props: {},
    initUpload: async () => ({ uploadId: 'test-upload' }),
    uploadChunk: async () => {}, finishUpload: async () => {},
    setTimeout: immediateTimer, ...api,
  };
  return new Function(...Object.keys(bindings), ts.transpileModule(`${source}\nreturn uploadSingleFile;`, {
    compilerOptions: { target: ts.ScriptTarget.ES2020 },
  }).outputText)(...Object.values(bindings));
}

test('file manager retains failure feedback and still recognizes user cancellation', async () => {
  const failed = { file: new File(['data'], 'file.txt') };
  await fileUploader({ uploadChunk: async () => { throw new Error('HTTP 413'); } })(failed);
  assert.equal(failed.status, 'error');
  assert.match(failed.errorMsg, /HTTP 413/);

  const canceled = { file: new File(['data'], 'file.txt') };
  await fileUploader({ uploadChunk: async () => { canceled.abortController.abort(); throw new Error('canceled'); } })(canceled);
  assert.equal(canceled.status, 'pending');
  assert.equal(canceled.speed, '已取消');
});

test('file manager accepts a folder containing empty and non-empty files', async () => {
  const saved = [];
  const upload = fileUploader({
    props: { instanceId: 1, currentPath: '/' },
    finishUpload: async (_id, chunks) => { saved.push(chunks); return { uploadId: 'test-upload' }; },
    saveUploadedFile: async () => {},
  });
  for (const file of [new File([], 'empty.txt'), new File(['data'], 'normal.txt')]) {
    const task = { file, path: `folder/${file.name}` };
    await upload(task);
    assert.equal(task.status, 'success');
  }
  assert.deepEqual(saved, [0, 1]);
});

test('creation drafts restore running and success steps to their confirmation page', () => {
  for (const [mode, lastFormStep] of [['Quick', 4], ['Bedrock', 3], ['Mcdr', 5]]) {
    const functionName = `restore${mode}Draft`;
    const source = scriptStatements(`pages/instance/createInstance/components/${mode}Mode.vue`, [functionName]);
    for (const storedStep of [0, lastFormStep, lastFormStep + 1, lastFormStep + 2]) {
      const currentStep = { value: 0 };
      const formData = { value: { retained: true } };
      const readCreateInstanceDraft = () => ({ state: { currentStep: storedStep, formData: { name: 'draft' } } });
      new Function('readCreateInstanceDraft', 'currentStep', 'formData', ts.transpileModule(
        `${source}\n${functionName}();`, { compilerOptions: { target: ts.ScriptTarget.ES2020 } },
      ).outputText)(readCreateInstanceDraft, currentStep, formData);
      assert.equal(currentStep.value, Math.min(storedStep, lastFormStep));
      assert.deepEqual(formData.value, { retained: true, name: 'draft' });
    }
  }
});

test('MCDR cancel button is visible during the core upload step', () => {
  const { parse, compileTemplate } = require('@vue/compiler-sfc');
  const template = parse(readSource('pages/instance/createInstance/components/McdrMode.vue')).descriptor.template.content;
  const button = template.match(/<t-button\s+v-if="[^"]+"[^>]*@click="cancelCurrentUpload"[^>]*>[\s\S]*?<\/t-button\s*>/)[0];
  const code = compileTemplate({ source: button, filename: 'cancel.vue', id: 'cancel-test' }).code;
  const render = evaluate(code, { vue: { ...require('vue'), resolveComponent: () => 'button' } }).render;
  const context = { currentStep: 2, isUploading: true, cancelCurrentUpload: () => {} };
  assert.notEqual(render(context, []).type, require('vue').Comment);
  assert.equal(render({ ...context, isUploading: false }, []).type, require('vue').Comment);
});

test('hash helpers and Worker agree on standard vectors and multiple blocks', async () => {
  const noble = await import('@noble/hashes/blake3.js');
  const hash = evaluate(readSource('utils/uploadHash.ts'), { '@noble/hashes/blake3.js': noble });
  const scope = { messages: [], postMessage(message) { this.messages.push(message); } };
  evaluate(readSource('utils/blake3Worker.ts'), { './uploadHash': hash }, { globalThis: scope });

  assert.equal(await hash.computeChunkCrc32cLocal(new ArrayBuffer(0)), '00000000');
  const bytes = new TextEncoder().encode('123456789');
  assert.equal(await hash.computeChunkCrc32cLocal(bytes.buffer), 'e3069283');
  await scope.onmessage({ data: { type: 'crc32c', buffer: bytes.buffer } });
  assert.equal(scope.messages.pop().crc32c, 'e3069283');

  for (const bytes of [new Uint8Array(), new TextEncoder().encode('abc'), new Uint8Array(4 * 1024 * 1024 + 17).fill(42)]) {
    const file = new File([bytes], 'hash.bin');
    const expected = hash.toHex(noble.blake3(bytes));
    if (!bytes.length) assert.equal(expected, 'af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262');
    assert.equal(await hash.computeFileBlake3Local(file), expected);
    await scope.onmessage({ data: { type: 'blake3', file } });
    assert.equal(scope.messages.pop().blake3, expected);
  }
});

for (const phase of ['init', 'status', 'session']) {
  test(`cancellation during ${phase} settles without starting chunks`, { timeout: 2000 }, async () => {
    const started = deferred();
    const release = deferred();
    const controller = new AbortController();
    const pause = async (value) => {
      started.resolve();
      await release.promise;
      return value;
    };
    const api = {
      uploadChunk: async () => assert.fail('canceled session must not send chunks'),
      ...(phase === 'init' ? { initUpload: () => pause({ uploadId: 'test-upload' }) } : {}),
      ...(phase === 'status' ? { getUploadStatus: () => pause({ receivedChunks: [] }) } : {}),
    };
    const { uploadFileInChunks } = setupChunkedUpload(api);
    const upload = uploadFileInChunks(new File(['data'], 'file.txt'), {
      nodeId: 'local', signal: controller.signal,
      ...(phase === 'status' ? { resumeRecord: { key: 'local:test', uploadId: 'test-upload' } } : {}),
      ...(phase === 'session' ? { onSession: () => pause() } : {}),
    });
    const rejection = assert.rejects(upload, /^Error: 已取消$/);
    await started.promise;
    controller.abort();
    release.resolve();
    await rejection;
  });
}

test('failure waits for sibling cleanup and aborts background hashing', { timeout: 2000 }, async () => {
  const bothStarted = deferred();
  const cleanup = deferred();
  const releaseCleanup = deferred();
  let hashAborted = false;
  let requests = 0;
  let settled = false;
  const { uploadFileInChunks } = setupChunkedUpload({
    uploadChunk: async (_id, index, _blob, _progress, signal) => {
      if (++requests === 2) bothStarted.resolve();
      await bothStarted.promise;
      if (index === 0) throw Object.assign(new Error('too large'), { status: 413 });
      await new Promise(resolve => signal.addEventListener('abort', resolve, { once: true }));
      cleanup.resolve();
      await releaseCleanup.promise;
      throw new Error('sibling canceled');
    },
  }, {
    computeFileBlake3WithOptions: async (_file, { signal }) => {
      await new Promise(resolve => signal.addEventListener('abort', resolve, { once: true }));
      hashAborted = true;
      throw new Error('hash canceled');
    },
  });
  const upload = uploadFileInChunks(new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'file.bin'), {
    nodeId: 'local', signal: new AbortController().signal,
  });
  const rejection = assert.rejects(upload, /HTTP 413.*too large/).then(() => { settled = true; });
  await cleanup.promise;
  assert.equal(settled, false);
  releaseCleanup.resolve();
  await rejection;
  assert.equal(hashAborted, true);
  assert.equal(requests, 2);
});
