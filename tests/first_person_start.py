"""
The start view of a seat: the last view the player left a vehicle in, or first person with the StartInFirstPerson
preference.

    python first_person_start.py --slot mptest

Spawns a car beside the player and enters it five times:
  1. preference off, last view chase   -> chase
  2. first person on, out, in          -> first person (last view)
  3. first person off, out, in         -> chase (last view)
  4. preference on                     -> first person (preference)
  5. first person off, out, in         -> first person (preference wins over the last view)
Reads the view from the `[View] first person on|off (...)` log lines. Prints PASS or FAIL per check; exit code 1 on
any FAIL. Debug build of Chassis needed, a save loaded.
"""
import argparse
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
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


def view_lines(slot):
    return re.findall(r"\[View\] first person (on|off) \(([^)]*)\)", s1(slot, "log", "--grep", "[View] first person", "-n", "400"))


def enter(slot):
    """Gets in and returns the view lines written since, after the start view had a few frames."""
    before = len(view_lines(slot))
    run(slot, "chassis car enter shitbox")
    time.sleep(2)
    return view_lines(slot)[before:]


def leave(slot):
    run(slot, "chassis exit")
    time.sleep(1.5)


def is_first(lines):
    return bool(lines) and lines[-1][0] == "on"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    slot = ap.parse_args().slot

    run(slot, "chassis exit")
    for _ in range(3):
        run(slot, "chassis despawn car")
    run(slot, "chassis view start off")
    run(slot, "spawnvehicle shitbox")
    time.sleep(2)

    got = enter(slot)
    check("chase-first", not is_first(got), "first seat with the preference off and chase as last view: %s" % (got or "no view change"))

    run(slot, "chassis view")
    leave(slot)
    got = enter(slot)
    check("last-first-person", is_first(got) and "last view" in got[-1][1], "left in first person, gets in: %s" % got)

    run(slot, "chassis view")
    leave(slot)
    got = enter(slot)
    check("last-chase", not is_first(got), "left in chase view, gets in: %s" % (got or "no view change"))

    run(slot, "chassis view start on")
    leave(slot)
    got = enter(slot)
    check("pref-on", is_first(got) and "start in first person" in got[-1][1], "preference on, gets in: %s" % got)

    run(slot, "chassis view")
    leave(slot)
    got = enter(slot)
    check("pref-wins", is_first(got) and "start in first person" in got[-1][1], "preference on, left in chase view, gets in: %s" % got)

    shot = s1(slot, "shot").strip().splitlines()
    print("INFO first person screenshot: " + (shot[-1] if shot else "none"))
    run(slot, "chassis view start off")
    leave(slot)
    run(slot, "chassis despawn car")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
