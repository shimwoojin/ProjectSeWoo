"""스팀 인벤토리 아이템 정의(server/steam-inventory/itemdefs.json)를 game/shop/items.json 에서 만든다.

    python tools\\make-itemdefs.py

아이템의 유일한 원본은 items.json 이다 (docs/B17-CURSOR-REWORK.md §2). 이 스크립트가 만든 파일을 파트너 사이트
"인벤토리 서비스 → 아이템 정의" 에 올리고 게시한다. 기본 지급품(tier 0)은 스팀 인벤토리에 없어서 빠진다.

아이콘은 GitHub raw URL 이다(공개 저장소). **그림을 push 한 뒤에 게시해야** 스팀이 아이콘을 가져간다.
"""

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
ITEMS = ROOT / "game" / "shop" / "items.json"
OUT = ROOT / "server" / "steam-inventory" / "itemdefs.json"
APP_ID = 5281130
ICON_BASE = "https://raw.githubusercontent.com/shimwoojin/ProjectSeWoo/main/assets/cursor"

DESCRIPTION = {
    "monkey": ("A cursor decoration - a monkey that hangs from the banana on your cursor and reacts as you move and type.",
               "커서의 바나나에 매달려, 움직이고 칠 때마다 반응하는 원숭이 장식"),
    "banana": ("A cursor decoration - the banana your monkey hangs from.",
               "원숭이가 매달리는 커서의 바나나 장식"),
    "deco": ("A cursor decoration - an ornament that follows your cursor.",
             "커서를 따라다니는 장식"),
}


def main():
    manifest = json.loads(ITEMS.read_text(encoding="utf-8"))
    defs = []
    for item in manifest["items"]:
        if "steamItemDefId" not in item:
            continue
        en, ko = DESCRIPTION[item["category"]]
        icon = f"{ICON_BASE}/{item['category']}/{item['id']}/icon.png"
        defs.append({
            "itemdefid": item["steamItemDefId"],
            "type": "item",
            "name": item["name"]["en"],
            "name_koreana": item["name"]["ko"],
            "description": en,
            "description_koreana": ko,
            "icon_url": icon,
            "icon_url_large": icon,
            "tradable": True,
            "marketable": True,
        })

    defs.sort(key=lambda d: d["itemdefid"])
    OUT.write_text(json.dumps({"appid": APP_ID, "items": defs}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{OUT.relative_to(ROOT)}: {len(defs)}개 (itemdefid {defs[0]['itemdefid']}~{defs[-1]['itemdefid']})")


if __name__ == "__main__":
    main()
