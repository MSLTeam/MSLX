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
  <div class="settings-container">
    <div class="page-header">
      <div class="section-title">Server.properties 配置编辑器</div>
      <t-space>
        <t-button variant="text" shape="square" :loading="loading" @click="loadData">
          <template #icon><refresh-icon /></template>
        </t-button>
        <t-button theme="primary" :loading="saving" @click="handleSave">
          <template #icon><save-icon /></template>
          保存配置
        </t-button>
      </t-space>
    </div>

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
            <div class="key-name-row">
              <span v-if="!item.isUnknown" class="key-name">{{ item.key }}</span>
            </div>
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

    <div class="form-actions">
      <t-button theme="primary" size="large" :loading="saving" @click="handleSave">保存设置</t-button>
      <t-button theme="default" variant="base" size="large" @click="loadData">重置更改</t-button>
    </div>
  </div>
</template>

<style scoped lang="less">
.settings-container {
  margin: 0 auto;
  max-width: 100%;
}

/* 顶部标题栏 - 蓝色标题风格 */
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 32px;
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);
}

.section-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  display: flex;
  align-items: center;

  &::before {
    content: '';
    display: inline-block;
    width: 4px;
    height: 16px;
    background-color: var(--td-brand-color);
    margin-right: 8px;
    border-radius: 2px;
  }
}

.config-list {
  display: flex;
  flex-direction: column;
}

.empty-tip {
  text-align: center;
  padding: 40px;
  color: var(--td-text-color-placeholder);
  background: var(--td-bg-color-container);
  border-radius: var(--td-radius-medium);
  border: 1px dashed var(--td-component-stroke);
}

.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 24px; /* 增加左右内边距 */
  border-bottom: 1px dashed var(--td-component-stroke);
  flex-wrap: wrap;
  transition: background-color 0.2s;

  &:last-child {
    border-bottom: none;
  }

  /* 未知项略微区分 */
  &.unknown-item {
    .title {
      color: var(--td-warning-color);
    }
  }

  .setting-info {
    flex: 1;
    padding-right: 32px;
    max-width: 40%; /* 限制左侧宽度，与模板一致 */
    min-width: 200px;

    .title {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 500;
      line-height: 22px;
      margin-bottom: 4px;
    }

    .key-name-row {
      display: flex;
      align-items: center;
      margin-bottom: 2px;
    }

    .key-name {
      font-family: 'Consolas', monospace;
      font-size: 12px;
      color: var(--td-text-color-secondary);
      background-color: var(--td-bg-color-secondarycontainer);
      padding: 1px 4px;
      border-radius: 3px;
    }

    .unknown-tag {
      font-size: 12px;
      color: var(--td-warning-color);
      background-color: var(--td-bg-color-secondarycontainer);
      padding: 1px 4px;
      border-radius: 3px;
    }

    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
      line-height: 20px;
    }
  }

  .setting-control {
    flex: 1;
    max-width: 60%;
    display: flex;
    justify-content: flex-end;
    align-items: center; /* 垂直居中 */

    /* 让控件统一样式 */
    .t-input, .t-select, .t-input-number {
      width: 100%;
      max-width: 400px;
    }
  }
}

.form-actions {
  margin-top: 24px;
  display: flex;
  gap: 16px;
  padding-top: 24px;
  border-top: 1px solid var(--td-component-stroke);
  /* 恢复左对齐，如果需要右对齐可改为 justify-content: flex-end */
}

/* 适配暗黑模式的滚动条等细节由 TDesign 变量自动处理 */

@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;
    padding: 16px;

    .setting-info {
      padding-right: 0;
      margin-bottom: 12px;
      max-width: 100%;
    }

    .setting-control {
      max-width: 100%;
      justify-content: flex-start;

      .t-input, .t-select, .t-input-number {
        max-width: 100%;
      }
    }
  }

  .form-actions {
    flex-direction: column;

    button {
      width: 100%;
    }
  }
}
</style>
