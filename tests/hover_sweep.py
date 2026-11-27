"""Which cockpit control the first-person view lands on, over a grid of pitch and yaw, per car and seat (#110).

Runs `chassis view sweep` in each car and seat and prints its grid: a letter names the hovered control, '.' is
none. Shows how much of the normal view a click would give to a switch instead of the item in the hand.

  sweep-<car>-<seat>  - the sweep finished and printed its grid (a measurement, no pass mark on the share)

Usage: python -I tests/hover_sweep.py --slot mptest3 [--cars cheetah,shitbox,hotbox,veeper] [--seats 0,1] [--step 5]
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hand_items_coop import run, log_lines, where  # noqa: E402

HEAD = r"\[Console\] view sweep: \d+ of \d+ cells"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--cars", default="cheetah,shitbox,hotbox,veeper")
    ap.add_argument("--seats", default="0,1")
    ap.add_argument("--step", type=int, default=5, help="grid step in degrees")
    args = ap.parse_args()
    s = args.slot
    results = []
    x, y, z = where(s)["position"]
    for car in args.cars.split(","):
        run(s, "chassis car put %s %.2f %.2f %.2f 0" % (car, x + 4, y, z), max_wait=5000)
        time.sleep(2)
        for seat in args.seats.split(","):
            run(s, "chassis car enter %s %s" % (car, seat))
            time.sleep(2.5)
            before = len(log_lines(s, HEAD))
            run(s, "chassis view sweep %d" % args.step, until=r"view sweep")
            grid = []
            end = time.time() + 300
            while time.time() < end:
                time.sleep(2)
                if len(log_lines(s, HEAD)) > before:
                    time.sleep(1)
                    lines = log_lines(s, r"\[Console\] view sweep")
                    start = max(i for i, l in enumerate(lines) if "cells on a control" in l)
                    grid = lines[start:]
                    break
            results.append(("sweep-%s-%s" % (car, seat), bool(grid), grid[0] if grid else "no sweep result within 300 s"))
            for l in grid:
                print("  " + l.split("[Console] ", 1)[-1])
            run(s, "chassis view", until=r"view=")
            run(s, "chassis items exit")
            time.sleep(1.5)
        run(s, "chassis car remove", max_wait=5000)
        time.sleep(1)
    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    print("===== %d PASS, %d FAIL" % (len(results) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
