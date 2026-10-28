import { request } from '@/utils/request';

export interface PasskeySummary {
  id: string;
  name: string;
  rpId: string;
  createdAt: string;
  lastUsedAt?: string;
}
export interface AccountSecurity {
  totpEnabled: boolean;
  recoveryCodesRemaining: number;
  passkeys: PasskeySummary[];
}
export interface SecurityProof {
  password: string;
  twoFactorCode?: string;
}
export interface Ceremony {
  challengeId: string;
  options: any;
}
export function securityPost<T = { token: string }>(path: string, data: unknown) {
  return request.post<T>({ url: `/api/user/security/${path}`, data }, { requestToSlaveNode: false });
}
export function passkeyLoginOptions() {
  return request.post<Ceremony>(
    { url: '/api/auth/passkey/options', baseURL: window.location.origin },
    { requestToSlaveNode: false, withToken: false },
  );
}
export function passkeyLoginVerify(data: unknown) {
  return request.post<{ token: string; userInfo: any }>(
    { url: '/api/auth/passkey/verify', baseURL: window.location.origin, data },
    { requestToSlaveNode: false, withToken: false },
  );
}
