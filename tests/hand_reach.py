"""The seated body's hand reaches for the light switch, cabin light, glovebox and ignition (Chassis#112).

Singleplayer, on every vanilla car (or the ones named). Per car the driver uses each switch the way a click does
(`chassis reach use <switch>`), first in first person, then in the chase view, and checks:
  <car>-<switch>-fp-delay   - first person: the switch goes 0.1-0.5 s after the click, when the hand arrives
  <car>-<switch>-fp-reach   - the hand got there: reach peak 1, wrist on its target, fingertips at the switch
  <car>-<switch>-tp-reach   - the same in the chase view (ignition by its key binding, `chassis ign key`)
  <car>-off-at-once         - with `chassis reach off` the glovebox opens at once, no reach starts
  <car>-off-still           - and the hand stays on the wheel
Screenshots per switch: the first-person view while the hand is on the switch, and a 6-view rig from outside the
car (front, both sides, above), so the arm can be checked for clipping through doors and the dash. Paths are printed.

Usage: python -I tests/hand_reach.py --slot mptest5 [--cars shitbox,cheetah]
Needs a Debug build of Chassis (the `chassis` console command) in a loaded save.
"""
import argparse
import os
import re
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
CARS = ["shitbox", "cheetah", "dinkler", "hounddog", "hotbox", "bruiser", "veeper", "shitbox_police", "bruiser_police"]
SWITCHES = ["lights", "dome", "glovebox", "ignition"]
X, Y, Z, YAW = -50.8, 0.6, 57.0, 290  # an open street: the rig cameras see the cabin without trees in the way
HOLD = 2.5           # extra seconds the hand stays on the switch for the screenshots
TIP_MAX = 0.10       # m the fingertips may stay off the switch's middle
WRIST_MAX = 0.06     # m the wrist may stay off its IK target
DELAY = (0.10, 0.50)  # s between the click and the switch
USED = re.compile(r"'(\w+)' in .+? used (?:after ([\d.]+) s|at once)")
LAST = re.compile(r"reach: local .+? seat (\d+) (idle|reaching .+?) \| last '(\w+)' hand ([RL]) tip ([\d.\-]+) wrist ([\d.\-]+) lean ([\d.]+) peak ([\d.]+)")

results = []


def s1(slot, *args):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    return out.stdout + out.stderr


def run(slot, words, until=None, max_wait=8000, quiet=False):
    args = ["run"] + words.split() + ["--max-wait", str(max_wait)]
    if until:
        args += ["--until", until]
    text = s1(slot, *args)
    if not quiet:
        print("  > " + words)
        for line in text.strip().splitlines()[-3:]:
            print("    " + line)
    return text


def check(name, ok, what):
    results.append((name, ok, what))
    print(("  PASS " if ok else "  FAIL ") + name + ": " + what)


def shot(slot, label):
    lines = s1(slot, "shot").strip().splitlines()
    path = lines[-1] if lines else "none"
    print("  shot " + label + ": " + path)
    return path


def rig(slot, label):
    """Six views around the cabin at head height. The seated player's own position stays where it boarded, so the
    rig aims at the car's middle instead."""
    target = ["%.2f" % X, "%.2f" % (Y + 0.6), "%.2f" % Z]
    text = s1(slot, "cam", "rig", "--target", *target, "--radius", "2.4", "--height", "0.4", "--hud", "hide")
    m = re.search(r'"path":\s*"([^"]+)"', text)
    path = m.group(1).replace("\\\\", "\\") if m else "none: " + text.strip()[-200:]
    print("  rig " + label + ": " + path)
    return path


def last(slot, switch):
    text = run(slot, "chassis reach", until=r"reach: (local|no seated)", quiet=True)
    for m in LAST.finditer(text):
        if m.group(3) == switch:
            return {"state": m.group(2), "hand": m.group(4), "tip": float(m.group(5)) if m.group(5) != "-" else 9.0,
                    "wrist": float(m.group(6)) if m.group(6) != "-" else 9.0, "lean": float(m.group(7)), "peak": float(m.group(8))}
    return None


def reach_checks(name, r):
    if r is None:
        check(name, False, "no finished reach in `chassis reach`")
        return
    ok = r["peak"] > 0.99 and r["wrist"] < WRIST_MAX and r["tip"] < TIP_MAX
    check(name, ok, "hand %s peak %.2f, wrist %.3f m off target, fingertips %.3f m from the switch, lean %.2f m"
          % (r["hand"], r["peak"], r["wrist"], r["tip"], r["lean"]))


def bound(slot, switch):
    """`chassis aim` turns first person on and looks at the switch; it names a switch the car does not have."""
    text = run(slot, "chassis aim " + switch, quiet=True)
    return "no switch" not in text


def chase(slot):
    """`chassis view` toggles first person and prints the new view; toggle until it is the chase view."""
    for _ in range(2):
        if "view=chase" in run(slot, "chassis view", quiet=True):
            return
    print("  could not leave first person")


def car(slot, code):
    print("== " + code)
    run(slot, "chassis exit", quiet=True)
    time.sleep(1.0)
    run(slot, "chassis car remove", quiet=True)
    s1(slot, "tp", "%.2f" % (X + 4), "%.2f" % (Y + 0.6), "%.2f" % Z)
    time.sleep(1.0)
    run(slot, "chassis car put %s %.2f %.2f %.2f %d" % (code, X, Y, Z, YAW), max_wait=5000)
    time.sleep(2.0)
    if "(driver)" not in run(slot, "chassis car enter " + code):
        check(code + "-setup", False, "not in the driver seat")
        return
    time.sleep(2.5)
    run(slot, "chassis reach on", quiet=True)
    run(slot, "chassis reach hold %.1f" % HOLD, quiet=True)

    # First person: look at the switch, click it, shoot while the hand is on it.
    for sw in SWITCHES:
        run(slot, "chassis view 10", quiet=True)
        if not bound(slot, sw):
            print("  " + code + " has no '" + sw + "' switch - skipped")
            continue
        time.sleep(0.6)
        text = run(slot, "chassis reach use " + sw, until=r"used (after|at once)", max_wait=3000)
        m = USED.search(text)
        delay = float(m.group(2)) if m and m.group(2) else -1.0
        check(code + "-" + sw + "-fp-delay", DELAY[0] <= delay <= DELAY[1], "switch went %.2f s after the click" % delay)
        time.sleep(0.4)
        shot(slot, code + " " + sw + " first person, hand on the switch")
        time.sleep(HOLD + 0.8)
        reach_checks(code + "-" + sw + "-fp-reach", last(slot, sw))
        if sw == "glovebox":
            run(slot, "chassis cabin close", quiet=True)
            time.sleep(0.5)

    # Chase view: the same switches, the ignition by its key binding; a rig from outside while the hand is out.
    for sw in SWITCHES:
        if not bound(slot, sw):
            continue
        chase(slot)
        cmd = "chassis ign key" if sw == "ignition" else "chassis reach use " + sw
        run(slot, cmd, until=r"used (after|at once)", max_wait=3000)
        time.sleep(0.5)
        rig(slot, code + " " + sw + " chase view, from outside")
        time.sleep(HOLD + 0.5)
        reach_checks(code + "-" + sw + "-tp-reach", last(slot, sw))
        if sw == "glovebox":
            run(slot, "chassis cabin close", quiet=True)
            time.sleep(0.5)

    # Setting off: the glovebox opens at once and no hand moves.
    run(slot, "chassis reach off", quiet=True)
    if bound(slot, "glovebox"):
        before = last(slot, "glovebox")
        text = run(slot, "chassis reach use glovebox", until=r"used (after|at once)", max_wait=3000)
        started = "reaches for '" in text
        check(code + "-off-at-once", "used at once (preference" in text and not started,
              "log: " + ("at once" if "used at once" in text else "delayed") + (", a reach started" if started else ", no reach"))
        time.sleep(0.6)
        now = run(slot, "chassis reach", until=r"reach: (local|no seated)", quiet=True)
        check(code + "-off-still", "reaching" not in now and (before is None or last(slot, "glovebox") == before),
              "no reach running or recorded after the use")
        run(slot, "chassis cabin close", quiet=True)
    run(slot, "chassis reach on", quiet=True)
    run(slot, "chassis reach hold 0", quiet=True)
    run(slot, "chassis view", quiet=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--cars", default=",".join(CARS))
    a = ap.parse_args()
    s1(a.slot, "time", "1200")
    for code in [c.strip() for c in a.cars.split(",") if c.strip()]:
        car(a.slot, code)
    run(a.slot, "chassis exit", quiet=True)
    failed = [r for r in results if not r[1]]
    print("\n%d checks, %d failed" % (len(results), len(failed)))
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
