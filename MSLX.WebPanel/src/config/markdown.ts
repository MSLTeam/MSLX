import { config } from 'md-editor-v3';
import hljs from 'highlight.js/lib/common';

// 全局配置 md-editor-v3 依赖，实现 100% 离线自包含，杜绝向 unpkg.com 发起任何动态请求
config({
  editorExtensions: {
    highlight: {
      instance: hljs,
    },
    // 防止任何 md-preview 未显式指定 noKatex 时自动向 unpkg 请求 katex
    katex: {
      instance: {
        renderToString: (str: string) => str,
      },
    },
    // 防止任何 md-preview 未显式指定 noMermaid 时自动向 unpkg 请求 mermaid
    mermaid: {
      instance: {
        initialize: () => {},
        render: async () => ({ svg: '' }),
      },
    },
  },
});
