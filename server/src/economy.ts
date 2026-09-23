import { isKnownItem, isStarterItem, priceOf, steamItemDefIdOf, upgradePriceOf } from "./catalog";
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

// 기획서 §2-2 수치. shared/Mocks/MockEconomyService.cs 와 같은 공식을 쓴다 -
// 목과 서버가 최소한 같은 그림으로 보여야 시험이 의미 있다.
const BASE_GROWTH_MS = 480_000;
const MIN_GROWTH_MS = 180_000;
const CYCLE_STEP_MS = 30_000;
const BASE_SLOTS = 3;
const MAX_SLOTS = 8;

function growthMsFor(cycleLevel: number): number {
  return Math.max(MIN_GROWTH_MS, BASE_GROWTH_MS - cycleLevel * CYCLE_STEP_MS);
}

function slotCountFor(slotsLevel: number): number {
  return Math.min(MAX_SLOTS, BASE_SLOTS + slotsLevel);
}

/**
 * 서버 클럭 재계산의 핵심. `player.last_sync_utc` 이후 지난 시간만큼 각
 * 슬롯의 경과 시간을 밀어 올리되, 다 자란 슬롯은 <see>growthMs</see>에서
 * 멈춘다(오프라인 무한 축적 방지 - 기획서 §2-2 "슬롯이 상한이다").
 *
 * 슬롯 수가 강화로 늘었으면 배열을 늘리고(새 슬롯은 0부터), 줄어들 일은
 * 없으므로 줄이는 경로는 다루지 않는다.
 *
 * **호출부가 반드시 결과를 저장해야 한다.** 이 함수 자체는 DB 를 안 건드린다 -
 * 순수 계산이라 시험하기 쉽게 하려는 것이고, `last_sync_utc` 갱신까지 하는
 * 것은 `syncAndPersist`.
 */
function recomputeSlots(player: PlayerRow, nowMs: number): number[] {
  const growth = growthMsFor(player.cycle_level);
  const count = slotCountFor(player.slots_level);
  const lastSyncMs = Date.parse(player.last_sync_utc);
  const deltaMs = Math.max(0, nowMs - lastSyncMs);

  let elapsed: number[] = JSON.parse(player.slot_elapsed_ms);
  if (elapsed.length < count) {
    elapsed = [...elapsed, ...new Array(count - elapsed.length).fill(0)];
  } else if (elapsed.length > count) {
    elapsed = elapsed.slice(0, count);
  }

  return elapsed.map((e) => Math.min(growth, e + deltaMs));
}

function toSlotDtos(elapsed: number[], growthMs: number): SlotStateDto[] {
  return elapsed.map((elapsedMs) => ({ elapsedMs, growthMs }));
}

function upgradesOf(player: PlayerRow) {
  return { power: player.power_level, cycle: player.cycle_level, slots: player.slots_level };
}

/** 최신 슬롯 상태로 재계산하고 그 자리에서 저장까지 한다. */
async function syncAndPersist(env: Env, player: PlayerRow, nowMs: number): Promise<number[]> {
  const elapsed = recomputeSlots(player, nowMs);
  player.slot_elapsed_ms = JSON.stringify(elapsed);
  player.last_sync_utc = new Date(nowMs).toISOString();
  await savePlayer(env, player);
  return elapsed;
}

export async function getState(env: Env, steamId: string): Promise<EconomyStateDto> {
  const player = await getOrCreatePlayer(env, steamId);
  const elapsed = await syncAndPersist(env, player, Date.now());
  const growth = growthMsFor(player.cycle_level);

  return {
    balance: player.balance,
    slots: toSlotDtos(elapsed, growth),
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
  const elapsed = recomputeSlots(player, nowMs);
  const growth = growthMsFor(player.cycle_level);

  const ready = slotIndex >= 0 && slotIndex < elapsed.length && elapsed[slotIndex] >= growth;
  if (ready) {
    elapsed[slotIndex] = 0;
    player.balance += 1 + player.power_level;
  }

  player.slot_elapsed_ms = JSON.stringify(elapsed);
  player.last_sync_utc = new Date(nowMs).toISOString();
  await savePlayer(env, player);

  const response: HarvestResponseDto = {
    accepted: ready,
    ...(ready ? {} : { reason: "not_ready" as const }),
    balance: player.balance,
    slots: toSlotDtos(elapsed, growth),
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

  if (!isKnownItem(itemDefId)) {
    const player = await getOrCreatePlayer(env, steamId);
    return respond({ outcome: "item_unknown", balance: player.balance });
  }

  const player = await getOrCreatePlayer(env, steamId);

  if (isStarterItem(itemDefId) || (await isOwned(env, steamId, itemDefId))) {
    return respond({ outcome: "already_owned", balance: player.balance });
  }

  const price = priceOf(itemDefId);
  const steamItemDefId = steamItemDefIdOf(itemDefId);
  if (price === null || steamItemDefId === null || player.balance < price) {
    return respond({ outcome: "insufficient_balance", balance: player.balance });
  }

  // 먼저 깎고 지급을 시도한다 - 지급이 실패하면 되돌린다. 순서를 반대로
  // 하면(지급 먼저) 지급 성공 후 차감이 실패하는 경우 잔액이 안 깎인 채
  // 아이템만 나가는 더 나쁜 실패 모드가 된다.
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

  const player = await getOrCreatePlayer(env, steamId);
  const levelField = `${axis}_level` as const;
  const currentLevel = (player as unknown as Record<string, number>)[levelField];
  const price = upgradePriceOf(currentLevel);

  if (player.balance < price) {
    const response: PurchaseResponseDto = { outcome: "insufficient_balance", balance: player.balance };
    await storeIdempotentResponse(env, clientRequestId, steamId, JSON.stringify(response));
    return response;
  }

  player.balance -= price;
  (player as unknown as Record<string, number>)[levelField] = currentLevel + 1;

  // 나무 슬롯 수/성장 주기가 저장된 elapsed 배열 길이·의미와 바로 어긋나지
  // 않게, 이 자리에서 한 번 재계산해서 반영한다.
  const elapsed = recomputeSlots(player, Date.now());
  player.slot_elapsed_ms = JSON.stringify(elapsed);
  await savePlayer(env, player);

  const response: PurchaseResponseDto = {
    outcome: "success",
    balance: player.balance,
    upgrades: upgradesOf(player),
  };
  await storeIdempotentResponse(env, clientRequestId, steamId, JSON.stringify(response));
  return response;
}
