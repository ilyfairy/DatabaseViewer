<script setup lang="ts">
import { ref, watch } from 'vue';
import { NButton, NCheckbox, NInput, NModal } from 'naive-ui';
import type { ProviderType, RoutineInfo } from '../types/explorer';

const props = defineProps<{
  show: boolean;
  connectionId: string;
  database: string;
  provider: ProviderType;
  routine: RoutineInfo;
}>();

const emit = defineEmits<{
  'update:show': [value: boolean];
  'execute': [sql: string];
}>();

interface ParamEntry {
  name: string;
  dataType: string;
  direction: string;
  defaultValue: string | null;
  value: string;
  isNull: boolean;
}

const params = ref<ParamEntry[]>([]);

watch(() => props.show, (visible) => {
  if (visible) {
    params.value = props.routine.parameters
      .filter((p) => p.direction !== 'RETURN_VALUE')
      .map((p) => ({
        name: p.name,
        dataType: p.dataType,
        direction: p.direction,
        defaultValue: p.defaultValue ?? null,
        value: p.defaultValue ?? '',
        isNull: false,
      }));
  }
}, { immediate: true });

/** 根据 provider 和参数值生成可执行的 SQL 语句。 */
function buildExecuteSql(): string {
  const routine = props.routine;
  const provider = props.provider;
  const qualifiedName = buildQualifiedName(provider, routine);

  if (provider === 'sqlserver') {
    return buildSqlServerExecSql(qualifiedName, routine, params.value);
  }

  if (provider === 'mysql') {
    return buildMySqlCallSql(qualifiedName, routine, params.value);
  }

  if (provider === 'postgresql') {
    return buildPostgreSqlCallSql(qualifiedName, routine, params.value);
  }

  return `-- 不支持的数据库类型`;
}

function buildQualifiedName(provider: ProviderType, routine: RoutineInfo): string {
  if (!routine.schema) {
    return quoteId(provider, routine.name);
  }

  if (provider === 'mysql') {
    return quoteId(provider, routine.name);
  }

  return `${quoteId(provider, routine.schema)}.${quoteId(provider, routine.name)}`;
}

function quoteId(provider: ProviderType, name: string): string {
  if (provider === 'mysql') {
    return `\`${name.replace(/`/g, '``')}\``;
  }

  if (provider === 'postgresql') {
    return `"${name.replace(/"/g, '""')}"`;
  }

  return `[${name.replace(/\]/g, ']]')}]`;
}

function formatParamValue(entry: ParamEntry): string {
  if (entry.isNull) {
    return 'NULL';
  }

  const val = entry.value;
  const dt = entry.dataType.toLowerCase();

  if (val === '' && entry.defaultValue !== null) {
    return 'DEFAULT';
  }

  if (/^(int|bigint|smallint|tinyint|decimal|numeric|float|double|real|money|smallmoney|bit|serial|integer)/.test(dt)) {
    return val === '' ? 'NULL' : val;
  }

  return `N'${val.replace(/'/g, "''")}'`;
}

function buildSqlServerExecSql(qualifiedName: string, routine: RoutineInfo, entries: ParamEntry[]): string {
  if (routine.routineType === 'Procedure') {
    if (entries.length === 0) {
      return `EXEC ${qualifiedName};`;
    }

    const outParams = entries.filter((e) => e.direction === 'OUT' || e.direction === 'INOUT');

    if (outParams.length > 0) {
      const declares = outParams.map((e) => `DECLARE ${e.name}_out ${e.dataType};`).join('\n');
      const initOuts = outParams
        .filter((e) => e.direction === 'INOUT')
        .map((e) => `SET ${e.name}_out = ${formatParamValue(e)};`)
        .join('\n');
      const paramListWithVars = entries.map((e) => {
        if (e.direction === 'OUT' || e.direction === 'INOUT') {
          return `    ${e.name} = ${e.name}_out OUTPUT`;
        }
        return `    ${e.name} = ${formatParamValue(e)}`;
      }).join(',\n');
      const selectOuts = `\n\nSELECT ${outParams.map((e) => `${e.name}_out AS ${e.name}`).join(', ')};`;

      return `${declares}\n${initOuts ? initOuts + '\n' : ''}\nEXEC ${qualifiedName}\n${paramListWithVars};${selectOuts}`;
    }

    const paramList = entries.map((e) => {
      const val = formatParamValue(e);
      return `    ${e.name} = ${val}`;
    }).join(',\n');

    return `EXEC ${qualifiedName}\n${paramList};`;
  }

  const argList = entries.map((e) => formatParamValue(e)).join(', ');
  return `SELECT ${qualifiedName}(${argList});`;
}

function buildMySqlCallSql(qualifiedName: string, routine: RoutineInfo, entries: ParamEntry[]): string {
  if (routine.routineType === 'Procedure') {
    const outParams = entries.filter((e) => e.direction === 'OUT' || e.direction === 'INOUT');
    const declares = outParams.map((e) => `SET @${e.name.replace(/^@/, '')} = ${e.direction === 'INOUT' ? formatParamValue(e) : 'NULL'};`).join('\n');
    const argList = entries.map((e) => {
      if (e.direction === 'OUT' || e.direction === 'INOUT') {
        return `@${e.name.replace(/^@/, '')}`;
      }
      return formatParamValue(e);
    }).join(', ');
    const selectOuts = outParams.length > 0
      ? `\nSELECT ${outParams.map((e) => `@${e.name.replace(/^@/, '')} AS ${e.name}`).join(', ')};`
      : '';

    return `${declares ? declares + '\n' : ''}CALL ${qualifiedName}(${argList});${selectOuts}`;
  }

  const argList = entries.map((e) => formatParamValue(e)).join(', ');
  return `SELECT ${qualifiedName}(${argList});`;
}

function buildPostgreSqlCallSql(qualifiedName: string, routine: RoutineInfo, entries: ParamEntry[]): string {
  if (routine.routineType === 'Procedure') {
    const argList = entries.map((e) => formatParamValue(e)).join(', ');
    return `CALL ${qualifiedName}(${argList});`;
  }

  const argList = entries.map((e) => formatParamValue(e)).join(', ');
  return `SELECT * FROM ${qualifiedName}(${argList});`;
}

function submit() {
  const sql = buildExecuteSql();
  emit('execute', sql);
  emit('update:show', false);
}
</script>

<template>
  <NModal
    :show="props.show"
    preset="card"
    style="width: min(520px, 92vw)"
    :title="`执行 ${routine.schema ? routine.schema + '.' : ''}${routine.name}`"
    @update:show="emit('update:show', $event)"
  >
    <div class="connection-dialog-form">
      <div v-for="(entry, index) in params" :key="index" class="routine-exec-param">
        <div class="routine-exec-param-header">
          <span class="routine-exec-param-name">{{ entry.name }}</span>
          <span class="routine-exec-param-type">{{ entry.dataType }}</span>
          <span v-if="entry.direction !== 'IN'" class="routine-exec-param-direction">{{ entry.direction }}</span>
        </div>
        <div class="routine-exec-param-input">
          <NInput
            v-model:value="entry.value"
            :placeholder="entry.defaultValue ? `默认: ${entry.defaultValue}` : '输入值'"
            :disabled="entry.isNull"
            size="small"
            @keydown.enter="submit()"
          />
          <NCheckbox v-model:checked="entry.isNull" size="small" class="routine-exec-null-check">
            NULL
          </NCheckbox>
        </div>
      </div>

      <div class="connection-dialog-actions">
        <NButton tertiary @click="emit('update:show', false)">取消</NButton>
        <NButton type="primary" @click="submit()">执行</NButton>
      </div>
    </div>
  </NModal>
</template>

<style scoped lang="scss">
.routine-exec-param {
  margin-bottom: 4px;
}

.routine-exec-param-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 3px;
}

.routine-exec-param-name {
  font-weight: 600;
  font-size: 12px;
}

.routine-exec-param-type {
  font-size: 11px;
  color: #8b95a5;
}

.routine-exec-param-direction {
  font-size: 10px;
  color: #e8a735;
  background: rgba(232, 167, 53, 0.1);
  padding: 0 4px;
  border-radius: 3px;
}

.routine-exec-null-check {
  flex-shrink: 0;
  white-space: nowrap;
}

.routine-exec-param-input {
  display: flex;
  align-items: center;
  gap: 8px;
}
</style>
