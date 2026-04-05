<script setup lang="ts">
import { NCheckbox, NInput, NInputNumber, NTooltip } from 'naive-ui';
import type { MockColumnRule } from '../lib/mock-data-generator';

withDefaults(defineProps<{
  rule: MockColumnRule;
  layout?: 'compact' | 'panel';
}>(), {
  layout: 'compact',
});
</script>

<template>
  <div class="mock-rule-params" :class="{ 'mock-rule-params--panel': layout === 'panel' }">
    <template v-if="rule.ruleType === 'static'">
      <div class="mock-param-field mock-param-field--wide">
        <NInput v-model:value="rule.options.staticValue" size="small" placeholder="固定值" />
      </div>
    </template>
    <template v-else-if="rule.ruleType === 'sequence'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.sequenceStart" size="small" :min="-999999" placeholder="起始" />
          </div>
        </template>
        起始值
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.sequenceStep" size="small" :min="1" placeholder="步长" />
          </div>
        </template>
        步长
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'random-string'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.minLength" size="small" :min="1" :max="200" placeholder="最小" />
          </div>
        </template>
        最小长度
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.maxLength" size="small" :min="1" :max="500" placeholder="最大" />
          </div>
        </template>
        最大长度
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'random-number'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.minNumber" size="small" :placeholder="layout === 'panel' ? '最小值' : undefined" />
          </div>
        </template>
        最小值
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.maxNumber" size="small" :placeholder="layout === 'panel' ? '最大值' : undefined" />
          </div>
        </template>
        最大值
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--short">
            <NInputNumber v-model:value="rule.options.decimalPlaces" size="small" :min="0" :max="6" :placeholder="layout === 'panel' ? '小数位' : undefined" />
          </div>
        </template>
        小数位
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'random-boolean'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.trueProbability" size="small" :min="0" :max="100" placeholder="True %" />
          </div>
        </template>
        True 概率
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'enum'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--wide">
            <NInput v-model:value="rule.options.enumValuesText" size="small" placeholder="a, b, c" />
          </div>
        </template>
        枚举值，使用逗号分隔
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'regex'">
      <div class="mock-param-field mock-param-field--wide">
        <NInput v-model:value="rule.options.regexPattern" size="small" placeholder="例如 [A-Z]{3}-\d{4}" />
      </div>
    </template>
    <template v-else-if="rule.ruleType === 'sql-expression'">
      <div class="mock-param-field mock-param-field--wide">
        <NInput v-model:value="rule.options.sqlExpression" size="small" placeholder="例如 UUID() / NOW() / GETDATE()" />
      </div>
    </template>
    <template v-else-if="rule.ruleType === 'random-past-datetime' || rule.ruleType === 'random-future-datetime'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field">
            <NInputNumber v-model:value="rule.options.daysRange" size="small" :min="1" :max="3650" placeholder="天数" />
          </div>
        </template>
        时间范围（天）
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'hex-color'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-checkbox">
            <NCheckbox :checked="rule.options.includeHashPrefix" @update:checked="(value) => { rule.options.includeHashPrefix = value; }">
              带 # 前缀
            </NCheckbox>
          </div>
        </template>
        是否带 # 前缀
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'version'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--short">
            <NInputNumber v-model:value="rule.options.versionSegmentCount" size="small" :min="2" :max="4" />
          </div>
        </template>
        版本号段数
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <div class="mock-param-checkbox">
            <NCheckbox :checked="rule.options.versionIncludePrefix" @update:checked="(value) => { rule.options.versionIncludePrefix = value; }">
              带 v 前缀
            </NCheckbox>
          </div>
        </template>
        是否带 v 前缀
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'sentence'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--short">
            <NInputNumber v-model:value="rule.options.sentenceWordCount" size="small" :min="2" :max="12" />
          </div>
        </template>
        词数
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'paragraph'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--short">
            <NInputNumber v-model:value="rule.options.paragraphSentenceCount" size="small" :min="2" :max="6" />
          </div>
        </template>
        句数
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'email'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--wide">
            <NInput v-model:value="rule.options.emailDomain" size="small" placeholder="例如 example.com" />
          </div>
        </template>
        自定义邮箱域名，留空则随机生成
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'latitude' || rule.ruleType === 'longitude'">
      <NTooltip>
        <template #trigger>
          <div class="mock-param-field mock-param-field--short">
            <NInputNumber v-model:value="rule.options.coordinateDecimalPlaces" size="small" :min="2" :max="8" />
          </div>
        </template>
        小数位
      </NTooltip>
    </template>
    <template v-else-if="rule.ruleType === 'database-auto'">
      <span class="mock-rule-hint">自动 / 默认</span>
    </template>
    <template v-else-if="rule.ruleType === 'ignore'">
      <span class="mock-rule-hint">已忽略</span>
    </template>
    <template v-else>
      <span class="mock-rule-hint">无需参数</span>
    </template>
  </div>
</template>

<style scoped lang="scss">
.mock-rule-params {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  min-width: 0;
}

.mock-rule-params--panel {
  flex-direction: column;
  align-items: stretch;
}

.mock-rule-params--panel .mock-param-field,
.mock-rule-params--panel .mock-param-field--short,
.mock-rule-params--panel .mock-param-field--wide,
.mock-rule-params--panel .mock-param-checkbox {
  flex: none;
  width: 100%;
  min-width: 0;
}

.mock-param-field {
  flex: 1 1 84px;
  min-width: 0;
}

.mock-param-field--short {
  flex-basis: 68px;
}

.mock-param-field--wide {
  flex: 1 1 220px;
  min-width: 160px;
}

.mock-param-checkbox {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  padding: 0 4px;
}

.mock-rule-hint {
  display: inline-flex;
  align-items: center;
  min-height: 24px;
  font-size: 12px;
  color: var(--n-text-color-disabled);
}

:deep(.mock-param-field .n-input),
:deep(.mock-param-field .n-input-number),
:deep(.mock-param-field .n-base-selection) {
  width: 100%;
}
</style>