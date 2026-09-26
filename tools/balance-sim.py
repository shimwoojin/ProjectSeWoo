"""B14 밸런스 시뮬레이터 (docs/B14-BALANCE.md).

    python tools\balance-sim.py

지금 가격표(game/shop/items.json)와 강화 표로 '하루 N시간 켜 두는 사람' 이 장식을 다 모으는 날짜를 전략별로 낸다.
가정: 켜 둔 동안은 익는 대로 다 딴다(타건 충분), 꺼 둔 동안은 슬롯이 한 번 차고 멈춘다(오프라인 상한).
강화 표는 여기 BASE_UP 이 shared/UpgradeTable.cs · server/src/catalog.ts 와 같아야 한다 (지금 값을 적어 둔다).
"""
import itertools
import json
import sys

sys.stdout.reconfigure(encoding="utf-8")
import pathlib
M = json.load(open(pathlib.Path(__file__).resolve().parents[1] / "game" / "shop" / "items.json", encoding="utf-8"))

BASE_UP = {
    "slots": ([3, 4, 5, 6], [120, 400, 1200]),
    "cycle": ([480, 420, 360, 300, 240], [80, 240, 700, 1800]),
    "golden": ([0, 5, 10, 15, 20], [100, 300, 900, 2400]),
}


def items(tier_prices, banana_shift):
    out = []
    for i in M["items"]:
        t = i["tier"]
        if t == 0:
            continue
        if i["category"] == "banana" and banana_shift:
            t = max(1, t - 1)
        out.append((i["id"], i["category"], t, tier_prices[t - 1]))
    return out


def ups(mult):
    return {k: (v[0], [round(p * mult / 10) * 10 for p in v[1]]) for k, v in BASE_UP.items()}


def rate(up, lv):
    slots = up["slots"][0][lv["slots"]]
    growth = up["cycle"][0][lv["cycle"]]
    g = up["golden"][0][lv["golden"]] / 100
    return slots * 3600 / growth * (1 - g + 5 * g)


def offline(up, lv):
    g = up["golden"][0][lv["golden"]] / 100
    return up["slots"][0][lv["slots"]] * (1 - g + 5 * g)


def simulate(its, up, hours, strategy, days=600):
    lv = {k: 0 for k in up}
    bank, owned, log = 0.0, set(), {}
    todo = sorted(its, key=lambda x: x[3])
    for day in range(1, days + 1):
        bank += offline(up, lv) + rate(up, lv) * hours
        while True:
            u = sorted((up[a][1][lv[a]], a) for a in up if lv[a] < len(up[a][1]))
            it = next((x for x in todo if x[0] not in owned), None)
            cands = []
            if strategy == "upgrades_first":
                cands = [("u", u[0])] if u else ([("i", it)] if it else [])
            elif strategy == "mixed":
                cands = sorted([("u", x) for x in u] + ([("i", it)] if it else []),
                               key=lambda c: c[1][0] if c[0] == "u" else c[1][3])
            else:
                cands = [("i", it)] if it else []
            if not cands:
                break
            kind, obj = cands[0]
            cost = obj[0] if kind == "u" else obj[3]
            if bank < cost:
                break
            bank -= cost
            if kind == "u":
                lv[obj[1]] += 1
                log.setdefault("up1", day)
            else:
                owned.add(obj[0])
                log.setdefault("item1", day)
                if len(owned) == 10:
                    log.setdefault("item10", day)
                if len(owned) == len(its):
                    log["all"] = day
                    return log
    return log


def payback_hours(up):
    base = {k: 0 for k in up}
    r0 = rate(up, base)
    res = {}
    for a in up:
        lv = dict(base)
        lv[a] = 1
        res[a] = up[a][1][0] / (rate(up, lv) - r0)
    return res


def report(tier_prices, mult, shift, label):
    its = items(tier_prices, shift)
    up = ups(mult)
    total_i = sum(x[3] for x in its)
    total_u = sum(sum(v[1]) for v in up.values())
    pb = payback_hours(up)
    print(f"\n== {label}: tier {tier_prices}, 강화 x{mult}, 바나나 한 티어 싸게={shift}")
    print(f"   장식 합 {total_i:,} / 강화 합 {total_u:,}  / 첫 강화 본전(플레이 시간) 가지 {pb['slots']:.0f}h · 빨리 익기 {pb['cycle']:.0f}h · 황금 {pb['golden']:.0f}h")
    for h in (2, 4, 8):
        row = []
        for st in ("mixed", "upgrades_first", "items_only"):
            lg = simulate(its, up, h, st)
            row.append(f"{st}: 첫 {lg.get('item1','-')}일, 10개 {lg.get('item10','-')}일, 전부 {lg.get('all','-')}일")
        print(f"   하루 {h}h | " + " | ".join(row))


if __name__ == "__main__":
    # 지금 표 그대로 (items.json 의 티어는 이미 바나나가 한 티어 싸다 - banana_shift 는 끈다)
    report(M["tierPrices"], 1, False, "지금 (items.json + 강화 표)")
