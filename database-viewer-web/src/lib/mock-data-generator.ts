import { fakerDE, fakerEN_US, fakerFR, fakerJA, fakerZH_CN } from '@faker-js/faker';
import dayjs from 'dayjs';
import RandExp from 'randexp';
import type { ProviderType, TableColumn, TableDesign, TableSummary } from '../types/explorer';

export type MockBeforeInsertStrategy = 'none' | 'truncate' | 'delete';

export type MockDataLocale = 'zh-CN' | 'en-US' | 'ja-JP' | 'fr-FR' | 'de-DE';

export type MockRuleType =
  | 'database-auto'
  | 'ignore'
  | 'static'
  | 'sequence'
  | 'random-string'
  | 'random-number'
  | 'random-boolean'
  | 'enum'
  | 'username'
  | 'domain'
  | 'json'
  | 'imei'
  | 'imsi'
  | 'vehicle-plate'
  | 'bank-account'
  | 'swift-bic'
  | 'currency-code'
  | 'department'
  | 'barcode'
  | 'tracking-number'
  | 'sentence'
  | 'paragraph'
  | 'first-name'
  | 'last-name'
  | 'gender'
  | 'ssn'
  | 'country-code'
  | 'state-abbr'
  | 'time-zone'
  | 'flight-number'
  | 'hospital-npi'
  | 'icd10-code'
  | 'currency-name'
  | 'credit-card-type'
  | 'stock-symbol'
  | 'airport-code'
  | 'airport-name'
  | 'airline-code'
  | 'airline-name'
  | 'drug-company'
  | 'drug-name'
  | 'hospital-name'
  | 'nhs-number'
  | 'iban'
  | 'product-category'
  | 'product-description'
  | 'stock-market'
  | 'language'
  | 'language-code'
  | 'university'
  | 'airport-country-code'
  | 'flight-duration-hours'
  | 'money'
  | 'product-price'
  | 'mime-type'
  | 'file-extension'
  | 'port-number'
  | 'http-status-code'
  | 'file-name'
  | 'version'
  | 'postal-code'
  | 'ipv4'
  | 'ipv4-cidr'
  | 'ipv6'
  | 'mac-address'
  | 'md5'
  | 'sha1'
  | 'sha256'
  | 'name-localized'
  | 'name-zh'
  | 'name-en'
  | 'phone'
  | 'email'
  | 'id-card'
  | 'ip'
   | 'hk-id-card'
  | 'country'
  | 'province'
  | 'city'
  | 'address'
  | 'company'
  | 'job-title'
  | 'bank-card'
  | 'uuid'
  | 'url'
  | 'hex-color'
  | 'latitude'
  | 'longitude'
  | 'current-datetime'
  | 'random-past-datetime'
  | 'random-future-datetime'
  | 'regex'
  | 'sql-expression';

export interface MockRuleOptions {
  staticValue: string;
  sequenceStart: number;
  sequenceStep: number;
  minNumber: number;
  maxNumber: number;
  decimalPlaces: number;
  minLength: number;
  maxLength: number;
  enumValuesText: string;
  regexPattern: string;
  sqlExpression: string;
  trueProbability: number;
  nullProbability: number;
  daysRange: number;
  includeHashPrefix: boolean;
  versionSegmentCount: number;
  versionIncludePrefix: boolean;
  sentenceWordCount: number;
  paragraphSentenceCount: number;
  emailDomain: string;
  coordinateDecimalPlaces: number;
  uuidUppercase: boolean;
}

export interface MockColumnRule {
  columnName: string;
  columnType: string;
  displayType: string;
  nullable: boolean;
  unique: boolean;
  generatedByDatabase: boolean;
  comment: string | null;
  ruleType: MockRuleType;
  options: MockRuleOptions;
}

export interface MockDataPlan {
  provider: ProviderType;
  connectionId: string;
  database: string;
  table: TableSummary;
  design: TableDesign;
  locale: MockDataLocale;
  rowCount: number;
  previewRowLimit?: number;
  beforeInsert: MockBeforeInsertStrategy;
  columns: MockColumnRule[];
}

export interface GeneratedPreviewRow {
  [key: string]: string;
}

export interface GeneratedMockDataset {
  sql: string;
  csv: string;
  json: string;
  previewRows: GeneratedPreviewRow[];
  warnings: string[];
}

export const beforeInsertOptions = [
  { label: '不操作', value: 'none' },
  { label: '清空表 (TRUNCATE TABLE)', value: 'truncate' },
  { label: '删除数据 (DELETE FROM)', value: 'delete' },
] satisfies Array<{ label: string; value: MockBeforeInsertStrategy }>;

export const mockDataLocaleOptions = [
  { label: '中文 (简体)', value: 'zh-CN' },
  { label: 'English (US)', value: 'en-US' },
  { label: '日本語', value: 'ja-JP' },
  { label: 'Français', value: 'fr-FR' },
  { label: 'Deutsch', value: 'de-DE' },
] satisfies Array<{ label: string; value: MockDataLocale }>;

export const mockRuleOptions = [
  { label: '数据库自增 / 默认值', value: 'database-auto' },
  { label: '忽略此字段', value: 'ignore' },
  { label: '固定值', value: 'static' },
  { label: '递增序列', value: 'sequence' },
  { label: '随机字符串', value: 'random-string' },
  { label: '随机数字', value: 'random-number' },
  { label: '随机布尔值', value: 'random-boolean' },
  { label: '枚举', value: 'enum' },
  { label: '用户名', value: 'username' },
  { label: '域名', value: 'domain' },
  { label: 'JSON', value: 'json' },
  { label: 'IMEI', value: 'imei' },
  { label: 'IMSI', value: 'imsi' },
  { label: '车牌号', value: 'vehicle-plate' },
  { label: '银行账号', value: 'bank-account' },
  { label: 'SWIFT / BIC', value: 'swift-bic' },
  { label: '币种代码', value: 'currency-code' },
  { label: '部门名称', value: 'department' },
  { label: '条码', value: 'barcode' },
  { label: '物流单号', value: 'tracking-number' },
  { label: '短句', value: 'sentence' },
  { label: '段落', value: 'paragraph' },
  { label: '名', value: 'first-name' },
  { label: '姓', value: 'last-name' },
  { label: '性别', value: 'gender' },
  { label: 'SSN', value: 'ssn' },
  { label: '国家代码', value: 'country-code' },
  { label: '州缩写', value: 'state-abbr' },
  { label: '时区', value: 'time-zone' },
  { label: '航班号', value: 'flight-number' },
  { label: '医院 NPI', value: 'hospital-npi' },
  { label: 'ICD10 诊断编码', value: 'icd10-code' },
  { label: '币种名称', value: 'currency-name' },
  { label: '信用卡类型', value: 'credit-card-type' },
  { label: '股票代码', value: 'stock-symbol' },
  { label: '机场代码', value: 'airport-code' },
  { label: '机场名称', value: 'airport-name' },
  { label: '航空公司代码', value: 'airline-code' },
  { label: '航空公司名称', value: 'airline-name' },
  { label: '药企名称', value: 'drug-company' },
  { label: '药品名称', value: 'drug-name' },
  { label: '医院名称', value: 'hospital-name' },
  { label: 'NHS Number', value: 'nhs-number' },
  { label: 'IBAN', value: 'iban' },
  { label: '产品分类', value: 'product-category' },
  { label: '产品描述', value: 'product-description' },
  { label: '股票市场', value: 'stock-market' },
  { label: '语言', value: 'language' },
  { label: '语言代码', value: 'language-code' },
  { label: '大学', value: 'university' },
  { label: '机场国家代码', value: 'airport-country-code' },
  { label: '飞行时长（小时）', value: 'flight-duration-hours' },
  { label: '金额', value: 'money' },
  { label: '产品价格', value: 'product-price' },
  { label: 'MIME 类型', value: 'mime-type' },
  { label: '文件扩展名', value: 'file-extension' },
  { label: '端口号', value: 'port-number' },
  { label: 'HTTP 状态码', value: 'http-status-code' },
  { label: '文件名', value: 'file-name' },
  { label: '版本号', value: 'version' },
  { label: '邮编 / 邮政编码', value: 'postal-code' },
  { label: 'IPv4', value: 'ipv4' },
  { label: 'IPv4 CIDR', value: 'ipv4-cidr' },
  { label: 'IPv6', value: 'ipv6' },
  { label: 'MAC 地址', value: 'mac-address' },
  { label: 'MD5', value: 'md5' },
  { label: 'SHA1', value: 'sha1' },
  { label: 'SHA256', value: 'sha256' },
  { label: '本地化姓名', value: 'name-localized' },
  { label: '中文姓名', value: 'name-zh' },
  { label: '英文姓名', value: 'name-en' },
  { label: '手机号', value: 'phone' },
  { label: '邮箱地址', value: 'email' },
  { label: '身份证号', value: 'id-card' },
   { label: '香港身份证号', value: 'hk-id-card' },
  { label: 'IP 地址', value: 'ip' },
  { label: '国家', value: 'country' },
  { label: '省份', value: 'province' },
  { label: '城市', value: 'city' },
  { label: '完整地址', value: 'address' },
  { label: '公司名', value: 'company' },
  { label: '职位', value: 'job-title' },
  { label: '银行卡号', value: 'bank-card' },
  { label: 'UUID', value: 'uuid' },
  { label: 'URL 链接', value: 'url' },
  { label: 'Hex 颜色值', value: 'hex-color' },
  { label: '纬度', value: 'latitude' },
  { label: '经度', value: 'longitude' },
  { label: '当前时间', value: 'current-datetime' },
  { label: '随机过去时间', value: 'random-past-datetime' },
  { label: '随机未来时间', value: 'random-future-datetime' },
  { label: '正则表达式', value: 'regex' },
  { label: 'SQL 表达式', value: 'sql-expression' },
] satisfies Array<{ label: string; value: MockRuleType }>;

type SqlLiteralValue = string | number | boolean | null | { sql: string; preview: string };

const defaultOptions: MockRuleOptions = {
  staticValue: '',
  sequenceStart: 1,
  sequenceStep: 1,
  minNumber: 1,
  maxNumber: 100,
  decimalPlaces: 0,
  minLength: 6,
  maxLength: 18,
  enumValuesText: '0,1,2',
  regexPattern: '[A-Z]{3}-\\d{4}',
  sqlExpression: '',
  trueProbability: 50,
  nullProbability: 0,
  daysRange: 30,
  includeHashPrefix: true,
  versionSegmentCount: 3,
  versionIncludePrefix: true,
  sentenceWordCount: 4,
  paragraphSentenceCount: 2,
  emailDomain: '',
  coordinateDecimalPlaces: 6,
  uuidUppercase: false,
};

const integerTypePattern = /(tinyint|smallint|int|bigint|serial|bigserial|integer)/i;
const decimalTypePattern = /(decimal|numeric|float|double|real|money|smallmoney)/i;
const booleanTypePattern = /(bit|bool|boolean)/i;
const datetimeTypePattern = /(date|time|datetime|timestamp)/i;
const uuidTypePattern = /(uuid|uniqueidentifier)/i;

function getLocalizedFaker(locale: MockDataLocale) {
  switch (locale) {
    case 'de-DE':
      return fakerDE;
    case 'fr-FR':
      return fakerFR;
    case 'ja-JP':
      return fakerJA;
    case 'en-US':
      return fakerEN_US;
    case 'zh-CN':
    default:
      return fakerZH_CN;
  }
}

function createPhoneValue(locale: MockDataLocale): string {
  switch (locale) {
    case 'de-DE':
      return fakerDE.helpers.fromRegExp(/0[0-9]{10,11}/);
    case 'fr-FR':
      return fakerFR.helpers.fromRegExp(/0[1-9][0-9]{8}/);
    case 'ja-JP':
      return fakerJA.helpers.fromRegExp(/0[0-9]{1,4}-[0-9]{1,4}-[0-9]{4}/);
    case 'en-US':
      return fakerEN_US.helpers.fromRegExp(/\+1-[0-9]{3}-[0-9]{3}-[0-9]{4}/);
    case 'zh-CN':
    default:
      return fakerZH_CN.helpers.fromRegExp(/1[3-9][0-9]{9}/);
  }
}

function cloneDefaultOptions(): MockRuleOptions {
  return { ...defaultOptions };
}

function isIntegerColumn(column: TableColumn): boolean {
  return integerTypePattern.test(column.type);
}

function isDecimalColumn(column: TableColumn): boolean {
  return decimalTypePattern.test(column.type);
}

function isBooleanColumn(column: TableColumn): boolean {
  return booleanTypePattern.test(column.type);
}

function isDatetimeColumn(column: TableColumn): boolean {
  return datetimeTypePattern.test(column.type);
}

function quoteIdentifier(provider: ProviderType, identifier: string): string {
  if (provider === 'mysql') {
    return `\`${identifier.replace(/`/g, '``')}\``;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `"${identifier.replace(/"/g, '""')}"`;
  }

  return `[${identifier.replace(/\]/g, ']]')}]`;
}

function quoteQualifiedTable(provider: ProviderType, table: TableSummary): string {
  const parts = [] as string[];
  if (table.schema) {
    parts.push(quoteIdentifier(provider, table.schema));
  }

  parts.push(quoteIdentifier(provider, table.name));
  return parts.join('.');
}

function currentDatetimeExpression(provider: ProviderType): string {
  if (provider === 'sqlserver') {
    return 'SYSDATETIME()';
  }

  if (provider === 'sqlite') {
    return 'CURRENT_TIMESTAMP';
  }

  return 'CURRENT_TIMESTAMP';
}

function normalizeEnumValues(text: string): string[] {
  return text
    .split(/[\n,，]/)
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function formatColumnType(column: TableColumn): string {
  if (column.maxLength !== null && column.maxLength !== undefined) {
    return `${column.type}(${column.maxLength < 0 ? 'max' : column.maxLength})`;
  }

  if (column.numericPrecision !== null && column.numericPrecision !== undefined) {
    return column.numericScale !== null && column.numericScale !== undefined
      ? `${column.type}(${column.numericPrecision}, ${column.numericScale})`
      : `${column.type}(${column.numericPrecision})`;
  }

  return column.type;
}

function buildUniqueColumnSet(design: TableDesign): Set<string> {
  const uniqueColumns = new Set<string>();
  for (const column of design.columns) {
    if (column.isPrimaryKey) {
      uniqueColumns.add(column.name);
    }
  }

  for (const index of design.indexes) {
    if (index.isUnique || index.isPrimaryKey) {
      for (const columnName of index.columns) {
        uniqueColumns.add(columnName);
      }
    }
  }

  return uniqueColumns;
}

function createRuleType(column: TableColumn, unique: boolean): MockRuleType {
  const columnName = column.name.toLowerCase();
  const commentEnumValues = extractEnumValuesFromComment(column.comment);

  if (column.isAutoGenerated || column.isComputed) {
    return 'database-auto';
  }

  if (commentEnumValues.length >= 2) {
    return 'enum';
  }

  if (uuidTypePattern.test(column.type) || /(uuid|guid)/i.test(columnName)) {
    return 'uuid';
  }

  if (/(sha256)/i.test(columnName)) {
    return 'sha256';
  }

  if (/(sha1)/i.test(columnName)) {
    return 'sha1';
  }

  if (/(md5)/i.test(columnName)) {
    return 'md5';
  }

  if (/(ipv6)/i.test(columnName)) {
    return 'ipv6';
  }

  if (/(cidr|subnet)/i.test(columnName)) {
    return 'ipv4-cidr';
  }

  if (/(ipv4|ip_?v4)/i.test(columnName)) {
    return 'ipv4';
  }

  if (/(mac(_?address)?)/i.test(columnName)) {
    return 'mac-address';
  }

  if (/(postal|zip|postcode)/i.test(columnName)) {
    return 'postal-code';
  }

  if (/(imei)/i.test(columnName)) {
    return 'imei';
  }

  if (/(imsi)/i.test(columnName)) {
    return 'imsi';
  }

  if (/(plate|license_?plate|car_?no)/i.test(columnName)) {
    return 'vehicle-plate';
  }

  if (/(bank_?account|account_?no|iban)/i.test(columnName)) {
    return 'bank-account';
  }

  if (/(swift|bic)/i.test(columnName)) {
    return 'swift-bic';
  }

  if (/(currency(_?code)?)/i.test(columnName)) {
    return 'currency-code';
  }

  if (/(department|team|group)/i.test(columnName)) {
    return 'department';
  }

  if (/(barcode)/i.test(columnName)) {
    return 'barcode';
  }

  if (/(tracking|waybill|shipment|express)/i.test(columnName)) {
    return 'tracking-number';
  }

  if (/(json|meta|config|payload|extra|ext)/i.test(columnName)) {
    return 'json';
  }

  if (/(summary|caption|subject)/i.test(columnName) || (/(title)/i.test(columnName) && !/(job|position|role)/i.test(columnName))) {
    return 'sentence';
  }

  if (/(description|remark|comment|memo|content|detail|message|note)/i.test(columnName)) {
    return 'paragraph';
  }

  if (/(file_?name|filename)/i.test(columnName)) {
    return 'file-name';
  }

  if (/(domain|host)/i.test(columnName)) {
    return 'domain';
  }

  if (/(login|account|username)/i.test(columnName)) {
    return 'username';
  }

  if (/^(first_?name|given_?name)$/i.test(columnName)) {
    return 'first-name';
  }

  if (/^(last_?name|family_?name|surname)$/i.test(columnName)) {
    return 'last-name';
  }

  if (/(gender)/i.test(columnName)) {
    return 'gender';
  }

  if (/(ssn)/i.test(columnName)) {
    return 'ssn';
  }

  if (/(country_?code)/i.test(columnName)) {
    return 'country-code';
  }

  if (/(state_?abbr|province_?abbr|region_?code)/i.test(columnName)) {
    return 'state-abbr';
  }

  if (/(time_?zone|timezone|tz)/i.test(columnName)) {
    return 'time-zone';
  }

  if (/(flight_?number)/i.test(columnName)) {
    return 'flight-number';
  }

  if (/(hospital_?npi|npi)/i.test(columnName)) {
    return 'hospital-npi';
  }

  if (/(icd10|diagnosis_?code)/i.test(columnName)) {
    return 'icd10-code';
  }

  if (/(currency$|currency_?name)/i.test(columnName)) {
    return 'currency-name';
  }

  if (/(credit_?card_?type|card_?type)/i.test(columnName)) {
    return 'credit-card-type';
  }

  if (/(stock_?symbol|ticker)/i.test(columnName)) {
    return 'stock-symbol';
  }

  if (/(airport_?code)/i.test(columnName)) {
    return 'airport-code';
  }

  if (/(airport_?name)/i.test(columnName)) {
    return 'airport-name';
  }

  if (/(airline_?code)/i.test(columnName)) {
    return 'airline-code';
  }

  if (/(airline_?name)/i.test(columnName)) {
    return 'airline-name';
  }

  if (/(drug_?company|pharma|pharmaceutical)/i.test(columnName)) {
    return 'drug-company';
  }

  if (/(drug_?name|medicine|medication)/i.test(columnName)) {
    return 'drug-name';
  }

  if (/(hospital_?name)/i.test(columnName)) {
    return 'hospital-name';
  }

  if (/(nhs_?number)/i.test(columnName)) {
    return 'nhs-number';
  }

  if (/\biban\b/i.test(columnName)) {
    return 'iban';
  }

  if (/(product_?category|category)/i.test(columnName)) {
    return 'product-category';
  }

  if (/(product_?description)/i.test(columnName)) {
    return 'product-description';
  }

  if (/(stock_?market|exchange)/i.test(columnName)) {
    return 'stock-market';
  }

  if (/^(language)$/i.test(columnName)) {
    return 'language';
  }

  if (/(language_?code|locale_?code)/i.test(columnName)) {
    return 'language-code';
  }

  if (/(university|college|school)/i.test(columnName)) {
    return 'university';
  }

  if (/(airport_?country_?code)/i.test(columnName)) {
    return 'airport-country-code';
  }

  if (/(flight_?duration|duration_?hours)/i.test(columnName)) {
    return 'flight-duration-hours';
  }

  if (/^(money)$/i.test(columnName)) {
    return 'money';
  }

  if (/(product_?price|price)/i.test(columnName)) {
    return 'product-price';
  }

  if (/(mime_?type|content_?type)/i.test(columnName)) {
    return 'mime-type';
  }

  if (/(file_?extension|extension|ext$)/i.test(columnName)) {
    return 'file-extension';
  }

  if (/(port|port_?number|http_?port|tcp_?port)/i.test(columnName)) {
    return 'port-number';
  }

  if (/(http_?status|status_?code)/i.test(columnName)) {
    return 'http-status-code';
  }

  if (/(firmware|os_?version|app_?version|version|build)/i.test(columnName)) {
    return 'version';
  }

  if (/(email|mail)/i.test(columnName)) {
    return 'email';
  }

  if (/(phone|mobile|tel)/i.test(columnName)) {
    return 'phone';
  }

  if (/(idcard|identity)/i.test(columnName)) {
    return 'id-card';
  }

  if (/(hkid|hk_?id|hong_?kong.*id)/i.test(columnName)) {
    return 'hk-id-card';
  }

  if (/(avatar|image|logo|cover).*url/i.test(columnName) || /(avatar|image|logo|cover)/i.test(columnName)) {
    return 'url';
  }

  if (/(user_?name|nick_?name|full_?name|display_?name|name)$/i.test(columnName)) {
    return 'name-localized';
  }

  if (/(company|corp|enterprise)/i.test(columnName)) {
    return 'company';
  }

  if (/(job|title|position)/i.test(columnName)) {
    return 'job-title';
  }

  if (/(country)/i.test(columnName)) {
    return 'country';
  }

  if (/(province|state)/i.test(columnName)) {
    return 'province';
  }

  if (/(city)/i.test(columnName)) {
    return 'city';
  }

  if (/(address|addr|location)/i.test(columnName)) {
    return 'address';
  }

  if (/(ip)/i.test(columnName)) {
    return 'ip';
  }

  if (/(url|link)/i.test(columnName)) {
    return 'url';
  }

  if (/(color|colour)/i.test(columnName)) {
    return 'hex-color';
  }

  if (/(latitude|lat)/i.test(columnName)) {
    return 'latitude';
  }

  if (/(longitude|lng|lon)/i.test(columnName)) {
    return 'longitude';
  }

  if (/(status|state|level|type|role|gender|sex)/i.test(columnName)) {
    return 'enum';
  }

  if (/(expire|expiry|due|deadline|valid|invalid|end(_date)?|expired)/i.test(columnName)) {
    return 'random-future-datetime';
  }

  if (/(birth|birthday|dob|start(_date)?|begin|effective)/i.test(columnName)) {
    return 'random-past-datetime';
  }

  if (/(created|updated|modified|time|date|at)$/i.test(columnName) || isDatetimeColumn(column)) {
    return /(created|updated|modified)/i.test(columnName) ? 'current-datetime' : 'random-past-datetime';
  }

  if (isBooleanColumn(column) || /^(is_|has_|can_|enable|enabled|active|deleted|locked)/i.test(columnName)) {
    return 'random-boolean';
  }

  if ((column.isPrimaryKey && isIntegerColumn(column)) || (unique && isIntegerColumn(column))) {
    return 'sequence';
  }

  if (isDecimalColumn(column)) {
    return 'random-number';
  }

  if (isIntegerColumn(column)) {
    return /age/i.test(columnName) ? 'random-number' : 'sequence';
  }

  return 'random-string';
}

function buildRuleOptions(column: TableColumn, ruleType: MockRuleType): MockRuleOptions {
  const options = cloneDefaultOptions();
  const columnName = column.name.toLowerCase();
  const commentEnumValues = extractEnumValuesFromComment(column.comment);

  if (ruleType === 'random-number') {
    if (/age/i.test(columnName)) {
      options.minNumber = 18;
      options.maxNumber = 65;
    } else if (/(percent|percentage|ratio|rate)/i.test(columnName)) {
      options.minNumber = 0;
      options.maxNumber = 100;
      options.decimalPlaces = 2;
    } else if (/(count|qty|quantity|stock|inventory|times)/i.test(columnName)) {
      options.minNumber = 0;
      options.maxNumber = 500;
      options.decimalPlaces = 0;
    } else if (/(weight|height|length|width|depth)/i.test(columnName)) {
      options.minNumber = 0.1;
      options.maxNumber = 999;
      options.decimalPlaces = 2;
    } else if (/(year)/i.test(columnName)) {
      options.minNumber = 2020;
      options.maxNumber = 2035;
      options.decimalPlaces = 0;
    } else if (/(month)/i.test(columnName)) {
      options.minNumber = 1;
      options.maxNumber = 12;
      options.decimalPlaces = 0;
    } else if (/(day)/i.test(columnName)) {
      options.minNumber = 1;
      options.maxNumber = 31;
      options.decimalPlaces = 0;
    } else if (/(amount|price|total|balance|score|rate)/i.test(columnName)) {
      options.minNumber = 0.01;
      options.maxNumber = 50000;
      options.decimalPlaces = isDecimalColumn(column) ? 2 : 0;
    } else {
      options.minNumber = 1;
      options.maxNumber = 9999;
      options.decimalPlaces = isDecimalColumn(column) ? 2 : 0;
    }
  }

  if (ruleType === 'random-string') {
    const maxLength = column.maxLength && column.maxLength > 0 ? column.maxLength : 24;
    const preferredMaxLength = /(description|remark|comment|memo|content|detail|summary|message|note)/i.test(columnName)
      ? Math.min(maxLength, 120)
      : Math.min(maxLength, 32);

    options.maxLength = preferredMaxLength;
    options.minLength = Math.min(6, options.maxLength);
  }

  if (ruleType === 'enum') {
    if (commentEnumValues.length >= 2) {
      options.enumValuesText = commentEnumValues.join(',');
    } else if (/(gender|sex)/i.test(columnName)) {
      options.enumValuesText = '男,女';
    } else {
      options.enumValuesText = /status|state/i.test(columnName) ? '0,1,2' : 'A,B,C';
    }
  }

  if (ruleType === 'static') {
    options.staticValue = column.comment ?? '';
  }

  if (ruleType === 'current-datetime') {
    options.sqlExpression = '';
  }

  if (ruleType === 'hex-color') {
    options.includeHashPrefix = true;
  }

  if (ruleType === 'version') {
    options.versionSegmentCount = 3;
    options.versionIncludePrefix = true;
  }

  if (ruleType === 'sentence') {
    options.sentenceWordCount = 4;
  }

  if (ruleType === 'paragraph') {
    options.paragraphSentenceCount = 2;
  }

  if (ruleType === 'email') {
    options.emailDomain = '';
  }

  if (ruleType === 'latitude' || ruleType === 'longitude') {
    options.coordinateDecimalPlaces = 6;
  }

  if (ruleType === 'uuid') {
    options.uuidUppercase = false;
  }

  if (ruleType === 'random-past-datetime' || ruleType === 'random-future-datetime') {
    if (/(birth|birthday|dob)/i.test(columnName)) {
      options.daysRange = 365 * 40;
    } else if (/(expire|expiry|due|deadline|valid|end(_date)?|expired)/i.test(columnName)) {
      options.daysRange = 365 * 3;
    } else if (/(created|updated|modified|start(_date)?|begin|effective)/i.test(columnName)) {
      options.daysRange = 365;
    } else {
      options.daysRange = 30;
    }
  }

  if (column.isNullable) {
    options.nullProbability = 0;
  }

  return options;
}

export function createRuleOptionsForType(column: TableColumn, ruleType: MockRuleType): MockRuleOptions {
  return buildRuleOptions(column, ruleType);
}

export function buildDefaultMockPlan(input: {
  provider: ProviderType;
  connectionId: string;
  database: string;
  table: TableSummary;
  design: TableDesign;
}): MockDataPlan {
  const uniqueColumns = buildUniqueColumnSet(input.design);
  const columns = input.design.columns.map((column) => {
    const unique = uniqueColumns.has(column.name);
    const ruleType = createRuleType(column, unique);
    return {
      columnName: column.name,
      columnType: column.type,
      displayType: formatColumnType(column),
      nullable: !!column.isNullable,
      unique,
      generatedByDatabase: !!column.isAutoGenerated || !!column.isComputed,
      comment: column.comment ?? null,
      ruleType,
      options: buildRuleOptions(column, ruleType),
    } satisfies MockColumnRule;
  });

  return {
    provider: input.provider,
    connectionId: input.connectionId,
    database: input.database,
    table: input.table,
    design: input.design,
    locale: 'zh-CN',
    rowCount: 100,
    beforeInsert: 'none',
    columns,
  };
}

function getColumnMaxLength(designColumn: TableColumn | undefined): number | null {
  const maxLength = designColumn?.maxLength;
  if (maxLength === undefined || maxLength === null || maxLength < 0) {
    return null;
  }

  return maxLength;
}

function applyLengthLimit(value: string, limit: number | null): string {
  if (!limit || value.length <= limit) {
    return value;
  }

  return value.slice(0, limit);
}

function extractEnumValuesFromComment(comment: string | null | undefined): string[] {
  if (!comment) {
    return [];
  }

  const values = Array.from(comment.matchAll(/(?:^|[\s,，;；、])([A-Za-z0-9_-]+)\s*(?:[:：=＝-])\s*[^,，;；、]+/g))
    .map((matchResult) => matchResult[1]?.trim() ?? '')
    .filter(Boolean);

  return values.filter((value, index) => values.indexOf(value) === index);
}

function createCodeLikeValue(columnName: string, rowIndex: number): string {
  const prefix = columnName
    .replace(/(code|number|num|no|sn|serial|barcode|batch|ticket|order|ref|tracking)/gi, '')
    .replace(/[^a-z0-9]+/gi, '')
    .slice(0, 4)
    .toUpperCase() || 'NO';

  return `${prefix}${dayjs().format('YYMMDD')}${String(rowIndex + 1).padStart(6, '0')}`;
}

function createSemanticVersionValue(segmentCount: number, includePrefix: boolean): string {
  const safeSegmentCount = Math.max(2, Math.min(4, segmentCount));
  const segments = Array.from({ length: safeSegmentCount }, (_, index) => fakerEN_US.number.int({ min: index === 0 ? 1 : 0, max: index === 0 ? 9 : 99 }));
  const versionCore = segments.join('.');

  return includePrefix ? `v${versionCore}` : versionCore;
}

function createHashValue(length: number): string {
  return fakerEN_US.helpers.fromRegExp(new RegExp(`[a-f0-9]{${length}}`)).toLowerCase();
}

function createUsernameValue(): string {
  const baseName = fakerEN_US.person.firstName().replace(/[^a-z0-9]/gi, '').toLowerCase() || 'user';
  return `${baseName}${fakerEN_US.number.int({ min: 10, max: 9999 })}`;
}

function createEmailValue(domainOverride: string): string {
  const normalizedDomain = domainOverride.trim().toLowerCase();
  const localPart = createUsernameValue();

  return `${localPart}@${normalizedDomain || fakerEN_US.internet.domainName().toLowerCase()}`;
}

function createCoordinateValue(kind: 'latitude' | 'longitude', decimalPlaces: number): number {
  const safeDecimalPlaces = Math.max(2, Math.min(8, decimalPlaces));
  const rawValue = kind === 'latitude'
    ? Number(fakerEN_US.location.latitude({ precision: safeDecimalPlaces }))
    : Number(fakerEN_US.location.longitude({ precision: safeDecimalPlaces }));

  return Number(rawValue.toFixed(safeDecimalPlaces));
}

function createUuidValue(): string {
  return fakerEN_US.string.uuid().toLowerCase();
}

function createLocalizedAddress(locale: MockDataLocale): string {
  const localizedFaker = getLocalizedFaker(locale);

  if (locale === 'zh-CN') {
    return `${fakerZH_CN.location.state()}${fakerZH_CN.location.city()}${fakerZH_CN.location.streetAddress()}`;
  }

  return `${localizedFaker.location.streetAddress()}, ${localizedFaker.location.city()}, ${localizedFaker.location.state()}`;
}

function createChinaIdCardNumber(): string {
  const areaCodes = ['110101', '310101', '440303', '330106', '320102', '420106'];
  const areaCode = fakerEN_US.helpers.arrayElement(areaCodes);
  const year = fakerEN_US.number.int({ min: 1970, max: 2004 });
  const month = fakerEN_US.number.int({ min: 1, max: 12 });
  const daysInMonth = dayjs(`${year}-${String(month).padStart(2, '0')}-01`).daysInMonth();
  const day = fakerEN_US.number.int({ min: 1, max: daysInMonth });
  const birthday = `${year}${String(month).padStart(2, '0')}${String(day).padStart(2, '0')}`;
  const sequence = String(fakerEN_US.number.int({ min: 1, max: 999 })).padStart(3, '0');
  const firstSeventeenDigits = `${areaCode}${birthday}${sequence}`;
  const weightFactors = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
  const checksumMap = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2'];
  const checksumIndex = firstSeventeenDigits
    .split('')
    .reduce((total, digit, index) => total + (Number(digit) * (weightFactors[index] ?? 0)), 0) % 11;

  return `${firstSeventeenDigits}${checksumMap[checksumIndex] ?? '0'}`;
}

function createHongKongIdCardNumber(): string {
  const prefixLength = fakerEN_US.helpers.arrayElement([1, 2]);
  const prefixLetters = Array.from({ length: prefixLength }, () => fakerEN_US.string.alpha({ length: 1, casing: 'upper' }));
  const digits = fakerEN_US.string.numeric({ length: 6 });
  const primaryPrefixLetter = prefixLetters[0] ?? 'A';
  const valueSequence = prefixLength === 1
    ? [36, primaryPrefixLetter.charCodeAt(0) - 55]
    : prefixLetters.map((letter) => letter.charCodeAt(0) - 55);

  const digitValues = digits.split('').map((digit) => Number(digit));
  const weightedValues = [...valueSequence, ...digitValues];
  const sum = weightedValues.reduce((result, value, index) => result + (value * (9 - index)), 0);
  const remainder = sum % 11;
  const checkValue = (11 - remainder) % 11;
  const checkDigit = checkValue === 10 ? 'A' : String(checkValue);

  return `${prefixLetters.join('')}${digits}(${checkDigit})`;
}

function createCurrentDatetimePreview(): string {
  return formatDatetimeValue(new Date());
}

function createNarrativeText(locale: MockDataLocale, maxLength: number | null): string {
  const localizedFaker = getLocalizedFaker(locale);

  if (maxLength !== null && maxLength <= 24) {
    return localizedFaker.lorem.words(3);
  }

  if (maxLength !== null && maxLength <= 80) {
    return localizedFaker.lorem.sentence();
  }

  return [localizedFaker.lorem.sentence(), localizedFaker.lorem.sentence()].join(' ');
}

function createJsonLikeValue(locale: MockDataLocale, rowIndex: number): string {
  const localizedFaker = getLocalizedFaker(locale);

  return JSON.stringify({
    code: fakerEN_US.string.alphanumeric(8).toUpperCase(),
    enabled: fakerEN_US.datatype.boolean(),
    label: localizedFaker.commerce.productName(),
    sort: rowIndex + 1,
    updatedAt: formatDatetimeValue(fakerEN_US.date.recent({ days: 30 })),
  });
}

function createSemanticStringValue(locale: MockDataLocale, rule: MockColumnRule, designColumn: TableColumn | undefined, rowIndex: number): string {
  const localizedFaker = getLocalizedFaker(locale);
  const columnName = rule.columnName.toLowerCase();
  const contextText = `${columnName} ${(rule.comment ?? '').toLowerCase()}`;
  const maxLength = getColumnMaxLength(designColumn);

  if (/(mac(_?address)?)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.internet.mac(), maxLength);
  }

  if (/(imei)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.helpers.fromRegExp(/[0-9]{15}/), maxLength);
  }

  if (/(imsi)/i.test(contextText)) {
    return applyLengthLimit(`4600${fakerEN_US.helpers.fromRegExp(/[0-9]{10}/)}`, maxLength);
  }

  if (/(description|remark|comment|memo|content|detail|summary|message|note)/i.test(contextText)) {
    return applyLengthLimit(createNarrativeText(locale, maxLength), maxLength);
  }

  if (/(user_?name|username|login|account)/i.test(contextText)) {
    return applyLengthLimit(createUsernameValue(), maxLength);
  }

  if (/(domain|host)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.internet.domainName().toLowerCase(), maxLength);
  }

  if (/(postal|zip|postcode)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.location.zipCode(), maxLength);
  }

  if (/(file_?name|filename)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.system.fileName(), maxLength);
  }

  if (/(product|item|goods|sku)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.commerce.productName(), maxLength);
  }

  if (/(department|team|group|category|catalog)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.commerce.department(), maxLength);
  }

  if (/(brand|vendor|supplier|merchant)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.company.name(), maxLength);
  }

  if (/(tenant|organization|org_?name|customer|client)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.company.name(), maxLength);
  }

  if (/(nickname|alias|display_?name)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.person.firstName(), maxLength);
  }

  if (/(currency(_?code)?)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.helpers.arrayElement(['CNY', 'USD', 'EUR', 'JPY', 'HKD']), maxLength);
  }

  if (/(warehouse|storehouse)/i.test(contextText)) {
    return applyLengthLimit(`WH-${fakerEN_US.string.alphanumeric(4).toUpperCase()}`, maxLength);
  }

  if (/(shelf|bin|slot|location_?code)/i.test(contextText)) {
    return applyLengthLimit(`A-${String(fakerEN_US.number.int({ min: 1, max: 99 })).padStart(2, '0')}-${String(fakerEN_US.number.int({ min: 1, max: 20 })).padStart(2, '0')}`, maxLength);
  }

  if (/(tax_?(no|code)|vat)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.helpers.fromRegExp(/[A-Z0-9]{15,18}/), maxLength);
  }

  if (/(bank_?account|account_?no|iban)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.helpers.fromRegExp(/[0-9]{16,19}/), maxLength);
  }

  if (/(swift|bic)/i.test(contextText)) {
    return applyLengthLimit(fakerEN_US.helpers.fromRegExp(/[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?/), maxLength);
  }

  if (/(invoice_?title)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.company.name(), maxLength);
  }

  if (/(model|device_?model|product_?model)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.commerce.productName(), maxLength);
  }

  if (/(tag|label)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.lorem.words(2), maxLength);
  }

  if (/(plate|license_?plate|car_?no)/i.test(contextText)) {
    return locale === 'zh-CN'
    ? applyLengthLimit(fakerZH_CN.helpers.fromRegExp(/[京沪粤浙苏][A-Z][A-HJ-NP-Z0-9]{5}/), maxLength)
    : applyLengthLimit(fakerEN_US.helpers.fromRegExp(/[A-Z0-9]{6,8}/), maxLength);
  }

  if (/(firmware|os_?version|app_?version|version|build)/i.test(contextText)) {
    return applyLengthLimit(createSemanticVersionValue(rule.options.versionSegmentCount, rule.options.versionIncludePrefix), maxLength);
  }

  if (/(title|subject|caption)/i.test(contextText)) {
    return applyLengthLimit(localizedFaker.lorem.words(fakerEN_US.number.int({ min: 2, max: 4 })), maxLength);
  }

  if (/(device|machine|terminal|station|line|lot|shipment|parcel|waybill|express|invoice|contract|voucher|barcode|batch|ticket|tracking|serial|order_?no|ref(erence)?|code|number|num|sn|no)$/i.test(columnName)) {
    return applyLengthLimit(createCodeLikeValue(columnName, rowIndex), maxLength);
  }

  const length = randomLength(rule);
  return localizedFaker.string.alphanumeric(length);
}

function createNullableValue(rule: MockColumnRule): boolean {
  return rule.nullable && rule.options.nullProbability > 0 && Math.random() * 100 < rule.options.nullProbability;
}

function formatDatetimeValue(date: Date): string {
  return dayjs(date).format('YYYY-MM-DD HH:mm:ss');
}

function randomLength(rule: MockColumnRule): number {
  if (rule.options.maxLength <= rule.options.minLength) {
    return rule.options.maxLength;
  }

  return fakerZH_CN.number.int({ min: rule.options.minLength, max: rule.options.maxLength });
}

function coerceEnumValue(rawValue: string, designColumn: TableColumn | undefined): SqlLiteralValue {
  if (isBooleanColumn(designColumn ?? { type: '', name: '' })) {
    return rawValue === '1' || rawValue.toLowerCase() === 'true';
  }

  if (isIntegerColumn(designColumn ?? { type: '', name: '' }) || isDecimalColumn(designColumn ?? { type: '', name: '' })) {
    const numberValue = Number(rawValue);
    return Number.isNaN(numberValue) ? rawValue : numberValue;
  }

  return rawValue;
}

function createRuleValue(plan: MockDataPlan, rule: MockColumnRule, designColumn: TableColumn | undefined, rowIndex: number): SqlLiteralValue {
  const localizedFaker = getLocalizedFaker(plan.locale);

  if (createNullableValue(rule)) {
    return null;
  }

  switch (rule.ruleType) {
    case 'database-auto':
    case 'ignore':
      return null;
    case 'static':
      return rule.options.staticValue;
    case 'sequence':
      return rule.options.sequenceStart + (rowIndex * rule.options.sequenceStep);
    case 'random-string': {
      return createSemanticStringValue(plan.locale, rule, designColumn, rowIndex);
    }
    case 'random-number': {
      const value = fakerZH_CN.number.float({
        min: rule.options.minNumber,
        max: rule.options.maxNumber,
        fractionDigits: Math.max(0, rule.options.decimalPlaces),
      });
      if (isIntegerColumn(designColumn ?? { type: '', name: '' }) && rule.options.decimalPlaces === 0) {
        return Math.round(value);
      }
      return value;
    }
    case 'random-boolean':
      return fakerZH_CN.datatype.boolean({ probability: Math.max(0, Math.min(1, rule.options.trueProbability / 100)) });
    case 'enum': {
      const enumValues = normalizeEnumValues(rule.options.enumValuesText);
      if (enumValues.length === 0) {
        return '';
      }
      return coerceEnumValue(fakerZH_CN.helpers.arrayElement(enumValues), designColumn);
    }
    case 'username':
      return createUsernameValue();
    case 'domain':
      return fakerEN_US.internet.domainName().toLowerCase();
    case 'json':
      return createJsonLikeValue(plan.locale, rowIndex);
    case 'imei':
      return fakerEN_US.helpers.fromRegExp(/[0-9]{15}/);
    case 'imsi':
      return `4600${fakerEN_US.helpers.fromRegExp(/[0-9]{10}/)}`;
    case 'vehicle-plate':
      return plan.locale === 'zh-CN' ? fakerZH_CN.helpers.fromRegExp(/[京沪粤浙苏][A-Z][A-HJ-NP-Z0-9]{5}/) : fakerEN_US.helpers.fromRegExp(/[A-Z0-9]{6,8}/);
    case 'bank-account':
      return fakerEN_US.helpers.fromRegExp(/[0-9]{16,19}/);
    case 'swift-bic':
      return fakerEN_US.helpers.fromRegExp(/[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?/);
    case 'currency-code':
      return fakerEN_US.helpers.arrayElement(['CNY', 'USD', 'EUR', 'JPY', 'HKD']);
    case 'department':
      return localizedFaker.commerce.department();
    case 'barcode':
      return createCodeLikeValue('barcode', rowIndex);
    case 'tracking-number':
      return createCodeLikeValue('tracking', rowIndex);
    case 'sentence':
      return getLocalizedFaker(plan.locale).lorem.words(Math.max(2, Math.min(12, rule.options.sentenceWordCount)));
    case 'paragraph':
      return Array.from({ length: Math.max(2, Math.min(6, rule.options.paragraphSentenceCount)) }, () => getLocalizedFaker(plan.locale).lorem.sentence()).join(' ');
    case 'first-name':
      return localizedFaker.person.firstName();
    case 'last-name':
      return localizedFaker.person.lastName();
    case 'gender':
      return fakerEN_US.helpers.arrayElement(['Male', 'Female', 'Non-binary']);
    case 'ssn':
      return fakerEN_US.helpers.fromRegExp(/[0-9]{3}-[0-9]{2}-[0-9]{4}/);
    case 'country-code':
      return fakerEN_US.helpers.arrayElement(['US', 'CN', 'DE', 'FR', 'JP', 'GB', 'CA']);
    case 'state-abbr':
      return fakerEN_US.helpers.arrayElement(['CA', 'NY', 'TX', 'WA', 'PA', 'QC', 'NSW']);
    case 'time-zone':
      return fakerEN_US.helpers.arrayElement(['America/Los_Angeles', 'Europe/Berlin', 'Asia/Shanghai', 'Asia/Tokyo', 'Europe/Paris']);
    case 'flight-number':
      return fakerEN_US.helpers.arrayElement(['AA', 'DL', 'UA', 'JL', 'AF', 'CA']) + fakerEN_US.number.int({ min: 100, max: 9999 });
    case 'hospital-npi':
      return fakerEN_US.helpers.fromRegExp(/[0-9]{10}/);
    case 'icd10-code':
      return fakerEN_US.helpers.arrayElement(['A41.9', 'E11.9', 'I10', 'J18.9', 'M54.5', 'R50.9']);
    case 'currency-name':
      return fakerEN_US.helpers.arrayElement(['Dollar', 'Euro', 'Yen', 'Pound', 'Peso', 'Yuan']);
    case 'credit-card-type':
      return fakerEN_US.helpers.arrayElement(['visa', 'mastercard', 'amex', 'discover']);
    case 'stock-symbol':
      return fakerEN_US.helpers.arrayElement(['AAPL', 'MSFT', 'NVDA', 'AMZN', 'TSLA', 'META']);
    case 'airport-code':
      return fakerEN_US.helpers.arrayElement(['LAX', 'JFK', 'SFO', 'NRT', 'CDG', 'FRA']);
    case 'airport-name':
      return fakerEN_US.helpers.arrayElement(['Los Angeles International Airport', 'John F. Kennedy International Airport', 'Tokyo Narita Airport', 'Paris Charles de Gaulle Airport']);
    case 'airline-code':
      return fakerEN_US.helpers.arrayElement(['AA', 'DL', 'UA', 'JL', 'AF', 'CA']);
    case 'airline-name':
      return fakerEN_US.helpers.arrayElement(['American Airlines', 'Delta Air Lines', 'United Airlines', 'Japan Airlines', 'Air France']);
    case 'drug-company':
      return fakerEN_US.helpers.arrayElement(['Eli Lilly and Company', 'Novartis', 'Pfizer', 'Teva Pharmaceuticals']);
    case 'drug-name':
      return fakerEN_US.helpers.arrayElement(['Lipitor', 'Nexium', 'Cialis', 'Naproxen Sodium', 'Acetaminophen']);
    case 'hospital-name':
      return fakerEN_US.helpers.arrayElement(['Mayo Clinic', 'Massachusetts General Hospital', 'Johns Hopkins Hospital', 'Cleveland Clinic']);
    case 'nhs-number':
      return fakerEN_US.helpers.fromRegExp(/[0-9]{3} [0-9]{3} [0-9]{4}/);
    case 'iban':
      return fakerEN_US.helpers.arrayElement(['FR76 3000 6000 0112 3456 7890 189', 'DE89 3704 0044 0532 0130 00', 'GB29 NWBK 6016 1331 9268 19']);
    case 'product-category':
      return fakerEN_US.helpers.arrayElement(['Electronics', 'Books', 'Health & Beauty', 'Outdoor', 'Home Decor']);
    case 'product-description':
      return fakerEN_US.helpers.arrayElement(['Compact travel adapter with fast charging support.', 'Plant-based snack with sea salt flavor.', 'Industrial sensor enclosure with anti-static coating.']);
    case 'stock-market':
      return fakerEN_US.helpers.arrayElement(['NASDAQ', 'NYSE', 'LSE', 'SSE', 'HKEX']);
    case 'language':
      return fakerEN_US.helpers.arrayElement(['English', 'German', 'Japanese', 'French', 'Chinese']);
    case 'language-code':
      return fakerEN_US.helpers.arrayElement(['en', 'de', 'ja', 'fr', 'zh']);
    case 'university':
      return fakerEN_US.helpers.arrayElement(['Pepperdine University', 'University of Texas', 'The Johns Hopkins University', 'Tsinghua University']);
    case 'airport-country-code':
      return fakerEN_US.helpers.arrayElement(['US', 'CA', 'DE', 'FR', 'JP', 'CN']);
    case 'flight-duration-hours':
      return Number(fakerEN_US.number.float({ min: 0.75, max: 14, fractionDigits: 2 }));
    case 'money':
      return Number(fakerEN_US.number.float({ min: 1, max: 10000, fractionDigits: 2 }));
    case 'product-price':
      return Number(fakerEN_US.number.float({ min: 1, max: 3000, fractionDigits: 2 }));
    case 'mime-type':
      return fakerEN_US.helpers.arrayElement(['application/json', 'image/png', 'text/plain', 'application/pdf', 'text/csv']);
    case 'file-extension':
      return fakerEN_US.helpers.arrayElement(['json', 'png', 'txt', 'pdf', 'csv', 'xlsx']);
    case 'port-number':
      return fakerEN_US.number.int({ min: 1, max: 65535 });
    case 'http-status-code':
      return fakerEN_US.helpers.arrayElement([200, 201, 202, 204, 301, 302, 400, 401, 403, 404, 409, 500, 502]);
    case 'file-name':
      return fakerEN_US.system.fileName();
    case 'version':
      return createSemanticVersionValue(rule.options.versionSegmentCount, rule.options.versionIncludePrefix);
    case 'postal-code':
      return localizedFaker.location.zipCode();
    case 'ipv4':
      return fakerEN_US.internet.ipv4();
    case 'ipv4-cidr':
      return `${fakerEN_US.internet.ipv4()}/${fakerEN_US.number.int({ min: 8, max: 30 })}`;
    case 'ipv6':
      return fakerEN_US.internet.ipv6();
    case 'mac-address':
      return fakerEN_US.internet.mac();
    case 'md5':
      return createHashValue(32);
    case 'sha1':
      return createHashValue(40);
    case 'sha256':
      return createHashValue(64);
    case 'name-localized':
      return localizedFaker.person.fullName();
    case 'name-zh':
      return fakerZH_CN.person.fullName();
    case 'name-en':
      return fakerEN_US.person.fullName();
    case 'phone':
      return createPhoneValue(plan.locale);
    case 'email':
      return createEmailValue(rule.options.emailDomain);
    case 'id-card':
      return createChinaIdCardNumber();
    case 'hk-id-card':
      return createHongKongIdCardNumber();
    case 'ip':
      return fakerEN_US.internet.ip();
    case 'country':
      return localizedFaker.location.country();
    case 'province':
      return localizedFaker.location.state();
    case 'city':
      return localizedFaker.location.city();
    case 'address':
      return createLocalizedAddress(plan.locale);
    case 'company':
      return localizedFaker.company.name();
    case 'job-title':
      return localizedFaker.person.jobTitle();
    case 'bank-card':
      return fakerZH_CN.finance.creditCardNumber();
    case 'uuid':
      return createUuidValue();
    case 'url':
      return fakerEN_US.internet.url();
    case 'hex-color':
      return rule.options.includeHashPrefix
        ? fakerEN_US.color.rgb({ format: 'hex', casing: 'upper' })
        : fakerEN_US.color.rgb({ format: 'hex', casing: 'upper' }).replace(/^#/, '');
    case 'latitude':
      return createCoordinateValue('latitude', rule.options.coordinateDecimalPlaces);
    case 'longitude':
      return createCoordinateValue('longitude', rule.options.coordinateDecimalPlaces);
    case 'current-datetime':
      return {
        sql: currentDatetimeExpression(plan.provider),
        preview: createCurrentDatetimePreview(),
      };
    case 'random-past-datetime':
      return formatDatetimeValue(fakerEN_US.date.recent({ days: Math.max(1, rule.options.daysRange) }));
    case 'random-future-datetime':
      return formatDatetimeValue(fakerEN_US.date.soon({ days: Math.max(1, rule.options.daysRange) }));
    case 'regex': {
      try {
        return new RandExp(new RegExp(rule.options.regexPattern)).gen();
      } catch {
        return rule.options.regexPattern;
      }
    }
    case 'sql-expression':
      return {
        sql: rule.options.sqlExpression || 'NULL',
        preview: rule.options.sqlExpression || 'NULL',
      };
    default:
      return '';
  }
}

function normalizeValueForColumn(value: SqlLiteralValue, designColumn: TableColumn | undefined): SqlLiteralValue {
  if (value === null || typeof value === 'number' || typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'object') {
    return value;
  }

  const limited = applyLengthLimit(value, getColumnMaxLength(designColumn));
  if (isIntegerColumn(designColumn ?? { type: '', name: '' })) {
    const numericValue = Number(limited);
    return Number.isNaN(numericValue) ? 0 : Math.round(numericValue);
  }

  if (isDecimalColumn(designColumn ?? { type: '', name: '' })) {
    const numericValue = Number(limited);
    return Number.isNaN(numericValue) ? 0 : numericValue;
  }

  if (isBooleanColumn(designColumn ?? { type: '', name: '' })) {
    return limited === '1' || limited.toLowerCase() === 'true';
  }

  return limited;
}

function ensureUniqueValue(
  rowIndex: number,
  rule: MockColumnRule,
  designColumn: TableColumn | undefined,
  candidate: SqlLiteralValue,
  usedValues: Map<string, Set<string>>,
): SqlLiteralValue {
  if (!rule.unique || candidate === null) {
    return candidate;
  }

  const bucket = usedValues.get(rule.columnName) ?? new Set<string>();
  usedValues.set(rule.columnName, bucket);

  const stringify = (value: SqlLiteralValue) => {
    if (value === null) {
      return 'NULL';
    }

    return typeof value === 'object' ? value.preview : String(value);
  };
  let nextValue = candidate;
  let attempts = 0;
  while (bucket.has(stringify(nextValue)) && attempts < 24) {
    attempts += 1;
    if (rule.ruleType === 'sequence') {
      nextValue = rule.options.sequenceStart + ((rowIndex + attempts) * rule.options.sequenceStep);
      continue;
    }

    if (typeof nextValue === 'string') {
      nextValue = applyLengthLimit(`${nextValue}_${rowIndex + attempts}`, getColumnMaxLength(designColumn));
      continue;
    }

    if (typeof nextValue === 'number') {
      nextValue += attempts;
      continue;
    }

    if (typeof nextValue === 'object') {
      nextValue = { ...nextValue, preview: `${nextValue.preview}_${rowIndex + attempts}` };
      continue;
    }

    break;
  }

  if (bucket.has(stringify(nextValue))) {
    throw new Error(`字段 ${rule.columnName} 的生成数据违反唯一约束，请扩大随机范围或调整规则。`);
  }

  bucket.add(stringify(nextValue));
  return nextValue;
}

function valueToPreview(value: SqlLiteralValue): string {
  if (value === null) {
    return 'NULL';
  }

  if (typeof value === 'object') {
    return value.preview;
  }

  return String(value);
}

function escapeSqlString(value: string): string {
  return value.replace(/'/g, "''");
}

function valueToSqlLiteral(provider: ProviderType, value: SqlLiteralValue): string {
  if (value === null) {
    return 'NULL';
  }

  if (typeof value === 'object') {
    return value.sql;
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? String(value) : 'NULL';
  }

  if (typeof value === 'boolean') {
    return provider === 'postgresql' ? (value ? 'TRUE' : 'FALSE') : (value ? '1' : '0');
  }

  return `'${escapeSqlString(value)}'`;
}

function exportCsv(rows: GeneratedPreviewRow[]): string {
  if (rows.length === 0) {
    return '';
  }

  const headers = Object.keys(rows[0] ?? {});
  const body = rows.map((row) => headers.map((header) => {
    const cell = String(row[header] ?? '');
    if (/[,"\n]/.test(cell)) {
      return `"${cell.replace(/"/g, '""')}"`;
    }
    return cell;
  }).join(','));

  return [headers.join(','), ...body].join('\n');
}

function buildBeforeInsertSql(plan: MockDataPlan, warnings: string[]): string {
  if (plan.beforeInsert === 'none') {
    return '';
  }

  const qualifiedTable = quoteQualifiedTable(plan.provider, plan.table);
  if (plan.beforeInsert === 'truncate') {
    if (plan.provider === 'sqlite') {
      warnings.push('SQLite 不支持 TRUNCATE TABLE，已自动降级为 DELETE FROM。');
      return `DELETE FROM ${qualifiedTable};\n\n`;
    }

    return `TRUNCATE TABLE ${qualifiedTable};\n\n`;
  }

  return `DELETE FROM ${qualifiedTable};\n\n`;
}

function chunkRows<T>(rows: T[], batchSize: number): T[][] {
  const chunks: T[][] = [];
  for (let index = 0; index < rows.length; index += batchSize) {
    chunks.push(rows.slice(index, index + batchSize));
  }
  return chunks;
}

export function generateRulePreviewExamples(plan: MockDataPlan, rule: MockColumnRule, count = 3): string[] {
  const designColumn = plan.design.columns.find((column) => column.name === rule.columnName);
  const usedValues = new Map<string, Set<string>>();
  const samples: string[] = [];

  for (let rowIndex = 0; rowIndex < count; rowIndex += 1) {
    const rawValue = createRuleValue(plan, rule, designColumn, rowIndex);
    const previewValue = rawValue;
    const uniqueValue = ensureUniqueValue(rowIndex, rule, designColumn, previewValue, usedValues);
    samples.push(valueToPreview(uniqueValue));
  }

  return samples;
}

export function generateMockDataset(plan: MockDataPlan): GeneratedMockDataset {
  const previewRowLimit = Math.max(1, plan.previewRowLimit ?? 10);
  const insertableColumns = plan.columns.filter((column) => column.ruleType !== 'ignore' && column.ruleType !== 'database-auto');
  const designColumnMap = new Map(plan.design.columns.map((column) => [column.name, column] as const));
  const usedValues = new Map<string, Set<string>>();
  const warnings: string[] = [];
  const previewRows: GeneratedPreviewRow[] = [];
  const sqlRows: SqlLiteralValue[][] = [];

  for (let rowIndex = 0; rowIndex < plan.rowCount; rowIndex++) {
    const rowPreview: GeneratedPreviewRow = {};
    const rowValues: SqlLiteralValue[] = [];

    for (const rule of insertableColumns) {
      const designColumn = designColumnMap.get(rule.columnName);
      const rawValue = createRuleValue(plan, rule, designColumn, rowIndex);
      const normalizedValue = normalizeValueForColumn(rawValue, designColumn);
      const uniqueValue = ensureUniqueValue(rowIndex, rule, designColumn, normalizedValue, usedValues);
      rowValues.push(uniqueValue);
      if (previewRows.length < previewRowLimit) {
        rowPreview[rule.columnName] = valueToPreview(uniqueValue);
      }
    }

    if (previewRows.length < previewRowLimit) {
      previewRows.push(rowPreview);
    }
    sqlRows.push(rowValues);
  }

  const qualifiedTable = quoteQualifiedTable(plan.provider, plan.table);
  const columnList = insertableColumns.map((column) => quoteIdentifier(plan.provider, column.columnName)).join(', ');
  const sqlStatements: string[] = [];

  if (insertableColumns.length === 0) {
    sqlStatements.push(...Array.from({ length: plan.rowCount }, () => `INSERT INTO ${qualifiedTable} DEFAULT VALUES;`));
  } else {
    const batches = chunkRows(sqlRows, plan.provider === 'sqlserver' ? 500 : 1000);
    for (const batch of batches) {
      const values = batch
        .map((row) => `(${row.map((value) => valueToSqlLiteral(plan.provider, value)).join(', ')})`)
        .join(',\n');
      sqlStatements.push(`INSERT INTO ${qualifiedTable} (${columnList})\nVALUES\n${values};`);
    }
  }

  const sql = `${buildBeforeInsertSql(plan, warnings)}${sqlStatements.join('\n\n')}`.trim();
  const exportRows = sqlRows.map((row) => Object.fromEntries(insertableColumns.map((column, index) => [column.columnName, valueToPreview(row[index] ?? null)] )));
  const json = JSON.stringify(previewRows.length === plan.rowCount ? previewRows : exportRows, null, 2);
  const csv = exportCsv(exportRows);

  return {
    sql,
    csv,
    json,
    previewRows,
    warnings,
  };
}
