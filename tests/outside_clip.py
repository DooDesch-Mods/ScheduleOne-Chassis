"""
Kit parts and occupants seen from outside: for every vanilla car, spawns it, gets in as the driver, runs
`chassis interior probe off` and checks that no kit part and no occupant's head (an ellipsoid around each seat's eyes)
reaches out of the body or through the glass. The side mirror plates and their glass sit on the body's own mirrors
on purpose; they only have to be there. Then gets out and takes six outside shots of the windscreen top and the
roof (front, both front 3/4 views, both sides, from above) and one sheet of all six per car.

    python outside_clip.py --slot mptest2 --label after
    python outside_clip.py --slot mptest2 --label before --cabinfit off --vehicles cheetah,veeper

`--cabinfit off` turns the shell fit of the cabin light and the rear-view mirror off first (the 1.10.2 places),
`on` turns it on; without it the setting stays as it is. Writes out/outside-<label>/<code>-<view>.png and
<code>-sheet.png, prints PASS or FAIL per car and part and a Markdown table. Each car is removed after its run.

Needs a Debug build of Chassis in the slot and a loaded save.
"""
import argparse
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import OUT, SPOT, YAW, Run  # noqa: E402

CODES = ["hotbox", "cheetah", "dinkler", "hounddog", "shitbox", "shitbox_police", "bruiser", "bruiser_police", "veeper"]

# Kit meshes by the part they belong to; the first match wins.
GROUPS = [
    ("cabin light", ("Chassis_DomeHousing", "Chassis_DomeLight")),
    ("rear-view mirror", ("Chassis_RearMirror",)),
    ("side mirror glass", ("Chassis_SideMirror",)),
    ("glovebox", ("Chassis_GloveboxLid",)),
    ("steering wheel", ("Chassis_Wheel", "Chassis_wheel")),
    ("seats", ("Chassis_seat", "Chassis_bench")),
    ("dashboard", ("Chassis_dash",)),
    ("gauges and switches", ("Chassis_gauges", "Needle", "Switch", "Stalk", "Horn", "lamp", "Lamp", "Chassis_")),
]
# Parts every car's kit has; a missing one fails.
REQUIRED = ["cabin light", "rear-view mirror", "side mirror glass", "glovebox", "steering wheel", "seats", "dashboard"]
OUTSIDE_ON_PURPOSE = {"side mirror glass"}
VIEWS = ["front34L", "front", "front34R", "left", "top", "right"]
VEC = r"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)"


def group_of(name):
    for label, needles in GROUPS:
        if any(n in name for n in needles):
            return label
    return "other " + name


def vec(text):
    m = re.search(VEC, text)
    return [float(m.group(i)) for i in (1, 2, 3)] if m else None


def add(a, *terms):
    out = list(a)
    for scale, v in terms:
        out = [out[i] + scale * v[i] for i in range(3)]
    return [round(x, 3) for x in out]


def shots(r, code, folder, target, fwd, right):
    """Six close views of `target`, each saved as <code>-<view>.png; returns the saved paths."""
    up = [0.0, 1.0, 0.0]
    eyes = {
        "front": add(target, (3.0, fwd), (0.45, up)),
        "front34L": add(target, (2.3, fwd), (-2.0, right), (0.8, up)),
        "front34R": add(target, (2.3, fwd), (2.0, right), (0.8, up)),
        "left": add(target, (-0.3, fwd), (-2.7, right), (0.3, up)),
        "right": add(target, (-0.3, fwd), (2.7, right), (0.3, up)),
        "top": add(target, (-0.5, fwd), (2.8, up)),
    }
    saved = {}
    for view in VIEWS:
        answer = r.g.world("camera_shot", {"eye": eyes[view], "target": target, "fov": 30, "hideHud": True, "settleFrames": 3}, timeout_ms=15000)
        res = (answer.get("result") or {}) if isinstance(answer, dict) else {}
        path = res.get("path")
        if not path or not os.path.isfile(path):
            r.check("%s outside shot %s" % (code, view), False, "no file: %s" % (answer.get("error") if isinstance(answer, dict) else answer))
            continue
        dest = os.path.join(folder, "%s-%s.png" % (code, view))
        shutil.copyfile(path, dest)
        saved[view] = dest
    release = r.g.world("camera_release", {}, timeout_ms=8000)
    if not (isinstance(release, dict) and release.get("ok")):
        r.note("%s camera release answered %s" % (code, release))
    r.check("%s outside shots taken (%d of %d)" % (code, len(saved), len(VIEWS)), len(saved) == len(VIEWS), folder)
    return saved


def sheet(code, folder, saved, label):
    """One 3x2 image of the six views with their names."""
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("INFO  no PIL - no sheet for " + code, flush=True)
        return None
    w, h = 640, 360
    img = Image.new("RGB", (w * 3, h * 2), (20, 20, 20))
    draw = ImageDraw.Draw(img)
    for i, view in enumerate(VIEWS):
        if view not in saved:
            continue
        tile = Image.open(saved[view]).convert("RGB").resize((w, h))
        x, y = (i % 3) * w, (i // 3) * h
        img.paste(tile, (x, y))
        draw.rectangle([x, y, x + 230, y + 20], fill=(0, 0, 0))
        draw.text((x + 6, y + 4), "%s %s %s" % (code, view, label), fill=(255, 255, 255))
    dest = os.path.join(folder, code + "-sheet.png")
    img.save(dest)
    return dest


def run_car(r, code, folder, label):
    g = r.g
    c = re.escape(code)
    cell = {}

    def check(part, name, ok, detail=""):
        if not r.check("%s %s: %s" % (code, part, name), ok, detail):
            cell.setdefault(part, []).append(name)
        else:
            cell.setdefault(part, [])
        return ok

    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    g.run("spawnvehicle " + code, settle_ms=3000, max_wait_ms=8000)
    whole = (r.mark()[0], 0)
    test = r.lines(whole, r"\[Interior\] %s shell test" % c)
    r.note("%s %s" % (code, test[-1].split("[Interior] ", 1)[-1] if test else "no shell test line"))

    entered = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
    if not check("car", "got in", any("driver" in l for l in entered), entered[-1] if entered else "no answer"):
        return cell

    start = r.mark()
    said = r.say("chassis interior probe off", "interior probe", settle_ms=1200)
    check("car", "probe answered", bool(said) and code in said[-1], said[-1] if said else "no answer")
    tested = r.lines(start, r"\[Interior\] probe %s tested: " % c)
    names = [n.strip() for n in tested[-1].split("tested: ", 1)[1].rstrip(".").split(",")] if tested else []
    outside = {}
    for line in r.lines(start, r"\[Interior\] probe %s \S+ outside at" % c):
        m = re.search(r"probe %s (\S+) outside at (.*)$" % c, line)
        if m:
            outside[m.group(1)] = len(re.findall(VEC, m.group(2)))
    present = {}
    for n in names:
        present.setdefault(group_of(n), []).append(n)
    for part in REQUIRED:
        check(part, "in the kit", part in present, ", ".join(present.get(part, [])) or "no mesh")
    for part, meshes in present.items():
        if part in OUTSIDE_ON_PURPOSE:
            continue
        bad = ["%s %d" % (m, outside[m]) for m in meshes if m in outside]
        check(part, "inside the body and behind the glass", not bad, ", ".join(bad) if bad else "%d mesh(es)" % len(meshes))
    heads = r.lines(start, r"\[Interior\] probe %s heads: " % c)
    seats = re.findall(r"seat (\d+) (inside|\d+ point\(s\) outside)", heads[-1]) if heads else []
    check("occupants", "heads tested", bool(seats), heads[-1] if heads else "no heads line")
    for seat, verdict in seats:
        check("occupants", "seat %s head under the roof" % seat, verdict == "inside", verdict)

    world = {}
    for line in r.lines(start, r"\[Interior\] probe %s world " % c):
        m = re.search(r"world (\S+) " + VEC, line)
        if m:
            world[m.group(1)] = [float(m.group(i)) for i in (2, 3, 4)]
    pose = r.lines(start, r"\[Interior\] probe %s pose: " % c)
    if pose:
        top = vec(pose[-1].split("windscreen front top", 1)[1])
        fwd = vec(pose[-1].split("forward", 1)[1])
        right = vec(pose[-1].split("right", 1)[1])
        points = [world[k] for k in ("Chassis_DomeHousing", "Chassis_RearMirror") if k in world]
        target = [sum(p[i] for p in points) / len(points) for i in range(3)] if points else top
        target = add(target, (0.05, [0.0, 1.0, 0.0]))
        # Seated, the vehicle camera wins over the free camera: the shots are taken standing beside the car.
        left = r.say("chassis exit", "exit:", settle_ms=2000)
        check("car", "got out for the outside shots", any("left net" in l for l in left), left[-1] if left else "no answer")
        saved = shots(r, code, folder, target, fwd, right)
        back = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
        check("car", "got back in to remove it", any("driver" in l for l in back), back[-1] if back else "no answer")
        made = sheet(code, folder, saved, label)
        if made:
            r.note("%s sheet %s" % (code, made))
    else:
        check("car", "pose line for the outside shots", False, "no pose line")

    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return cell


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicles", default=",".join(CODES))
    ap.add_argument("--cabinfit", choices=["on", "off"])
    a = ap.parse_args()
    folder = os.path.join(OUT, "outside-" + a.label)
    os.makedirs(folder, exist_ok=True)
    r = Run(a.slot, "outside-" + a.label)
    if a.cabinfit:
        said = r.say("chassis interior cabinfit " + a.cabinfit, "interior cabinfit", settle_ms=3000)
        r.check("cabin fit " + a.cabinfit, bool(said) and ("cabinfit " + a.cabinfit) in said[-1], said[-1] if said else "no answer")
    table = {}
    for code in [v.strip().lower() for v in a.vehicles.split(",") if v.strip()]:
        print("\n----- " + code, flush=True)
        table[code] = run_car(r, code, folder, a.label)

    columns = ["cabin light", "rear-view mirror", "side mirror glass", "glovebox", "steering wheel", "seats", "dashboard", "gauges and switches", "occupants"]
    print("\n| car | " + " | ".join(columns) + " |")
    print("|---|" + "---|" * len(columns))
    for code, cell in table.items():
        row = []
        for col in columns:
            if col not in cell:
                row.append("-")
            else:
                row.append("PASS" if not cell[col] else "FAIL")
        extra = ["%s: %s" % (k, ", ".join(v)) for k, v in cell.items() if k not in columns and v]
        print("| %s | %s |%s" % (code, " | ".join(row), (" " + "; ".join(extra)) if extra else ""))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
