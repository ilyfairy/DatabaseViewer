<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { NAlert, NButton, NCard, NSpin, NSwitch, NText } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';

const store = useExplorerStore();
const loadError = ref<string | null>(null);

onMounted(async () => {
  if (store.explorerSettingsLoaded) {
    return;
  }

  try {
    await store.ensureExplorerSettingsLoaded();
  }
  catch (error) {
    loadError.value = error instanceof Error ? error.message : '设置加载失败';
  }
});

async function saveSettings() {
  loadError.value = null;

  try {
    await store.saveExplorerSettings();
  }
  catch (error) {
    loadError.value = error instanceof Error ? error.message : '设置保存失败';
  }
}
</script>

<template>
  <NCard embedded class="workspace-panel settings-panel">
    <template #header>
      <div class="panel-header settings-panel-header">
        <div>
          <h3>设置</h3>
          <span class="panel-meta">全局界面与性能选项</span>
        </div>
      </div>
    </template>

    <div class="settings-panel-body">
      <NAlert v-if="loadError" type="warning" :show-icon="false">
        {{ loadError }}
      </NAlert>

      <NSpin :show="!store.explorerSettingsLoaded && !loadError">
      <section class="settings-section compact-panel">
        <div class="settings-item-head">
          <div>
            <div class="settings-item-title">在表列表显示行数</div>
            <NText depth="3">关闭后，左侧表树不再显示 `xxx rows`，并且后端不会在表列表和表分页接口里请求总行数。</NText>
          </div>
          <NSwitch v-model:value="store.settingsDraft.showTableRowCounts" />
        </div>

        <div class="settings-actions">
          <NButton tertiary :disabled="!store.isSettingsDirty || store.settingsSaving" @click="store.resetExplorerSettingsDraft">重置</NButton>
          <NButton type="primary" :loading="store.settingsSaving" :disabled="!store.isSettingsDirty" @click="saveSettings">保存</NButton>
        </div>
      </section>
      </NSpin>
    </div>
  </NCard>
</template>

<style scoped lang="scss">
.settings-panel {
  height: 100%;
}

.settings-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.settings-panel-body {
  display: flex;
  flex-direction: column;
  gap: $gap-lg;
}

.settings-section {
  padding: $gap-lg;
}

.settings-item-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $gap-md;
}

.settings-item-title {
  margin-bottom: $gap-xs;
  font-size: $font-size-md;
  font-weight: 700;
  color: $color-text-heading;
}

.settings-actions {
  display: flex;
  justify-content: flex-end;
  gap: $gap-sm;
  margin-top: $gap-lg;
}

@media (max-width: 768px) {
  .settings-item-head {
    flex-direction: column;
    align-items: stretch;
  }

  .settings-actions {
    justify-content: stretch;
  }
}
</style>