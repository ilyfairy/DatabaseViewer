import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
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
          'ace-editor': ['ace-builds'],
        },
      },
    },
  },
});
