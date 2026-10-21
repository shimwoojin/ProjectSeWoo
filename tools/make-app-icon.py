"""앱 아이콘 (실행 파일 / 작업 표시줄 / 트레이) - 스토어 커뮤니티 아이콘과 같은 원숭이 얼굴.

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-app-icon.py

그림은 make-store-capsules.py 의 community_icon 을 그대로 쓴다. 스토어에서 본 얼굴과
설치 후 바탕화면·작업 표시줄에서 보는 얼굴이 달라지면 안 되기 때문이다.

  icon.png  256x256 - project.godot config/icon (창 아이콘, 트레이)
  icon.ico  16~256  - config/windows_native_icon, 내보내기 application/icon (exe 리소스)
"""

import importlib.util
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 파일 이름에 하이픈이 있어 import 문으로는 못 불러온다.
_spec = importlib.util.spec_from_file_location(
    "capsules", os.path.join(ROOT, "tools", "make-store-capsules.py"))
capsules = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(capsules)


def main():
    im = capsules.community_icon(256).convert("RGB")
    im.save(os.path.join(ROOT, "icon.png"))
    im.save(os.path.join(ROOT, "icon.ico"),
            sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("icon.png 256x256, icon.ico 16~256")


if __name__ == "__main__":
    main()
