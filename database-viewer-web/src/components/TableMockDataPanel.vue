<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  NAlert,
  NButton,
  NDataTable,
  NEmpty,
  NInput,
  NInputNumber,
  NModal,
  NSelect,
  NSpin,
  NTabPane,
  NTabs,
  NText,
  useMessage,
} from 'naive-ui';
import MockRuleParameterFields from './MockRuleParameterFields.vue';
import { useExplorerStore } from '../stores/explorer';
import type { TableColumn, TableMockWorkspaceTab } from '../types/explorer';
import {
  beforeInsertOptions,
  buildDefaultMockPlan,
  createRuleOptionsForType,
  generateRulePreviewExamples,
  generateMockDataset,
  mockDataLocaleOptions,
  mockRuleOptions,
  type GeneratedMockDataset,
  type MockColumnRule,
  type MockDataPlan,
  type MockRuleType,
} from '../lib/mock-data-generator';

const props = defineProps<{
  tab: TableMockWorkspaceTab;
}>();

const store = useExplorerStore();
const message = useMessage();

const loading = ref(false);
const error = ref<string | null>(null);
const plan = ref<MockDataPlan | null>(null);
const previewDataset = ref<GeneratedMockDataset | null>(null);
const generatedDataset = ref<GeneratedMockDataset | null>(null);
const generating = ref(false);
const activeTab = ref<'rules' | 'preview' | 'sql'>('rules');
const previewRowLimit = ref(10);

const table = computed(() => store.getTable(props.tab.tableKey));
const provider = computed(() => store.getConnectionInfo(props.tab.connectionId)?.provider ?? 'sqlserver');
const design = computed(() => store.getTableDesign(props.tab.tableKey));
const designColumns = computed(() => design.value?.columns ?? []);
const previewRows = computed(() => previewDataset.value?.previewRows ?? []);
const rulePickerVisible = ref(false);
const rulePickerSearch = ref('');
const activeRuleCategory = ref<'all' | 'basic' | 'identity' | 'location' | 'network' | 'business' | 'time' | 'advanced'>('all');
const editingRule = ref<MockColumnRule | null>(null);
const recentRuleTypes = ref<MockRuleType[]>([]);
const ruleCatalogSampleMap = ref(new Map<MockRuleType, string>());
const invalidRuleColumns = ref<string[]>([]);
const lastRuleConflictKey = ref<string | null>(null);

const ruleCategoryOptions = [
  { label: '全部', value: 'all' },
  { label: '基础', value: 'basic' },
  { label: '身份与个人', value: 'identity' },
  { label: '位置与地址', value: 'location' },
  { label: '网络与技术', value: 'network' },
  { label: '业务与组织', value: 'business' },
  { label: '时间', value: 'time' },
  { label: '高级', value: 'advanced' },
] as const;

const ruleOptionLabelByValue = new Map(mockRuleOptions.map((option) => [option.value, option.label]));

const mockRuleCatalog = [
  { value: 'database-auto', category: 'advanced', description: '交给数据库默认值或自增逻辑处理', keywords: ['default', 'identity'] },
  { value: 'ignore', category: 'advanced', description: '忽略当前字段，不写入 SQL', keywords: ['skip'] },
  { value: 'static', category: 'basic', description: '始终输出固定值', keywords: ['constant'], sample: '固定值' },
  { value: 'sequence', category: 'basic', description: '按起始值和步长递增', keywords: ['increment', 'serial'], sample: '1001, 1002, 1003' },
  { value: 'random-string', category: 'basic', description: '按长度生成随机文本，并带语义推断', keywords: ['string', 'text'], sample: 'A9K72T' },
  { value: 'random-number', category: 'basic', description: '按范围生成随机数字', keywords: ['number', 'float', 'int'], sample: '128.52' },
  { value: 'random-boolean', category: 'basic', description: '按概率生成 true / false', keywords: ['bool', 'flag'], sample: 'true' },
  { value: 'enum', category: 'basic', description: '从枚举候选值中随机选择', keywords: ['status', 'type'], sample: 'ACTIVE' },
  { value: 'name-localized', category: 'identity', description: '按当前数据语言生成本地化姓名', keywords: ['name', 'locale'], sample: 'Akira Sato / 张伟' },
  { value: 'name-zh', category: 'identity', description: '固定生成中文姓名', keywords: ['chinese', 'name'], sample: '张伟' },
  { value: 'name-en', category: 'identity', description: '固定生成英文姓名', keywords: ['english', 'name'], sample: 'Olivia Carter' },
  { value: 'username', category: 'identity', description: '生成用户名 / 登录名', keywords: ['login', 'account'], sample: 'olivia.carter' },
  { value: 'phone', category: 'identity', description: '按当前数据语言生成手机号或电话号码', keywords: ['mobile', 'tel'], sample: '+1-415-555-0186' },
  { value: 'email', category: 'identity', description: '生成邮箱地址', keywords: ['mail'], sample: 'olivia@example.com' },
  { value: 'id-card', category: 'identity', description: '生成证件号，中文环境下带校验位', keywords: ['identity'], sample: '11010119940307682X' },
  { value: 'hk-id-card', category: 'identity', description: '生成香港身份证号', keywords: ['identity', 'hong kong'], sample: 'Z123456(7)' },
  { value: 'country', category: 'location', description: '生成国家名称', keywords: ['nation'], sample: 'Germany / 中国' },
  { value: 'province', category: 'location', description: '生成省份 / 州', keywords: ['state', 'region'], sample: 'California / 浙江省' },
  { value: 'city', category: 'location', description: '生成城市', keywords: ['town'], sample: 'Paris / 杭州' },
  { value: 'address', category: 'location', description: '生成完整地址', keywords: ['street', 'location'], sample: '221B Baker Street' },
  { value: 'postal-code', category: 'location', description: '生成邮编 / 邮政编码', keywords: ['zip', 'postcode'], sample: '100000' },
  { value: 'latitude', category: 'location', description: '生成纬度', keywords: ['lat'], sample: '31.230416' },
  { value: 'longitude', category: 'location', description: '生成经度', keywords: ['lng', 'lon'], sample: '121.473701' },
  { value: 'ip', category: 'network', description: '生成通用 IP 地址', keywords: ['network'], sample: '192.168.1.10' },
  { value: 'ipv4', category: 'network', description: '生成 IPv4 地址', keywords: ['network'], sample: '10.24.18.5' },
  { value: 'ipv4-cidr', category: 'network', description: '生成 IPv4 CIDR 段', keywords: ['subnet', 'cidr'], sample: '10.24.18.0/24' },
  { value: 'ipv6', category: 'network', description: '生成 IPv6 地址', keywords: ['network'], sample: '2001:db8::8a2e:370:7334' },
  { value: 'mac-address', category: 'network', description: '生成 MAC 地址', keywords: ['hardware'], sample: '00:1A:2B:3C:4D:5E' },
  { value: 'domain', category: 'network', description: '生成域名', keywords: ['host', 'dns'], sample: 'example.net' },
  { value: 'url', category: 'network', description: '生成 URL 链接', keywords: ['link', 'http'], sample: 'https://example.net/demo' },
  { value: 'md5', category: 'network', description: '生成 MD5 摘要字符串', keywords: ['hash'], sample: '5d41402abc4b2a76b9719d911017c592' },
  { value: 'sha1', category: 'network', description: '生成 SHA1 摘要字符串', keywords: ['hash'], sample: '2fd4e1c67a2d28fced849ee1bb76e7391b93eb12' },
  { value: 'sha256', category: 'network', description: '生成 SHA256 摘要字符串', keywords: ['hash'], sample: '9f86d081884c7d659a2feaa0c55ad015...' },
  { value: 'json', category: 'network', description: '生成 JSON 对象字符串', keywords: ['payload', 'meta'], sample: '{"code":"A1B2C3D4"}' },
  { value: 'imei', category: 'network', description: '生成 15 位 IMEI', keywords: ['device'], sample: '490154203237518' },
  { value: 'imsi', category: 'network', description: '生成 IMSI', keywords: ['sim', 'device'], sample: '460012345678901' },
  { value: 'vehicle-plate', category: 'network', description: '生成车牌号', keywords: ['plate', 'car'], sample: '沪A12345' },
  { value: 'bank-account', category: 'business', description: '生成银行账号', keywords: ['finance'], sample: '6222021234567890123' },
  { value: 'swift-bic', category: 'business', description: '生成 SWIFT / BIC 代码', keywords: ['bank', 'finance'], sample: 'DEUTDEFF500' },
  { value: 'currency-code', category: 'business', description: '生成 ISO 币种代码', keywords: ['money'], sample: 'USD' },
  { value: 'department', category: 'business', description: '生成部门名称', keywords: ['team', 'group'], sample: 'Engineering' },
  { value: 'barcode', category: 'business', description: '生成条码风格编号', keywords: ['code'], sample: 'BAR240405000123' },
  { value: 'tracking-number', category: 'business', description: '生成物流单号', keywords: ['shipment', 'waybill'], sample: 'TRK240405000123' },
  { value: 'sentence', category: 'business', description: '生成短句文本', keywords: ['summary', 'title'], sample: 'System generated note.' },
  { value: 'paragraph', category: 'business', description: '生成段落文本', keywords: ['remark', 'content'], sample: 'This is a generated paragraph.' },
  { value: 'first-name', category: 'identity', description: '生成名字', keywords: ['given name', 'personal'], sample: 'Olivia' },
  { value: 'last-name', category: 'identity', description: '生成姓氏', keywords: ['surname', 'family name'], sample: 'Carter' },
  { value: 'gender', category: 'identity', description: '生成性别文本', keywords: ['sex'], sample: 'Female' },
  { value: 'ssn', category: 'identity', description: '生成美国 SSN', keywords: ['social security'], sample: '684-62-5799' },
  { value: 'country-code', category: 'location', description: '生成国家代码', keywords: ['iso', 'country'], sample: 'US' },
  { value: 'state-abbr', category: 'location', description: '生成州 / 省缩写代码', keywords: ['region', 'abbr'], sample: 'CA' },
  { value: 'time-zone', category: 'location', description: '生成时区标识', keywords: ['timezone', 'tz'], sample: 'Asia/Shanghai' },
  { value: 'flight-number', category: 'business', description: '生成航班号', keywords: ['travel', 'airline'], sample: 'AA1234' },
  { value: 'hospital-npi', category: 'business', description: '生成医院 NPI', keywords: ['health', 'hospital'], sample: '1234567890' },
  { value: 'icd10-code', category: 'business', description: '生成 ICD10 诊断编码', keywords: ['health', 'diagnosis'], sample: 'E11.9' },
  { value: 'currency-name', category: 'business', description: '生成币种名称', keywords: ['money', 'finance'], sample: 'Dollar' },
  { value: 'credit-card-type', category: 'business', description: '生成信用卡类型', keywords: ['finance', 'card'], sample: 'visa' },
  { value: 'stock-symbol', category: 'business', description: '生成股票代码', keywords: ['ticker', 'market'], sample: 'MSFT' },
  { value: 'airport-code', category: 'business', description: '生成机场代码', keywords: ['travel', 'iata'], sample: 'LAX' },
  { value: 'airport-name', category: 'business', description: '生成机场名称', keywords: ['travel', 'airport'], sample: 'Tokyo Narita Airport' },
  { value: 'airline-code', category: 'business', description: '生成航空公司代码', keywords: ['travel', 'airline'], sample: 'AA' },
  { value: 'airline-name', category: 'business', description: '生成航空公司名称', keywords: ['travel', 'airline'], sample: 'American Airlines' },
  { value: 'drug-company', category: 'business', description: '生成药企名称', keywords: ['health', 'pharma'], sample: 'Novartis' },
  { value: 'drug-name', category: 'business', description: '生成药品名称', keywords: ['health', 'medicine'], sample: 'Lipitor' },
  { value: 'hospital-name', category: 'business', description: '生成医院名称', keywords: ['health', 'hospital'], sample: 'Mayo Clinic' },
  { value: 'nhs-number', category: 'business', description: '生成 NHS Number', keywords: ['health', 'patient'], sample: '943 476 5919' },
  { value: 'iban', category: 'business', description: '生成 IBAN 银行账户号', keywords: ['finance', 'bank'], sample: 'DE89 3704 0044 0532 0130 00' },
  { value: 'product-category', category: 'business', description: '生成产品分类', keywords: ['commerce', 'product'], sample: 'Electronics' },
  { value: 'product-description', category: 'business', description: '生成产品描述', keywords: ['commerce', 'product'], sample: 'Compact travel adapter with fast charging support.' },
  { value: 'stock-market', category: 'business', description: '生成股票市场 / 交易所', keywords: ['finance', 'exchange'], sample: 'NASDAQ' },
  { value: 'language', category: 'identity', description: '生成语言名称', keywords: ['personal', 'locale'], sample: 'English' },
  { value: 'language-code', category: 'identity', description: '生成语言代码', keywords: ['locale', 'code'], sample: 'en' },
  { value: 'university', category: 'identity', description: '生成大学名称', keywords: ['education', 'school'], sample: 'University of Texas' },
  { value: 'airport-country-code', category: 'business', description: '生成机场国家代码', keywords: ['travel', 'airport'], sample: 'US' },
  { value: 'flight-duration-hours', category: 'business', description: '生成飞行时长（小时）', keywords: ['travel', 'duration'], sample: '5.75' },
  { value: 'money', category: 'business', description: '生成金额数值', keywords: ['finance', 'amount'], sample: '129.94' },
  { value: 'product-price', category: 'business', description: '生成产品价格', keywords: ['commerce', 'price'], sample: '59.99' },
  { value: 'mime-type', category: 'network', description: '生成 MIME / Content-Type', keywords: ['content type', 'http'], sample: 'application/json' },
  { value: 'file-extension', category: 'business', description: '生成文件扩展名', keywords: ['file', 'extension'], sample: 'png' },
  { value: 'port-number', category: 'network', description: '生成端口号', keywords: ['tcp', 'http', 'port'], sample: '443' },
  { value: 'http-status-code', category: 'network', description: '生成 HTTP 状态码', keywords: ['status', 'http'], sample: '200' },
  { value: 'company', category: 'business', description: '生成公司 / 组织名称', keywords: ['org', 'tenant'], sample: 'Acme Manufacturing GmbH' },
  { value: 'job-title', category: 'business', description: '生成职位名称', keywords: ['position', 'title'], sample: 'Senior Process Engineer' },
  { value: 'bank-card', category: 'business', description: '生成银行卡号', keywords: ['finance'], sample: '6225881234567890' },
  { value: 'uuid', category: 'business', description: '生成 UUID / GUID', keywords: ['guid'], sample: '550e8400-e29b-41d4-a716-446655440000' },
  { value: 'file-name', category: 'business', description: '生成文件名', keywords: ['file'], sample: 'report-2026-04.csv' },
  { value: 'version', category: 'business', description: '生成版本号', keywords: ['build', 'firmware'], sample: 'v2.14.37' },
  { value: 'hex-color', category: 'business', description: '生成十六进制颜色值', keywords: ['color'], sample: '#3FA9F5' },
  { value: 'current-datetime', category: 'time', description: '写入数据库当前时间表达式，预览显示当前时间', keywords: ['now'], sample: 'CURRENT_TIMESTAMP' },
  { value: 'random-past-datetime', category: 'time', description: '生成过去时间', keywords: ['history'], sample: '2026-03-18 09:25:31' },
  { value: 'random-future-datetime', category: 'time', description: '生成未来时间', keywords: ['future', 'expire'], sample: '2026-06-30 18:40:12' },
  { value: 'regex', category: 'advanced', description: '按正则表达式生成值', keywords: ['pattern'], sample: 'ABC-1024' },
  { value: 'sql-expression', category: 'advanced', description: '直接写 SQL 表达式', keywords: ['sql'], sample: 'UUID() / GETDATE()' },
] satisfies Array<{ value: MockRuleType; category: 'basic' | 'identity' | 'location' | 'network' | 'business' | 'time' | 'advanced'; description: string; keywords: string[]; sample?: string }>;

const ruleCategoryCountMap = computed(() => mockRuleCatalog.reduce((result, item) => {
  result[item.category] = (result[item.category] ?? 0) + 1;
  return result;
}, { basic: 0, identity: 0, location: 0, network: 0, business: 0, time: 0, advanced: 0 } as Record<'basic' | 'identity' | 'location' | 'network' | 'business' | 'time' | 'advanced', number>));

const selectedRuleCatalogItem = computed(() => {
  if (!editingRule.value) {
    return null;
  }

  return mockRuleCatalog.find((item) => item.value === editingRule.value?.ruleType) ?? null;
});

const recentRuleCatalog = computed(() => recentRuleTypes.value
  .map((ruleType) => mockRuleCatalog.find((item) => item.value === ruleType) ?? null)
  .filter((item): item is NonNullable<typeof item> => item !== null));

const selectedRulePreviewSamples = computed(() => {
  if (!plan.value || !editingRule.value) {
    return [] as string[];
  }

  try {
    return generateRulePreviewExamples(plan.value, editingRule.value, 3);
  } catch {
    return [] as string[];
  }
});

const selectedRulePrimarySample = computed(() => {
  return selectedRulePreviewSamples.value[0] ?? selectedRuleCatalogItem.value?.sample ?? null;
});

const filteredRuleCatalog = computed(() => {
  const searchText = rulePickerSearch.value.trim().toLowerCase();

  return mockRuleCatalog.filter((item) => {
    if (activeRuleCategory.value !== 'all' && item.category !== activeRuleCategory.value) {
      return false;
    }

    if (!searchText) {
      return true;
    }

    const label = ruleOptionLabelByValue.get(item.value)?.toLowerCase() ?? '';
    return label.includes(searchText)
      || item.description.toLowerCase().includes(searchText)
      || item.keywords.some((keyword) => keyword.toLowerCase().includes(searchText));
  });
});

function getMockRuleLabel(ruleType: MockRuleType): string {
  return ruleOptionLabelByValue.get(ruleType) ?? ruleType;
}

function isRuleColumnInvalid(columnName: string): boolean {
  return invalidRuleColumns.value.includes(columnName);
}

function extractUniqueConstraintColumnName(errorMessage: string): string | null {
  const matched = errorMessage.match(/^字段\s+(.+?)\s+的生成数据违反唯一约束/);
  return matched?.[1] ?? null;
}

function clearRuleConflictState(columnName?: string): void {
  if (!columnName) {
    invalidRuleColumns.value = [];
    lastRuleConflictKey.value = null;
    return;
  }

  invalidRuleColumns.value = invalidRuleColumns.value.filter((item) => item !== columnName);
  if (!invalidRuleColumns.value.length) {
    lastRuleConflictKey.value = null;
  }
}

function handleRuleGenerationConflict(errorMessage: string): boolean {
  const conflictedColumnName = extractUniqueConstraintColumnName(errorMessage);
  if (!conflictedColumnName) {
    return false;
  }

  invalidRuleColumns.value = [conflictedColumnName];

  if (lastRuleConflictKey.value !== errorMessage) {
    store.showNotice('warning', errorMessage);
    lastRuleConflictKey.value = errorMessage;
  }

  return true;
}

function getRuleCatalogSample(ruleType: MockRuleType, fallbackSample?: string): string | null {
  return ruleCatalogSampleMap.value.get(ruleType) ?? fallbackSample ?? null;
}

function buildRulePickerSourceColumn(rule: MockColumnRule): TableColumn {
  return designColumns.value.find((column) => column.name === rule.columnName)
    ?? design.value?.columns.find((column) => column.name === rule.columnName)
    ?? {
      name: rule.columnName,
      type: rule.columnType,
      nullable: rule.nullable,
      isPrimaryKey: false,
      isIdentity: false,
      hasDefaultValue: false,
      defaultValue: null,
      comment: rule.comment,
      maxLength: null,
      precision: null,
      scale: null,
      isForeignKey: false,
      foreignKeyReference: null,
    } as TableColumn;
}

function refreshRuleCatalogSamples(): void {
  if (!plan.value || !editingRule.value) {
    ruleCatalogSampleMap.value = new Map<MockRuleType, string>();
    return;
  }

  const sourceColumn = buildRulePickerSourceColumn(editingRule.value);
  const nextSampleMap = new Map<MockRuleType, string>();

  mockRuleCatalog.forEach((item) => {
    const previewRule: MockColumnRule = {
      ...editingRule.value as MockColumnRule,
      ruleType: item.value,
      unique: false,
      nullable: false,
      generatedByDatabase: false,
      options: createRuleOptionsForType(sourceColumn, item.value),
    };

    try {
      const sample = generateRulePreviewExamples(plan.value as MockDataPlan, previewRule, 1)[0];
      if (sample) {
        nextSampleMap.set(item.value, sample);
        return;
      }
    } catch {
      // Fall back to static samples when dynamic generation is unavailable.
    }

    if (item.sample) {
      nextSampleMap.set(item.value, item.sample);
    }
  });

  ruleCatalogSampleMap.value = nextSampleMap;
}

function openRulePicker(rule: MockColumnRule): void {
  editingRule.value = rule;
  rulePickerVisible.value = true;
  rulePickerSearch.value = '';
  activeRuleCategory.value = 'all';
  refreshRuleCatalogSamples();
}

function closeRulePicker(): void {
  rulePickerVisible.value = false;
}

function handleRulePickerAfterLeave(): void {
  editingRule.value = null;
  rulePickerSearch.value = '';
  activeRuleCategory.value = 'all';
  ruleCatalogSampleMap.value = new Map<MockRuleType, string>();
}

function selectRuleTypeFromPicker(ruleType: MockRuleType): void {
  if (!editingRule.value) {
    return;
  }

  updateRuleType(editingRule.value, ruleType);

  recentRuleTypes.value = [ruleType, ...recentRuleTypes.value.filter((value) => value !== ruleType)].slice(0, 8);
}

function confirmRuleTypeFromPicker(ruleType: MockRuleType): void {
  selectRuleTypeFromPicker(ruleType);
  closeRulePicker();
}

function clampPreviewColumnWidth(value: number, minWidth: number, maxWidth: number): number {
  return Math.min(maxWidth, Math.max(minWidth, value));
}

function getPreviewColumnWidthConfig(dataType: string): { minWidth: number; maxWidth: number; charWidth: number } {
  if (/int|decimal|numeric|float|double|real|money|bit/.test(dataType)) {
    return {
      minWidth: 72,
      maxWidth: 104,
      charWidth: 7.2,
    };
  }

  if (/date|time/.test(dataType)) {
    return {
      minWidth: 96,
      maxWidth: 136,
      charWidth: 7,
    };
  }

  if (/uuid|uniqueidentifier/.test(dataType)) {
    return {
      minWidth: 108,
      maxWidth: 156,
      charWidth: 6.8,
    };
  }

  if (/char|text|json|xml|binary|image/.test(dataType)) {
    return {
      minWidth: 84,
      maxWidth: 168,
      charWidth: 6.4,
    };
  }

  return {
    minWidth: 84,
    maxWidth: 128,
    charWidth: 6.6,
  };
}

function formatPreviewCellValue(value: unknown): string {
  if (value === null || value === undefined) {
    return 'NULL';
  }

  if (typeof value === 'object') {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }

  return String(value);
}

const previewColumns = computed(() => Object.keys(previewRows.value[0] ?? {}).map((key) => {
  const designColumn = getDesignColumn(key);
  const dataType = (designColumn?.type ?? '').toLowerCase();
  const { minWidth, maxWidth, charWidth } = getPreviewColumnWidthConfig(dataType);
  const contentLength = previewRows.value.reduce((maxLength, row) => {
    return Math.max(maxLength, formatPreviewCellValue(row[key]).length);
  }, key.length);
  // Estimate an initial width from the current preview payload so columns stay compact until resized manually.
  const width = clampPreviewColumnWidth(Math.ceil((contentLength * charWidth) + 22), minWidth, maxWidth);

  return {
    title: key,
    key,
    ellipsis: {
      tooltip: true,
    },
    width,
    minWidth,
    maxWidth,
    resizable: true,
  };
}));
const previewScrollX = computed(() => previewColumns.value.reduce((totalWidth, column) => totalWidth + Number(column.width ?? column.minWidth ?? 84), 0));
const hasForeignKeys = computed(() => (design.value?.foreignKeys.length ?? 0) > 0);

type SaveFilePickerWindow = Window & {
  showSaveFilePicker?: (options?: {
    suggestedName?: string;
    types?: Array<{
      description?: string;
      accept: Record<string, string[]>;
    }>;
  }) => Promise<{
    createWritable: () => Promise<{
      write: (data: Blob | string) => Promise<void>;
      close: () => Promise<void>;
    }>;
  }>;
};

function getDesignColumn(columnName: string): TableColumn | undefined {
  return designColumns.value.find((column) => column.name === columnName);
}

async function initializePlan(): Promise<void> {
  if (!table.value) {
    error.value = '目标表不存在。';
    return;
  }

  loading.value = true;
  error.value = null;
  try {
    const loadedDesign = await store.ensureTableDesignLoaded(props.tab.tableKey);
    if (!loadedDesign) {
      throw new Error('表结构加载失败。');
    }

    plan.value = buildDefaultMockPlan({
      provider: provider.value,
      connectionId: props.tab.connectionId,
      database: props.tab.database,
      table: table.value,
      design: loadedDesign,
    });
    generatedDataset.value = null;
    activeTab.value = 'rules';
    refreshPreview();
  } catch (loadError) {
    error.value = loadError instanceof Error ? loadError.message : 'Mock 数据配置初始化失败';
  } finally {
    loading.value = false;
  }
}

function refreshPreview(): void {
  if (!plan.value) {
    previewDataset.value = null;
    return;
  }

  try {
    previewDataset.value = generateMockDataset({
      ...plan.value,
      rowCount: Math.min(previewRowLimit.value, Math.max(1, plan.value.rowCount)),
      previewRowLimit: previewRowLimit.value,
    });
    clearRuleConflictState();
  } catch (previewError) {
    previewDataset.value = null;
    const previewErrorMessage = previewError instanceof Error ? previewError.message : '预览数据生成失败';
    if (!handleRuleGenerationConflict(previewErrorMessage)) {
      error.value = previewErrorMessage;
    } else {
      error.value = null;
    }
  }
}

async function ensureGeneratedDataset(): Promise<GeneratedMockDataset | null> {
  if (!plan.value) {
    return null;
  }

  generating.value = true;
  error.value = null;
  try {
    generatedDataset.value = generateMockDataset(plan.value);
    activeTab.value = 'sql';
    clearRuleConflictState();
    return generatedDataset.value;
  } catch (generationError) {
    const generationErrorMessage = generationError instanceof Error ? generationError.message : '生成 Mock SQL 失败';
    if (!handleRuleGenerationConflict(generationErrorMessage)) {
      error.value = generationErrorMessage;
    } else {
      error.value = null;
    }
    return null;
  } finally {
    generating.value = false;
  }
}

function updateRuleType(rule: MockColumnRule, nextType: MockRuleType): void {
  const designColumn = getDesignColumn(rule.columnName);
  if (!designColumn) {
    return;
  }

  rule.ruleType = nextType;
  rule.options = createRuleOptionsForType(designColumn, nextType);
  clearRuleConflictState(rule.columnName);
}

function downloadTextFile(fileName: string, mimeType: string, text: string): void {
  const blob = new Blob([text], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

function requireGeneratedDataset(actionLabel: string): GeneratedMockDataset | null {
  if (!generatedDataset.value) {
    message.warning(`请先点击“生成 SQL”，再执行${actionLabel}`);
    return null;
  }

  return generatedDataset.value;
}

async function saveTextOutput(fileName: string, mimeType: string, text: string): Promise<void> {
  const hasWebViewHost = !!(window as typeof window & { chrome?: { webview?: { postMessage: (message: unknown) => void } } }).chrome?.webview;
  if (hasWebViewHost) {
    const result = await store.saveTextFile(fileName, text);
    if (!result.canceled) {
      message.success('文件已保存');
    }
    return;
  }

  const pickerWindow = window as SaveFilePickerWindow;
  if (pickerWindow.showSaveFilePicker) {
    const handle = await pickerWindow.showSaveFilePicker({
      suggestedName: fileName,
      types: [
        {
          description: fileName.endsWith('.json') ? 'JSON 文件' : fileName.endsWith('.csv') ? 'CSV 文件' : 'SQL 文件',
          accept: {
            [mimeType.split(';')[0] ?? 'text/plain']: [fileName.slice(fileName.lastIndexOf('.')) || '.txt'],
          },
        },
      ],
    });
    const writable = await handle.createWritable();
    await writable.write(text);
    await writable.close();
    message.success('文件已保存');
    return;
  }

  downloadTextFile(fileName, mimeType, text);
}

async function copySql(): Promise<void> {
  const dataset = requireGeneratedDataset('复制 SQL');
  if (!dataset) {
    return;
  }

  await navigator.clipboard.writeText(dataset.sql);
  message.success('Mock SQL 已复制到剪贴板');
}

async function exportSql(): Promise<void> {
  const dataset = requireGeneratedDataset('导出 SQL');
  if (!dataset || !table.value) {
    return;
  }

  await saveTextOutput(`${table.value.name}-mock.sql`, 'application/sql;charset=utf-8', dataset.sql);
}

async function exportCsv(): Promise<void> {
  const dataset = requireGeneratedDataset('导出 CSV');
  if (!dataset || !table.value) {
    return;
  }

  await saveTextOutput(`${table.value.name}-mock.csv`, 'text/csv;charset=utf-8', dataset.csv);
}

async function exportJson(): Promise<void> {
  const dataset = requireGeneratedDataset('导出 JSON');
  if (!dataset || !table.value) {
    return;
  }

  await saveTextOutput(`${table.value.name}-mock.json`, 'application/json;charset=utf-8', dataset.json);
}

watch(
  [() => props.tab.id, () => table.value?.key ?? null],
  ([nextTabId, nextTableKey], [previousTabId, previousTableKey]) => {
    if (!nextTableKey) {
      if (nextTabId !== previousTabId) {
        plan.value = null;
        previewDataset.value = null;
        generatedDataset.value = null;
        error.value = null;
      }

      return;
    }

    if (loading.value) {
      return;
    }

    if (nextTabId !== previousTabId || nextTableKey !== previousTableKey || !plan.value) {
      void initializePlan();
    }
  },
  { immediate: true },
);

watch(() => plan.value, () => {
  if (!plan.value) {
    return;
  }

  generatedDataset.value = null;
  refreshPreview();
}, { deep: true });

watch(() => plan.value?.locale, () => {
  if (!rulePickerVisible.value) {
    return;
  }

  refreshRuleCatalogSamples();
});

watch(() => previewRowLimit.value, () => {
  if (!plan.value) {
    return;
  }

  refreshPreview();
});
</script>

<template>
  <div class="mock-panel-shell">
    <NSpin :show="loading || generating">
      <div v-if="plan && table" class="mock-panel-content">
        <div class="mock-panel-header">
          <div>
            <div class="mock-panel-title">数据生成 <span class="mock-panel-title-meta">{{ table.schema ? `${table.schema}.` : '' }}{{ table.name }} · {{ props.tab.database }}</span></div>
          </div>
          <div class="mock-panel-header-actions">
            <NButton size="small" tertiary @click="refreshPreview">刷新预览</NButton>
            <NButton size="small" type="primary" @click="ensureGeneratedDataset">生成 SQL</NButton>
            <NButton size="small" tertiary @click="copySql">复制 SQL</NButton>
            <NButton size="small" tertiary @click="exportSql">导出 SQL</NButton>
            <NButton size="small" tertiary @click="exportCsv">导出 CSV</NButton>
            <NButton size="small" tertiary @click="exportJson">导出 JSON</NButton>
          </div>
        </div>

        <NAlert v-if="hasForeignKeys" type="warning" :show-icon="false">
          当前表存在外键约束。V1 版本不会自动级联生成外键数据，请对外键字段使用固定值、枚举或 SQL 表达式手动控制。
        </NAlert>
        <NAlert v-if="error" type="error" :show-icon="false">{{ error }}</NAlert>
        <NAlert v-for="warning in generatedDataset?.warnings ?? previewDataset?.warnings ?? []" :key="warning" type="warning" :show-icon="false">
          {{ warning }}
        </NAlert>

        <div class="mock-global-grid">
          <label class="mock-field">
            <span class="mock-field-label">生成行数</span>
            <NInputNumber v-model:value="plan.rowCount" size="small" :min="1" :max="100000" />
          </label>
          <label class="mock-field">
            <span class="mock-field-label">数据语言</span>
            <NSelect v-model:value="plan.locale" size="small" :options="mockDataLocaleOptions" />
          </label>
          <label class="mock-field">
            <span class="mock-field-label">插入前策略</span>
            <NSelect v-model:value="plan.beforeInsert" size="small" :options="beforeInsertOptions" />
          </label>
        </div>

        <NTabs v-model:value="activeTab" type="line" animated>
          <NTabPane name="rules" tab="字段规则">
            <div class="mock-tab-body mock-tab-body-scroll">
            <div class="mock-rule-table">
              <div class="mock-rule-head">
                <span>字段</span>
                <span>规则类型</span>
                <span>规则参数</span>
                <span>NULL 概率</span>
              </div>
              <div v-for="rule in plan.columns" :key="rule.columnName" class="mock-rule-row">
                <div class="mock-rule-column-meta">
                  <div class="mock-rule-column-inline">
                    <span class="mock-rule-column-name" style="font-size: 11px;">{{ rule.columnName }}</span>
                    <span class="mock-rule-column-type">{{ rule.displayType }}</span>
                    <span v-if="rule.unique" class="mock-flag">唯一</span>
                    <span v-if="rule.generatedByDatabase" class="mock-flag">数据库生成</span>
                    <span v-if="rule.nullable" class="mock-flag">可空</span>
                  </div>
                </div>

                <div class="mock-rule-type-cell">
                  <NButton class="mock-rule-picker-trigger" :class="{ 'is-invalid': isRuleColumnInvalid(rule.columnName) }" size="tiny" tertiary @click="openRulePicker(rule)">
                    {{ getMockRuleLabel(rule.ruleType) }}
                  </NButton>
                </div>

                <MockRuleParameterFields :rule="rule" />

                <div>
                  <NInputNumber v-model:value="rule.options.nullProbability" size="small" :min="0" :max="100" :disabled="!rule.nullable" placeholder="0-100" />
                </div>
              </div>
            </div>
            </div>
          </NTabPane>

          <NTabPane name="preview" tab="预览数据">
            <div class="mock-tab-body">
              <div class="mock-preview-toolbar">
                <div class="mock-preview-toolbar-info">
                  <NText depth="3">预览行数可调，默认 10 行，用于快速检查规则。</NText>
                  <div class="mock-preview-toolbar-controls">
                    <span class="mock-preview-toolbar-label">预览行数</span>
                    <NInputNumber v-model:value="previewRowLimit" size="small" :min="1" :max="50" />
                  </div>
                </div>
                <NButton size="small" tertiary @click="refreshPreview">重新生成预览</NButton>
              </div>
              <div v-if="previewRows.length" class="mock-preview-table-wrap">
                <NDataTable class="mock-preview-table" :columns="previewColumns" :data="previewRows" size="small" :pagination="false" :scroll-x="previewScrollX" />
              </div>
              <NEmpty v-else description="暂无可预览的数据，先检查字段规则是否全部被忽略。" />
            </div>
          </NTabPane>

          <NTabPane name="sql" tab="SQL 预览">
            <div class="mock-tab-body">
              <pre class="mock-sql-preview">{{ generatedDataset?.sql ?? '点击“生成 SQL”后在这里预览完整 INSERT 语句。' }}</pre>
            </div>
          </NTabPane>
        </NTabs>

        <NModal v-model:show="rulePickerVisible" class="mock-rule-picker-modal" :mask-closable="true" @close="closeRulePicker" @after-leave="handleRulePickerAfterLeave">
          <div class="mock-rule-picker-shell">
            <div class="mock-rule-picker-header">
              <div class="mock-rule-picker-dialog-title">选择规则类型</div>
              <NButton class="mock-rule-picker-close-button" size="small" tertiary @click="closeRulePicker">关闭</NButton>
            </div>

            <div class="mock-rule-picker-toolbar">
              <NInput v-model:value="rulePickerSearch" size="small" placeholder="搜索类型、描述或关键字" @keydown.enter.prevent="filteredRuleCatalog[0] && selectRuleTypeFromPicker(filteredRuleCatalog[0].value)" />
            </div>

            <div class="mock-rule-picker-layout">
            <aside class="mock-rule-picker-sidebar">
              <button
                v-for="category in ruleCategoryOptions"
                :key="category.value"
                type="button"
                class="mock-rule-category-button"
                :class="{ 'is-active': activeRuleCategory === category.value }"
                @click="activeRuleCategory = category.value"
              >
                {{ category.label }}
                <span v-if="category.value !== 'all'" class="mock-rule-category-count">{{ ruleCategoryCountMap[category.value] }}</span>
              </button>
            </aside>

            <section class="mock-rule-picker-main">
              <div class="mock-rule-picker-recent">
                <span class="mock-rule-picker-section-title">最近使用</span>
                <div class="mock-rule-picker-recent-list" :class="{ 'is-empty': !recentRuleCatalog.length }">
                  <button
                    v-if="recentRuleCatalog.length"
                    v-for="item in recentRuleCatalog"
                    :key="item.value"
                    type="button"
                    class="mock-rule-picker-chip"
                    @click="selectRuleTypeFromPicker(item.value)"
                    @dblclick="confirmRuleTypeFromPicker(item.value)"
                  >
                    {{ getMockRuleLabel(item.value) }}
                  </button>

                  <span v-else class="mock-rule-picker-empty-hint">选择规则后会记录到这里</span>
                </div>
              </div>

              <div v-if="filteredRuleCatalog.length" class="mock-rule-picker-grid">
                <button
                  v-for="item in filteredRuleCatalog"
                  :key="item.value"
                  type="button"
                  class="mock-rule-picker-card"
                  :class="{ 'is-selected': editingRule?.ruleType === item.value }"
                  @click="selectRuleTypeFromPicker(item.value)"
                  @dblclick="confirmRuleTypeFromPicker(item.value)"
                >
                  <span class="mock-rule-picker-card-title">{{ getMockRuleLabel(item.value) }}</span>
                  <span class="mock-rule-picker-card-desc">{{ item.description }}</span>
                  <span v-if="getRuleCatalogSample(item.value, item.sample)" class="mock-rule-picker-card-sample">{{ getRuleCatalogSample(item.value, item.sample) }}</span>
                </button>
              </div>
              <NEmpty v-else description="没有匹配的规则类型。" />
            </section>

            <section v-if="editingRule" class="mock-rule-picker-config">
              <div class="mock-rule-picker-config-head">
                <div class="mock-rule-picker-config-title">参数配置</div>
                <div class="mock-rule-picker-config-subtitle">{{ getMockRuleLabel(editingRule.ruleType) }}</div>
              </div>

              <div class="mock-rule-picker-config-body">
                <div v-if="selectedRuleCatalogItem" class="mock-rule-picker-config-desc">{{ selectedRuleCatalogItem.description }}</div>
                <div v-if="selectedRulePrimarySample" class="mock-rule-picker-config-sample">当前示例: {{ selectedRulePrimarySample }}</div>
                <div v-if="selectedRulePreviewSamples.length" class="mock-rule-picker-live-preview">
                  <span class="mock-rule-picker-section-title">即时预览</span>
                  <div class="mock-rule-picker-preview-list">
                    <span v-for="(sample, sampleIndex) in selectedRulePreviewSamples" :key="`${sampleIndex}-${sample}`" class="mock-rule-picker-preview-item">{{ sample }}</span>
                  </div>
                </div>
                <MockRuleParameterFields :rule="editingRule" layout="panel" />
              </div>
            </section>
            </div>
          </div>
        </NModal>
      </div>
      <NEmpty v-else-if="!loading" description="当前表不存在，无法生成 Mock 数据。" />
    </NSpin>
  </div>
</template>

<style scoped lang="scss">
.mock-panel-shell {
  display: flex;
  flex-direction: column;
  gap: $gap-md;
  height: 100%;
  min-height: 0;
  padding: 12px;
  background: $color-surface-white;
}

.mock-panel-content {
  display: flex;
  flex-direction: column;
  gap: $gap-md;
  flex: 1 1 auto;
  min-height: 0;
}

.mock-tab-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.mock-tab-body-scroll {
  overflow-x: hidden;
  overflow-y: auto;
}

.mock-tab-body.mock-tab-body-scroll {
  overflow-y: auto;
}

.mock-panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $gap-md;
}

.mock-panel-title {
  font-size: $font-size-xl;
  font-weight: 800;
  color: $color-text-heading;
}

.mock-panel-title-meta {
  font-size: $font-size-sm;
  font-weight: 700;
  color: $color-text-secondary;
}

.mock-panel-header-actions,
.mock-inline-actions {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.mock-global-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 220px));
  gap: 10px;
}

.mock-field {
  display: flex;
  flex-direction: column;
  gap: $gap-xs;
}

.mock-field-label {
  font-size: $font-size-sm;
  font-weight: 700;
  color: $color-text-secondary;
}

.mock-rule-table {
  display: flex;
  flex: none;
  flex-direction: column;
  min-height: max-content;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
  overflow: visible;
}

.mock-rule-head,
.mock-rule-row {
  display: grid;
  box-sizing: border-box;
  grid-template-columns: minmax(180px, 1.05fr) minmax(150px, 0.8fr) minmax(300px, 1.35fr) 92px;
  gap: 4px;
  padding: 2px 10px;
}

.mock-rule-head {
  background: $color-bg-subtle;
  font-size: $font-size-xs;
  font-weight: 700;
  color: $color-text-secondary;
}

.mock-rule-row {
  align-items: center;
  border-top: 1px solid rgba(148, 163, 184, 0.14);
  min-height: 34px;
}

.mock-rule-column-meta {
  display: flex;
  align-items: center;
  min-height: 22px;
}

.mock-rule-type-cell {
  display: flex;
  align-items: center;
  min-height: 28px;
}

.mock-rule-picker-trigger {
  width: 100%;
  min-height: 28px;
  justify-content: flex-start;
}

:deep(.mock-rule-picker-trigger .n-button__content) {
  line-height: 1.2;
}

:deep(.mock-rule-picker-trigger.n-button) {
  --n-padding: 0 8px;
}

:deep(.mock-rule-picker-trigger.is-invalid.n-button) {
  --n-text-color: #b91c1c;
  --n-text-color-hover: #991b1b;
  --n-text-color-pressed: #7f1d1d;
  --n-color: rgba(254, 242, 242, 0.9);
  --n-color-hover: rgba(254, 242, 242, 0.96);
  --n-color-pressed: rgba(254, 226, 226, 0.98);
  box-shadow: inset 0 0 0 1px rgba(220, 38, 38, 0.64);
}

.mock-rule-column-inline {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 3px;
}

.mock-rule-column-name {
  font-size: 11px;
  font-weight: 700;
  color: $color-text-heading;
}

.mock-rule-column-type {
  font-size: $font-size-xs;
  color: $color-text-secondary;
}

.mock-flag {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: var(--radius-pill);
  background: rgba(226, 232, 240, 0.8);
  color: $color-text-secondary;
  font-size: 10px;
  font-weight: 700;
}

.mock-rule-params {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
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

:deep(.mock-param-field .n-input),
:deep(.mock-param-field .n-input-number),
:deep(.mock-param-field .n-base-selection) {
  width: 100%;
}

.mock-rule-hint {
  display: inline-flex;
  align-items: center;
  min-height: 24px;
  font-size: $font-size-xs;
  color: $color-text-secondary;
}

.mock-preview-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}

.mock-preview-toolbar-info {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.mock-preview-toolbar-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.mock-preview-toolbar-label {
  font-size: 12px;
  font-weight: 700;
  color: $color-text-secondary;
}

.mock-preview-table-wrap {
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  overflow: auto;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
}

.mock-sql-preview {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  padding: $gap-md;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
  background: $color-bg-subtle;
  color: $color-text-primary;
  font-family: 'Cascadia Code', 'JetBrains Mono', 'Cascadia Mono', 'Consolas', monospace;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.mock-rule-picker-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.mock-rule-picker-shell {
  width: min(1240px, calc(100vw - 112px));
  margin: 0 auto;
  padding: 20px 32px 24px;
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.98);
  box-shadow: 0 24px 80px rgba(15, 23, 42, 0.22);
}

.mock-rule-picker-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.mock-rule-picker-dialog-title {
  font-size: 18px;
  font-weight: 800;
  color: $color-text-heading;
}

.mock-rule-picker-close-button {
  min-width: 48px;
}

.mock-rule-picker-layout {
  display: grid;
  grid-template-columns: 150px minmax(0, 1fr) 280px;
  gap: 12px;
  height: min(560px, calc(100vh - 220px));
  min-height: 480px;
  padding: 0 4px;
  align-items: stretch;
}

.mock-rule-picker-sidebar {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-height: 0;
  overflow: auto;
  padding-right: 4px;
}

.mock-rule-category-button {
  border: 1px solid rgba(148, 163, 184, 0.18);
  border-radius: 10px;
  background: rgba(248, 250, 252, 0.72);
  color: $color-text-primary;
  font-size: 12px;
  font-weight: 700;
  text-align: left;
  padding: 8px 10px;
  cursor: pointer;
  transition: border-color 140ms ease, background 140ms ease, color 140ms ease;
}

.mock-rule-category-count {
  margin-left: 6px;
  font-size: 11px;
  color: $color-text-secondary;
}

.mock-rule-category-button.is-active {
  border-color: rgba(14, 116, 144, 0.24);
  background: rgba(236, 254, 255, 0.92);
  color: #0f766e;
}

.mock-rule-picker-main {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: auto;
  padding-right: 4px;
}

.mock-rule-picker-recent {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
  min-height: 54px;
}

.mock-rule-picker-section-title {
  font-size: 11px;
  font-weight: 800;
  color: $color-text-secondary;
}

.mock-rule-picker-recent-list,
.mock-rule-picker-preview-list {
  display: flex;
  gap: 6px;
}

.mock-rule-picker-recent-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  min-height: 24px;
  align-content: flex-start;
}

.mock-rule-picker-recent-list.is-empty {
  align-items: center;
}

.mock-rule-picker-empty-hint {
  font-size: 11px;
  color: $color-text-secondary;
}

.mock-rule-picker-chip {
  display: inline-flex;
  align-items: center;
  padding: 5px 8px;
  border-radius: 999px;
  background: rgba(226, 232, 240, 0.72);
  font-size: 11px;
  line-height: 1.4;
}

.mock-rule-picker-chip {
  border: none;
  color: $color-text-primary;
  cursor: pointer;
}

.mock-rule-picker-preview-list {
  flex-direction: column;
}

.mock-rule-picker-preview-item {
  display: block;
  box-sizing: border-box;
  width: 100%;
  max-width: 100%;
  padding: 8px 10px;
  border-radius: 10px;
  background: rgba(236, 254, 255, 0.92);
  color: #0f766e;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  overflow-wrap: anywhere;
}

.mock-rule-picker-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 10px;
}

.mock-rule-picker-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  min-height: 88px;
  padding: 12px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  border-radius: 12px;
  background: rgba(248, 250, 252, 0.82);
  text-align: left;
  cursor: pointer;
  transition: border-color 140ms ease, background 140ms ease, transform 140ms ease;
}

.mock-rule-picker-card:hover,
.mock-rule-picker-card.is-selected {
  border-color: rgba(14, 116, 144, 0.24);
  background: rgba(236, 254, 255, 0.96);
  transform: translateY(-1px);
}

.mock-rule-picker-card-title {
  font-size: 13px;
  font-weight: 800;
  color: $color-text-heading;
  max-width: 100%;
  word-break: break-word;
}

.mock-rule-picker-card-desc {
  font-size: 11px;
  line-height: 1.45;
  color: $color-text-secondary;
  max-width: 100%;
  word-break: break-word;
}

.mock-rule-picker-card-sample {
  display: block;
  max-width: 100%;
  font-size: 11px;
  line-height: 1.4;
  color: #0f766e;
  white-space: normal;
  word-break: break-word;
  overflow: hidden;
}

.mock-rule-picker-config {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
  padding: 12px;
  border: 1px solid $color-border-subtle;
  border-radius: 12px;
  background: rgba(248, 250, 252, 0.72);
  overflow: hidden;
}

.mock-rule-picker-config-head {
  display: flex;
  flex-direction: column;
  gap: 10px;
  flex: none;
}

.mock-rule-picker-config-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
  overflow: auto;
  padding-right: 4px;
}

.mock-rule-picker-config-title {
  font-size: 12px;
  font-weight: 800;
  color: $color-text-secondary;
}

.mock-rule-picker-config-subtitle {
  font-size: 14px;
  font-weight: 800;
  color: $color-text-heading;
}

.mock-rule-picker-config-desc {
  font-size: 12px;
  line-height: 1.5;
  color: $color-text-secondary;
  max-width: 100%;
  word-break: break-word;
}

.mock-rule-picker-config-sample {
  display: block;
  max-width: 100%;
  font-size: 12px;
  line-height: 1.5;
  color: #0f766e;
  white-space: pre-wrap;
  word-break: break-word;
  overflow-wrap: anywhere;
}

.mock-rule-picker-live-preview {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

:deep(.n-spin-content) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
}

:deep(.n-spin),
:deep(.n-spin-container) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

:deep(.n-tabs) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

:deep(.n-tabs-pane-wrapper) {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}

:deep(.n-tab-pane) {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}

:deep(.n-tab-pane > .mock-tab-body:not(.mock-tab-body-scroll)) {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}

:deep(.n-tab-pane > .mock-tab-body.mock-tab-body-scroll) {
  flex: 1 1 auto;
  min-height: 0;
  overflow-x: hidden;
  overflow-y: auto;
}

:deep(.mock-preview-table .n-data-table-wrapper) {
  overflow: auto;
}

:deep(.mock-preview-table .n-data-table-table) {
  min-width: max-content;
}

:deep(.mock-preview-table .n-data-table-th),
:deep(.mock-preview-table .n-data-table-td) {
  padding: 4px 8px;
  font-size: 12px;
  overflow: hidden;
}

:deep(.mock-preview-table .n-data-table-th__title) {
  font-size: 11px;
  font-weight: 700;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:deep(.mock-preview-table .n-data-table-td__ellipsis) {
  max-width: 100%;
}

:deep(.mock-preview-table .n-data-table-td__ellipsis > span),
:deep(.mock-preview-table .n-data-table-td__ellipsis > div) {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1120px) {
  .mock-global-grid {
    grid-template-columns: 1fr;
  }

  .mock-rule-picker-layout {
    grid-template-columns: 1fr;
    height: auto;
    min-height: 0;
  }

  .mock-rule-picker-sidebar {
    flex-direction: row;
    flex-wrap: wrap;
    overflow: visible;
  }

  .mock-rule-picker-main,
  .mock-rule-picker-config-body {
    overflow: visible;
  }

  .mock-rule-head,
  .mock-rule-row {
    grid-template-columns: 1fr;
  }

  .mock-panel-header {
    flex-direction: column;
  }
}
</style>
