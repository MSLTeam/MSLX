<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { SecuredIcon, RefreshIcon, VerifiedIcon } from 'tdesign-icons-vue-next';
import { getSslSettings, updateSslSettings } from '@/api/settings';
import type { SslSettingsResponse, UpdateSslSettingsRequest } from '@/api/model/settings';

const loading = ref(false);
const submitLoading = ref(false);

const originalData = reactive<SslSettingsResponse>({
  enableSsl: false,
  hasCertificate: false,
  certificateContent: null,
});

const formData = reactive({
  enableSsl: false,
  useSelfSigned: false,
  certificate: '',
  privateKey: '',
});

const emit = defineEmits(['refresh']);

const initData = async () => {
  loading.value = true;
  try {
    const res = await getSslSettings();
    Object.assign(originalData, res);

    formData.enableSsl = res.enableSsl;
    formData.useSelfSigned = !res.hasCertificate;
    formData.certificate = '';
    formData.privateKey = '';
  } catch (e: any) {
    MessagePlugin.error(e.message || 'SSL 设置加载失败');
  } finally {
    loading.value = false;
  }
};

const onSubmit = async () => {
  submitLoading.value = true;
  try {
    const payload: UpdateSslSettingsRequest = {
      enableSsl: formData.enableSsl,
      useSelfSignedCert: formData.useSelfSigned,
    };

    if (!formData.useSelfSigned) {
      if ((formData.certificate || formData.privateKey) && (!formData.certificate || !formData.privateKey)) {
        MessagePlugin.warning('请完整填写公钥和私钥内容');
        submitLoading.value = false;
        return;
      }

      if (formData.certificate && formData.privateKey) {
        payload.certificate = formData.certificate;
        payload.privateKey = formData.privateKey;
      }
    }

    await updateSslSettings(payload);

    await initData();
    MessagePlugin.success('SSL 配置已保存，部分网络协议修改需重启面板后生效');
  } catch (error: any) {
    MessagePlugin.error(error.message || '保存失败');
  } finally {
    submitLoading.value = false;
  }
};

const handleRefresh = () => {
  initData();
  emit('refresh');
};

defineExpose({ initData });

onMounted(() => {
  initData();
});
</script>

<template>
  <div
    class="design-card relative flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm transition-all duration-300"
  >
    <t-loading :loading="loading" show-overlay>
      <div class="p-5 sm:p-6 sm:px-8">
        <div
          class="flex items-center justify-between mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
        >
          <div class="flex items-center gap-3">
            <div
              class="w-1.5 h-5 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"
            ></div>
            <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">
              HTTPS 加密访问 (SSL)
            </h2>
          </div>
          <t-button variant="dashed" size="small" class="!bg-transparent" @click="handleRefresh">
            <template #icon><refresh-icon /></template>
            刷新数据
          </t-button>
        </div>

        <t-form :data="formData" :label-width="140" label-align="left" @submit="onSubmit">
          <div class="flex items-center gap-3 mt-2 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >核心配置</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <t-form-item label="开启 HTTPS">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                开启后，面板将强制使用 HTTPS 协议进行加密通信。
              </span>
            </template>
            <div class="flex items-center gap-3">
              <t-switch v-model="formData.enableSsl" />
              <span
                class="text-[11px] font-extrabold px-2 py-0.5 rounded-md transition-colors"
                :class="
                  formData.enableSsl
                    ? 'bg-[var(--color-primary)]/10 text-[var(--color-primary)] border border-[var(--color-primary)]/20'
                    : 'bg-zinc-100 dark:bg-zinc-800 text-zinc-500 border border-zinc-200 dark:border-zinc-700'
                "
              >
                {{ formData.enableSsl ? '安全连接已启用' : '已关闭' }}
              </span>
            </div>
          </t-form-item>

          <t-form-item :label="originalData.hasCertificate ? '重新生成自签证书' : '自动生成自签证书'">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                开启后系统将{{
                  originalData.hasCertificate ? '重新' : '自动'
                }}生成长期本地自签名证书（适合局域网/内网穿透）。关闭则允许您手动配置域名证书。
              </span>
              <span
                v-if="formData.useSelfSigned"
                class="text-[11px] font-medium text-amber-500/80 dark:text-amber-500/70 mt-1 block"
              >
                注意：使用自签名证书时，浏览器会提示“您的连接不是私密连接”，点击 高级->继续访问 即可。
              </span>
            </template>
            <t-switch v-model="formData.useSelfSigned" />
          </t-form-item>

          <template v-if="!formData.useSelfSigned">
            <div class="flex items-center gap-3 mt-8 mb-6">
              <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
                >自定义证书内容</span
              >
              <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
            </div>

            <template v-if="originalData.hasCertificate">
              <div
                class="mb-6 p-4 rounded-xl bg-[var(--color-primary)]/5 border border-[var(--color-primary)]/10 flex gap-3"
              >
                <verified-icon class="text-[var(--color-primary)] shrink-0 mt-0.5" />
                <div>
                  <div class="text-sm font-bold text-[var(--td-text-color-primary)]">已配置本地证书</div>
                  <div class="text-[11px] text-[var(--td-text-color-secondary)] mt-1 mb-2">
                    系统内已有生效的证书。若需更新证书请在下方填入；<b class="text-[var(--td-text-color-primary)]"
                      >若保持不变，请将下方输入框留空。</b
                    >
                  </div>
                  <div
                    v-if="originalData.certificateContent"
                    class="whitespace-pre-wrap break-all text-[10px] font-mono text-zinc-400 bg-zinc-100/50 dark:bg-zinc-900/50 p-2 rounded max-h-24 overflow-hidden relative"
                  >
                    <div
                      class="absolute inset-0 bg-gradient-to-b from-transparent to-zinc-100/90 dark:to-zinc-900/90 pointer-events-none"
                    ></div>
                    {{ originalData.certificateContent }}
                  </div>
                </div>
              </div>
            </template>

            <t-form-item label="公钥 (Certificate)">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block"
                  >Nginx 格式的公钥，通常以 <code>-----BEGIN CERTIFICATE-----</code> 开头。</span
                >
              </template>
              <t-textarea
                v-model="formData.certificate"
                :placeholder="
                  originalData.hasCertificate ? '留空以保持现有证书不变...' : '请粘贴 PEM 格式的公钥内容...'
                "
                :autosize="{ minRows: 4, maxRows: 8 }"
                class="!w-full sm:!w-[500px] !font-mono !text-xs !bg-zinc-50/50 dark:!bg-zinc-900/30"
              />
            </t-form-item>

            <t-form-item label="私钥 (Private Key)">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block"
                  >通常以 <code>-----BEGIN PRIVATE KEY-----</code> 结尾。私钥仅保存在本地服务器。</span
                >
              </template>
              <t-textarea
                v-model="formData.privateKey"
                :placeholder="
                  originalData.hasCertificate ? '留空以保持现有私钥不变...' : '请粘贴 PEM 格式的私钥内容...'
                "
                :autosize="{ minRows: 4, maxRows: 8 }"
                class="!w-full sm:!w-[500px] !font-mono !text-xs !bg-zinc-50/50 dark:!bg-zinc-900/30"
              />
            </t-form-item>
          </template>

          <div
            class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center justify-between"
          >
            <t-button
              theme="primary"
              type="submit"
              :loading="submitLoading"
              class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow"
            >
              保存 SSL 设置
            </t-button>

            <span class="text-[11px] text-zinc-400 dark:text-zinc-500 hidden sm:flex items-center gap-1">
              <secured-icon /> 开启 HTTPS 以提高面板的安全性
            </span>
          </div>
        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
