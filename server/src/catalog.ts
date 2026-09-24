// 상점 상품표 서버 사본. **원본은 game/shop/ShopCatalog.cs 다.**
//
// 진실은 한 군데(C# 클라이언트)인데 서버가 같은 표를 또 들고 있는 이유는,
// 가격을 클라이언트가 보내게 하면 클라이언트가 가격을 조작할 여지가 생기기
// 때문이다 (docs/ECONOMY-SERVER-API.md §2-3) - 그래서 서버가 자기 사본으로
// 가격을 판정해야 한다.
//
// **두 표가 갈라지면 조용히 갈라진다.** ShopCatalog.cs 에 아이템을 추가/변경할
// 때 이 파일도 같이 고칠 것 - 지금은 이 주석이 유일한 안전장치다. 아이템
// 수가 늘어나면 빌드 스크립트로 ShopCatalog.cs 에서 이 파일을 생성하는 쪽을
// 고려한다 (지금은 16종 고정이라 손으로 맞추는 비용이 낮다).

export interface ShopItem {
  id: string;
  tier: 0 | 1 | 2 | 3 | 4 | 5;
  /**
   * 스팀 인벤토리 서비스의 아이템 정의 번호. 스팀은 문자열 id 를 모르고
   * 정수(SteamItemDef_t)만 안다 - 이 번호가 우리가 정한 값이고, 파트너
   * 사이트에 이 번호로 아이템 정의를 등록해야 grantInventoryItem 이 맞는
   * 아이템을 지급한다. **platform/SteamInventoryService.cs 의 같은 표와
   * 정확히 같아야 한다** (그쪽은 클라이언트가 스팀에서 돌려받은 정수를
   * 다시 문자열 id 로 되돌리는 반대 방향 매핑이다).
   *
   * tier 0(기본 지급품)은 스팀 인벤토리에 없으므로 번호가 없다.
   */
  steamItemDefId?: number;
}

/** §2-2 의 5티어 가격. 인덱스는 tier - 1. */
export const TIER_PRICES = [10, 40, 150, 500, 1500];

/** tier 0 = 기본 지급품. 살 수 없다 (ShopCatalog.cs 의 IsStarter). */
export const CATALOG: ShopItem[] = [
  { id: "monkey_01", tier: 0 },
  { id: "monkey_02", tier: 1, steamItemDefId: 1 },
  { id: "monkey_03", tier: 2, steamItemDefId: 2 },
  { id: "monkey_04", tier: 3, steamItemDefId: 3 },
  { id: "monkey_05", tier: 4, steamItemDefId: 4 },
  { id: "monkey_06", tier: 5, steamItemDefId: 5 },
  { id: "leaf_01", tier: 1, steamItemDefId: 6 },
  { id: "chunk_01", tier: 2, steamItemDefId: 7 },
  { id: "leaf_02", tier: 3, steamItemDefId: 8 },
  { id: "chunk_02", tier: 4, steamItemDefId: 9 },
  { id: "spark_01", tier: 3, steamItemDefId: 10 },
  { id: "spark_02", tier: 5, steamItemDefId: 11 },
  { id: "halo_01", tier: 1, steamItemDefId: 12 },
  { id: "ring_01", tier: 2, steamItemDefId: 13 },
  { id: "halo_02", tier: 4, steamItemDefId: 14 },
  { id: "ring_02", tier: 5, steamItemDefId: 15 },
];

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
 * 강화 3축의 단계별 효과와 가격 (B13, 2026-09-24 — docs/B13-UPGRADES.md).
 *
 * **C# 사본이 따로 있다** — shared/UpgradeTable.cs (목 경제와 강화 UI 가 쓴다). 두 표가
 * 갈라지면 조용히 갈라지므로 한쪽을 고치면 다른 쪽도 같이 고친다. 진짜 판정은 이 표로 한다.
 *
 * 레벨 L 의 효과는 effect[L], L 에서 L+1 로 올리는 가격은 price[L]. 최대 레벨 = price.length.
 * 표보다 큰 레벨(예전 곡선으로 올린 개발 계정)은 마지막 단계로 본다.
 */
export const UPGRADES = {
  /** 가지 늘리기: 슬롯 수. */
  slots: { effect: [3, 4, 5, 6], price: [60, 200, 600] },
  /** 빨리 익기: 한 송이가 익는 시간(ms). 8분에서 1분씩. */
  cycle: { effect: [480_000, 420_000, 360_000, 300_000, 240_000], price: [40, 120, 350, 900] },
  /** 황금 바나나: 한 송이가 황금일 확률(%). */
  golden: { effect: [0, 5, 10, 15, 20], price: [50, 150, 450, 1200] },
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
