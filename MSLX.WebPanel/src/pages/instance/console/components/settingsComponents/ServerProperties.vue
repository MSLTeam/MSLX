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

watch(
  () => route.params.id,
  (newId) => {
    if (newId) {
      instanceId.value = parseInt(newId as string);
      loadData();
    }
  },
  { immediate: true }
);

</script>

<template>
  <div class="settings-container">
    <t-card :bordered="false" title="Server.properties 配置编辑器">
      <template #actions>
        <t-space>
          <t-button variant="text" shape="square" :loading="loading" @click="loadData">
            <template #icon><refresh-icon /></template>
          </t-button>
          <t-button theme="primary" :loading="saving" @click="handleSave">
            <template #icon><save-icon /></template>
            保存配置
          </t-button>
        </t-space>
      </template>

      <t-loading :loading="loading" text="正在读取配置文件...">
        <div class="config-list">

          <div v-if="!loading && renderList.length === 0" class="empty-tip">
            无法找到配置项或文件为空
          </div>

          <div
            v-for="item in renderList"
            :key="item.key"
            class="setting-item"
            :class="{ 'unknown-item': item.isUnknown }"
          >
            <div class="setting-info">
              <div class="title">
                {{ item.label }}
              </div>
              <div v-if="!item.isUnknown" class="key-name">{{ item.key }}</div>
              <div v-if="!item.isUnknown" class="desc">{{ item.desc || '暂无描述' }}</div>
            </div>

            <div class="setting-control">
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
                  @update:model-value="(v) => setBindValue(item.key, v, 'string')"
                />
              </template>

              <template v-else-if="item.type === 'number'">
                <t-input-number
                  :model-value="getBindValue(item.key, 'number') as number"
                  theme="column"
                  style="width: 100%"
                  @update:model-value="(v) => setBindValue(item.key, v, 'number')"
                />
              </template>

              <template v-else>
                <t-input
                  :model-value="getBindValue(item.key, 'string') as string"
                  placeholder="未设置"
                  @update:model-value="(v) => setBindValue(item.key, v, 'string')"
                />
              </template>
            </div>
          </div>
        </div>
      </t-loading>
    </t-card>

    <div class="form-actions">
      <t-button theme="primary" size="large" :loading="saving" @click="handleSave">保存设置</t-button>
      <t-button theme="default" variant="base" size="large" @click="loadData">重置更改</t-button>
    </div>
  </div>
</template>

<style scoped lang="less">

.settings-container {
  margin: 0 calc(0px - var(--td-comp-paddingLR-xl)); // 消掉tcard自带的padding
}

.config-list {
  display: flex;
  flex-direction: column;
}

.empty-tip {
  text-align: center;
  padding: 40px;
  color: var(--td-text-color-placeholder);
}

.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 0;
  border-bottom: 1px dashed var(--td-component-stroke);
  flex-wrap: wrap;
  transition: background-color 0.2s;

  &:last-child {
    border-bottom: none;
  }

  /* 未知项略微区分 */
  &.unknown-item {
    .key-name {
      color: var(--td-warning-color);
    }
  }

  .setting-info {
    flex: 1;
    padding-right: 32px;
    min-width: 200px;

    .title {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 600;
      line-height: 22px;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .key-name {
      font-family: 'Consolas', monospace;
      font-size: 12px;
      color: var(--td-text-color-secondary); // 次要文本颜色
      margin-top: 2px;
    }

    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
      line-height: 20px;
    }
  }

  .setting-control {
    width: 300px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    /* 让控件垂直居中一点 */
    padding-top: 2px;
  }
}

.form-actions {
  margin-top: 24px;
  display: flex;
  gap: 16px;
  justify-content: flex-end; /* 按钮靠右更符合表单习惯，或者根据你原本风格靠左 */
  padding-top: 24px;
  border-top: 1px solid var(--td-component-stroke);
}

/* 适配暗黑模式的滚动条等细节由 TDesign 变量自动处理 */

@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;
    padding: 16px 0;

    .setting-info {
      padding-right: 0;
      margin-bottom: 12px;
    }

    .setting-control {
      width: 100%;
    }
  }

  .form-actions {
    justify-content: stretch;
    flex-direction: column;

    button {
      width: 100%;
    }
  }
}
</style>
