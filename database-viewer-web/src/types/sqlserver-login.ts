export type SqlServerLoginAuthenticationType = 'sql' | 'windows' | 'certificate' | 'asymmetric-key';

export type SqlServerPermissionState = 'none' | 'grant' | 'grant-with-grant-option' | 'deny';

export interface SqlServerLoginSummary {
  name: string;
  principalId: number;
  defaultDatabase?: string | null;
  isEnabled: boolean;
  principalTypeCode: string;
  principalTypeLabel: string;
  authenticationType: SqlServerLoginAuthenticationType;
  isSystem: boolean;
  supportsGeneralEditing: boolean;
  supportsPasswordEditing: boolean;
  supportsRoleEditing: boolean;
  supportsPermissionEditing: boolean;
  canDelete: boolean;
}

export interface SqlServerPermissionAssignment {
  name: string;
  state: SqlServerPermissionState;
}

export interface SqlServerEndpointPermission {
  endpointName: string;
  permissions: SqlServerPermissionAssignment[];
}

export interface SqlServerLoginPermissionTarget {
  loginName: string;
  principalTypeCode: string;
  principalTypeLabel: string;
  isSystem: boolean;
  permissions: SqlServerPermissionAssignment[];
}

export interface SqlServerSelectOption {
  value: string;
  label: string;
}

export interface SqlServerLoginEditorOptions {
  databases: string[];
  languages: SqlServerSelectOption[];
  serverRoles: string[];
  serverPermissions: string[];
  endpointPermissions: string[];
  loginPermissions: string[];
  endpoints: string[];
  loginTargets: SqlServerLoginSummary[];
}

export interface SqlServerLoginDetail {
  originalName: string;
  name: string;
  principalId: number;
  principalTypeCode: string;
  principalTypeLabel: string;
  authenticationType: SqlServerLoginAuthenticationType;
  isSystem: boolean;
  canDelete: boolean;
  supportsGeneralEditing: boolean;
  supportsPasswordEditing: boolean;
  supportsRoleEditing: boolean;
  supportsPermissionEditing: boolean;
  supportsRename: boolean;
  supportsEnableDisable: boolean;
  supportsPasswordPolicy: boolean;
  defaultDatabase?: string | null;
  defaultLanguage?: string | null;
  isEnabled: boolean;
  checkPasswordPolicy: boolean;
  checkPasswordExpiration: boolean;
  serverRoles: string[];
  serverPermissions: SqlServerPermissionAssignment[];
  endpointPermissions: SqlServerEndpointPermission[];
  loginPermissions: SqlServerLoginPermissionTarget[];
  options: SqlServerLoginEditorOptions;
}

export interface SaveSqlServerLoginRequest {
  connectionId: string;
  originalName?: string | null;
  name: string;
  authenticationType: SqlServerLoginAuthenticationType;
  password?: string | null;
  oldPassword?: string | null;
  useOldPassword: boolean;
  isEnabled: boolean;
  checkPasswordPolicy: boolean;
  checkPasswordExpiration: boolean;
  mustChangePasswordOnNextLogin: boolean;
  defaultDatabase?: string | null;
  defaultLanguage?: string | null;
  serverRoles: string[];
  serverPermissions: SqlServerPermissionAssignment[];
  endpointPermissions: SqlServerEndpointPermission[];
  loginPermissions: SqlServerLoginPermissionTarget[];
}

export interface SqlServerLoginSqlPreviewResponse {
  sql: string;
}