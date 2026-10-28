export function passkeySupported(baseUrl = window.location.origin) {
  try {
    return (
      window.isSecureContext &&
      !!window.PublicKeyCredential &&
      new URL(baseUrl || window.location.origin, window.location.origin).origin === window.location.origin &&
      (window.location.protocol === 'https:' || window.location.hostname === 'localhost') &&
      !/^\[?[\d:.]+\]?$/.test(window.location.hostname)
    );
  } catch {
    return false;
  }
}

function decode(value: string): Uint8Array<ArrayBuffer> {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
  return Uint8Array.from(atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=')), (c) => c.charCodeAt(0));
}
function encode(value: ArrayBuffer | ArrayBufferView): string {
  const bytes =
    value instanceof ArrayBuffer
      ? new Uint8Array(value)
      : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
  return btoa(Array.from(bytes, (b) => String.fromCharCode(b)).join(''))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}
function credentialJSON(credential: PublicKeyCredential) {
  const modern = credential as PublicKeyCredential & { toJSON?: () => unknown };
  if (modern.toJSON) return modern.toJSON();
  const response = credential.response;
  const encoded: Record<string, unknown> = { clientDataJSON: encode(response.clientDataJSON) };
  if ('attestationObject' in response) {
    const attestation = response as AuthenticatorAttestationResponse;
    encoded.attestationObject = encode(attestation.attestationObject);
    encoded.transports = attestation.getTransports?.() || [];
  } else {
    const assertion = response as AuthenticatorAssertionResponse;
    encoded.authenticatorData = encode(assertion.authenticatorData);
    encoded.signature = encode(assertion.signature);
    encoded.userHandle = assertion.userHandle ? encode(assertion.userHandle) : null;
  }
  return {
    id: credential.id,
    rawId: encode(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: encoded,
  };
}

export async function createPasskey(options: any, signal: AbortSignal) {
  const api = PublicKeyCredential as any;
  const publicKey = api.parseCreationOptionsFromJSON
    ? api.parseCreationOptionsFromJSON(options)
    : {
        ...options,
        challenge: decode(options.challenge),
        user: { ...options.user, id: decode(options.user.id) },
        excludeCredentials: options.excludeCredentials?.map((c: any) => ({ ...c, id: decode(c.id) })),
      };
  const credential = await navigator.credentials.create({ publicKey, signal });
  if (!credential) throw new Error('未创建通行密钥');
  return credentialJSON(credential as PublicKeyCredential);
}

export async function getPasskey(options: any, signal: AbortSignal) {
  const api = PublicKeyCredential as any;
  const publicKey = api.parseRequestOptionsFromJSON
    ? api.parseRequestOptionsFromJSON(options)
    : {
        ...options,
        challenge: decode(options.challenge),
        allowCredentials: options.allowCredentials?.map((c: any) => ({ ...c, id: decode(c.id) })),
      };
  const credential = await navigator.credentials.get({ publicKey, signal });
  if (!credential) throw new Error('未选择通行密钥');
  return credentialJSON(credential as PublicKeyCredential);
}

export function passkeyCancelled(error: any) {
  return ['NotAllowedError', 'AbortError'].includes(error?.name);
}

export async function signalPasskeys(userId: string, credentials: { id: string; rpId: string }[]) {
  const api = window.PublicKeyCredential as any;
  if (!passkeySupported() || !api.signalAllAcceptedCredentials) return;
  try {
    await api.signalAllAcceptedCredentials({
      rpId: window.location.hostname,
      userId: encode(new TextEncoder().encode(userId)),
      allAcceptedCredentialIds: credentials.filter((c) => c.rpId === window.location.hostname).map((c) => c.id),
    });
  } catch {
    /* Password-manager synchronization is optional. */
  }
}

export async function signalUnknownPasskey(credential: any, error: any) {
  const api = window.PublicKeyCredential as any;
  if (error?.status !== 404 || !api?.signalUnknownCredential) return;
  try {
    await api.signalUnknownCredential({ rpId: window.location.hostname, credentialId: credential.id });
  } catch {
    /* 没啥用 */
  }
}
