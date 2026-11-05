"""
One image per Mirrors mode from the screenshots of mirrors_cars.py: a row per car, a column per mirror, each cell the
middle of the screen, where `chassis mirror look` puts the mirror.

    python contact_sheet.py --label before

Writes out/cars-<label>/sheet-mode<m>.png.
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors_cars import CODES, MIRRORS, MODES  # noqa: E402
from mirrors import OUT  # noqa: E402

CROP_W, CROP_H, CELL_W, CELL_H, LABEL = 760, 440, 380, 220, 18


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", required=True)
    ap.add_argument("--vehicles", default=",".join(CODES))
    a = ap.parse_args()
    folder = os.path.join(OUT, "cars-" + a.label)
    codes = [v.strip().lower() for v in a.vehicles.split(",") if v.strip()]
    for mode in MODES:
        sheet = Image.new("RGB", (CELL_W * len(MIRRORS), (CELL_H + LABEL) * len(codes)), (20, 20, 20))
        draw = ImageDraw.Draw(sheet)
        missing = 0
        for row, code in enumerate(codes):
            for col, mirror in enumerate(MIRRORS):
                x, y = col * CELL_W, row * (CELL_H + LABEL)
                draw.text((x + 4, y + 3), "%s %s mode %d" % (code, mirror, mode), fill=(230, 230, 230))
                path = os.path.join(folder, "%s-mode%d-%s.png" % (code, mode, mirror))
                if not os.path.isfile(path):
                    missing += 1
                    draw.text((x + 4, y + LABEL + 4), "no screenshot", fill=(255, 80, 80))
                    continue
                shot = Image.open(path).convert("RGB")
                cx, cy = shot.width // 2, shot.height // 2
                cell = shot.crop((cx - CROP_W // 2, cy - CROP_H // 2, cx + CROP_W // 2, cy + CROP_H // 2)).resize((CELL_W, CELL_H))
                sheet.paste(cell, (x, y + LABEL))
        dest = os.path.join(folder, "sheet-mode%d.png" % mode)
        sheet.save(dest)
        print("sheet mode %d: %s (%d screenshot(s) missing)" % (mode, dest, missing))
    return 0


if __name__ == "__main__":
    sys.exit(main())
