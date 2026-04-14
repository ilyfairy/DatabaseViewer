import { defineConfig, searchForWorkspaceRoot, type PluginOption } from 'vite';
import vue from '@vitejs/plugin-vue';
import { visualizer } from 'rollup-plugin-visualizer';

const apiTarget = process.env.VITE_API_TARGET ?? 'http://127.0.0.1:5027';
const devServerOrigin = process.env.VITE_DEV_SERVER_ORIGIN ?? 'http://127.0.0.1:5173';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    // 按需启用包体分析：仅在显式设置 VISUALIZE=1 时生成可视化报告。
    process.env.VISUALIZE === '1'
      ? visualizer({
          filename: 'vite-bundle-report.html',
          gzipSize: true,
          brotliSize: true,
          open: false,
        }) as PluginOption
      : null,
  ].filter((plugin): plugin is PluginOption => plugin !== null),
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    origin: devServerOrigin,
    allowedHosts: ['127.0.0.1', 'localhost'],
    forwardConsole: {
      unhandledErrors: true,
      logLevels: ['debug', 'log', 'info', 'warn', 'error'],
    },
    warmup: {
      clientFiles: [
        './index.html',
        './src/App.vue',
        './src/stores/explorer.ts',
        './src/components/ExplorerSidebar.vue',
        './src/components/WorkspaceTabsBar.vue',
      ],
    },
    fs: {
      allow: [searchForWorkspaceRoot(process.cwd())],
    },
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
  css: {
    // 使用 Lightning CSS 替代 PostCSS 做 CSS 变换和压缩，大幅减少构建时 vite:css 耗时。
    transformer: 'lightningcss',
    preprocessorOptions: {
      scss: {
        additionalData: `@use "@/variables" as *;\n`,
      },
    },
  },
  resolve: {
    alias: {
      '@': '/src',
    },
    tsconfigPaths: true,
  },
  build: {
    // WPF/WebView2 desktop app — local transport is fast, no need for gzip overhead
    reportCompressedSize: false,
    chunkSizeWarningLimit: 2000,
    // 桌面应用目标浏览器切到 esnext，跳过不必要的语法降级转换。
    target: 'esnext',
    // 使用 Lightning CSS 做 CSS 压缩（Vite 8 默认值，显式声明以防回退）。
    cssMinify: 'lightningcss',
    // WebView2 只运行在一个进程里，无需为每个动态 import 注入 modulepreload polyfill。
    modulePreload: { polyfill: false },
  },
});
