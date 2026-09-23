// 클라이언트 계약(shared/Contracts/IEconomyService.cs, IInventoryService.cs)과
// 1:1로 맞춘 타입. 한쪽을 바꾸면 반드시 다른 쪽도 같이 본다 - 서버는 컴파일
// 타임에 그 어긋남을 잡아주지 못한다 (docs/ECONOMY-SERVER-API.md).

export interface Env {
  DB: D1Database;

  /** 스팀 파트너 사이트 Publisher Web API Key. 클라이언트에 절대 내려주지 않는다. */
  STEAM_PUBLISHER_WEB_API_KEY: string;

  /** SaveData/A8 문서의 그 값과 같다 (docs/A8-STEAM.md §2-2). */
  STEAM_APP_ID: string;

  /** 세션 토큰 서명용 대칭키. */
  SESSION_SIGNING_SECRET: string;
}

/** shared/Contracts/IEconomyService.cs 의 SlotState 와 대응. */
export interface SlotStateDto {
  elapsedMs: number;
  growthMs: number;
}

/** shared/Contracts/IEconomyService.cs 의 UpgradeAxis 와 대응. 문자열 그대로 JSON 에 싣는다. */
export type UpgradeAxis = "power" | "cycle" | "slots";

/** shared/Contracts/IEconomyService.cs 의 PurchaseOutcome 과 대응. */
export type PurchaseOutcome =
  | "success"
  | "insufficient_balance"
  | "item_unknown"
  | "already_owned"
  | "rejected";

export interface EconomyStateDto {
  balance: number;
  slots: SlotStateDto[];
  upgrades: { power: number; cycle: number; slots: number };
  lastSyncUtc: string;
}

export interface HarvestResponseDto {
  accepted: boolean;
  reason?: "not_ready";
  balance: number;
  slots: SlotStateDto[];
}

export interface PurchaseResponseDto {
  outcome: PurchaseOutcome;
  balance: number;
  grantedItemDefId?: string;
  upgrades?: { power: number; cycle: number; slots: number };
}

/** players 테이블 한 행. DB 에 저장되는 원시 형태 - 밖으로는 안 나간다. */
export interface PlayerRow {
  steam_id: string;
  balance: number;
  power_level: number;
  cycle_level: number;
  slots_level: number;
  slot_elapsed_ms: string; // JSON.stringify(number[])
  last_sync_utc: string; // ISO
}
