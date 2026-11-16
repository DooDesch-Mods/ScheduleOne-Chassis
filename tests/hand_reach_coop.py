"""Hand reach in co-op (Chassis#112): host drives, client sits beside, each sees the other's hand reach.

Needs a joined pair from `pwsh -File tests/coop_up.ps1` and a Debug build of Chassis on both. Checks:
  client-sees-host-<switch>  - on the client, the host's body reaches the light switch, cabin light and ignition
  host-sees-client-<switch>  - on the host, the client's body reaches the glovebox and the cabin light
  client-used-<switch>       - the switch the host used reached the client (its log names it)
  off-unseen                 - with `chassis reach off` on the client, the host sees no reach of the client
Each observer looks at the other seat while the hand is out; screenshot paths are printed.

Usage: python -I tests/hand_reach_coop.py --host mptest5 --client mptest6
"""
import argparse
import os
import re
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
X, Y, Z = 60.0, 4.4, -70.0
HOLD = 2.5
TIP_MAX = 0.10
WRIST_MAX = 0.06
LAST = re.compile(r"reach: remote .+? seat (\d+) (idle|reaching .+?) \| last '(\w+)' hand ([RL]) tip ([\d.\-]+) wrist ([\d.\-]+) lean ([\d.]+) peak ([\d.]+)")
results = []


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
    for line in text.strip().splitlines()[-3:]:
        print("    " + line)
    return text


def check(name, ok, what):
    results.append((name, ok, what))


def shot(slot, label):
    # The joining client's character creator can open well after clear_screen gave up; close it before every shot.
    if "creator: done" in run(slot, "chassis creator"):
        time.sleep(0.5)
    lines = s1(slot, "shot").strip().splitlines()
    path = lines[-1] if lines else "none"
    print("  shot " + label + ": " + path)
    return path


def clear_screen(slot, joined):
    """A joining test client opens the character creator some seconds after it is in the world, and the phone
    stays up after it; both cover the observer's screenshots. Finish the creator once it shows, then lower the phone."""
    for _ in range(10 if joined else 0):
        if "creator: done" in run(slot, "chassis creator"):
            break
        time.sleep(2)
    else:
        if joined:
            print("  [" + slot + "] no character creator showed within 20 s")
    sys.path.insert(0, os.path.dirname(S1))
    import s1 as bridge
    try:
        print("  [" + slot + "] phone away: " + str(bridge.Game(slot).call("phone", {"open": False}))[:160])
    except Exception as e:  # the bridge raises its own types for a dead game and a busy main thread
        print("  [" + slot + "] phone away failed: " + str(e))


def remote_last(slot):
    text = run(slot, "chassis reach", until=r"reach: (remote|local|no seated)")
    m = LAST.search(text)
    if not m:
        return None
    return {"switch": m.group(3), "hand": m.group(4), "tip": float(m.group(5)) if m.group(5) != "-" else 9.0,
            "wrist": float(m.group(6)) if m.group(6) != "-" else 9.0, "peak": float(m.group(8)), "raw": m.group(0)}


def seen(name, observer, user, cmd, switch):
    """`user` uses a switch; `observer` shoots while the hand is out and reads the remote reach afterwards."""
    text = run(user, cmd, until=r"used (after|at once)", max_wait=3000)
    time.sleep(0.6)
    shot(observer, name + " (" + switch + ")")
    time.sleep(HOLD + 0.8)
    r = remote_last(observer)
    ok = r is not None and r["switch"] == switch and r["peak"] > 0.99 and r["tip"] < TIP_MAX and r["wrist"] < WRIST_MAX
    check(name + "-" + switch, ok, r["raw"] if r else "no remote reach row on the observer")
    return text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    a = ap.parse_args()
    h, c = a.host, a.client

    for slot in (h, c):
        clear_screen(slot, slot == c)
        run(slot, "chassis reach on")
        run(slot, "chassis reach hold %.1f" % HOLD)
    s1(h, "time", "1200")  # once, on the host: daylight in the cabin for the screenshots
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
    time.sleep(2.5)

    # The client watches the driver; the host uses its switches.
    run(c, "chassis view 5 -60")
    run(h, "chassis view 10")
    for sw in ("lights", "dome"):
        run(h, "chassis aim " + sw)
        seen("client-sees-host", c, h, "chassis reach use " + sw, sw)
    seen("client-sees-host", c, h, "chassis ign key", "ignition")
    log = s1(c, "log", "-n", "600")
    for sw in ("lights", "dome", "ignition"):
        check("client-used-" + sw, ("switch '" + sw + "'") in log, "client log names the use of '" + sw + "'")

    # The host watches the passenger; the client uses the glovebox and the cabin light.
    run(h, "chassis view 5 60")
    run(c, "chassis view 10")
    for sw in ("glovebox", "dome"):
        run(c, "chassis aim " + sw)
        seen("host-sees-client", h, c, "chassis reach use " + sw, sw)
        if sw == "glovebox":
            run(c, "chassis cabin close")
            time.sleep(0.5)

    # Setting off on the client: the host sees no reach of the client.
    before = remote_last(h)
    run(c, "chassis reach off")
    run(c, "chassis aim glovebox")
    run(c, "chassis reach use glovebox", until=r"used (after|at once)", max_wait=3000)
    time.sleep(0.6)
    shot(h, "host sees client with the setting off (glovebox)")
    time.sleep(1.0)
    after = remote_last(h)
    check("off-unseen", (before or {}).get("raw") == (after or {}).get("raw"),
          "host's last remote reach before " + str((before or {}).get("raw")) + " / after " + str((after or {}).get("raw")))
    run(c, "chassis cabin close")
    for slot in (h, c):
        run(slot, "chassis reach on")
        run(slot, "chassis reach hold 0")
    # Both get out, so a later `chassis car remove` on the host does not take the car away under the client.
    for slot in (c, h):
        run(slot, "chassis exit")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
