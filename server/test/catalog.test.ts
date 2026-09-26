import { describe, expect, it } from "vitest";
import manifest from "../../game/shop/items.json";
import {
  CATALOG,
  TIER_PRICES,
  goldenChanceAt,
  growthMsAt,
  isKnownItem,
  isStarterItem,
  priceOf,
  slotCountAt,
  steamItemDefIdOf,
  upgradePriceOf,
} from "../src/catalog";

// 가격표는 서버가 스스로 판정한다 (클라이언트가 가격을 안 보낸다). 원본은 game/shop/items.json (B17).

describe("catalog", () => {
  it("items.json 을 그대로 읽는다", () => {
    expect(CATALOG.map((i) => i.id)).toEqual(manifest.items.map((i) => i.id));
    expect(TIER_PRICES).toEqual(manifest.tierPrices);
  });

  it("id 와 스팀 itemdefid 가 겹치지 않는다", () => {
    const ids = CATALOG.map((i) => i.id);
    expect(new Set(ids).size).toBe(ids.length);
    const steam = CATALOG.flatMap((i) => (i.steamItemDefId === undefined ? [] : [i.steamItemDefId]));
    expect(new Set(steam).size).toBe(steam.length);
  });

  it("기본 지급품은 원숭이·바나나 하나씩이고 못 산다 (가격 null, 스팀 번호 없음)", () => {
    const starters = CATALOG.filter((i) => i.tier === 0);
    expect(starters.map((i) => i.category).sort()).toEqual(["banana", "monkey"]);
    for (const s of starters) {
      expect(isStarterItem(s.id)).toBe(true);
      expect(priceOf(s.id)).toBeNull();
      expect(steamItemDefIdOf(s.id)).toBeNull();
    }
  });

  it("살 수 있는 것은 티어 가격이고 스팀 번호가 있다", () => {
    for (const item of CATALOG.filter((i) => i.tier > 0)) {
      expect(priceOf(item.id)).toBe(TIER_PRICES[item.tier - 1]);
      expect(steamItemDefIdOf(item.id)).toBe(item.steamItemDefId);
    }

    expect(priceOf("banana_02")).toBe(10);
  });

  it("모르는 id 는 살 수 없다", () => {
    expect(isKnownItem("../evil")).toBe(false);
    expect(priceOf("nope")).toBeNull();
    expect(steamItemDefIdOf("nope")).toBeNull();
  });

  it("강화 표: 최대 단계에서 다음 가격은 null, 표보다 큰 레벨은 마지막 단계", () => {
    expect(upgradePriceOf("golden", 0)).toBe(50);
    expect(upgradePriceOf("golden", 4)).toBeNull();
    expect(upgradePriceOf("slots", 3)).toBeNull();
    expect(slotCountAt(99)).toBe(6);
    expect(growthMsAt(99)).toBe(240_000);
    expect(goldenChanceAt(0)).toBe(0);
  });
});
