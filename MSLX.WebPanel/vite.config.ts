import { ConfigEnv, defineConfig, UserConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';
import vueJsx from '@vitejs/plugin-vue-jsx';
import svgLoader from 'vite-svg-loader';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserslistToTargets } from 'lightningcss';
import browserslist from 'browserslist';
import https from 'node:https';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function getBackendTarget(port: number = 1027): Promise<string> {
  return new Promise((resolve) => {
    const req = https.get(`https://localhost:${port}`, { rejectUnauthorized: false }, () => {
      resolve(`https://localhost:${port}`);
    });

    req.on('error', () => {
      resolve(`http://localhost:${port}`);
    });

    req.setTimeout(800, () => {
      req.destroy();
      resolve(`http://localhost:${port}`);
    });
  });
}

export default defineConfig(async ({ mode:_mode }: ConfigEnv): Promise<UserConfig> => {
  const targetUrl = await getBackendTarget(1027);
  console.log(`[Vite Dev] 代理后端路由: ${targetUrl}`);
  // @ts-ignore
  // @ts-ignore
  return {
    base: '/',
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
      dedupe: ['@codemirror/state', '@codemirror/view', '@codemirror/language', '@codemirror/commands'],
    },
    css: {
      lightningcss: {
        targets: browserslistToTargets(browserslist('chrome >= 109')),
        drafts: {
          customMedia: true,
        },
        // @ts-ignore
        minify: true,
      },
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
    plugins: [vue(), tailwindcss(), vueJsx(), svgLoader()],
    server: {
      port: 1102,
      host: '0.0.0.0',
      proxy: {
        '/api': {
          target: targetUrl,
          changeOrigin: true,
          ws: true,
          secure: false,
        },
        '/plugins': {
          target: targetUrl,
          changeOrigin: true,
          secure: false,
        },
      },
    },
    build: {
      target: ['es2020', 'chrome109'],
      cssMinify: 'lightningcss',
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
              if (id.includes('echarts') || id.includes('zrender')) return 'mslx-libs-charts';
              if (id.includes('prettier')) return 'mslx-libs-formatter';
              if (id.includes('md-editor-v3') || id.includes('codemirror')) return 'mslx-libs-editor';
              if (id.includes('artplayer')) return 'mslx-libs-artplayer';
              return 'mslx-libs';
            }
          },

          assetFileNames: (assetInfo) => {
            const name = assetInfo.name || '';
            const extIndex = name.lastIndexOf('.');
            const baseName = extIndex !== -1 ? name.slice(0, extIndex) : name;
            const kebabName = baseName.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase();
            return kebabName.startsWith('mslx-')
              ? `assets/[ext]/${kebabName}.[hash].[ext]`
              : `assets/[ext]/mslx-${kebabName}.[hash].[ext]`;
          },

          entryFileNames: 'assets/js/mslx-entry.[hash].js',
          chunkFileNames: (chunkInfo) => {
            const name = chunkInfo.name || '';
            const kebabName = name.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase();
            return kebabName.startsWith('mslx-')
              ? `assets/js/${kebabName}.[hash].js`
              : `assets/js/mslx-${kebabName}.[hash].js`;
          },
        },
      },
    },
  };
});
