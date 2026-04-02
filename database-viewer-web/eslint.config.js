import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import pluginVue from 'eslint-plugin-vue';

export default tseslint.config(
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
      },
    },
  },
  {
    languageOptions: {
      globals: {
        // Browser globals
        window: 'readonly',
        document: 'readonly',
        navigator: 'readonly',
        console: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        requestAnimationFrame: 'readonly',
        cancelAnimationFrame: 'readonly',
        fetch: 'readonly',
        URL: 'readonly',
        Blob: 'readonly',
        File: 'readonly',
        FileReader: 'readonly',
        FormData: 'readonly',
        HTMLElement: 'readonly',
        HTMLInputElement: 'readonly',
        HTMLTextAreaElement: 'readonly',
        HTMLSelectElement: 'readonly',
        HTMLCanvasElement: 'readonly',
        Element: 'readonly',
        Event: 'readonly',
        KeyboardEvent: 'readonly',
        MouseEvent: 'readonly',
        DragEvent: 'readonly',
        WheelEvent: 'readonly',
        PointerEvent: 'readonly',
        FocusEvent: 'readonly',
        InputEvent: 'readonly',
        ClipboardEvent: 'readonly',
        CustomEvent: 'readonly',
        MessageEvent: 'readonly',
        ResizeObserver: 'readonly',
        MutationObserver: 'readonly',
        IntersectionObserver: 'readonly',
        AbortController: 'readonly',
        crypto: 'readonly',
        structuredClone: 'readonly',
        queueMicrotask: 'readonly',
        alert: 'readonly',
        confirm: 'readonly',
      },
    },
    rules: {
      // 强制分号
      'semi': ['error', 'always'],
      '@typescript-eslint/semi': 'off',

      // 关闭不需要的规则
      'no-undef': 'off', // TypeScript handles this
      'vue/multi-word-component-names': 'off',
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/html-indent': 'off',
      'vue/first-attribute-linebreak': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/attribute-hyphenation': 'off',
      'vue/v-on-event-hyphenation': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_' }],
    },
  },
  {
    ignores: ['dist/**', 'node_modules/**'],
  },
);
