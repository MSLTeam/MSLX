import { ConfigEnv, defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import vueJsx from '@vitejs/plugin-vue-jsx';
import svgLoader from 'vite-svg-loader';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig(({ mode }: ConfigEnv) => {
  return {
    base: '/',
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    css: {
      preprocessorOptions: {
        less: {
          modifyVars: {
            hack: `true; @import (reference) "${path.resolve(__dirname, 'src/style/variables.less')}";`,
          },
          math: 'strict',
          javascriptEnabled: true,
        },
      },
    },
    plugins: [vue(), vueJsx(), svgLoader()],
    server: {
      port: 1102,
      host: '0.0.0.0',
      proxy: {
        '/api': {
          target: 'http://localhost:1027',
          changeOrigin: true,
          ws: true,
        },
      },
    },
    build: {
      chunkSizeWarningLimit: 2000,
      rollupOptions: {
        onwarn(warning, warn) {
          if (warning.code === 'INVALID_ANNOTATION' && warning.message.includes('signalr')) return;
          warn(warning);
        },
        output: {
          manualChunks: (id: string) => {
            if (id.includes('node_modules')) {
              if (
                id.includes('vue') ||
                id.includes('pinia') ||
                id.includes('tdesign') ||
                id.includes('axios') ||
                id.includes('signalr')
              ) {
                return 'mslx-core';
              }
              if (id.includes('echarts') || id.includes('zrender')) return 'mslx-charts';
              if (id.includes('prettier')) return 'mslx-formatter';
              if (id.includes('md-editor-v3') || id.includes('codemirror')) return 'mslx-editor';
              return 'mslx-libs';
            }
            if (id.includes('src/')) return 'mslx-app-main';
          },

          assetFileNames: (assetInfo) => {
            const name = assetInfo.name || '';
            return name.startsWith('mslx-')
              ? 'assets/[ext]/[name].[hash].[ext]'
              : 'assets/[ext]/mslx-[name].[hash].[ext]';
          },

          entryFileNames: 'assets/js/mslx-entry.[hash].js',
          chunkFileNames: 'assets/js/[name].[hash].js',
        },
      },
    },
  };
});
