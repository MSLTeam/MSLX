import js from '@eslint/js';
import ts from 'typescript-eslint';
import pluginVue from 'eslint-plugin-vue';
import vueScopedCss from 'eslint-plugin-vue-scoped-css';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';

import prettierConfig from 'eslint-config-prettier';
import pluginImport from 'eslint-plugin-import';

export default defineConfigWithVueTs([
  // 1. 全局忽略配置
  {
    ignores: ['node_modules/', 'dist/'],
  },

  // 2. 基础 JS 配置 (对象, 无 '...')
  js.configs.recommended,

  // 3. 基础 TS 配置 (数组, 需要 '...')
  ...ts.configs.recommended,

  // 4. VueJS 配置 (数组, 需要 '...')
  ...pluginVue.configs['flat/recommended'],

  // 5. VueTs 配置 (对象, 无 '...')
  vueTsConfigs.recommended,

  // 6. Vue Scoped CSS 配置 (数组, 需要 '...')
  ...vueScopedCss.configs['flat/base'],

  // 7. Import 插件配置
  {
    plugins: { 'import': pluginImport, },
    settings: {
      'import/resolver': { typescript: true, node: true, },
      'import/extensions': [".js", ".jsx", ".ts", ".tsx"],
    },
  },

  // 8. Prettier 配置 (对象, 无 '...')
  prettierConfig,

  // 9. 你的全局自定义规则
  {
    files: ['**/*.{js,jsx,ts,tsx,vue}'],
    languageOptions: {
      globals: { defineProps: 'readonly', defineEmits: 'readonly', },
    },
    rules: {
      "no-console": "off",
      "no-continue": "off",
      "no-restricted-syntax": "off",
      "no-plusplus": "off",
      "no-param-reassign": "off",
      "no-shadow": "off",
      "guard-for-in": "off",
      "import/extensions": "off",
      "import/no-unresolved": "off",
      "import/no-extraneous-dependencies": "off",
      "import/prefer-default-export": "off",
      "import/first": "off",
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/explicit-module-boundary-types": "off",
      "vue/first-attribute-linebreak": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { "argsIgnorePattern": "^_", "varsIgnorePattern": "^_" }
      ],
      "no-unused-vars": [
        "error",
        { "argsIgnorePattern": "^_", "varsIgnorePattern": "^_" }
      ],
      "no-use-before-define": "off",
      "@typescript-eslint/no-use-before-define": "off",
      "@typescript-eslint/ban-ts-comment": "off",
      "@typescript-eslint/ban-types": "off",
      "class-methods-use-this": "off"
    }
  },

  // 10. 针对 .vue 文件的覆盖
  {
    files: ["*.vue"],
    rules: {
      "vue/component-name-in-template-casing": ["error", "kebab-case"],
      "vue/require-default-prop": "off",
      "vue/multi-word-component-names": "off",
      "vue/no-reserved-props": "off",
      "vue/no-v-html": "off",
      "vue-scoped-css/enforce-style-type": ["error", { "allows": ["scoped"] }]
    }
  },

  // 11. 针对 .ts 文件的覆盖
  {
    files: ["*.ts", "*.tsx"],
    rules: {
      "constructor-super": "off",
      "getter-return": "off",
      "no-const-assign": "off",
      "no-dupe-args": "off",
      "no-dupe-class-members": "off",
      "no-dupe-keys": "off",
      "no-func-assign": "off",
      "no-import-assign": "off",
      "no-new-symbol": "off",
      "no-obj-calls": "off",
      "no-redeclare": "off",
      "no-setter-return": "off",
      "no-this-before-super": "off",
      "no-undef": "off",
      "no-unreachable": "off",
      "no-unsafe-negation": "off",
      "no-var": "error",
      "prefer-const": "error",
      "prefer-rest-params": "error",
      "prefer-spread": "error",
      "valid-typeof": "off"
    }
  }
]);
