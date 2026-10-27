import { h, ref } from 'vue';
import { postCreateFrpTunnel } from '@/api/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { DialogPlugin, Input, Select, MessagePlugin } from 'tdesign-vue-next';

const tunnelsStore = useTunnelsStore();

export async function createFrpTunnel(
  name: string,
  config: string,
  provider: string,
  format: string = 'toml',
  showCustomName = true,
  clientPath?: string,
) {
  let finalName = name;
  let finalTags: string[] = [];

  if (showCustomName) {
    try {
      if (tunnelsStore.frpList.length === 0) {
        await tunnelsStore.getTunnels();
      }
      const existingTags = Array.from(new Set(tunnelsStore.frpList.flatMap((i) => i.tags || [])));
      const tagOptions = existingTags.map((t) => ({ label: t, value: t }));

      const result = await new Promise<{ name: string; tags: string[] }>((resolve, reject) => {
        const inputName = ref(name);
        const inputTags = ref<string[]>([]);

        const dialog = DialogPlugin({
          header: '自定义隧道设置',
          body: () =>
            h('div', { class: 'flex flex-col gap-4 py-2' }, [
              h('div', { class: 'flex flex-col gap-1.5' }, [
                h('label', { class: 'text-xs text-[var(--td-text-color-secondary)] font-medium' }, '隧道名称'),
                h(Input, {
                  value: inputName.value,
                  placeholder: '请输入隧道名称',
                  clearable: true,
                  onChange: (val: string) => {
                    inputName.value = val;
                  },
                }),
              ]),
              h('div', { class: 'flex flex-col gap-1.5' }, [
                h('label', { class: 'text-xs text-[var(--td-text-color-secondary)] font-medium' }, '隧道标签 (选填，支持回车创建新标签)'),
                h(Select, {
                  value: inputTags.value,
                  multiple: true,
                  filterable: true,
                  creatable: true,
                  placeholder: '选择已有标签或输入新标签并回车',
                  options: tagOptions,
                  onChange: (val: any) => {
                    inputTags.value = val;
                  },
                }),
              ]),
            ]),
          onConfirm: () => {
            if (!inputName.value.trim()) {
              MessagePlugin.warning('隧道名称不能为空');
              return;
            }
            dialog.hide();
            resolve({ name: inputName.value.trim(), tags: inputTags.value });
          },
          onClose: () => {
            dialog.hide();
            reject(new Error('cancel'));
          },
          onCancel: () => {
            dialog.hide();
            reject(new Error('cancel'));
          },
        });
      });
      finalName = result.name;
      finalTags = result.tags;
    } catch {
      return;
    }
  }
  await postCreateFrpTunnel(finalName, config, provider, format, clientPath, finalTags);
  MessagePlugin.success('添加成功');
  await tunnelsStore.getTunnels();
  changeUrl('/frp/list');
}

export function convertIniToToml(config: string) {
  const content = config.trim();
  if (!content) {
    MessagePlugin.warning('请先输入 INI 配置内容');
    return '';
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
    customDomains: 'customDomains',
    domains: 'customDomains',
    locations: 'locations',
    use_encryption: 'transport.useEncryption',
    use_compression: 'transport.useCompression',
    useEncryption: 'transport.useEncryption',
    useCompression: 'transport.useCompression',
    sub_domain: 'subdomain',
    host_header_rewrite: 'hostHeaderRewrite',
    role: 'role',
    sk: 'sk',
  };

  const arrayKeys = new Set(['customDomains', 'locations']);

  const formatTomlValue = (val: any) => {
    if (Array.isArray(val)) {
      return `[${val.map((item) => `"${item}"`).join(', ')}]`;
    }
    if (typeof val === 'string') {
      return `"${val}"`;
    }
    return val;
  };

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
      const rawVal = kvMatch[2].trim();

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
      let value: any;
      if (arrayKeys.has(key)) {
        const cleaned = rawVal.replace(/^\[|\]$/g, '').trim();
        value = cleaned
          ? cleaned
              .split(',')
              .map((s) => s.trim().replace(/^['"]|['"]$/g, ''))
              .filter(Boolean)
          : [];
      } else if (rawVal === 'true') {
        value = true;
      } else if (rawVal === 'false') {
        value = false;
      } else if (!isNaN(Number(rawVal)) && rawVal !== '') {
        value = Number(rawVal);
      } else {
        value = String(rawVal);
      }

      // 代理协议类型强制小写（例如 HTTP -> http）
      if (key === 'type' && typeof value === 'string') {
        value = value.toLowerCase();
      }

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
    tomlOutput += `${k} = ${formatTomlValue(v)}\n`;
  });

  // 写入 Proxies
  proxies.forEach((proxy) => {
    tomlOutput += `\n[[proxies]]\n`;
    Object.entries(proxy).forEach(([k, v]) => {
      tomlOutput += `${k} = ${formatTomlValue(v)}\n`;
    });
  });

  return tomlOutput.trim();
}
