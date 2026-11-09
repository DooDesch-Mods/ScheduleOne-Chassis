"""
Underglow must light the ground as one strip: lights close enough that their spots overlap.

    python underglow_strip.py --slot mptest6
    python underglow_strip.py --slot mptest6 --vehicles shitbox,cheetah --shots

Spawns each car on the road, fits neon on all four sides and reads the fitting line Chassis logs
("<side> <length> m every <gap> m"). A side passes when its light gap is at most MAX_GAP; the spot of each light is
about as wide as the car's underside is high, so a larger gap shows separate spots. --shots takes a side and a top
view at night for a look by eye. Prints PASS or FAIL per car and side; exit code 1 on any FAIL.
"""
import argparse
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
POS = (-50.8, 0.6, 57.0, 290)
MAX_GAP = 0.35
SIDE = re.compile(r"\b([lrfb]) ([0-9.]+) m every ([0-9.]+) m")


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def run(slot, cmd):
    return s1(slot, "run", cmd)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--vehicles", default="shitbox")
    ap.add_argument("--shots", action="store_true")
    a = ap.parse_args()
    fails = 0
    run(a.slot, "setpoliceignoreplayers true")
    for code in [c.strip() for c in a.vehicles.split(",") if c.strip()]:
        run(a.slot, "chassis exit")
        run(a.slot, "chassis car remove")
        s1(a.slot, "tp", str(POS[0] + 4), "1", str(POS[2]))
        out = run(a.slot, "chassis car put %s %s %s %s %s" % ((code,) + POS))
        if "car put:" not in out:
            print("FAIL %s: spawn failed: %s" % (code, out.strip()[-200:]))
            fails += 1
            continue
        time.sleep(1.5)
        run(a.slot, "chassis car enter " + code)
        # A car left from an earlier run keeps its neon, and an unchanged value logs no new fitting.
        run(a.slot, "chassis tune remove neon")
        time.sleep(0.5)
        out = run(a.slot, "chassis tune set neon 00FFAA,lrfb")
        run(a.slot, "chassis exit")
        line = next((l for l in out.splitlines() if "fitted to the underside" in l), None)
        if line is None:
            print("FAIL %s: no neon fitting line in the log: %s" % (code, out.strip()[-300:]))
            fails += 1
            continue
        sides = SIDE.findall(line)
        if len(sides) != 4:
            print("FAIL %s: expected 4 sides, got %d: %s" % (code, len(sides), line.strip()))
            fails += 1
        for side, length, gap in sides:
            ok = float(gap) <= MAX_GAP
            print("%s %s side %s: %s m, light every %s m (max %.2f)" % ("PASS" if ok else "FAIL", code, side, length,
                                                                       gap, MAX_GAP))
            fails += 0 if ok else 1
        if a.shots:
            x, y, z = POS[0], POS[1], POS[2]
            s1(a.slot, "tp", "-35", "1", "50")
            s1(a.slot, "time", "2200")
            time.sleep(2)
            eye = ["--eye", str(x + 7.6), "1.7", str(z + 0.4), "--target", str(x), "0.2", str(z), "--hud", "hide",
                   "--settle", "10"]
            print("shot side: " + s1(a.slot, "cam", "shot", *eye).strip())
            top = ["--eye", str(x), "7.5", str(z + 0.05), "--target", str(x), "0", str(z), "--hud", "hide",
                   "--settle", "10"]
            print("shot top: " + s1(a.slot, "cam", "shot", *top).strip())
            s1(a.slot, "cam", "release")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
