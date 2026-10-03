"""두부컴퍼니 음식 아이템 생성기 (3D 로우폴리).

실행: python generate_food.py            # 전부 만들기
      python generate_food.py Apple Bread  # 일부만 (미리보기 시트는 preview_tmp.png)
- items_*.py 의 3D 모델(mesh3d.py 도구)을 렌더링해서 PNG(512x512, 투명 배경)로 뽑는다.
  PNG: CompanyGame/Assets/Art/Items/Food/ (+ 처음 만들 때 Unity .meta)
  모델: ArtSource/Items/Food/Models/*.obj + .mtl (재질=색)
  카탈로그: ArtSource/Items/Food/catalog.md, 미리보기 시트: Food_Sheet.png
- Pillow, numpy 필요.
"""
import re
import sys
import uuid
from pathlib import Path

from PIL import Image

from mesh3d import Mesh, render, write_obj

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / "CompanyGame" / "Assets" / "Art" / "Items" / "Food"
MODELS = HERE / "Models"
META_TEMPLATE = REPO / "CompanyGame" / "Assets" / "Art" / "Items" / "Currency" / "Currency_100.png.meta"
SIZE = 512
THUMB = 128
COLS = 10


def load_items():
    items = []
    for mod in ("items_a", "items_b"):
        try:
            items += __import__(mod).ITEMS
        except ModuleNotFoundError as e:
            if e.name != mod:
                raise
    return items


def write_meta(path, folder=False):
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return
    if folder:
        meta.write_text("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                        "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex, newline="\n")
        return
    lines = META_TEMPLATE.read_text().splitlines()
    done = False
    for i, ln in enumerate(lines):
        if ln.startswith("guid:") and not done:
            lines[i] = "guid: " + uuid.uuid4().hex
            done = True
        elif ln.strip().startswith("spriteID:"):
            lines[i] = "    spriteID: " + uuid.uuid4().hex
        elif ln.strip().startswith("maxTextureSize:"):
            lines[i] = ln.split(":")[0] + ": 1024"
    meta.write_text("\n".join(lines) + "\n", newline="\n")


def item_id(name):
    return "item_" + re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def main(names):
    items = [it for it in load_items() if not names or it[0] in names]
    OUT.mkdir(parents=True, exist_ok=True)
    MODELS.mkdir(exist_ok=True)
    write_meta(OUT, folder=True)
    rows = (len(items) + COLS - 1) // COLS
    sheet = Image.new("RGBA", (THUMB * COLS, THUMB * rows), (240, 232, 218, 255))
    for i, (name, ko, fn) in enumerate(items):
        m = Mesh()
        fn(m)
        img = render(m, SIZE)
        p = OUT / f"Food_{name}.png"
        img.save(p)
        write_meta(p)
        write_obj(m, MODELS / f"Food_{name}.obj")
        sheet.alpha_composite(img.resize((THUMB, THUMB), Image.LANCZOS), (i % COLS * THUMB, i // COLS * THUMB))
        print(f"{name} ({ko}) tris={len(m.F)}", flush=True)
    if names:
        sheet.save(HERE / "preview_tmp.png")
        return
    sheet.save(HERE / "Food_Sheet.png")
    lines = ["# 음식 아이템 목록", "", f"총 {len(items)}종. 아이템 ID는 제안이며 `ItemData` 연결은 아직 없습니다.", "",
             "| 파일 | 이름 | 제안 아이템 ID |", "| --- | --- | --- |"]
    lines += [f"| Food_{n}.png | {ko} | `{item_id(n)}` |" for n, ko, _ in items]
    (HERE / "catalog.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main(sys.argv[1:])
