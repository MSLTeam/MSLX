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
    '@/utils/uploadResume': { saveUploadResumeRecord: async () => {}, deleteUploadResumeRecord: async () => {}, ...resume },
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
    let sessionPublished = false;
    const upload = uploadFileInChunks(new File(['data'], 'file.txt'), {
      nodeId: 'local', signal: controller.signal,
      ...(phase === 'status' ? { resumeRecord: { key: 'local:test', uploadId: 'test-upload' } } : {}),
      onSession: () => {
        sessionPublished = true;
        if (phase === 'session') return pause();
      },
    });
    const rejection = assert.rejects(upload, /^Error: 已取消$/);
    await started.promise;
    controller.abort();
    release.resolve();
    await rejection;
    assert.equal(sessionPublished, phase === 'session');
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

for (const [name, destinations] of [
  ['relative path', [{ path: 'a/server.properties' }, { path: 'b/server.properties' }]],
  ['instance', [{ instanceId: 1 }, { instanceId: 2 }]],
  ['current directory', [{ currentPath: '/a' }, { currentPath: '/b' }]],
]) {
  test(`file uploads to different ${name}s use separate consumable sessions`, { timeout: 2000 }, async () => {
    const sessions = new Map();
    const consumed = new Set();
    const bothStarted = deferred();
    let chunks = 0;
    const api = {
      initUpload: async (_node, metadata) => {
        const key = JSON.stringify(metadata);
        if (!sessions.has(key)) sessions.set(key, `upload-${sessions.size}`);
        return { uploadId: sessions.get(key) };
      },
      uploadChunk: async () => {
        if (++chunks === 2) bothStarted.resolve();
        await bothStarted.promise;
      },
      finishUpload: async id => ({ uploadId: id }),
      saveUploadedFile: async (_instance, id) => {
        assert.equal(consumed.has(id), false, 'saving consumes the session');
        consumed.add(id);
      },
    };
    const tasks = await Promise.all(destinations.map(async destination => {
      const task = {
        path: destination.path || 'server.properties',
        file: new File(['same contents'], 'server.properties', { lastModified: 12345 }),
      };
      await fileUploader({ ...api, props: { instanceId: 1, currentPath: '/', ...destination } })(task);
      return task;
    }));
    assert.deepEqual(tasks.map(task => task.status), ['success', 'success']);
    assert.equal(sessions.size, 2);
    assert.equal(consumed.size, 2);
  });
}

test('a scoped upload resumes the same destination after failure', async () => {
  const records = new Map();
  let initCount = 0;
  let fail = true;
  const { uploadFileInChunks } = setupChunkedUpload({
    initUpload: async () => { initCount++; return { uploadId: 'resumable' }; },
    uploadChunk: async () => { if (fail) throw Object.assign(new Error('failed'), { status: 403 }); },
  }, {
    getUploadResumeRecords: async () => [...records.values()].map(record => structuredClone(record)),
    saveUploadResumeRecord: async record => records.set(record.key, structuredClone(record)),
    deleteUploadResumeRecord: async key => records.delete(key),
  });
  const file = new File(['data'], 'file.txt');
  const options = { nodeId: 'local', signal: new AbortController().signal, kind: 'file', resumeScope: 'destination' };
  await assert.rejects(uploadFileInChunks(file, options), /HTTP 403/);
  fail = false;
  assert.equal((await uploadFileInChunks(file, options)).uploadId, 'resumable');
  assert.equal(initCount, 1);
  assert.equal(records.size, 0);
});

test('a canceled initialization cannot replace the active upload session', { timeout: 2000 }, async () => {
  const firstInit = deferred();
  const firstStarted = deferred();
  const secondStarted = deferred();
  const deleted = [];
  let initCount = 0;
  const hook = setupUpload({
    initUpload: async () => {
      if (++initCount === 1) { firstStarted.resolve(); return firstInit.promise; }
      return { uploadId: 'new-session' };
    },
    uploadChunk: async (_id, _index, _chunk, _progress, signal) => {
      secondStarted.resolve();
      await new Promise((_resolve, reject) => signal.addEventListener('abort', () => reject(new Error('canceled')), { once: true }));
    },
    deleteUpload: async id => deleted.push(id),
  });
  const first = assert.rejects(hook.startUpload(new File(['first'], 'first.jar')), /^Error: 已取消$/);
  await firstStarted.promise;
  const second = assert.rejects(hook.startUpload(new File(['second'], 'second.jar')), /^Error: 已取消$/);
  try {
    await secondStarted.promise;
    firstInit.resolve({ uploadId: 'old-session' });
    await first;
    assert.equal(hook.isUploading.value, true);
    await hook.removeUploadData();
    assert.deepEqual(deleted, ['new-session']);
  } finally {
    await hook.cancelUpload();
    await second;
  }
});


async function setupCrcWorkers(failAt) {
  const noble = await import('@noble/hashes/blake3.js');
  const hash = evaluate(readSource('utils/uploadHash.ts'), { '@noble/hashes/blake3.js': noble });
  const workers = [];
  let fallbackCount = 0;
  class MockWorker {
    constructor() {
      if (failAt === 'create') throw new Error('create failed');
      this.messages = [];
      this.terminated = false;
      workers.push(this);
    }
    postMessage(message) {
      if (failAt === 'post') throw new Error('post failed');
      this.messages.push(message);
    }
    terminate() { this.terminated = true; }
    reply(messageIndex = this.messages.length - 1, crc32c = 'e3069283') {
      this.onmessage?.({ data: { taskId: this.messages[messageIndex].taskId, crc32c } });
    }
  }
  // Only replace import.meta in the test evaluator; production keeps Vite's Worker URL detection.
  const resume = evaluate(readSource('utils/uploadResume.ts').replaceAll('import.meta.url', '"file:///worker-test.ts"'), {
    '@noble/hashes/blake3.js': noble,
    './uploadHash': {
      ...hash,
      computeChunkCrc32cLocal: (...args) => { fallbackCount++; return hash.computeChunkCrc32cLocal(...args); },
    },
  }, { Worker: MockWorker });
  return { ...resume, workers, fallbackCount: () => fallbackCount };
}

const flushCrcQueue = () => new Promise(resolve => queueMicrotask(resolve));
const crcInput = () => new Blob(['123456789']);

function trackedAbortController() {
  const controller = new AbortController();
  const listeners = new Set();
  const add = controller.signal.addEventListener.bind(controller.signal);
  const remove = controller.signal.removeEventListener.bind(controller.signal);
  controller.signal.addEventListener = (type, listener, options) => {
    if (type === 'abort') listeners.add(listener);
    add(type, listener, options);
  };
  controller.signal.removeEventListener = (type, listener, options) => {
    if (type === 'abort') listeners.delete(listener);
    remove(type, listener, options);
  };
  return { controller, listeners };
}

test('CRC workers reuse two slots, dispatch FIFO, and ignore stale task results', async () => {
  const pool = await setupCrcWorkers();
  const blobs = Array.from({ length: 5 }, crcInput);
  for (const blob of blobs) blob.arrayBuffer = () => assert.fail('main thread must not read the chunk');
  const controllers = blobs.map(trackedAbortController);
  const tasks = blobs.map((blob, index) => pool.computeChunkCrc32c(blob, { signal: controllers[index].controller.signal }));
  assert.equal(pool.workers.length, 2);
  const [first, second] = pool.workers;
  assert.equal(first.messages[0].chunk, blobs[0]);
  assert.equal(second.messages[0].chunk, blobs[1]);
  second.reply(); // Completion order must not dictate queue order.
  await flushCrcQueue();
  assert.equal(second.messages[1].chunk, blobs[2]);
  let thirdSettled = false;
  tasks[2].then(() => { thirdSettled = true; });
  second.reply(0, 'stale');
  await flushCrcQueue();
  assert.equal(thirdSettled, false);
  first.reply();
  await flushCrcQueue();
  assert.equal(first.messages[1].chunk, blobs[3]);
  second.reply();
  await flushCrcQueue();
  assert.equal(second.messages[2].chunk, blobs[4]);
  first.reply();
  second.reply();
  assert.deepEqual(await Promise.all(tasks), Array(5).fill('e3069283'));
  assert.equal(pool.workers.length, 2);
  assert.equal(pool.fallbackCount(), 0);
  for (const { listeners } of controllers) assert.equal(listeners.size, 0);
});

test('canceling queued CRC work removes it without terminating other workers or reading its Blob', async () => {
  const pool = await setupCrcWorkers();
  const first = pool.computeChunkCrc32c(crcInput());
  const second = pool.computeChunkCrc32c(crcInput());
  const { controller, listeners } = trackedAbortController();
  const canceled = pool.computeChunkCrc32c(crcInput(), { signal: controller.signal });
  const rejection = assert.rejects(canceled, /^Error: 已取消$/);
  const lastBlob = crcInput();
  const last = pool.computeChunkCrc32c(lastBlob);
  controller.abort();
  await rejection;
  assert.equal(listeners.size, 0);
  assert.equal(pool.workers.some(worker => worker.terminated), false);
  pool.workers[0].reply();
  await flushCrcQueue();
  assert.equal(pool.workers[0].messages[1].chunk, lastBlob);
  pool.workers[1].reply();
  pool.workers[0].reply();
  await Promise.all([first, second, last]);
  assert.equal(pool.fallbackCount(), 0);
});

test('canceling active CRC work replaces only its worker and ignores messages from the old instance', async () => {
  const pool = await setupCrcWorkers();
  const { controller, listeners } = trackedAbortController();
  const canceled = pool.computeChunkCrc32c(crcInput(), { signal: controller.signal });
  const rejection = assert.rejects(canceled, /^Error: 已取消$/);
  const second = pool.computeChunkCrc32c(crcInput());
  const next = pool.computeChunkCrc32c(crcInput());
  const [old, unaffected] = pool.workers;
  const lateMessage = old.onmessage;
  const lateError = old.onerror;
  controller.abort();
  await rejection;
  await flushCrcQueue();
  const replacement = pool.workers[2];
  assert.equal(old.terminated, true);
  assert.equal(unaffected.terminated, false);
  assert.equal(pool.workers.filter(worker => !worker.terminated).length, 2);
  let nextSettled = false;
  next.then(() => { nextSettled = true; });
  lateMessage({ data: { taskId: replacement.messages[0].taskId, crc32c: 'stale' } });
  lateError({ message: 'late error' });
  await flushCrcQueue();
  assert.equal(nextSettled, false);
  replacement.reply();
  unaffected.reply();
  await Promise.all([next, second]);
  assert.equal(pool.fallbackCount(), 0);
  assert.equal(listeners.size, 0);
});

for (const failure of ['create', 'post', 'error', 'messageerror', 'response']) {
  test(`CRC ${failure} failure settles, falls back, and keeps the queue moving`, async () => {
    const pool = await setupCrcWorkers(failure);
    const controllers = Array.from({ length: 4 }, trackedAbortController);
    const tasks = controllers.map(({ controller }) => pool.computeChunkCrc32c(crcInput(), { signal: controller.signal }));
    if (!['create', 'post'].includes(failure)) {
      let processed = 0;
      while (processed < tasks.length) {
        const active = pool.workers.filter(worker => !worker.terminated && worker.messages.length);
        assert.ok(active.length > 0, 'queued work must obtain a worker slot');
        for (const worker of active) {
          if (failure === 'response') {
            const message = worker.messages.shift();
            worker.onmessage({ data: { taskId: message.taskId, error: 'hash failed' } });
          } else {
            worker[`on${failure}`]({ message: 'failed' });
          }
          processed++;
        }
        await flushCrcQueue();
      }
    }
    assert.deepEqual(await Promise.all(tasks), Array(4).fill('e3069283'));
    assert.equal(pool.fallbackCount(), 4);
    assert.ok(pool.workers.filter(worker => !worker.terminated).length <= 2);
    for (const { listeners } of controllers) assert.equal(listeners.size, 0);
  });
}

test('incremental CRC reads at most 1 MiB at a time and agrees across inputs and Worker messages', async () => {
  const noble = await import('@noble/hashes/blake3.js');
  const hash = evaluate(readSource('utils/uploadHash.ts'), { '@noble/hashes/blake3.js': noble });
  const scope = { messages: [], postMessage(message) { this.messages.push(message); } };
  evaluate(readSource('utils/blake3Worker.ts'), { './uploadHash': hash }, { globalThis: scope });
  for (const size of [0, 1024 * 1024 - 1, 1024 * 1024, 1024 * 1024 + 17]) {
    const bytes = Uint8Array.from({ length: size }, (_, index) => index % 251);
    let crc = 0xffffffff;
    for (const byte of bytes) {
      crc ^= byte;
      for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0x82f63b78 : 0);
    }
    const expected = ((crc ^ 0xffffffff) >>> 0).toString(16).padStart(8, '0');
    const blob = new Blob([bytes]);
    const slice = blob.slice.bind(blob);
    blob.arrayBuffer = () => assert.fail('must not read the entire Blob');
    blob.slice = (start, end) => { assert.ok(end - start <= 1024 * 1024); return slice(start, end); };
    assert.equal(await hash.computeChunkCrc32cLocal(bytes.buffer), expected);
    assert.equal(await hash.computeChunkCrc32cLocal(blob), expected);
    await scope.onmessage({ data: { type: 'crc32c', taskId: size, chunk: blob } });
    assert.deepEqual(scope.messages.pop(), { taskId: size, crc32c: expected });
  }
  await scope.onmessage({ data: { type: 'crc32c', taskId: 42 } });
  assert.equal(scope.messages.pop().taskId, 42);
});

test('CRC fallback honors cancellation before reading, during reading and between blocks', async () => {
  const pool = await setupCrcWorkers();
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(pool.computeChunkCrc32c(crcInput(), { signal: controller.signal }), /^Error: 已取消$/);
  assert.equal(pool.workers.length, 0);
  for (const phase of ['read', 'yield']) {
    const controller = new AbortController();
    const blob = new Blob([new Uint8Array(1024 * 1024 + 1)]);
    const slice = blob.slice.bind(blob);
    let reads = 0;
    blob.slice = (...args) => {
      reads++;
      const block = slice(...args);
      const arrayBuffer = block.arrayBuffer.bind(block);
      block.arrayBuffer = async () => {
        if (phase === 'read') controller.abort();
        else setTimeout(() => controller.abort(), 0);
        return arrayBuffer();
      };
      return block;
    };
    await assert.rejects(pool.computeChunkCrc32c(blob, { useWorker: false, signal: controller.signal }), /^Error: 已取消$/);
    assert.equal(reads, 1);
  }
});

test('BLAKE3 Worker skips timer yields while main-thread fallback retains them', async () => {
  const noble = await import('@noble/hashes/blake3.js');
  let yields = 0;
  const hash = evaluate(readSource('utils/uploadHash.ts'), { '@noble/hashes/blake3.js': noble }, {
    setTimeout: callback => { yields++; queueMicrotask(callback); },
  });
  const file = new File([new Uint8Array(4 * 1024 * 1024 + 17).fill(42)], 'hash.bin');
  const expected = await hash.computeFileBlake3Local(file);
  assert.equal(yields, 2);
  yields = 0;
  const scope = { messages: [], postMessage(message) { this.messages.push(message); } };
  evaluate(readSource('utils/blake3Worker.ts'), { './uploadHash': hash }, { globalThis: scope });
  await scope.onmessage({ data: { type: 'blake3', file } });
  assert.equal(scope.messages.pop().blake3, expected);
  assert.equal(yields, 0);
});
