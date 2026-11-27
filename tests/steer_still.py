"""Front wheels of a standing car keep still while the local player drives and presses nothing (#89).

Per car: put it beside the player, take the driver's seat, wait until it has settled, then watch the steered wheels
for a few seconds with `chassis car steerwatch`. A console steer input afterwards must still turn them.

  still-<car>  - every steered wheel's yaw stays within 0.5 degrees and never changes direction
  input-<car>  - `chassis drive 0 1` turns the wheels past 5 degrees
  foreign-warn - a steer angle moved from outside (`chassis car steerpoke`) gets one warning naming the other patches
  errors       - no Chassis exception and no Better Driving error

Works with other steering mods installed (Better Driving), which is what #89 was reported with.

Usage: python -I tests/steer_still.py --slot mptest3 [--cars cheetah,hotbox] [--seconds 6]
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hand_items_coop import run, log_lines, where  # noqa: E402


def watch(slot, seconds, then=None):
    """Watches the wheels for `seconds`; `then` is a console command sent once the watch runs."""
    before = len(log_lines(slot, r"\[Console\] car steerwatch .*frames"))
    run(slot, "chassis car steerwatch %d" % seconds, until=r"car steerwatch")
    if then:
        run(slot, then, until=r"drive:")
    end = time.time() + seconds + 20
    while time.time() < end:
        time.sleep(1.0)
        lines = log_lines(slot, r"\[Console\] car steerwatch .*frames")
        if len(lines) > before:
            return lines[-1]
    return ""


def still(line):
    found = re.findall(r"yaw (-?[\d.]+)\.\.(-?[\d.]+) flips (\d+)", line)
    return bool(found) and all(float(b) - float(a) < 0.5 and int(f) == 0 for a, b, f in found)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--cars", default="cheetah,hotbox")
    ap.add_argument("--seconds", type=int, default=6)
    args = ap.parse_args()
    s = args.slot
    results = []
    x, y, z = where(s)["position"]
    for car in args.cars.split(","):
        run(s, "chassis car put %s %.2f %.2f %.2f 0" % (car, x + 4, y, z), max_wait=5000)
        time.sleep(2)
        a = run(s, "chassis car enter " + car)
        if "(driver)" not in a:
            results.append(("still-" + car, False, "did not get the driver's seat"))
            continue
        time.sleep(4)
        line = watch(s, args.seconds)
        results.append(("still-" + car, still(line), line or "no steerwatch line"))
        line = watch(s, 3, then="chassis drive 0 1 2")
        m = re.search(r"steer (-?[\d.]+)\.\.(-?[\d.]+)", line)
        results.append(("input-" + car, bool(m) and max(abs(float(m.group(1))), abs(float(m.group(2)))) > 5.0, line or "no steerwatch line"))
        if car == args.cars.split(",")[0]:
            # A second writer moves the angle between two frames: Chassis names it once in a warning.
            time.sleep(3)
            warn = r"\[Steering\] .*changed from .* outside Chassis"
            n0 = len(log_lines(s, warn))
            run(s, "chassis car steerpoke 8", until=r"car steerpoke")
            time.sleep(1.5)
            got = log_lines(s, warn)
            results.append(("foreign-warn", len(got) > n0, got[-1] if len(got) > n0 else "no warning after the poke"))
        time.sleep(3)
        run(s, "chassis items exit")
        time.sleep(1.5)
        run(s, "chassis car remove", max_wait=5000)
        time.sleep(1)
    errs = log_lines(s, r"Exception.*(Chassis|BetterDriving)|\[(Chassis|BetterDriving)\].*(Exception|failed)|\[Steering\].*(failed|Warning)")
    results.append(("errors", not errs, errs[0] if errs else "no Chassis or Better Driving error"))
    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    print("===== %d PASS, %d FAIL" % (len(results) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
