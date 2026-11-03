"""Arrow keys and key hint for the indicators on a vanilla car.

Spawns a Shitbox next to the player, takes the driver seat and checks:
  hint   - the key hints list the arrow keys as Indicators
  left   - one left arrow press makes the car signal left (host round trip)
  off    - a second left press switches the signal off

Usage: python -I tests/indicator_keys.py --slot mptest5
Needs a Debug build of Chassis (the `chassis` console command) in a loaded save.
"""
import argparse
import os
import subprocess
import sys

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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--code", default="shitbox")
    args = ap.parse_args()
    results = []

    run(args.slot, "spawnvehicle " + args.code, max_wait=4000)
    enter = run(args.slot, "chassis car enter " + args.code)
    if "(driver)" not in enter:
        print("FAIL setup: not in the driver seat of " + args.code)
        return 1

    hints = run(args.slot, "chassis hints")
    results.append(("hint", "Indicators" in hints, "key hints list Indicators"))

    left = run(args.slot, "chassis car arrow l", until=r"signals left")
    results.append(("left", "signals left" in left, "left arrow makes the car signal left"))

    off = run(args.slot, "chassis car arrow l", until=r"signals off")
    results.append(("off", "signals off" in off, "second left arrow switches the signal off"))

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
