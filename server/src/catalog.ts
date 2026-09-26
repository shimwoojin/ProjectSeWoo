// 상점 상품표. **원본은 game/shop/items.json 이다** (docs/B17-CURSOR-REWORK.md §2).
//
// 서버가 가격을 스스로 판정해야 한다 - 클라이언트가 가격을 보내게 하면 조작할 여지가 생긴다
// (docs/ECONOMY-SERVER-API.md §2-3). 2026-09-26 전에는 그래서 이 파일에 같은 표를 손으로 따로 적었고
// "갈라지면 조용히 갈라진다" 가 유일한 경고였다. 이제 게임과 같은 JSON 을 번들에 넣어 읽는다 -
// 장식이 업데이트로 늘어도 서버는 그 JSON 을 고친 뒤 다시 배포만 하면 된다.

import manifest from "../../game/shop/items.json";

export interface ShopItem {
  id: string;
  category: "monkey" | "banana" | "deco";
  tier: number;
  /**
   * 스팀 인벤토리 서비스의 아이템 정의 번호. 스팀은 문자열 id 를 모르고
   * 정수(SteamItemDef_t)만 안다 - 이 번호로 파트너 사이트에 아이템 정의를 등록해야
   * grantInventoryItem 이 맞는 아이템을 지급한다. 클라이언트(SteamInventoryService)도
   * 같은 JSON 에서 반대 방향 표를 만든다.
   *
   * tier 0(기본 지급품)은 스팀 인벤토리에 없으므로 번호가 없다.
   */
  steamItemDefId?: number;
}

/** §2-2 의 5티어 가격. 인덱스는 tier - 1. */
export const TIER_PRICES: number[] = manifest.tierPrices;

/** tier 0 = 기본 지급품. 살 수 없다 (ShopCatalog 의 IsStarter). */
export const CATALOG: ShopItem[] = manifest.items.map((item) => ({
  id: item.id,
  category: item.category as ShopItem["category"],
  tier: item.tier,
  steamItemDefId: "steamItemDefId" in item ? (item as { steamItemDefId: number }).steamItemDefId : undefined,
}));

const byId = new Map(CATALOG.map((item) => [item.id, item]));

/** 가격. 모르는 id 이거나 기본 지급품(tier 0)이면 null - 둘 다 "못 산다"는 뜻이지만
 * 호출부가 item_unknown 과 already_owned(=starter)를 구분해서 답해야 한다. */
export function priceOf(itemDefId: string): number | null {
  const item = byId.get(itemDefId);
  if (!item || item.tier === 0) {
    return null;
  }

  return TIER_PRICES[item.tier - 1];
}

export function isKnownItem(itemDefId: string): boolean {
  return byId.has(itemDefId);
}

export function isStarterItem(itemDefId: string): boolean {
  return byId.get(itemDefId)?.tier === 0;
}

/** 스팀에 넘길 정수 itemdefid. 카탈로그에 없거나 기본 지급품이면 null. */
export function steamItemDefIdOf(itemDefId: string): number | null {
  return byId.get(itemDefId)?.steamItemDefId ?? null;
}

/**
 * 강화 3축의 단계별 효과와 가격 (B13, 2026-09-24 — docs/B13-UPGRADES.md). 가격은 B14(2026-09-26)에서 두 배 - docs/B14-BALANCE.md.
 *
 * **C# 사본이 따로 있다** — shared/UpgradeTable.cs (목 경제와 강화 UI 가 쓴다). 두 표가
 * 갈라지면 조용히 갈라지므로 한쪽을 고치면 다른 쪽도 같이 고친다. 진짜 판정은 이 표로 한다.
 *
 * 레벨 L 의 효과는 effect[L], L 에서 L+1 로 올리는 가격은 price[L]. 최대 레벨 = price.length.
 * 표보다 큰 레벨(예전 곡선으로 올린 개발 계정)은 마지막 단계로 본다.
 */
export const UPGRADES = {
  /** 가지 늘리기: 슬롯 수. */
  slots: { effect: [3, 4, 5, 6], price: [120, 400, 1200] },
  /** 빨리 익기: 한 송이가 익는 시간(ms). 8분에서 1분씩. */
  cycle: { effect: [480_000, 420_000, 360_000, 300_000, 240_000], price: [80, 240, 700, 1800] },
  /** 황금 바나나: 한 송이가 황금일 확률(%). */
  golden: { effect: [0, 5, 10, 15, 20], price: [100, 300, 900, 2400] },
} as const;

/** 황금 바나나 한 송이의 값. 보통 바나나는 1. */
export const GOLDEN_MULTIPLIER = 5;

export type UpgradeKey = keyof typeof UPGRADES;

function at(values: readonly number[], level: number): number {
  return values[Math.max(0, Math.min(values.length - 1, level))];
}

export function slotCountAt(level: number): number {
  return at(UPGRADES.slots.effect, level);
}

export function growthMsAt(level: number): number {
  return at(UPGRADES.cycle.effect, level);
}

export function goldenChanceAt(level: number): number {
  return at(UPGRADES.golden.effect, level);
}

/** 다음 단계 가격. 최대 레벨이면 null. */
export function upgradePriceOf(axis: UpgradeKey, currentLevel: number): number | null {
  const prices = UPGRADES[axis].price;
  return currentLevel >= 0 && currentLevel < prices.length ? prices[currentLevel] : null;
}
