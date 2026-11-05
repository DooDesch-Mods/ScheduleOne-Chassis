"""
Every vanilla car's mirrors in first person: spawns each car of BaseInterior.VanillaCodes at the mirrors.py spot at
noon, gets in, turns first person on and looks at the rear-view mirror and both side mirrors, once with Mirrors =
Rear + Side (2) and once with + Nearby Cars (3). Per car and mirror it checks:

  - the mirror was placed (rear: under the windscreen, side: on the body's mirror face),
  - the side glass covers a real mirror face: at least MIN_SIDE_W wide and MIN_SIDE_H high,
  - the side glass takes the shape of the vanilla mirror face, not a rectangle laid over it,
  - the side view is widened like a convex mirror, not stretched from a sliver of glass (widen at most MAX_WIDEN),
  - in each mode the mirror renders and its glass shows (`chassis mirror info`),

and saves a screenshot per car, mode and mirror. Prints PASS or FAIL per check and a table of car x mirror at the end.

    python mirrors_cars.py --slot mptest2 --label before
    python mirrors_cars.py --slot mptest2 --label after --vehicles cheetah,shitbox

Screenshots go to out/cars-<label>/<code>-mode<m>-<mirror>.png. Needs a Debug build of Chassis in the slot and a
loaded save. Each car is removed after its run.
"""
import argparse
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run, SPOT, YAW, OUT, part  # noqa: E402

# BaseInterior.VanillaCodes
CODES = ["shitbox", "shitbox_police", "cheetah", "dinkler", "hounddog", "bruiser", "bruiser_police", "veeper", "hotbox"]
MODES = {2: "Rear + Side", 3: "+ Nearby Cars"}
MIRRORS = ("rear", "left", "right")
# A car's outside mirror glass is 12 to 25 cm wide; a face narrower than this is a sliver of it.
MIN_SIDE_W, MIN_SIDE_H = 0.08, 0.05
# A whole side glass is widened x14 to x23 to span 34 degrees; a sliver of it needs x80 and more.
MAX_WIDEN = 40.0


class CarRun(Run):
    """Run with the screenshots of one car in its own folder."""

    def __init__(self, slot, label, folder):
        super().__init__(slot, label)
        self.folder = folder

    def shot(self, name):
        path = self.g.shot()
        if not path or not os.path.isfile(path):
            path = self.g.newest_shot()
        if not path:
            return self.check("screenshot " + name, False, "no file")
        os.makedirs(self.folder, exist_ok=True)
        dest = os.path.join(self.folder, name + ".png")
        shutil.copyfile(path, dest)
        return self.check("screenshot " + name, True, dest)


def mirror_info(info, label):
    """The rear part (before the first `;`) or the left/right part of a `chassis mirror info` answer."""
    return info.split(";")[0] if label == "rear" else part(info, label)


def run_car(r, code, table):
    g = r.g
    c = re.escape(code)
    cell = {m: [] for m in MIRRORS}

    def check(mirror, name, ok, detail=""):
        if not r.check("%s %s: %s" % (code, mirror, name), ok, detail):
            cell[mirror].append(name)
        return ok

    start = r.mark()
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    g.run("spawnvehicle " + code, settle_ms=3000, max_wait_ms=8000)

    # The interior is built once per vehicle model, often at save load: read the whole log for the placement.
    whole = (start[0], 0)
    housing = r.lines(whole, r"\] %s: rear-view mirror at" % c)
    nowind = r.lines(whole, r"\] %s: no windscreen found" % c)
    check("rear", "placed under the windscreen", bool(housing) and not nowind, (housing or nowind or ["no placement line"])[-1])
    for side in ("left", "right"):
        placed = r.lines(whole, r"\] %s: %s side mirror at" % (c, side))
        missing = r.lines(whole, r"\] %s: (no %s outside mirror|the %s outside mirror face is)" % (c, side, side))
        check(side, "placed on the body mirror", bool(placed) and not missing, (placed or missing or ["no placement line"])[-1])
        size = re.search(r"side mirror at \S+ \S+ \S+ ([\d.]+) x ([\d.]+) m", placed[-1]) if placed else None
        w, h = (float(size.group(1)), float(size.group(2))) if size else (0.0, 0.0)
        check(side, "glass covers the mirror face (at least %.2f x %.2f m)" % (MIN_SIDE_W, MIN_SIDE_H),
              w >= MIN_SIDE_W and h >= MIN_SIDE_H, "%.2f x %.2f m" % (w, h))

    entered = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
    if not check("rear", "got in", any("driver" in l for l in entered), entered[-1] if entered else "no answer"):
        return cell
    view = r.say("chassis view 0", "view=")
    check("rear", "first person on", any("first person" in l for l in view), view[-1] if view else "no answer")

    for mode, name in MODES.items():
        said = r.say("chassis mirror mode %d" % mode, "mirror ")
        now = said[-1] if said else ""
        check("rear", "mode %d set" % mode, "mode " + name in now, now or "no answer")
        for mirror in MIRRORS:
            look = r.say("chassis mirror look" + ("" if mirror == "rear" else " " + mirror), "looking at")
            check(mirror, "mode %d view turned to it" % mode, bool(look), look[-1] if look else "no answer")
            time.sleep(1.5)
            mine = mirror_info(r.info(), mirror)
            # The side mirrors take turns: "rendering, skips this frame" counts as rendering.
            check(mirror, "mode %d renders with glass" % mode,
                  re.search(r"\brendering\b", mine) is not None and "glass yes" in mine, mine or "no %s part" % mirror)
            r.shot("%s-mode%d-%s" % (code, mode, mirror))

    for side in ("left", "right"):
        made = r.lines(start, r"\[Mirror\] glass added to the %s mirror" % side)
        check(side, "glass shaped like the vanilla mirror", bool(made) and "shaped like the vanilla mirror" in made[-1],
              made[-1] if made else "no glass line")
        first = r.lines(start, r"\[Mirror\] first mirror render \(%s\)" % side)
        wide = re.search(r"widened x([\d.]+)", first[0]) if first else None
        x = float(wide.group(1)) if wide else 0.0
        check(side, "widened like a convex mirror, not from a sliver (x1 to x%.0f)" % MAX_WIDEN,
              1.0 <= x <= MAX_WIDEN, first[0] if first else "no first-render line")

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Mirror\].*failed")
    check("rear", "no Chassis exception or mirror error", not errors, errors[0] if errors else "")

    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return cell


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicles", default=",".join(CODES))
    a = ap.parse_args()
    folder = os.path.join(OUT, "cars-" + a.label)
    r = CarRun(a.slot, "cars-" + a.label, folder)
    table = {}
    for code in [v.strip().lower() for v in a.vehicles.split(",") if v.strip()]:
        print("\n----- " + code, flush=True)
        table[code] = run_car(r, code, table)
    r.say("chassis mirror mode 2", "mirror ")

    print("\n| car | rear | left | right |")
    print("|---|---|---|---|")
    for code, cell in table.items():
        print("| %s | %s |" % (code, " | ".join("PASS" if not cell[m] else "FAIL: " + ", ".join(cell[m]) for m in MIRRORS)))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
