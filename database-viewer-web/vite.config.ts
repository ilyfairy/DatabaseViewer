import { defineConfig, type PluginOption } from 'vite';
import vue from '@vitejs/plugin-vue';
import { visualizer } from 'rollup-plugin-visualizer';

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
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5027',
        changeOrigin: true,
      },
    },
  },
  css: {
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
  },
  build: {
    // WPF/WebView2 desktop app — local transport is fast, no need for gzip overhead
    reportCompressedSize: false,
    chunkSizeWarningLimit: 2000,
    rollupOptions: {
      output: {
        manualChunks: {
          'naive-ui': ['naive-ui'],
          'vue-vendor': ['vue', 'pinia'],
          'vue-flow': ['@vue-flow/core', '@vue-flow/background', '@vue-flow/minimap'],
          'monaco-editor': ['monaco-editor/esm/vs/editor/editor.api.js'],
        },
      },
    },
  },
});
