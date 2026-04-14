import type {
  SaveSqlServerLoginRequest,
  SqlServerLoginDetail,
  SqlServerLoginEditorOptions,
  SqlServerLoginSqlPreviewResponse,
  SqlServerLoginSummary,
} from '../types/sqlserver-login';

async function requestJson<T>(input: string, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init);
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `${response.status} ${response.statusText}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return await response.json() as T;
}

export async function fetchSqlServerLogins(connectionId: string): Promise<SqlServerLoginSummary[]> {
  const response = await requestJson<{ logins: SqlServerLoginSummary[] }>(`/api/explorer/sqlserver-logins?connectionId=${encodeURIComponent(connectionId)}`);
  return response.logins;
}

export async function fetchSqlServerLoginEditorOptions(connectionId: string): Promise<SqlServerLoginEditorOptions> {
  return await requestJson<SqlServerLoginEditorOptions>(`/api/explorer/sqlserver-logins/editor-options?connectionId=${encodeURIComponent(connectionId)}`);
}

export async function fetchSqlServerLoginDetail(connectionId: string, loginName: string): Promise<SqlServerLoginDetail> {
  return await requestJson<SqlServerLoginDetail>(`/api/explorer/sqlserver-logins/${encodeURIComponent(loginName)}?connectionId=${encodeURIComponent(connectionId)}`);
}

export async function previewSqlServerLogin(request: SaveSqlServerLoginRequest): Promise<string> {
  const response = await requestJson<SqlServerLoginSqlPreviewResponse>('/api/explorer/sqlserver-logins/sql-preview', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  return response.sql;
}

export async function saveSqlServerLogin(request: SaveSqlServerLoginRequest): Promise<void> {
  await requestJson<void>('/api/explorer/sqlserver-logins', {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });
}

export async function deleteSqlServerLogin(connectionId: string, loginName: string): Promise<void> {
  await requestJson<void>(`/api/explorer/sqlserver-logins/${encodeURIComponent(loginName)}?connectionId=${encodeURIComponent(connectionId)}`, {
    method: 'DELETE',
  });
}