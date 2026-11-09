"""
Getting in: the driver's body sits from its first visible frame instead of standing up through the seat.

    python seat_pose.py --slot mptest2
    python seat_pose.py --slot mptest2 --vehicle cheetah

Spawns a car, then gets in twice while `chassis seatanim trace` logs the body's animator for the first frames of each
seating: once with the snap off (the vanilla sit-down transition, which shows the standing body) and once with it on.
A frame counts as standing when the body is shown and the animator is not in the seated state yet. Prints PASS or FAIL
per check; exit code 1 on any FAIL. Debug build of Chassis needed.
"""
import argparse
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
X, Y, Z = 60.0, 4.4, -70.0
FRAMES = 40
LINE = re.compile(r"\[SeatAnim\] .* frame (\d+) shown (\w+) active (\w+) state (\w+) transition ([\w-]+)")
fails = 0


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def run(slot, cmd):
    return s1(slot, "run", cmd)


def check(name, ok, detail):
    global fails
    print("%s %s: %s" % ("PASS" if ok else "FAIL", name, detail))
    fails += 0 if ok else 1


def seating(slot, code):
    """Gets in once and returns (standing frames, traced frames) of that seating."""
    run(slot, "chassis exit")
    time.sleep(1.5)
    run(slot, "chassis car enter " + code)
    time.sleep(2.5)
    rows = []
    for m in LINE.finditer(s1(slot, "log", "--grep", "[SeatAnim]", "-n", str(FRAMES * 3))):
        if m.group(1) == "1":
            rows = []  # a new seating starts; keep only the last one
        rows.append(m)
    standing = sum(1 for m in rows if m.group(2) == "True" and m.group(3) == "True"
                   and (m.group(4) != "pose" or m.group(5) == "to-pose"))
    return standing, len(rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--vehicle", default="cheetah")
    a = ap.parse_args()
    slot, code = a.slot, a.vehicle

    run(slot, "chassis exit")
    run(slot, "chassis car remove")
    s1(slot, "tp", str(X + 4), str(Y), str(Z))
    out = run(slot, "chassis car put %s %s %s %s 180" % (code, X, Y, Z))
    if "car put:" not in out:
        check("spawn", False, out.strip()[-200:])
        sys.exit(1)
    time.sleep(2)
    run(slot, "chassis seatanim trace %d" % FRAMES)

    run(slot, "chassis seatanim snap off")
    standing, traced = seating(slot, code)
    check("repro-without-snap", traced > 0 and standing > 1,
          "vanilla transition: %d of %d traced frames show the body not yet seated" % (standing, traced))

    run(slot, "chassis seatanim snap on")
    standing, traced = seating(slot, code)
    check("seated-first-frame", traced > 0 and standing <= 1,
          "with the snap: %d of %d traced frames show the body not yet seated" % (standing, traced))
    check("snap-log", "jumped straight into the pose" in s1(slot, "log", "--grep", "[Seat]", "-n", "40"),
          "the seat logged the jump into the pose")

    run(slot, "chassis seatanim trace 0")
    run(slot, "chassis exit")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
