<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { NAlert, NButton, NCard, NInput, NSelect, NSpin, NSwitch, NText, NTooltip } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import type { SqliteLoadableExtensionPhase } from '../types/explorer';

const store = useExplorerStore();
const loadError = ref<string | null>(null);
const sqliteExtensionPhaseOptions = [
  { label: '预打开', value: 'preOpen' },
  { label: '连接后', value: 'postOpen' },
] satisfies Array<{ label: string; value: SqliteLoadableExtensionPhase }>;

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

async function browseSqliteExtensionFile(index: number) {
  loadError.value = null;

  try {
    const current = store.settingsDraft.sqliteExtensions[index];
    if (!current) {
      return;
    }

    const filePath = await store.pickSqliteExtensionFile(current.path ?? null);
    if (filePath) {
      current.path = filePath;
    }
  }
  catch (error) {
    loadError.value = error instanceof Error ? error.message : 'SQLite 扩展文件选择失败';
  }
}

function addSqliteExtensionRow() {
  store.settingsDraft.sqliteExtensions = [...store.settingsDraft.sqliteExtensions, {
    path: '',
    entryPoint: '',
    phase: 'preOpen',
  }];
}

function removeSqliteExtensionRow(index: number) {
  store.settingsDraft.sqliteExtensions = store.settingsDraft.sqliteExtensions.filter((_, currentIndex) => currentIndex !== index);
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
          <NTooltip trigger="hover">
            <template #trigger>
              <div class="settings-item-title settings-item-title-help">在表列表显示行数</div>
            </template>
            <div class="settings-tooltip-content">
              关闭后，左侧表树不再显示 `xxx rows`，并且后端不会在表列表和表分页接口里请求总行数。
            </div>
          </NTooltip>
          <NSwitch v-model:value="store.settingsDraft.showTableRowCounts" />
        </div>

        <div class="settings-divider" />

        <div class="settings-item-stack">
          <div class="settings-item-head">
            <NTooltip trigger="hover">
              <template #trigger>
                <div class="settings-item-title settings-item-title-help">全局 SQLite 扩展</div>
              </template>
              <div class="settings-tooltip-content">
                这里配置的是进程级 SQLite 扩展加载列表。连接设置只负责选择启用哪个 VFS；真正的扩展加载统一由全局设置控制。预打开扩展会影响当前进程里的所有 SQLite 连接。
              </div>
            </NTooltip>
            <NButton tertiary type="primary" @click="addSqliteExtensionRow">新增扩展</NButton>
          </div>

          <div v-if="store.settingsDraft.sqliteExtensions.length > 0" class="settings-extension-grid">
            <template v-for="(extension, index) in store.settingsDraft.sqliteExtensions" :key="`settings-sqlite-extension-${index}`">
              <div class="settings-extension-row">
                <div class="settings-extension-meta">
                  <NTooltip trigger="hover">
                    <template #trigger>
                      <span class="settings-item-title settings-item-title-help">扩展路径 {{ index + 1 }}</span>
                    </template>
                    <div class="settings-tooltip-content">SQLite loadable extension 动态库路径。</div>
                  </NTooltip>
                </div>
                <div class="settings-inline-row">
                  <NInput v-model:value="extension.path" placeholder="例如 sqlite_ext_myext_db.dll" />
                  <NButton tertiary type="primary" @click="browseSqliteExtensionFile(index)">选择...</NButton>
                </div>
              </div>

              <div class="settings-extension-row">
                <div class="settings-extension-meta">
                  <NTooltip trigger="hover">
                    <template #trigger>
                      <span class="settings-item-title settings-item-title-help">入口函数 {{ index + 1 }}</span>
                    </template>
                    <div class="settings-tooltip-content">可选。留空时使用扩展库默认入口。</div>
                  </NTooltip>
                </div>
                <NInput v-model:value="extension.entryPoint" placeholder="例如 sqlite3_myext_init" />
              </div>

              <div class="settings-extension-row">
                <div class="settings-extension-meta">
                  <NTooltip trigger="hover">
                    <template #trigger>
                      <span class="settings-item-title settings-item-title-help">加载阶段 {{ index + 1 }}</span>
                    </template>
                    <div class="settings-tooltip-content">
                      预打开扩展会在进程中提前注册能力；连接后扩展会对每个 SQLite 连接在打开后加载。
                    </div>
                  </NTooltip>
                </div>
                <div class="settings-inline-row">
                  <NSelect v-model:value="extension.phase" :options="sqliteExtensionPhaseOptions" />
                  <NButton tertiary type="error" @click="removeSqliteExtensionRow(index)">删除</NButton>
                </div>
              </div>
            </template>
          </div>

          <NText v-else depth="3" class="settings-empty-text">当前没有配置任何全局 SQLite 扩展。</NText>
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
  font-size: $font-size-lg;
  line-height: 1.3;
  font-weight: 700;
  color: $color-text-heading;
}

.settings-item-title-help {
  display: inline-flex;
  align-items: center;
  cursor: help;
}

.settings-empty-text {
  font-size: $font-size-md;
  line-height: 1.5;
}

.settings-tooltip-content {
  max-width: min(30rem, calc(100vw - 3rem));
  white-space: normal;
  word-break: break-word;
  line-height: 1.5;
}

.settings-actions {
  display: flex;
  justify-content: flex-end;
  gap: $gap-sm;
  margin-top: $gap-lg;
}

.settings-divider {
  height: 1px;
  margin: $gap-lg 0;
  background: color-mix(in srgb, $color-border-light 78%, transparent);
}

.settings-item-stack {
  display: flex;
  flex-direction: column;
  gap: $gap-md;
}

.settings-extension-grid {
  display: grid;
  gap: $gap-md;
}

.settings-extension-row {
  display: grid;
  gap: $gap-sm;
}

.settings-extension-meta {
  display: flex;
  flex-direction: column;
  gap: $gap-xs;
}

.settings-inline-row {
  display: flex;
  gap: $gap-sm;
  align-items: center;
}

@media (max-width: 768px) {
  .settings-item-head {
    flex-direction: column;
    align-items: stretch;
  }

  .settings-actions {
    justify-content: stretch;
  }

  .settings-inline-row {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>