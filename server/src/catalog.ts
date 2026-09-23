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
 * 강화 가격. 임의 곡선이다 - 기획서 §5 는 "커서 아이템과 같은 바나나를 쓴다"만
 * 확정했고 구체적인 강화 가격표는 없다. shared/Mocks/MockEconomyService.cs 와
 * 같은 공식을 써서 목과 서버가 최소한 같은 그림으로 시험되게 맞췄다 - 실제
 * 밸런싱 전까지의 placeholder.
 */
export function upgradePriceOf(currentLevel: number): number {
  return 50 * (currentLevel + 1);
}
