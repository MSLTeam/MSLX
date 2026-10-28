<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import QrcodeVue from 'qrcode.vue';
import { securityPost, type AccountSecurity, type Ceremony } from '@/api/accountSecurity';
import { createPasskey, passkeySupported, passkeyCancelled, signalPasskeys } from '@/utils/passkey';
import { useUserStore } from '@/store';

const props = defineProps<{ security?: AccountSecurity; userId: string }>();
const emit = defineEmits<{ refresh: [] }>();
const store = useUserStore();
const busy = ref(false);
const visible = ref(false);
const action = ref<'enable' | 'disable' | 'register' | 'delete'>('enable');
const credentialId = ref('');
const form = reactive({ password: '', twoFactorCode: '', name: '' });
const setup = ref<{ setupId: string; secret: string; uri: string }>();
const recoveryCodes = ref<string[]>([]);
let abort: AbortController | undefined;
const canPasskey = computed(() => passkeySupported(store.baseUrl));
const title = computed(
  () =>
    ({ enable: '启用 TOTP 二步验证', disable: '关闭 TOTP 二步验证', register: '新增通行密钥', delete: '删除通行密钥' })[
      action.value
    ],
);
const needsCode = computed(() => !!props.security?.totpEnabled || !!setup.value);
const rules = computed(() => ({
  password: [{ required: true, message: '请输入当前密码', type: 'error' as const }],
  twoFactorCode: [{ required: needsCode.value, message: '请输入新的动态码或恢复码', type: 'error' as const }],
  name: [{ required: action.value === 'register', message: '请输入通行密钥名称', type: 'error' as const }],
}));
function clear() {
  abort?.abort();
  setup.value = undefined;
  form.password = '';
  form.twoFactorCode = '';
  form.name = '';
}
function open(next: typeof action.value, id = '') {
  clear();
  action.value = next;
  credentialId.value = id;
  visible.value = true;
}
function close() {
  if (!busy.value) {
    visible.value = false;
    clear();
  }
}
watch(visible, (value) => {
  if (!value) clear();
});
onBeforeUnmount(() => {
  clear();
  recoveryCodes.value = [];
});
onDeactivated(() => {
  visible.value = false;
  clear();
  recoveryCodes.value = [];
});
watch(
  () => props.security?.passkeys,
  (keys) => {
    if (keys && props.userId) void signalPasskeys(props.userId, keys);
  },
  { immediate: true },
);

async function submit({ validateResult }: any) {
  if (validateResult !== true || busy.value) return;
  busy.value = true;
  const proof = { password: form.password, twoFactorCode: form.twoFactorCode || undefined };
  try {
    let result: { token: string; recoveryCodes?: string[] };
    if (action.value === 'enable') {
      if (!setup.value) {
        setup.value = await securityPost('totp/setup', proof);
        return;
      }
      result = await securityPost('totp/enable', { ...proof, setupId: setup.value.setupId });
      recoveryCodes.value = result.recoveryCodes || [];
    } else if (action.value === 'disable') result = await securityPost('totp/disable', proof);
    else if (action.value === 'register') {
      abort = new AbortController();
      const { challengeId, options } = await securityPost<Ceremony>('passkeys/options', { ...proof, name: form.name });
      const credential = await createPasskey(options, abort.signal);
      result = await securityPost('passkeys/verify', { challengeId, credential });
    } else result = await securityPost(`passkeys/delete/${encodeURIComponent(credentialId.value)}`, proof);
    store.setSessionToken(result.token);
    visible.value = false;
    clear();
    emit('refresh');
    MessagePlugin.success('安全设置已更新，其他旧会话已失效');
  } catch (error: any) {
    if (!passkeyCancelled(error)) MessagePlugin.error(error.message || '安全设置更新失败');
    if (action.value === 'register') form.twoFactorCode = '';
  } finally {
    busy.value = false;
  }
}
async function copyRecoveryCodes() {
  try {
    await navigator.clipboard.writeText(recoveryCodes.value.join('\n'));
    MessagePlugin.success('恢复码已复制，请离线保存');
  } catch {
    MessagePlugin.warning('无法自动复制，请手动选择保存恢复码');
  }
}
</script>
<template>
  <section
    v-if="userId && userId !== 'system-admin'"
    class="security-settings design-card relative flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm"
  >
    <div class="p-5 sm:p-6 sm:px-8">
      <div
        class="security-header flex items-center gap-3 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
      >
        <div
          class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"
        ></div>
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">
            登录安全
          </h2>
          <span class="text-[11px] sm:text-xs text-[var(--td-text-color-secondary)] mt-1.5"
            >管理二步验证与通行密钥，保护账户登录。</span
          >
        </div>
      </div>
      <div class="security-row">
        <div>
          <strong>二步验证</strong>
          <p>使用验证器生成动态码，在密码验证后确认身份。</p>
          <span v-if="security?.totpEnabled" class="enabled"
            >已启用 · 剩余 {{ security.recoveryCodesRemaining }} 个恢复码</span
          ><span v-else>尚未启用</span>
        </div>
        <t-button
          theme="default"
          variant="outline"
          class="security-action !h-10 !px-5 !rounded-xl !font-medium"
          @click="open(security?.totpEnabled ? 'disable' : 'enable')"
          >{{ security?.totpEnabled ? '关闭验证' : '启用验证' }}</t-button
        >
      </div>
      <div class="security-row">
        <div>
          <strong>通行密钥</strong>
          <p>使用指纹、设备 PIN 或安全密钥快捷登录。</p>
          <span v-if="!security?.passkeys?.length && canPasskey">尚未绑定</span>
          <span v-if="!canPasskey">新增通行密钥需要同源 HTTPS 域名或 localhost 及支持 WebAuthn 的浏览器。</span>
        </div>
        <t-button
          v-if="canPasskey"
          theme="default"
          variant="outline"
          class="security-action !h-10 !px-5 !rounded-xl !font-medium"
          @click="open('register')"
          ><template #icon><t-icon name="add" /></template>新增通行密钥</t-button
        >
      </div>
      <div v-for="key in security?.passkeys || []" :key="key.id" class="security-row passkey-row">
        <div>
          <strong>{{ key.name }}</strong>
          <p>{{ key.rpId }} · 创建于 {{ new Date(key.createdAt).toLocaleString() }}</p>
          <span>上次使用：{{ key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleString() : '尚未使用' }}</span>
        </div>
        <t-button
          theme="danger"
          variant="outline"
          class="security-action !h-10 !px-5 !rounded-xl !font-medium"
          @click="open('delete', key.id)"
          ><template #icon><t-icon name="delete" /></template>删除</t-button
        >
      </div>
    </div>
    <t-dialog
      v-model:visible="visible"
      :header="title"
      :footer="false"
      :close-on-overlay-click="false"
      :close-btn="!busy"
      :close-on-esc-keydown="!busy"
      width="min(480px, 94vw)"
      attach="body"
    >
      <t-form :data="form" :rules="rules" label-align="top" @submit="submit">
        <p v-if="action === 'delete'">删除后，此通行密钥将无法再登录当前账户。</p>
        <t-form-item label="当前密码" name="password"
          ><t-input
            v-model="form.password"
            type="password"
            autocomplete="current-password"
            :disabled="busy || !!setup"
            :maxlength="1024"
        /></t-form-item>
        <t-form-item v-if="action === 'register'" label="通行密钥名称" name="name"
          ><t-input v-model="form.name" placeholder="例如：我的 MacBook" :maxlength="64"
        /></t-form-item>
        <div v-if="setup" class="totp-setup">
          <div class="qr-code"><qrcode-vue :value="setup.uri" :size="176" level="M" /></div>
          <p>使用验证器扫描二维码，也可手动输入密钥：</p>
          <code>{{ setup.secret }}</code>
          <p>动态码每 30 秒更新；已使用的动态码不能再次使用。</p>
        </div>
        <t-form-item v-if="needsCode" :label="setup ? '验证器动态码' : '动态码或恢复码'" name="twoFactorCode"
          ><t-input v-model="form.twoFactorCode" autocomplete="one-time-code" :maxlength="64"
        /></t-form-item>
        <div class="dialog-actions">
          <t-button variant="outline" :disabled="busy" @click="close">取消</t-button
          ><t-button type="submit" :loading="busy">{{
            action === 'enable' && !setup ? '生成绑定二维码' : '确认'
          }}</t-button>
        </div>
      </t-form>
    </t-dialog>
    <t-dialog
      :visible="recoveryCodes.length > 0"
      header="保存一次性恢复码"
      :footer="false"
      :close-on-overlay-click="false"
      :close-on-esc-keydown="false"
      :close-btn="false"
      width="min(540px, 94vw)"
      attach="body"
    >
      <p>恢复码仅显示这一次。验证器丢失时，可在密码验证后使用；每个恢复码只能使用一次。</p>
      <pre class="recovery-codes">{{ recoveryCodes.join('\n') }}</pre>
      <div class="dialog-actions">
        <t-button variant="outline" @click="copyRecoveryCodes">复制恢复码</t-button
        ><t-button @click="recoveryCodes = []">我已妥善保存</t-button>
      </div>
    </t-dialog>
  </section>
</template>
<style scoped>
.security-settings {
  color: var(--td-text-color-primary);
}
.security-heading {
  display: flex;
  gap: 12px;
  align-items: baseline;
  flex-wrap: wrap;
}
.security-heading h3 {
  margin: 0;
  font-size: 16px;
}
.security-heading span,
.security-row p,
.security-row span,
.empty-hint {
  color: var(--td-text-color-secondary);
  font-size: 12px;
  line-height: 1.6;
}
.security-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  padding: 20px 0;
}
.security-row + .security-row {
  border-top: 1px dashed var(--td-component-border);
}
.security-header + .security-row {
  padding-top: 0;
}
.security-row:last-child {
  padding-bottom: 0;
}
.security-row strong {
  font-size: 14px;
  font-weight: 600;
}
.security-row > .t-button {
  flex-shrink: 0;
}
.security-row span.enabled {
  color: var(--td-success-color);
}
.security-row > div {
  min-width: 0;
  overflow-wrap: anywhere;
}
.security-row p {
  margin: 6px 0;
}
.passkey-row {
  margin-top: 10px;
  padding: 12px;
  background: var(--td-bg-color-secondarycontainer);
  border: 1px solid var(--td-component-border);
  border-radius: 12px;
}
@media (max-width: 600px) {
  .security-row {
    flex-wrap: wrap;
    gap: 12px;
  }
  .security-row > div {
    flex-basis: 100%;
  }
  .security-action {
    width: 100%;
  }
}
.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
  flex-wrap: wrap;
}
.totp-setup {
  text-align: center;
  margin: 16px 0;
  overflow-wrap: anywhere;
}
.qr-code {
  display: inline-flex;
  padding: 12px;
  background: white;
  border-radius: 8px;
}
.recovery-codes {
  padding: 16px;
  font-size: 12px;
  line-height: 2;
  overflow: auto;
  background: var(--td-bg-color-secondarycontainer);
  border-radius: 8px;
}
</style>
