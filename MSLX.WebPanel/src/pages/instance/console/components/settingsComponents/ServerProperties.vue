<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  SaveIcon,
  RefreshIcon
} from 'tdesign-icons-vue-next';

import { getFileContent, saveFileContent } from '@/api/files';
import { SERVER_PROPERTIES_SCHEMA, type PropertySchema } from './metadatas/serverPropertiesMeta';

const route = useRoute();

// --- 状态定义 ---
const instanceId = ref(0);
const loading = ref(false);
const saving = ref(false);
const propertiesMap = ref<Record<string, string>>({});
const rawFileContent = ref('');

// --- 文件解析 ---
const parseProperties = (content: string) => {
  const map: Record<string, string> = {};
  const lines = content.split('\n');

  lines.forEach(line => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) return; // 跳过注释和空行

    const splitIndex = trimmed.indexOf('=');
    if (splitIndex !== -1) {
      const key = trimmed.substring(0, splitIndex).trim();
      const value = trimmed.substring(splitIndex + 1).trim();
      map[key] = value;
    }
  });

  return map;
};

// 将 Map > server.properties 字符串
const stringifyProperties = (map: Record<string, string>) => {
  let content = `#Minecraft server properties\n#${new Date().toString()}\n`;

  // 按metadata排序
  const definedKeys = SERVER_PROPERTIES_SCHEMA.map(s => s.key);
  const mapKeys = Object.keys(map);

  definedKeys.forEach(key => {
    if (Object.prototype.hasOwnProperty.call(map, key)) {
      content += `${key}=${map[key]}\n`;
    }
  });

  // 未知的
  mapKeys.forEach(key => {
    if (!definedKeys.includes(key)) {
      content += `${key}=${map[key]}\n`;
    }
  });

  return content;
};

// --- 数据获取与保存 ---

const loadData = async () => {
  if (!instanceId.value) return;
  loading.value = true;
  try {
    const res = await getFileContent(instanceId.value, 'server.properties');
    if (res) {
      rawFileContent.value = res;
      propertiesMap.value = parseProperties(res);
      //MessagePlugin.success('配置加载成功');
    }
  } catch (e: any) {
    MessagePlugin.error(`读取配置文件失败: ${e.message}`);
  } finally {
    loading.value = false;
  }
};

const handleSave = async () => {
  saving.value = true;
  try {
    const content = stringifyProperties(propertiesMap.value);
    await saveFileContent(instanceId.value, 'server.properties', content);
    MessagePlugin.success('配置文件已保存');
    // 重新加载
    loadData();
  } catch (e: any) {
    MessagePlugin.error(`保存失败: ${e.message}`);
  } finally {
    saving.value = false;
  }
};

// --- 计算属性：构建渲染列表 ---

interface RenderItem extends PropertySchema {
  isUnknown?: boolean;
}

const renderList = computed<RenderItem[]>(() => {
  const list: RenderItem[] = [];
  const map = propertiesMap.value;
  const fileKeys = new Set(Object.keys(map));

  // 查询匹配项
  SERVER_PROPERTIES_SCHEMA.forEach(item => {
    if (fileKeys.has(item.key)) {
      list.push(item);
      fileKeys.delete(item.key); // 标记已处理
    }
  });

  // 未知的
  fileKeys.forEach(key => {
    list.push({
      key: key,
      label: key,
      desc: '未收录的配置项',
      type: detectType(map[key]), // 自动推断类型
      isUnknown: true
    });
  });

  return list;
});

// 类型推断，用于未知 Key
const detectType = (val: string): 'boolean' | 'number' | 'string' => {
  if (val === 'true' || val === 'false') return 'boolean';
  if (!isNaN(Number(val)) && val !== '') return 'number';
  return 'string';
};

// --- 值的转换逻辑 (String <-> Type) ---

const getBindValue = (key: string, type: string) => {
  const val = propertiesMap.value[key];
  if (val === undefined) return type === 'boolean' ? false : ''; // 默认值

  if (type === 'boolean') {
    return val === 'true';
  }
  if (type === 'number') {
    return Number(val);
  }
  return val;
};

const setBindValue = (key: string, value: any, type: string) => {
  if (type === 'boolean') {
    propertiesMap.value[key] = String(value);
  } else {
    propertiesMap.value[key] = String(value);
  }
};

// --- 生命周期 ---

watch(() => route.params.serverId, (newId) => {
    if (route.name !== 'InstanceConsole') {
      return;
    }
    if (newId) {
      instanceId.value = parseInt(newId as string);
      loadData();
    }
  },
  { immediate: true }
);

</script>

<template>
  <div class="flex flex-col mx-auto w-full pb-8">

    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mt-5 mb-6 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60">
      <div class="flex items-center gap-2">
        <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
        <h2 class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0">Server.properties 配置编辑器</h2>
      </div>

      <t-space size="small" class="w-full sm:w-auto justify-end">
        <t-button variant="outline" class="!rounded-lg !bg-zinc-50 dark:!bg-zinc-800/50 !border-zinc-200 dark:!border-zinc-700 hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !text-zinc-600 dark:!text-zinc-300 transition-colors" :loading="loading" @click="loadData">
          <template #icon><refresh-icon /></template> 刷新
        </t-button>
        <t-button theme="primary" class="!rounded-lg shadow-sm" :loading="saving" @click="handleSave">
          <template #icon><save-icon /></template> 保存配置
        </t-button>
      </t-space>
    </div>

    <t-loading :loading="loading" text="正在读取配置文件...">
      <div class="bg-white/80 dark:bg-zinc-900/40 border border-zinc-200/60 dark:border-zinc-800 rounded-xl shadow-sm backdrop-blur-md overflow-hidden">

        <div v-if="!loading && renderList.length === 0" class="py-16 flex items-center justify-center text-sm font-medium text-zinc-400 dark:text-zinc-500">
          无法找到配置项或文件为空
        </div>

        <div class="flex flex-col divide-y divide-dashed divide-zinc-200/60 dark:divide-zinc-700/60">
          <div
            v-for="item in renderList"
            :key="item.key"
            class="flex flex-col md:flex-row md:items-start justify-between p-5 transition-colors hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20"
          >
            <div class="flex-1 md:max-w-[40%] pr-0 md:pr-8 mb-3 md:mb-0">
              <div class="text-sm font-bold mb-1" :class="item.isUnknown ? 'text-amber-600 dark:text-amber-500' : 'text-zinc-800 dark:text-zinc-200'">
                {{ item.label }}
              </div>

              <div class="flex items-center mb-1.5" v-if="!item.isUnknown">
                <span class="font-mono text-[11px] text-zinc-500 dark:text-zinc-400 bg-zinc-100 dark:bg-zinc-800/80 px-1.5 py-0.5 rounded tracking-wider shadow-inner">{{ item.key }}</span>
              </div>

              <div v-if="!item.isUnknown" class="text-xs text-zinc-500 dark:text-zinc-400 leading-relaxed">
                {{ item.desc || '暂无描述' }}
              </div>
            </div>

            <div class="flex-1 md:max-w-[60%] w-full flex md:justify-end items-center">

              <template v-if="item.type === 'boolean'">
                <t-switch
                  :model-value="getBindValue(item.key, 'boolean') as boolean"
                  @update:model-value="(v) => setBindValue(item.key, v, 'boolean')"
                />
              </template>

              <template v-else-if="item.type === 'select'">
                <t-select
                  :model-value="getBindValue(item.key, 'string')"
                  :options="item.options"
                  placeholder="请选择"
                  class="w-full md:max-w-[400px]"
                  @update:model-value="(v) => setBindValue(item.key, v, 'string')"
                />
              </template>

              <template v-else-if="item.type === 'number'">
                <t-input-number
                  :model-value="getBindValue(item.key, 'number') as number"
                  theme="column"
                  class="w-full md:max-w-[400px]"
                  @update:model-value="(v) => setBindValue(item.key, v, 'number')"
                />
              </template>

              <template v-else>
                <t-input
                  :model-value="getBindValue(item.key, 'string') as string"
                  placeholder="未设置"
                  class="w-full md:max-w-[400px]"
                  @update:model-value="(v) => setBindValue(item.key, v, 'string')"
                />
              </template>

            </div>
          </div>
        </div>
      </div>
    </t-loading>

    <div class="flex flex-col sm:flex-row gap-3 mt-6 pt-6 border-t border-zinc-200/60 dark:border-zinc-700/60">
      <t-button theme="primary" size="large" class="!rounded-lg shadow-sm w-full sm:w-auto" :loading="saving" @click="handleSave">
        <template #icon><save-icon /></template> 保存设置
      </t-button>
      <t-button theme="default" variant="base" size="large" class="!rounded-lg w-full sm:w-auto" @click="loadData">
        重置更改
      </t-button>
    </div>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
