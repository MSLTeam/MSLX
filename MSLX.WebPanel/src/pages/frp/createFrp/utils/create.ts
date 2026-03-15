import { h, ref } from 'vue';
import { postCreateFrpTunnel } from '@/api/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { DialogPlugin, Input, MessagePlugin } from 'tdesign-vue-next';

const tunnelsStore = useTunnelsStore();

export async function createFrpTunnel(
  name: string,
  config: string,
  provider: string,
  format: string = 'toml',
  showCustomName = true,
) {
  let finalName = name;

  if (showCustomName) {
    try {
      finalName = await new Promise((resolve, reject) => {
        const inputValue = ref(name);

        const dialog = DialogPlugin({
          header: '自定义隧道名称',
          body: () =>
            h(Input, {
              value: inputValue.value,
              placeholder: '请输入隧道名称',
              clearable: true,
              onChange: (val: string) => {
                inputValue.value = val;
              },
            }),
          onConfirm: () => {
            if (!inputValue.value.trim()) {
              MessagePlugin.warning('隧道名称不能为空');
              return;
            }
            dialog.hide();
            resolve(inputValue.value.trim());
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
    } catch {
      return;
    }
  }
  await postCreateFrpTunnel(finalName, config, provider, format);
  MessagePlugin.success('添加成功');
  await tunnelsStore.getTunnels();
  changeUrl('/frp/list');
}

export function convertIniToToml(config:string){
  const content = config.trim();
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

    return tomlOutput.trim();
}
