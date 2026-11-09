"""Freely bindable indicator and first-person keys, keyboard and controller.

Spawns a Shitbox next to the player, takes the driver seat and checks:
  prefs     - the bindings load with their defaults (LeftArrow, RightArrow, V)
  rekey     - the left indicator moved to the J key is listed as J
  pad-left  - a controller button bound to the left indicator makes the car signal left
  pad-off   - a second press switches the signal off
  pad-view  - a controller button bound to first person switches the view
  restore   - the session bindings go back to their defaults

Usage: python -I tests/bindings.py --slot mptest6
Needs a Debug build of Chassis (the `chassis` console command) in a loaded save. The controller is a virtual
gamepad the debug command adds; a real controller is not needed. It presses Select, the one pad button vanilla
leaves free: Y, for one, opens the phone, which then holds every driving key.
"""
import argparse
import os
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")


def run(slot, words, until=None, max_wait=8000):
    cmd = [sys.executable, "-I", S1, "--slot", slot, "run"] + words.split() + ["--max-wait", str(max_wait)]
    if until:
        cmd += ["--until", until]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = out.stdout + out.stderr
    print("  > " + words)
    for line in text.strip().splitlines()[-6:]:
        print("    " + line)
    return text


def press(slot, control, expect, tries=3):
    """Presses a virtual gamepad control until the expected line shows. The game ignores driving keys for a moment
    while another state (a menu, the key hints rebuilding) sits on top; the indicator logs that as 'takes no input'."""
    text = ""
    for _ in range(tries):
        text = run(slot, "chassis bind pad " + control, until=expect + "|takes no input", max_wait=4000)
        if expect in text or "takes no input" not in text:
            return text
        print("    (the car took no input, pressing again)")
        time.sleep(1.5)
    return text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--code", default="shitbox")
    args = ap.parse_args()
    results = []

    prefs = run(args.slot, "chassis bind")
    results.append(("prefs", "IndicatorLeft LeftArrow" in prefs and "IndicatorRight RightArrow" in prefs
                    and "FirstPerson V" in prefs, "bindings load with their default keys"))

    rekey = run(args.slot, "chassis bind IndicatorLeft key J")
    results.append(("rekey", "IndicatorLeft J" in rekey, "left indicator moved to J"))
    run(args.slot, "chassis bind IndicatorLeft key LeftArrow")

    run(args.slot, "chassis exit")
    run(args.slot, "spawnvehicle " + args.code, max_wait=4000)
    enter = run(args.slot, "chassis car enter " + args.code)
    if "(driver)" not in enter:
        print("FAIL setup: not in the driver seat of " + args.code)
        return 1

    run(args.slot, "chassis bind IndicatorLeft button <Gamepad>/select")

    # A car from an earlier run may still signal; start with the indicator off.
    run(args.slot, "chassis ctl indicator 0")
    time.sleep(1.0)
    left = press(args.slot, "select", "signals left")
    results.append(("pad-left", "signals left" in left, "controller button signals left"))
    time.sleep(1.0)
    off = press(args.slot, "select", "signals off")
    results.append(("pad-off", "signals off" in off, "second press switches the signal off"))
    run(args.slot, "chassis bind IndicatorLeft button none")
    run(args.slot, "chassis bind FirstPerson button <Gamepad>/select")
    view = run(args.slot, "chassis bind pad select", until=r"first person (on|off)")
    results.append(("pad-view", "first person on" in view or "first person off" in view,
                    "controller button switches the first-person view"))
    if "first person on" in view:
        run(args.slot, "chassis bind pad select", until=r"first person off")

    run(args.slot, "chassis bind IndicatorLeft button none")
    restore = run(args.slot, "chassis bind FirstPerson button none")
    results.append(("restore", "FirstPerson V" in restore and "/" not in restore.split("FirstPerson V")[-1][:3],
                    "bindings back to their defaults"))
    run(args.slot, "chassis exit")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
