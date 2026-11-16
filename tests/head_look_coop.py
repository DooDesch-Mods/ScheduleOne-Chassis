"""Seated heads follow the view in co-op: host drives, client sits beside, each sees the other's head turn.

Needs a joined pair from `pwsh -File tests/coop_up.ps1` and a Debug build of Chassis. Checks:
  on-turn-client  - the host sees the client's head turn when the client looks right, then left
  on-aim-client   - on the host, the client's head aims along the client's synced view
  on-body-client  - the client's body stays in its seat while the head turns
  on-turn-host    - the client sees the host's head turn when the host looks left, then right
  on-aim-host     - on the client, the host's head aims along the host's synced view
  on-body-host    - the host's body stays in its seat
  on-local        - each peer's own head aims along its own camera
  off-still       - with `chassis head off` the client's view no longer turns its head on the host (vanilla look)
Screenshots of each pose go to the slot's screenshot folder; their paths are printed.

Usage: python -I tests/head_look_coop.py --host mptest5 --client mptest6
"""
import argparse
import math
import os
import re
import subprocess
import sys
import time

X, Y, Z = 60.0, 4.4, -70.0
S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
SETTLE = 4.0  # vanilla lerps the look target at 1/s; after 4 s it is within 2 % of the new direction
TURN_MIN = 20.0  # degrees the head must turn between a right and a left look
AIM_MAX = 15.0  # degrees the IK aim may stay off the view direction
BODY_MAX = 3.0  # degrees the body may turn on its seat
STILL_MAX = 8.0  # degrees the head may move with the setting off
LINE = re.compile(r"head: (local|remote) (.+?) seat (\d+) aim-vs-view (-?[\d.]+) aim (on|off) headq (\S+) (\S+) (\S+) (\S+) bodyq (\S+) (\S+) (\S+) (\S+)")


def s1(slot, *args):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    return out.stdout + out.stderr


def run(slot, words, until=None, max_wait=8000):
    args = ["run"] + words.split() + ["--max-wait", str(max_wait)]
    if until:
        args += ["--until", until]
    text = s1(slot, *args)
    print("  [" + slot + "] > " + words)
    for line in text.strip().splitlines()[-4:]:
        print("    " + line)
    return text


def head(slot, who):
    """The `chassis head` row for the local or the remote seated player, as (aim offset, head quat, body quat)."""
    text = run(slot, "chassis head", until=r"head: (local|remote|no seated)")
    for m in LINE.finditer(text):
        if m.group(1) == who:
            f = [float(x) for x in m.groups()[3:4] + m.groups()[5:]]
            return f[0], f[1:5], f[5:9]
    return None


def angle(a, b):
    d = abs(sum(x * y for x, y in zip(a, b)))
    return math.degrees(2 * math.acos(min(1.0, d)))


def shot(slot, label):
    path = s1(slot, "shot").strip().splitlines()
    path = path[-1] if path else "none"
    print("  shot " + label + ": " + path)
    return path


def pose(looker, observer, yaw, label):
    run(looker, "chassis view 0 %d" % yaw)
    time.sleep(SETTLE)
    row = head(observer, "remote")
    shot(observer, label)
    return row


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    a = ap.parse_args()
    h, c = a.host, a.client
    results = []

    def check(name, ok, what):
        results.append((name, ok, what))

    for slot in (h, c):
        # A freshly joined client sits in the character creator, which covers its screenshots.
        run(slot, "chassis creator")
        run(slot, "chassis head on")
    time.sleep(2)
    # Flat ground (the spot of seat_pose.py): the car stands still for the screenshots.
    for slot in (c, h):
        run(slot, "chassis exit")
    time.sleep(1.5)
    run(h, "chassis car remove")
    s1(h, "tp", "%.2f" % (X + 4), "%.2f" % Y, "%.2f" % Z)
    time.sleep(1)
    run(h, "chassis car put shitbox %.2f %.2f %.2f 180" % (X, Y, Z), max_wait=5000)
    time.sleep(2)
    s1(c, "tp", "%.2f" % (X - 3), "%.2f" % Y, "%.2f" % (Z + 1))
    time.sleep(2)
    if "(driver)" not in run(h, "chassis car enter shitbox"):
        print("FAIL setup: host not in the driver seat")
        return 1
    if "(passenger)" not in run(c, "chassis car enter shitbox 1"):
        print("FAIL setup: client not in the passenger seat")
        return 1
    time.sleep(2)

    # The host watches the passenger; the client looks out of its window, then across to the driver's side.
    run(h, "chassis view 5 70")
    run(c, "chassis view 0 0")
    right = pose(c, h, 80, "host sees client looking right (on)")
    left = pose(c, h, -40, "host sees client looking left (on)")
    if right is None or left is None:
        check("on-turn-client", False, "no remote row on the host: " + str(right) + " " + str(left))
    else:
        turn = angle(right[1], left[1])
        check("on-turn-client", turn > TURN_MIN, "client's head turned %.1f deg on the host" % turn)
        check("on-aim-client", max(right[0], left[0]) < AIM_MAX, "aim off the client's view %.1f / %.1f deg" % (right[0], left[0]))
        body = angle(right[2], left[2])
        check("on-body-client", body < BODY_MAX, "client's body turned %.1f deg" % body)
    mine = head(h, "local")
    check("on-local", mine is not None and mine[0] < AIM_MAX, "host's own head off its camera: " + (("%.1f deg" % mine[0]) if mine else "no row"))

    # The client watches the driver; the host looks out of its window, then across to the passenger's side.
    run(c, "chassis view 5 -70")
    left_h = pose(h, c, -80, "client sees host looking left (on)")
    right_h = pose(h, c, 40, "client sees host looking right (on)")
    if left_h is None or right_h is None:
        check("on-turn-host", False, "no remote row on the client: " + str(left_h) + " " + str(right_h))
    else:
        turn = angle(left_h[1], right_h[1])
        check("on-turn-host", turn > TURN_MIN, "host's head turned %.1f deg on the client" % turn)
        check("on-aim-host", max(left_h[0], right_h[0]) < AIM_MAX, "aim off the host's view %.1f / %.1f deg" % (left_h[0], right_h[0]))
        body = angle(left_h[2], right_h[2])
        check("on-body-host", body < BODY_MAX, "host's body turned %.1f deg" % body)
    mine = head(c, "local")
    check("on-local", mine is not None and mine[0] < AIM_MAX, "client's own head off its camera: " + (("%.1f deg" % mine[0]) if mine else "no row"))

    # Setting off on the host: the client's view no longer moves the client's head on the host's screen.
    run(h, "chassis head off")
    run(h, "chassis view 5 70")
    right = pose(c, h, 80, "host sees client looking right (off)")
    left = pose(c, h, -40, "host sees client looking left (off)")
    if right is None or left is None:
        check("off-still", False, "no remote row on the host: " + str(right) + " " + str(left))
    else:
        turn = angle(right[1], left[1])
        check("off-still", turn < STILL_MAX, "client's head moved %.1f deg on the host with the setting off" % turn)
    run(h, "chassis head on")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
