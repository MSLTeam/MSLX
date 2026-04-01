import { request } from '@/utils/request';

const API_BASE_URL = '/api/frp/chmlfrp';
const ACCOUNT_OAUTH_ISSUER = 'https://account-api.qzhua.net';
const ACCOUNT_OAUTH_CLIENT_ID = '019d4510d87276958d6248aed40407e3';
const STORAGE_KEY = 'chmlfrp_user';
const LEGACY_STORAGE_KEY = 'chmlfrp-user-token';
const DEVICE_CODE_DEFAULT_SCOPE = 'profile email offline_access chmlfrp_api';
const CHMLFRP_PROXY_AUTHORIZATION_HEADER = 'X-Chmlfrp-Authorization';

interface RawHttpResponse {
  status: number;
  body: string;
}

export interface StoredChmlFrpUser {
  username: string;
  usergroup: string;
  userimg?: string | null;
  usertoken?: string;
  accessToken?: string;
  refreshToken?: string;
  accessTokenExpiresAt?: number;
  tokenType?: string;
  tunnelCount?: number;
  tunnel?: number;
}

export interface ChmlFrpUserInfo {
  id: number;
  username: string;
  password: string | null;
  userimg: string;
  qq: string;
  email: string;
  usertoken: string;
  usergroup: string;
  bandwidth: number;
  tunnel: number;
  realname: string;
  integral: number;
  term: string;
  scgm: string;
  regtime: string;
  realname_count: number | null;
  total_download: number | null;
  total_upload: number | null;
  tunnelCount: number;
  totalCurConns: number;
}

export interface ChmlFrpTunnel {
  id: number;
  name: string;
  localip: string;
  type: string;
  nport: number;
  dorp: string;
  node: string;
  ap: string;
  uptime: string | null;
  client_version: string | null;
  today_traffic_in: number | null;
  today_traffic_out: number | null;
  cur_conns: number | null;
  nodestate: string;
  ip: string;
  node_ip: string;
  node_ipv6: string | null;
  server_port: number;
  node_token: string;
  state?: string | boolean;
}

export interface ChmlFrpNodeInfo {
  id: number;
  name: string;
  area: string;
  nodegroup: string;
  notes: string;
}

export interface CreateChmlFrpTunnelParams {
  tunnelname: string;
  node: string;
  localip: string;
  porttype: string;
  localport: number;
  encryption: boolean;
  compression: boolean;
  extraparams: string;
  remoteport?: number;
}

export interface DeviceAuthorizationResponse {
  device_code: string;
  user_code: string;
  verification_uri: string;
  verification_uri_complete?: string;
  expires_in?: number;
  interval?: number;
}

export interface DeviceTokenResponse {
  access_token?: string;
  token_type?: string;
  expires_in?: number;
  refresh_token?: string;
  scope?: string;
  error?: string;
  error_description?: string;
  error_uri?: string;
}

function normalizeStoredUser(user: StoredChmlFrpUser | null): StoredChmlFrpUser | null {
  if (!user) {
    return null;
  }

  const normalized: StoredChmlFrpUser = { ...user };

  if (normalized.accessTokenExpiresAt != null) {
    const expiresAt = Number(normalized.accessTokenExpiresAt);
    normalized.accessTokenExpiresAt = Number.isFinite(expiresAt) ? expiresAt : undefined;
  }

  return normalized;
}

function getOAuthUrl(path: string) {
  return new URL(path, ACCOUNT_OAUTH_ISSUER).toString();
}

function getOAuthHeaders() {
  return {
    'Content-Type': 'application/x-www-form-urlencoded',
    Accept: 'application/json',
  };
}

function getOAuthErrorMessage(response: DeviceTokenResponse | undefined, fallback: string) {
  if (!response) {
    return fallback;
  }

  return response.error_description || response.error || fallback;
}

function parseOAuthJson<T>(response: RawHttpResponse, fallback: string): T {
  try {
    return JSON.parse(response.body) as T;
  } catch {
    const content = response.body.trim().toLowerCase();

    if (content.startsWith('<!doctype html') || content.startsWith('<html')) {
      throw new Error('账户中心返回了登录页而不是 OAuth 响应，请检查设备码授权配置。');
    }

    if (response.status === 401) {
      throw new Error('账户中心拒绝了当前客户端，请检查 ChmlFrp OAuth 配置。');
    }

    throw new Error(fallback);
  }
}

async function oauthRequest(path: string, body: URLSearchParams): Promise<RawHttpResponse> {
  const response = await fetch(getOAuthUrl(path), {
    method: 'POST',
    headers: getOAuthHeaders(),
    body: body.toString(),
    cache: 'no-store',
    credentials: 'omit',
  });

  return {
    status: response.status,
    body: await response.text(),
  };
}

function getLegacyApiToken(user: StoredChmlFrpUser | null) {
  if (!user?.usertoken) {
    return undefined;
  }

  if (user.accessToken && user.usertoken === user.accessToken) {
    return undefined;
  }

  return user.usertoken;
}

function getCurrentAccessToken(user: StoredChmlFrpUser | null) {
  if (user?.accessToken?.trim()) {
    return user.accessToken.trim();
  }

  return undefined;
}

function isAccessTokenExpiring(user: StoredChmlFrpUser | null) {
  const expiresAt = user?.accessTokenExpiresAt;

  if (!expiresAt) {
    return false;
  }

  return Date.now() >= expiresAt - 60_000;
}

function toBearerHeader(token: string) {
  return token.startsWith('Bearer ') ? token : `Bearer ${token}`;
}

function normalizeApiData<T>(response: any): T {
  return response?.code === 200 ? response.data : response;
}

export function getStoredChmlFrpUser(): StoredChmlFrpUser | null {
  const saved = localStorage.getItem(STORAGE_KEY);

  if (saved) {
    try {
      return normalizeStoredUser(JSON.parse(saved) as StoredChmlFrpUser);
    } catch {
      localStorage.removeItem(STORAGE_KEY);
    }
  }

  const legacyToken = localStorage.getItem(LEGACY_STORAGE_KEY);

  if (!legacyToken) {
    return null;
  }

  const migratedUser: StoredChmlFrpUser = {
    username: '',
    usergroup: '',
    usertoken: legacyToken,
  };

  saveStoredChmlFrpUser(migratedUser);
  localStorage.removeItem(LEGACY_STORAGE_KEY);

  return migratedUser;
}

export function saveStoredChmlFrpUser(user: StoredChmlFrpUser) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(normalizeStoredUser(user)));
  if (user.usertoken) {
    localStorage.setItem(LEGACY_STORAGE_KEY, user.usertoken);
  } else {
    localStorage.removeItem(LEGACY_STORAGE_KEY);
  }
}

export function clearStoredChmlFrpUser() {
  localStorage.removeItem(STORAGE_KEY);
  localStorage.removeItem(LEGACY_STORAGE_KEY);
}

async function refreshAccessToken(refreshToken: string) {
  const body = new URLSearchParams();
  body.set('grant_type', 'refresh_token');
  body.set('refresh_token', refreshToken);
  body.set('client_id', ACCOUNT_OAUTH_CLIENT_ID);

  const response = await oauthRequest('/oauth2/token', body);

  return parseOAuthJson<DeviceTokenResponse>(response, '账户服务返回了无法解析的刷新响应');
}

async function ensureAuthenticatedUser() {
  const storedUser = getStoredChmlFrpUser();

  if (!storedUser) {
    throw new Error('登录信息已过期，请重新授权');
  }

  const currentAccessToken = getCurrentAccessToken(storedUser);

  if (currentAccessToken) {
    if (storedUser.refreshToken && isAccessTokenExpiring(storedUser)) {
      const refreshed = await refreshAccessToken(storedUser.refreshToken);

      if (!refreshed.access_token) {
        clearStoredChmlFrpUser();
        throw new Error(getOAuthErrorMessage(refreshed, '登录信息已过期，请重新授权'));
      }

      const updatedUser: StoredChmlFrpUser = {
        ...storedUser,
        accessToken: refreshed.access_token,
        refreshToken: refreshed.refresh_token || storedUser.refreshToken,
        accessTokenExpiresAt: refreshed.expires_in
          ? Date.now() + refreshed.expires_in * 1000
          : storedUser.accessTokenExpiresAt,
        tokenType: refreshed.token_type || storedUser.tokenType || 'Bearer',
      };

      saveStoredChmlFrpUser(updatedUser);

      return {
        storedUser: updatedUser,
        accessToken: updatedUser.accessToken,
        legacyToken: getLegacyApiToken(updatedUser),
      };
    }

    return {
      storedUser,
      accessToken: currentAccessToken,
      legacyToken: getLegacyApiToken(storedUser),
    };
  }

  const legacyToken = getLegacyApiToken(storedUser);

  if (legacyToken) {
    return {
      storedUser,
      legacyToken,
    };
  }

  clearStoredChmlFrpUser();
  throw new Error('登录信息已过期，请重新授权');
}

export async function getChmlFrpAuthorizationHeader() {
  const { accessToken, legacyToken } = await ensureAuthenticatedUser();
  return toBearerHeader(accessToken || legacyToken!);
}

export async function createDeviceAuthorization(scope = DEVICE_CODE_DEFAULT_SCOPE) {
  const body = new URLSearchParams();
  body.set('client_id', ACCOUNT_OAUTH_CLIENT_ID);

  const normalizedScope = scope
    .split(/[,\s]+/)
    .map((item) => item.trim())
    .filter(Boolean)
    .join(' ');

  if (normalizedScope) {
    body.set('scope', normalizedScope);
  }

  const response = await oauthRequest('/oauth2/device_authorization', body);
  const data = parseOAuthJson<DeviceAuthorizationResponse | DeviceTokenResponse>(
    response,
    '账户服务返回了无法解析的响应',
  );

  if (response.status >= 200 && response.status < 300 && data && 'device_code' in data) {
    return data;
  }

  throw new Error(getOAuthErrorMessage(data ?? undefined, '申请设备授权失败'));
}

export async function exchangeDeviceCodeForToken(deviceCode: string) {
  const body = new URLSearchParams();
  body.set('grant_type', 'urn:ietf:params:oauth:grant-type:device_code');
  body.set('device_code', deviceCode);
  body.set('client_id', ACCOUNT_OAUTH_CLIENT_ID);

  const response = await oauthRequest('/oauth2/token', body);

  return parseOAuthJson<DeviceTokenResponse>(response, '账户服务返回了无法解析的令牌响应');
}

export async function fetchChmlFrpUserInfo(token?: string) {
  const authorization = token ? toBearerHeader(token) : await getChmlFrpAuthorizationHeader();
  const response = await request.get(
    {
      url: `${API_BASE_URL}/userinfo`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
    }
  );

  const data = normalizeApiData<ChmlFrpUserInfo>(response);

  if (data?.username) {
    return data;
  }

  throw new Error('未获取到有效的用户信息');
}

export async function loginWithAccessToken(
  accessToken: string,
  tokenResponse?: Pick<DeviceTokenResponse, 'refresh_token' | 'expires_in' | 'token_type'>,
) {
  const userInfo = await fetchChmlFrpUserInfo(accessToken);

  return {
    username: userInfo.username,
    usergroup: userInfo.usergroup,
    userimg: userInfo.userimg,
    usertoken: userInfo.usertoken,
    accessToken,
    refreshToken: tokenResponse?.refresh_token,
    accessTokenExpiresAt: tokenResponse?.expires_in
      ? Date.now() + tokenResponse.expires_in * 1000
      : undefined,
    tokenType: tokenResponse?.token_type || 'Bearer',
    tunnelCount: userInfo.tunnelCount,
    tunnel: userInfo.tunnel,
  } satisfies StoredChmlFrpUser;
}

export async function fetchChmlFrpTunnels() {
  const authorization = await getChmlFrpAuthorizationHeader();
  const response = await request.get(
    {
      url: `${API_BASE_URL}/tunnel`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
    }
  );

  const data = normalizeApiData<ChmlFrpTunnel[]>(response);

  if (Array.isArray(data)) {
    return data;
  }

  throw new Error('获取隧道列表失败');
}

export async function fetchChmlFrpNodes() {
  const authorization = await getChmlFrpAuthorizationHeader();
  const response = await request.get(
    {
      url: `${API_BASE_URL}/node`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
    }
  );

  const data = normalizeApiData<ChmlFrpNodeInfo[]>(response);

  if (Array.isArray(data)) {
    return data;
  }

  throw new Error('获取节点列表失败');
}

export async function createChmlFrpTunnel(params: CreateChmlFrpTunnelParams) {
  const authorization = await getChmlFrpAuthorizationHeader();

  return request.post(
    {
      url: `${API_BASE_URL}/create-tunnel`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
      data: params,
    }
  );
}

export async function deleteChmlFrpTunnel(tunnelId: number) {
  const authorization = await getChmlFrpAuthorizationHeader();

  return request.get(
    {
      url: `${API_BASE_URL}/delete-tunnel?tunnelId=${tunnelId}`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
    }
  );
}

export async function fetchChmlFrpTunnelConfig(node: string, tunnelName: string) {
  const authorization = await getChmlFrpAuthorizationHeader();
  const response = await request.get(
    {
      url: `${API_BASE_URL}/tunnel-config?node=${encodeURIComponent(node)}&tunnelName=${encodeURIComponent(tunnelName)}`,
      headers: { [CHMLFRP_PROXY_AUTHORIZATION_HEADER]: authorization },
    }
  );

  const data = normalizeApiData<string>(response);

  if (typeof data === 'string' && data) {
    return data;
  }

  throw new Error('获取配置失败：内容为空或格式异常');
}
