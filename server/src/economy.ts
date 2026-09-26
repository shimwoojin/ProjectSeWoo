import {
  GOLDEN_MULTIPLIER,
  goldenChanceAt,
  growthMsAt,
  isKnownItem,
  isStarterItem,
  priceOf,
  slotCountAt,
  steamItemDefIdOf,
  upgradePriceOf,
} from "./catalog";
import { findIdempotentResponse, getOrCreatePlayer, isOwned, markOwned, savePlayer, storeIdempotentResponse } from "./db";
import { grantInventoryItem } from "./steam";
import type {
  Env,
  EconomyStateDto,
  HarvestResponseDto,
  PlayerRow,
  PurchaseResponseDto,
  SlotStateDto,
  UpgradeAxis,
} from "./types";

// 강화 수치는 catalog.ts 의 UPGRADES 표 (B13). shared/Mocks/MockEconomyService.cs 가
// 같은 표(shared/UpgradeTable.cs)를 쓴다 - 목과 서버가 같은 그림으로 보여야 시험이 의미 있다.

/** 나무 슬롯 전부. 같은 인덱스끼리 한 송이다. DB 에는 JSON 배열 두 칸으로 들어간다. */
export interface Slots {
  elapsed: number[];
  golden: boolean[];
}

/** 새 송이가 황금인가. 송이가 자라기 시작할 때 서버가 굴린다 - 클라이언트는 고를 수 없다. */
function rollGolden(goldenLevel: number): boolean {
  const chance = goldenChanceAt(goldenLevel);
  if (chance <= 0) {
    return false;
  }

  const buf = new Uint32Array(1);
  crypto.getRandomValues(buf);
  return buf[0] % 100 < chance;
}

function parseArray<T>(json: string | null | undefined, fallback: T[]): T[] {
  try {
    const value = JSON.parse(json ?? "");
    return Array.isArray(value) ? value : fallback;
  } catch {
    return fallback;
  }
}

function loadSlots(player: PlayerRow): Slots {
  return {
    elapsed: parseArray<number>(player.slot_elapsed_ms, [0, 0, 0]),
    golden: parseArray<boolean>(player.slot_golden, []),
  };
}

function storeSlots(player: PlayerRow, slots: Slots): void {
  player.slot_elapsed_ms = JSON.stringify(slots.elapsed);
  player.slot_golden = JSON.stringify(slots.golden);
}

/**
 * 서버 클럭 재계산의 핵심. `player.last_sync_utc` 이후 지난 시간만큼 각 슬롯의 경과 시간을
 * 밀어 올리되, 다 자란 슬롯은 성장 시간에서 멈춘다(오프라인 무한 축적 방지 - 기획서 §2-2
 * "슬롯이 상한이다").
 *
 * 슬롯 수가 강화로 늘었으면 배열을 늘린다(새 슬롯은 0 부터, 황금 여부는 새로 굴림). 황금 칸이
 * 모자라면(이 칸이 생기기 전의 행, B13 마이그레이션 직후) 그 자리도 굴려서 채운다.
 *
 * **호출부가 반드시 결과를 저장해야 한다.** 이 함수는 DB 를 안 건드린다 - 순수 계산이라 시험하기
 * 쉽게 하려는 것이고, `last_sync_utc` 갱신까지 하는 것은 `syncAndPersist`.
 */
export function recomputeSlots(player: PlayerRow, nowMs: number): Slots {
  const growth = growthMsAt(player.cycle_level);
  const count = slotCountAt(player.slots_level);
  const deltaMs = Math.max(0, nowMs - Date.parse(player.last_sync_utc));
  const { elapsed, golden } = loadSlots(player);

  const nextElapsed: number[] = [];
  const nextGolden: boolean[] = [];
  for (let i = 0; i < count; i++) {
    nextElapsed.push(Math.min(growth, (elapsed[i] ?? 0) + (i < elapsed.length ? deltaMs : 0)));
    nextGolden.push(typeof golden[i] === "boolean" ? golden[i] : rollGolden(player.golden_level));
  }

  return { elapsed: nextElapsed, golden: nextGolden };
}

function toSlotDtos(slots: Slots, growthMs: number): SlotStateDto[] {
  return slots.elapsed.map((elapsedMs, i) => ({ elapsedMs, growthMs, golden: slots.golden[i] ?? false }));
}

function upgradesOf(player: PlayerRow) {
  return { golden: player.golden_level, cycle: player.cycle_level, slots: player.slots_level };
}

/** 최신 슬롯 상태로 재계산하고 그 자리에서 저장까지 한다. */
async function syncAndPersist(env: Env, player: PlayerRow, nowMs: number): Promise<Slots> {
  const slots = recomputeSlots(player, nowMs);
  storeSlots(player, slots);
  player.last_sync_utc = new Date(nowMs).toISOString();
  await savePlayer(env, player);
  return slots;
}

export async function getState(env: Env, steamId: string): Promise<EconomyStateDto> {
  const player = await getOrCreatePlayer(env, steamId);
  const slots = await syncAndPersist(env, player, Date.now());

  return {
    balance: player.balance,
    slots: toSlotDtos(slots, growthMsAt(player.cycle_level)),
    upgrades: upgradesOf(player),
    lastSyncUtc: player.last_sync_utc,
  };
}

export async function harvest(
  env: Env,
  steamId: string,
  slotIndex: number,
  clientRequestId: string,
): Promise<HarvestResponseDto> {
  const cached = await findIdempotentResponse(env, clientRequestId);
  if (cached) {
    return JSON.parse(cached) as HarvestResponseDto;
  }

  const player = await getOrCreatePlayer(env, steamId);
  const nowMs = Date.now();
  const slots = recomputeSlots(player, nowMs);
  const growth = growthMsAt(player.cycle_level);

  const ready = slotIndex >= 0 && slotIndex < slots.elapsed.length && slots.elapsed[slotIndex] >= growth;
  let gained = 0;
  if (ready) {
    gained = slots.golden[slotIndex] ? GOLDEN_MULTIPLIER : 1;
    player.balance += gained;
    slots.elapsed[slotIndex] = 0;
    slots.golden[slotIndex] = rollGolden(player.golden_level);
  }

  storeSlots(player, slots);
  player.last_sync_utc = new Date(nowMs).toISOString();
  await savePlayer(env, player);

  const response: HarvestResponseDto = {
    accepted: ready,
    ...(ready ? {} : { reason: "not_ready" as const }),
    gained,
    balance: player.balance,
    slots: toSlotDtos(slots, growth),
  };

  await storeIdempotentResponse(env, clientRequestId, steamId, JSON.stringify(response));
  return response;
}

export async function purchaseItem(
  env: Env,
  steamId: string,
  itemDefId: string,
  clientRequestId: string,
): Promise<PurchaseResponseDto> {
  const cached = await findIdempotentResponse(env, clientRequestId);
  if (cached) {
    return JSON.parse(cached) as PurchaseResponseDto;
  }

  const respond = async (r: PurchaseResponseDto) => {
    await storeIdempotentResponse(env, clientRequestId, steamId, JSON.stringify(r));
    return r;
  };

  const player = await getOrCreatePlayer(env, steamId);

  if (!isKnownItem(itemDefId)) {
    return respond({ outcome: "item_unknown", balance: player.balance });
  }

  if (isStarterItem(itemDefId) || (await isOwned(env, steamId, itemDefId))) {
    return respond({ outcome: "already_owned", balance: player.balance });
  }

  const price = priceOf(itemDefId);
  const steamItemDefId = steamItemDefIdOf(itemDefId);
  if (price === null || steamItemDefId === null || player.balance < price) {
    return respond({ outcome: "insufficient_balance", balance: player.balance });
  }

  // 먼저 깎고 지급을 시도한다 - 지급이 실패하면 되돌린다. 순서를 반대로 하면(지급 먼저)
  // 지급 성공 후 차감이 실패하는 경우 잔액이 안 깎인 채 아이템만 나가는 더 나쁜 실패 모드가 된다.
  const balanceAfterDeduction = player.balance - price;
  player.balance = balanceAfterDeduction;
  await savePlayer(env, player);

  const grant = await grantInventoryItem(env, steamId, steamItemDefId);
  if (!grant.ok) {
    player.balance += price; // 롤백
    await savePlayer(env, player);
    return respond({ outcome: "rejected", balance: player.balance });
  }

  await markOwned(env, steamId, itemDefId);
  return respond({ outcome: "success", balance: balanceAfterDeduction, grantedItemDefId: itemDefId });
}

const LEVEL_FIELD = {
  golden: "golden_level",
  cycle: "cycle_level",
  slots: "slots_level",
} as const satisfies Record<UpgradeAxis, keyof PlayerRow>;

export async function purchaseUpgrade(
  env: Env,
  steamId: string,
  axis: UpgradeAxis,
  clientRequestId: string,
): Promise<PurchaseResponseDto> {
  const cached = await findIdempotentResponse(env, clientRequestId);
  if (cached) {
    return JSON.parse(cached) as PurchaseResponseDto;
  }

  const respond = async (r: PurchaseResponseDto) => {
    await storeIdempotentResponse(env, clientRequestId, steamId, JSON.stringify(r));
    return r;
  };

  const player = await getOrCreatePlayer(env, steamId);
  const field = LEVEL_FIELD[axis];
  const currentLevel = player[field];
  const price = upgradePriceOf(axis, currentLevel);

  if (price === null) {
    return respond({ outcome: "max_level", balance: player.balance, upgrades: upgradesOf(player) });
  }

  if (player.balance < price) {
    return respond({ outcome: "insufficient_balance", balance: player.balance });
  }

  // 레벨을 올리기 **전에** 지금까지 흐른 시간을 옛 레벨로 확정하고 last_sync_utc 를 당긴다.
  // 이걸 빼면 다음 요청이 같은 구간을 한 번 더 더하고, 새로 늘어난 슬롯도 0 이 아니라 그
  // 구간만큼 자란 채로 생긴다(2026-09-24 b7696d3).
  const nowMs = Date.now();
  storeSlots(player, recomputeSlots(player, nowMs));
  player.last_sync_utc = new Date(nowMs).toISOString();

  player.balance -= price;
  player[field] = currentLevel + 1;

  // 흐른 시간이 0 이므로 이번 재계산은 새 레벨에 맞춰 배열만 맞춘다 - 늘어난 슬롯은 0 부터,
  // 성장 시간이 짧아졌으면 min(growth) 로 잘려 곧바로 익는다.
  const slots = recomputeSlots(player, nowMs);
  storeSlots(player, slots);
  await savePlayer(env, player);

  return respond({
    outcome: "success",
    balance: player.balance,
    upgrades: upgradesOf(player),
    slots: toSlotDtos(slots, growthMsAt(player.cycle_level)),
  });
}
