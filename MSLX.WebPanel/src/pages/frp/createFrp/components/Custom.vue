<script setup lang="ts">
import { reactive, ref } from 'vue';
import { MessagePlugin, type FormProps, FormRules } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';

// 表单数据接口
interface FormData {
  name: string;
  type: 'toml' | 'ini';
  content: string;
}

const formRef = ref(null);

const formData = reactive<FormData>({
  name: '',
  type: 'toml',
  content: '',
});

const rules: FormRules<FormData> = {
  name: [{ required: true, message: '请输入隧道名称', type: 'error' }],
  content: [{ required: true, message: '配置文件内容不能为空', type: 'error' }],
};

const onSubmit: FormProps['onSubmit'] = async ({ validateResult }) => {
  if (validateResult === true) {
    await createFrpTunnel(formData.name, formData.content, 'Custom', formData.type);
  } else {
    MessagePlugin.warning('请检查表单填写');
  }
};

const onReset = () => {
  MessagePlugin.info('表单已重置');
};
const convertIniToToml = () => {
  const content = formData.content.trim();
  if (!content) {
    MessagePlugin.warning('请先输入 INI 配置内容');
    return;
  }

  // 特殊字段映射表：定义 INI 字段到 TOML 字段的路径映射
  const keyMapping: Record<string, string> = {
    tls_enable: 'transport.tls.enable',
    token: 'auth.token',
    protocol: 'transport.protocol',
    pool_count: 'transport.poolCount',
    tcp_mux: 'transport.tcpMux',
    login_fail_exit: 'loginFailExit',
    custom_domains: 'customDomains',
    locations: 'locations',
    host_header_rewrite: 'hostHeaderRewrite',
    role: 'role',
    sk: 'sk',
  };

  try {
    const lines = content.split(/\r?\n/);
    let currentSection = '';
    const rootParams: Record<string, any> = {};
    const proxies: Record<string, any>[] = [];
    let currentProxy: Record<string, any> | null = null;

    lines.forEach((line) => {
      const trimmed = line.trim();
      // 跳过空行和注释
      if (!trimmed || trimmed.startsWith('#') || trimmed.startsWith(';')) return;

      // 匹配 Section: [xxx]
      const sectionMatch = trimmed.match(/^\[(.+)\]$/);
      if (sectionMatch) {
        currentSection = sectionMatch[1];
        if (currentSection === 'common') {
          currentProxy = null;
        } else {
          // 创建新的代理节点
          currentProxy = { name: currentSection };
          proxies.push(currentProxy);
        }
        return;
      }

      // 匹配 Key-Value: key = value
      const kvMatch = trimmed.match(/^([^=]+)=(.*)$/);
      if (kvMatch) {
        const rawKey = kvMatch[1].trim();
        let value: string | number | boolean = kvMatch[2].trim();

        // --- Key 转换逻辑 ---
        let key = rawKey;

        // 优先使用映射表
        if (keyMapping[rawKey]) {
          key = keyMapping[rawKey];
        } else {
          // 默认处理：snake_case -> camelCase
          key = key.replace(/_([a-z])/g, (_, letter) => letter.toUpperCase());
          // 修正常见缩写
          key = key.replace(/Ip/g, 'IP');
        }

        // --- Value 类型推断 ---
        if (value === 'true') value = true;
        else if (value === 'false') value = false;
        else if (!isNaN(Number(value)) && value !== '') value = Number(value);
        else value = String(value);

        // --- 赋值 ---
        if (currentSection === 'common' || !currentSection) {
          rootParams[key] = value;
        } else if (currentProxy) {
          currentProxy[key] = value;
        }
      }
    });

    // 构建 TOML 字符串
    let tomlOutput = '';

    // 写入根节点 (Common)
    Object.entries(rootParams).forEach(([k, v]) => {
      const valStr = typeof v === 'string' ? `"${v}"` : v;
      tomlOutput += `${k} = ${valStr}\n`;
    });

    // 写入 Proxies
    proxies.forEach((proxy) => {
      tomlOutput += `\n[[proxies]]\n`;
      Object.entries(proxy).forEach(([k, v]) => {
        const valStr = typeof v === 'string' ? `"${v}"` : v;
        tomlOutput += `${k} = ${valStr}\n`;
      });
    });

    // 更新表单
    formData.content = tomlOutput.trim();
    formData.type = 'toml';
    MessagePlugin.success('已转换为 TOML');
  } catch (e) {
    console.error(e);
    MessagePlugin.error('转换失败，请检查输入格式');
  }
};
</script>

<template>
  <div class="custom-frp-container">
    <t-card :bordered="false">
      <template #header>
        <span style="font-weight: bold; font-size: 16px">自定义 Frp 隧道</span>
      </template>

      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" @reset="onReset" @submit="onSubmit">
        <t-form-item label="隧道名称" name="name">
          <t-input v-model="formData.name" placeholder="请输入隧道名称" />
        </t-form-item>

        <t-form-item label="配置类型" name="type">
          <t-radio-group v-model="formData.type" variant="default-filled">
            <t-radio-button value="toml">TOML</t-radio-button>
            <t-radio-button value="ini">INI</t-radio-button>
          </t-radio-group>
          <t-button
            v-if="formData.type === 'ini'"
            style="margin-left: 8px"
            variant="outline"
            theme="primary"
            size="small"
            @click="convertIniToToml"
          >
            一键转 TOML
          </t-button>
        </t-form-item>

        <t-form-item label="隧道配置内容" name="content">
          <t-textarea
            v-model="formData.content"
            placeholder='serverAddr = "0.0.0.0"&#10;serverPort = 1027&#10;&#10;[[proxies]]&#10;name = "nahida_tcp"&#10;...'
            :autosize="{ minRows: 10, maxRows: 25 }"
            class="code-font-textarea"
          />
        </t-form-item>

        <t-form-item>
          <t-space>
            <t-button theme="primary" type="submit">保存配置</t-button>
            <t-button theme="default" variant="base" type="reset">重置</t-button>
          </t-space>
        </t-form-item>
      </t-form>
    </t-card>
  </div>
</template>

<style scoped lang="less">
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre;
}
</style>
