import type { Env } from "./types";

// 세션 토큰 = base64url(steamId.expiresAtMs) + "." + base64url(HMAC-SHA256 서명).
// 매 요청마다 스팀 AuthenticateUserTicket 을 부르면 밸브 쪽 레이트리밋에
// 걸리기 쉬워서 (docs/ECONOMY-SERVER-API.md §1), 짧은 수명의 자체 서명 토큰을
// 발급해 그 사이 요청들은 이 토큰만으로 검증한다.

const DEFAULT_TTL_SECONDS = 3600;

function toBase64Url(bytes: ArrayBuffer | Uint8Array): string {
  const arr = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let binary = "";
  for (const byte of arr) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function fromBase64Url(value: string): Uint8Array {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(padded + "=".repeat((4 - (padded.length % 4)) % 4));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
}

async function hmacKey(secret: string): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"],
  );
}

export interface SessionToken {
  token: string;
  expiresAt: string;
}

export async function issueSessionToken(
  env: Env,
  steamId: string,
  ttlSeconds: number = DEFAULT_TTL_SECONDS,
): Promise<SessionToken> {
  const expiresAtMs = Date.now() + ttlSeconds * 1000;
  const payload = `${steamId}.${expiresAtMs}`;
  const key = await hmacKey(env.SESSION_SIGNING_SECRET);
  const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload));

  return {
    token: `${toBase64Url(new TextEncoder().encode(payload))}.${toBase64Url(signature)}`,
    expiresAt: new Date(expiresAtMs).toISOString(),
  };
}

export type SessionVerifyResult = { ok: true; steamId: string } | { ok: false; error: string };

export async function verifySessionToken(env: Env, token: string | null): Promise<SessionVerifyResult> {
  if (!token) {
    return { ok: false, error: "missing_token" };
  }

  const [payloadPart, sigPart] = token.split(".");
  if (!payloadPart || !sigPart) {
    return { ok: false, error: "malformed_token" };
  }

  const key = await hmacKey(env.SESSION_SIGNING_SECRET);
  const valid = await crypto.subtle.verify(
    "HMAC",
    key,
    fromBase64Url(sigPart),
    fromBase64Url(payloadPart),
  );

  if (!valid) {
    return { ok: false, error: "bad_signature" };
  }

  const payload = new TextDecoder().decode(fromBase64Url(payloadPart));
  const [steamId, expiresAtMsStr] = payload.split(".");
  const expiresAtMs = Number(expiresAtMsStr);

  if (!steamId || !Number.isFinite(expiresAtMs)) {
    return { ok: false, error: "malformed_payload" };
  }

  if (Date.now() > expiresAtMs) {
    return { ok: false, error: "expired" };
  }

  return { ok: true, steamId };
}

/** `Authorization: Bearer <token>` 헤더에서 토큰만 뽑는다. */
export function bearerTokenFrom(request: Request): string | null {
  const header = request.headers.get("Authorization");
  if (!header?.startsWith("Bearer ")) {
    return null;
  }

  return header.slice("Bearer ".length).trim();
}
