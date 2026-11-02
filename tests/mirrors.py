"""
Live mirrors in the sedan: spawns a shitbox at a fixed spot at noon, gets in, turns first person on,
looks at the rear-view mirror and both side mirrors and checks the `[Mirror]` log lines and `chassis mirror`
answers. Takes screenshots of the player view (own car hidden and shown, each side mirror), steps through the
Reflections levels and measures frame times with the mirrors on and off. Prints PASS or FAIL per check.

    python mirrors.py --slot mptest4 --label main

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
    raise SystemExit("[mirrors] Workspace/tools/bridge/s1.py not found above " + os.path.abspath(__file__))


sys.path.insert(0, _workspace_bridge())
from s1 import Game  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

# East end of the west parking lot, facing west; the car spawns 4 m ahead of the player (vanilla spawnvehicle).
SPOT = (-133.0, -3.6, 54.2)
YAW = 270.0
# Picture size per Reflections level: width from Mirrors.Widths, height from the 0.24 x 0.07 m housing.
PICTURES = {0: "256x75", 1: "384x112", 2: "512x149", 3: "768x224"}
# Side mirror picture width per level (Mirrors.SideWidths); the height follows the housing the body yields.
SIDE_WIDTHS = {0: 128, 1: 192, 2: 256, 3: 320}


def part(info, label):
    """The `; left ...` or `; right ...` part of a `chassis mirror info` answer."""
    for piece in info.split(";")[1:]:
        if piece.strip().startswith(label + " "):
            return piece.strip()
    return ""


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

    def note(self, text):
        print("INFO  " + text, flush=True)

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

    def say(self, command, marker, settle_ms=1500):
        """Runs a console command and returns the answer lines that contain `marker`."""
        answer = self.g.run(command, settle_ms=settle_ms, max_wait_ms=8000)
        body = (answer.get("result") or answer) if isinstance(answer, dict) else {}
        return [l.get("text", "") for l in body.get("lines", []) if marker in l.get("text", "")]

    def info(self):
        said = self.say("chassis mirror info", "mirror ")
        return said[-1] if said else ""

    def shot(self, name):
        path = self.g.shot()
        if not path or not os.path.isfile(path):
            path = self.g.newest_shot()
        if not path:
            return self.check("screenshot " + name, False, "no file")
        os.makedirs(OUT, exist_ok=True)
        dest = os.path.join(OUT, "%s-%s.png" % (self.label, name))
        shutil.copyfile(path, dest)
        return self.check("screenshot " + name, True, dest)

    def frames(self, seconds):
        answer = self.g.world("frame_stats", {"seconds": seconds}, timeout_ms=int(seconds * 1000) + 8000)
        return (answer.get("result") or answer) if isinstance(answer, dict) else {}


def ms(stats, *keys):
    for key in keys:
        if isinstance(stats, dict) and key in stats:
            return stats[key]
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--seconds", type=float, default=6.0, help="length of each frame-time sample")
    a = ap.parse_args()
    r = Run(a.slot, a.label)
    g = r.g

    start = r.mark()
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    g.run("spawnvehicle shitbox", settle_ms=3000, max_wait_ms=8000)

    # The interior is built once per vehicle model, often at save load before the mark: read the whole log.
    whole = (start[0], 0)
    housing = r.lines(whole, r"\] shitbox: rear-view mirror at")
    r.note("mirror placement: " + (housing[-1] if housing else "no line"))
    for side in ("left", "right"):
        placed = r.lines(whole, r"\] shitbox: %s side mirror at" % side)
        missing = r.lines(whole, r"no %s outside mirror" % side)
        r.check("%s side mirror placed on the body mirror" % side, bool(placed) and not missing,
                (placed or missing or ["no placement line"])[0])

    entered = r.say("chassis car enter", "car enter", settle_ms=2500)
    r.check("got into the sedan", any("driver" in l for l in entered), entered[-1] if entered else "no answer")
    seated = r.wait_line(start, r"\[Mirror\] seated in shitbox.*mirror found", 10)
    r.check("mirror found in the seated car", seated is not None, seated or "no line within 10 s")

    view = r.say("chassis view 0", "view=")
    r.check("first person on", any("first person" in l for l in view), view[-1] if view else "no answer")
    look = r.say("chassis mirror look", "looking at")
    r.check("view turned to the mirror", bool(look), look[-1] if look else "no answer")
    time.sleep(1.5)

    first = r.wait_line(start, r"\[Mirror\] first mirror render \(rear\)", 10)
    r.check("mirror camera renders", first is not None, first or "no line within 10 s")
    reach = r.lines(start, r"\[Mirror\] range \d+ m")
    metres = int(re.search(r"range (\d+) m", reach[-1]).group(1)) if reach else 0
    r.check("mirror sees as far as the main camera (over 100 m)", metres > 100, reach[-1] if reach else "no range line")
    hidden = r.lines(start, r"\[Mirror\] \d+ body renderer\(s\)")
    r.check("own car renderers collected", bool(hidden) and not re.search(r"\] 0 body", hidden[0]), hidden[0] if hidden else "no line")
    now = r.info()
    r.check("info: rendering with glass", "rendering" in now and "glass yes" in now, now)
    r.check("info: own car hidden by default", "own car hidden" in now, now)
    r.shot("mirror-own-hidden")

    r.say("chassis mirror own on", "mirror ")
    time.sleep(1.0)
    now = r.info()
    r.check("own car shown on request", "own car shown" in now, now)
    r.shot("mirror-own-shown")
    r.say("chassis mirror own off", "mirror ")

    for side in ("left", "right"):
        look = r.say("chassis mirror look " + side, "looking at")
        r.check("view turned to the %s mirror" % side, bool(look), look[-1] if look else "no answer")
        time.sleep(1.5)
        first = r.wait_line(start, r"\[Mirror\] first mirror render \(%s\)" % side, 10)
        r.check("%s mirror camera renders" % side, first is not None, first or "no line within 10 s")
        mine = part(r.info(), side)
        r.check("info: %s rendering with glass" % side, mine.startswith(side + " rendering") and "glass yes" in mine, mine or "no %s part" % side)
        r.shot("mirror-" + side)
    r.say("chassis mirror look", "looking at")
    time.sleep(1.0)

    for level in (0, 2, 3, 1):
        mark = r.mark()
        r.say("chassis mirror level %d" % level, "mirror ")
        time.sleep(1.0)
        want = PICTURES[level]
        now = r.info()
        r.check("level %d picture %s" % (level, want), "picture " + want in now.split(";")[0], now)
        for side in ("left", "right"):
            mine = part(now, side)
            r.check("level %d %s picture %d wide" % (level, side, SIDE_WIDTHS[level]), "picture %dx" % SIDE_WIDTHS[level] in mine, mine or "no %s part" % side)
        if level == 3:
            r.shot("mirror-ultra")
        bad = r.lines(mark, r"\[Mirror\].*(failed|Error)")
        if bad:
            r.check("level %d without mirror error" % level, False, bad[0])

    on = r.frames(a.seconds)
    r.say("chassis mirror off", "mirror ")
    time.sleep(0.5)
    off = r.frames(a.seconds)
    r.say("chassis mirror on", "mirror ")
    r.note("frame stats with mirror: %r" % (on,))
    r.note("frame stats without mirror: %r" % (off,))
    avg_on, avg_off = ms(on, "avgMs", "meanMs", "avg_ms", "avg"), ms(off, "avgMs", "meanMs", "avg_ms", "avg")
    if isinstance(avg_on, (int, float)) and isinstance(avg_off, (int, float)):
        r.check("frame time with mirror measured", True, "%.2f ms with, %.2f ms without, +%.2f ms" % (avg_on, avg_off, avg_on - avg_off))
    else:
        r.check("frame time with mirror measured", False, "no average in frame_stats: %r" % (on,))

    # Looking straight ahead the mirror is still in view; looking down at the knees it is not: the camera stops.
    r.say("chassis view 60", "view=")
    time.sleep(1.0)
    now = r.info()
    r.check("camera stops when the mirror is off screen", "off screen" in now.split(";")[0], now)

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Mirror\].*failed")
    r.check("no Chassis exception or mirror error", not errors, errors[0] if errors else "")
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
