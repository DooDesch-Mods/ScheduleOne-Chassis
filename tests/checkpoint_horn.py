"""
Honking at a police checkpoint: the officer asks from the car, searches it and opens the gate.

    python checkpoint_horn.py --slot mptest2

Turns on the first checkpoint, puts its officers at their posts, spawns a car beside it and sits in it. Honks once
outside the search area (no talk expected), parks in search area 1 and honks through the horn key path. The officer
walks to the driver window and asks; then the test waits for the vanilla search and the open gate. Prints PASS or
FAIL per check and the path of a screenshot taken while the
officer talks; exit code 1 on any FAIL. Debug build of Chassis needed, at noon so the checkpoint is staffed.
"""
import argparse
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
WALKS = "honked in a search area - officer"
ASKS = "at the driver window of"
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


def count(slot, text):
    return s1(slot, "log", "--grep", "[Checkpoint]", "-n", "200").count(text)


def asks(slot):
    return count(slot, ASKS)


def state(slot):
    return run(slot, "chassis checkpoint state")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--wait", type=int, default=45, help="seconds to wait for the open gate")
    a = ap.parse_args()
    slot = a.slot

    run(slot, "chassis exit")
    for _ in range(3):
        run(slot, "chassis despawn car")
    s1(slot, "time", "1200")
    # After the clock jump the game switches its checkpoints for the new time, a game minute (about a second) at a
    # time; wait for one it runs with officers and that is still on a few seconds later.
    out = ""
    for _ in range(20):
        time.sleep(1)
        out = run(slot, "chassis checkpoint on")
        if "already has officer" not in out:
            continue
        time.sleep(4)
        if "checkpoint: Enabled" in state(slot):
            break
    m = re.search(r"checkpoint on: \w+ at \((-?[0-9.]+), (-?[0-9.]+), (-?[0-9.]+)\)", out)
    if not m:
        check("checkpoint", False, out.strip()[-200:])
        sys.exit(1)
    x, y, z = (float(v) for v in m.groups())
    # Officers walk over from the station once the checkpoint is on; up to 90 s.
    out = ""
    for _ in range(90):
        time.sleep(1)
        out = run(slot, "chassis checkpoint warp")
        if re.search(r"warp: [1-9]", out):
            break
    m = re.search(r"checkpoint warp: [^\n]*", out)
    check("officers", re.search(r"warp: [1-9]", out) is not None, m.group(0) if m else out.strip()[-120:])

    # A car about 25 m off the checkpoint, outside both search areas.
    s1(slot, "tp", str(x + 25), str(y + 1), str(z))
    run(slot, "spawnvehicle shitbox")
    time.sleep(2)
    run(slot, "chassis car enter shitbox")
    time.sleep(1.5)
    before = count(slot, WALKS)
    out = run(slot, "chassis car hornkey")
    m = re.search(r"car hornkey: [^\n]*", out)
    check("hornkey", "hornkey: pressed" in out, m.group(0) if m else out.strip()[-120:])
    time.sleep(1)
    check("away-no-talk", count(slot, WALKS) == before, "a honk outside the search areas starts no talk")

    run(slot, "chassis checkpoint park")
    time.sleep(2)
    st = state(slot)
    check("in-area", re.search(r"area1 \[[^\]]*shitbox|area1 \[[^\]]*Shitbox", st, re.I) is not None,
          (re.search(r"area1 \[[^\]]*\]", st) or re.search(".*", st)).group(0)[:120])

    before, walks = asks(slot), count(slot, WALKS)
    run(slot, "chassis car hornkey")
    time.sleep(1)
    check("called", count(slot, WALKS) > walks, "the honk in search area 1 sent an officer to the driver window")
    deadline = time.time() + 30
    while time.time() < deadline and asks(slot) == before:
        time.sleep(1)
    log = s1(slot, "log", "--grep", "[Checkpoint]", "-n", "6")
    check("window", asks(slot) > before and "did not reach" not in log, "the officer reached the driver window and asks")
    time.sleep(1.5)
    shot = s1(slot, "shot").strip().splitlines()
    print("INFO talk screenshot: " + (shot[-1] if shot else "none"))
    out = run(slot, "chassis checkpoint choose 1")
    m = re.search(r"checkpoint choose: [^\n]*", out)
    check("okay", "answer 1 in the talk of" in out, m.group(0) if m else out.strip()[-120:])

    searched, opened, last = False, False, ""
    deadline = time.time() + a.wait
    while time.time() < deadline and not (searched and opened):
        time.sleep(3)
        last = state(slot)
        searched = searched or "searching True" in last
        opened = re.search(r"gate1 True|gate2 True", last) is not None
    check("search", searched, "an officer searched the car")
    check("gate", opened, "a gate opened within %d s: %s" % (a.wait, last.strip().splitlines()[0][:160] if last.strip() else "no state"))
    shot = s1(slot, "shot").strip().splitlines()
    print("INFO gate screenshot: " + (shot[-1] if shot else "none"))

    run(slot, "chassis exit")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
