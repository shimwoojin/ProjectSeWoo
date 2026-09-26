import { describe, expect, it } from "vitest";
import { recomputeSlots } from "../src/economy";
import type { PlayerRow } from "../src/types";

// 서버 클럭 재계산 - 오프라인 성장, 상한, 강화로 늘어난 슬롯 (docs/ECONOMY-SERVER.md, B13).

const NOW = Date.parse("2026-09-26T12:00:00Z");

function player(over: Partial<PlayerRow> = {}): PlayerRow {
  return {
    steam_id: "76561198000000000",
    balance: 0,
    power_level: 0,
    golden_level: 0,
    cycle_level: 0,
    slots_level: 0,
    slot_elapsed_ms: JSON.stringify([0, 0, 0]),
    slot_golden: JSON.stringify([false, false, false]),
    last_sync_utc: new Date(NOW - 60_000).toISOString(),
    ...over,
  };
}

describe("recomputeSlots", () => {
  it("지난 시간만큼 자란다", () => {
    expect(recomputeSlots(player(), NOW).elapsed).toEqual([60_000, 60_000, 60_000]);
  });

  it("다 자라면 성장 시간(8분)에서 멈춘다 - 오래 비워도 무한히 쌓이지 않는다", () => {
    const p = player({ last_sync_utc: new Date(NOW - 3 * 86_400_000).toISOString() });
    expect(recomputeSlots(p, NOW).elapsed).toEqual([480_000, 480_000, 480_000]);
  });

  it("시계가 거꾸로 가도(서버 시각보다 미래 last_sync) 줄어들지 않는다", () => {
    const p = player({ slot_elapsed_ms: JSON.stringify([1000, 2000, 3000]), last_sync_utc: new Date(NOW + 60_000).toISOString() });
    expect(recomputeSlots(p, NOW).elapsed).toEqual([1000, 2000, 3000]);
  });

  it("가지 늘리기로 늘어난 슬롯은 0 부터 - 지난 시간을 소급해 받지 않는다", () => {
    const p = player({ slots_level: 2 });   // 5 송이
    expect(recomputeSlots(p, NOW).elapsed).toEqual([60_000, 60_000, 60_000, 0, 0]);
  });

  it("빨리 익기로 성장 시간이 짧아지면 이미 넘은 송이는 그 자리에서 익는다", () => {
    const p = player({ cycle_level: 4, slot_elapsed_ms: JSON.stringify([300_000, 0, 0]) });   // 4분
    expect(recomputeSlots(p, NOW).elapsed[0]).toBe(240_000);
  });

  it("황금 칸이 모자라면(옛 행) 채운다 - 황금 0 단계면 전부 보통", () => {
    const p = player({ slot_golden: "[]" });
    expect(recomputeSlots(p, NOW).golden).toEqual([false, false, false]);
  });

  it("깨진 JSON 은 기본값으로", () => {
    const p = player({ slot_elapsed_ms: "{oops", slot_golden: "null" });
    const slots = recomputeSlots(p, NOW);
    expect(slots.elapsed).toHaveLength(3);
    expect(slots.golden).toHaveLength(3);
  });
});
