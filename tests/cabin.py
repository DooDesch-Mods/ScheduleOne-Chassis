"""Cabin light and glovebox on a vanilla car.

Spawns a car next to the player, takes the driver seat and checks:
  auto-in     - getting in turns the cabin light on (automatic)
  auto-off    - the automatic light is off again after its 8 s
  click-on    - a click on the lamp (aim + click, as the mouse) holds it on
  click-off   - a second click lets it go back to automatic
  glove-own   - a click on the glovebox lid opens the own glovebox with its slots
  glove-keep  - an item put into the glovebox is still there after close and open
  glove-trunk - in trunk mode the same click opens the vanilla trunk
  exit-reset  - a light held on goes back to automatic once the car is empty

Usage: python -I tests/cabin.py --slot mptest5 [--code shitbox]
Needs a Debug build of Chassis (the `chassis` console command) in a loaded save.
"""
import argparse
import os
import re
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
    for line in text.strip().splitlines()[-8:]:
        print("    " + line)
    return text


def light(slot):
    return run(slot, "chassis cabin", until=r"cabin light (on|off)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--code", default="shitbox")
    args = ap.parse_args()
    results = []
    s = args.slot

    run(s, "chassis cabin mode own")
    run(s, "spawnvehicle " + args.code, max_wait=4000)
    enter = run(s, "chassis car enter " + args.code)
    if "(driver)" not in enter:
        print("FAIL setup: not in the driver seat of " + args.code)
        return 1
    results.append(("auto-in", "cabin light on (automatic" in light(s), "getting in turns the cabin light on"))

    time.sleep(9)
    results.append(("auto-off", "cabin light off" in light(s), "the automatic light is off after 8 s"))

    run(s, "chassis view 10")
    run(s, "chassis aim dome")
    run(s, "chassis click", until=r"click:")
    time.sleep(1)
    results.append(("click-on", "(switch on)" in light(s), "a click on the lamp holds it on"))
    run(s, "chassis click", until=r"click:")
    time.sleep(1)
    results.append(("click-off", "(switch on)" not in light(s), "a second click lets it go back to automatic"))

    run(s, "chassis aim glovebox")
    opened = run(s, "chassis click", until=r"glovebox opened|trunk opened|refused")
    time.sleep(1)
    info = run(s, "chassis cabin", until=r"glovebox")
    results.append(("glove-own", re.search(r"own glovebox, \d slots, open", info) is not None, "the lid opens the own glovebox"))

    run(s, "chassis cabin close")
    put = run(s, "chassis cabin put", until=r"put")
    run(s, "chassis cabin open", until=r"glovebox")
    kept = run(s, "chassis cabin", until=r"glovebox")
    results.append(("glove-keep", "put:" in put and re.search(r"1 item", kept) is not None, "an item stays in the glovebox"))
    run(s, "chassis cabin close")

    run(s, "chassis cabin mode trunk")
    run(s, "chassis aim glovebox")
    trunk = run(s, "chassis click", until=r"trunk opened|refused")
    results.append(("glove-trunk", "trunk opened" in trunk, "in trunk mode the lid opens the trunk"))
    run(s, "chassis cabin close")
    run(s, "chassis cabin mode own")

    run(s, "chassis aim dome")
    run(s, "chassis click", until=r"click:")
    time.sleep(1)
    run(s, "chassis exit")
    time.sleep(2)
    results.append(("exit-reset", "(switch on)" not in light(s), "the held light goes back to automatic when the car is empty"))

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
