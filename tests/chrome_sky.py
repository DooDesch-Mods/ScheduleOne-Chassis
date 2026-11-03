"""
Chrome reflections show the sky: spawns a car at a fixed spot at noon, paints it chrome, reads the
`[Tuning] chrome sky` log lines and takes two screenshots from fixed cameras. Then paints it metallic and
checks the material through `chassis tune mat`. Prints PASS or FAIL per check.

    python chrome_sky.py --slot mptest3 --label after
    python chrome_sky.py --slot mptest3 --label before --no-sky

`--no-sky` is for a build without the sky copy (the state before the fix): the sky lines are then expected
to be missing, and the run only produces the comparison screenshots.
Needs a Debug build of Chassis in the slot and a loaded save.
"""
import argparse
import os
import re
import shutil
import sys
import time


def _workspace_bridge():
    folder = os.path.dirname(os.path.abspath(__file__))
    for _ in range(6):
        candidate = os.path.join(folder, "Workspace", "tools", "bridge")
        if os.path.isfile(os.path.join(candidate, "s1.py")):
            return candidate
        folder = os.path.dirname(folder)
    raise SystemExit("[chrome_sky] Workspace/tools/bridge/s1.py not found above " + os.path.abspath(__file__))


sys.path.insert(0, _workspace_bridge())
from s1 import Game  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

# East end of the west parking lot, facing west; the car spawns 4 m ahead of the player (vanilla spawnvehicle).
SPOT = (-133.0, -3.6, 54.2)
YAW = 270.0


def ahead(yaw, dist, side=0.0, up=0.0):
    """A point relative to SPOT: `dist` along the facing, `side` to the right, `up` above the ground."""
    import math
    r = math.radians(yaw)
    fx, fz = math.sin(r), math.cos(r)
    rx, rz = fz, -fx
    return [round(SPOT[0] + fx * dist + rx * side, 2), round(SPOT[1] + up, 2), round(SPOT[2] + fz * dist + rz * side, 2)]


class Run:
    def __init__(self, slot, label):
        for stream in (sys.stdout, sys.stderr):
            stream.reconfigure(encoding="utf-8", errors="replace")
        self.g = Game(slot)
        self.label = label
        self.results = []

    def check(self, name, ok, detail=""):
        verdict = "PASS" if ok else "FAIL"
        self.results.append((name, verdict, detail))
        print("%s  %s%s" % (verdict, name, ("  - " + str(detail)) if detail else ""), flush=True)
        return ok

    def summary(self):
        fails = [r for r in self.results if r[1] == "FAIL"]
        print("\n===== %d PASS, %d FAIL" % (len(self.results) - len(fails), len(fails)))
        for name, verdict, detail in fails:
            print("%s  %s  - %s" % (verdict, name, detail))
        return 1 if fails else 0

    def mark(self):
        path = self.g.log_path()
        return (path, os.path.getsize(path) if path else 0)

    def lines(self, mark, pattern=None):
        path, offset = mark
        now = self.g.log_path()
        if not now:
            return []
        with open(now, "r", encoding="utf-8", errors="replace") as handle:
            handle.seek(offset if now == path else 0)
            found = handle.read().splitlines()
        if pattern:
            rx = re.compile(pattern)
            found = [l for l in found if rx.search(l)]
        return found

    def wait_line(self, mark, pattern, timeout):
        end = time.time() + timeout
        while True:
            hits = self.lines(mark, pattern)
            if hits:
                return hits[0]
            if time.time() >= end:
                return None
            time.sleep(0.4)

    def shoot(self, name, eye, target):
        answer = self.g.world("camera_shot", {"eye": eye, "target": target, "fov": 55, "hideHud": True,
                                             "settleFrames": 20}, timeout_ms=15000)
        path = (answer.get("result") or answer).get("path") if isinstance(answer, dict) else None
        if not path or not os.path.isfile(path):
            path = self.g.newest_shot()
        if not path:
            return self.check("screenshot " + name, False, "no file: %r" % (answer,))
        os.makedirs(OUT, exist_ok=True)
        dest = os.path.join(OUT, "%s-%s.png" % (self.label, name))
        shutil.copyfile(path, dest)
        return self.check("screenshot " + name, True, dest)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="after")
    ap.add_argument("--no-sky", action="store_true", help="build without the sky copy: expect no sky lines")
    a = ap.parse_args()
    r = Run(a.slot, a.label)
    g = r.g

    start = r.mark()
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)

    # tune set paints the car nearest within 15 m; vanilla spawnvehicle writes nothing to the log.
    g.run("spawnvehicle shitbox", settle_ms=2500, max_wait_ms=8000)

    paint = g.run("chassis tune set paint C0C0C0,chrome", settle_ms=2500, max_wait_ms=8000)
    said = [l.get("text", "") for l in (paint.get("result") or paint).get("lines", []) if "tune set" in l.get("text", "")]
    r.check("chrome paint accepted", any("asked for paint" in l for l in said), said[-1] if said else "no answer")

    if a.no_sky:
        r.check("no sky copy in this build", not r.lines(start, r"chrome sky: copy of the Skybox"))
    else:
        ready = r.wait_line(start, r"chrome sky: copy of the Skybox", 10)
        r.check("sky copy made", ready is not None, ready or "no line within 10 s")
        first = r.wait_line(start, r"chrome sky: first chrome probe render with the sky", 15)
        r.check("chrome probe renders with the sky", first is not None, first or "no line within 15 s")
        missing = r.lines(start, r"chrome sky: .*(no Skybox|failed|missing)")
        r.check("no sky warning or error", not missing, missing[0] if missing else "")

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis")
    r.check("no Chassis exception", not errors, errors[0] if errors else "")

    car = ahead(YAW, 4.0, up=1.0)
    r.shoot("side", ahead(YAW, 4.0, side=5.5, up=2.2), car)
    r.shoot("roof", ahead(YAW, 0.5, side=2.5, up=4.5), car)
    g.world("camera_release", {}, timeout_ms=8000)

    # Metallic sits just above the metal threshold: it must keep reflections on and the black metallic map off.
    g.run("chassis tune set paint C0C0C0,metallic", settle_ms=2500, max_wait_ms=8000)
    mat = g.run("chassis tune mat", settle_ms=1500, max_wait_ms=8000)
    slots = [l.get("text", "") for l in (mat.get("result") or mat).get("lines", []) if "tune mat:" in l.get("text", "")]
    r.check("tune mat lists paint slots", bool(slots), ("%d slot(s): %s" % (len(slots), slots[0])) if slots else "no answer")
    for want in ("metallic=0.45", "metalmap=off", "reflectionsOff=no", "highlightsOff=no"):
        wrong = [l for l in slots if want not in l]
        r.check("metallic paint " + want, bool(slots) and not wrong, wrong[0] if wrong else "")
    r.shoot("metallic-side", ahead(YAW, 4.0, side=5.5, up=2.2), car)
    g.world("camera_release", {}, timeout_ms=8000)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
