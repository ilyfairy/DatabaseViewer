import { createApp } from 'vue';
import { createPinia } from 'pinia';
import './style.scss';
import 'splitpanes/dist/splitpanes.css';
import App from './App.vue';

const app = createApp(App);

app.use(createPinia());
app.mount('#app');

/** 禁用中键自动滚动（autoscroll）光标 */
document.addEventListener('mousedown', (event: MouseEvent) => {
  if (event.button === 1) {
    event.preventDefault();
  }
}, { passive: false });
