"""Optional ignition in co-op: host drives, client sits beside, both may use the lock and the button.

Needs a joined pair from `pwsh -File tests/coop_up.ps1` with a Debug build. Checks:
  both-on       - with auto ignition (the default) the engine runs on both peers
  client-key    - the passenger turns the key: auto ignition and the engine go off on both peers
  no-throttle   - the host at the wheel then gets no throttle
  host-key      - the driver turns the key back on: the engine runs on both peers, auto ignition stays off
  client-auto   - the passenger presses auto ignition: on for both peers
  client-bind   - the passenger's ignition key binding (a controller button) turns the key off for both peers
  client-tag    - the host paints the car: the key fob on the client takes the new body colour
  client-tag-back - the host removes the paint: the client's fob follows the body back

Usage: python -I tests/ignition_coop.py --host mptest5 --client mptest6
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")
STATE = re.compile(r"ign: .*?: auto ignition (on|off), key (on|off), key shows (\d), driver (\w+), engine (on|off), "
                   r"rpm (\d+), speed (-?[0-9.]+) km/h")


def run(slot, words, until=None, max_wait=8000):
    cmd = [sys.executable, "-I", S1, "--slot", slot, "run"] + words.split() + ["--max-wait", str(max_wait)]
    if until:
        cmd += ["--until", until]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = out.stdout + out.stderr
    print("  [" + slot + "] > " + words)
    for line in text.strip().splitlines()[-4:]:
        print("    " + line)
    return text


def where(slot):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot, "where"], capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    return json.loads(out.stdout[out.stdout.index("{"):])


def ign(slot, act=""):
    text = run(slot, ("chassis ign " + act).strip(), until=r"ign: ")
    m = STATE.search(text)
    if not m:
        return {}
    return {"auto": m.group(1), "key": m.group(2), "engine": m.group(5), "rpm": int(m.group(6)), "speed": float(m.group(7))}


def both(h, c):
    time.sleep(1.5)
    return ign(h), ign(c)


TAG = re.compile(r"ign tag: fob #?([0-9A-Fa-f]{6}), body #?([0-9A-Fa-f]{6})")


def tag(slot):
    m = TAG.search(run(slot, "chassis ign tag", until=r"ign tag: "))
    return (m.group(1).upper(), m.group(2).upper()) if m else (None, None)


def fmt(s):
    return "auto %s key %s engine %s" % (s.get("auto"), s.get("key"), s.get("engine"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    a = ap.parse_args()
    h, c = a.host, a.client
    results = []

    pos = where(h)
    x, y, z = pos["position"] if "position" in pos else (pos["x"], pos["y"], pos["z"])
    run(h, "chassis car put shitbox %.2f %.2f %.2f 0" % (x + 4, y, z), max_wait=5000)
    time.sleep(2)
    subprocess.run([sys.executable, "-I", S1, "--slot", c, "tp", "%.2f" % (x + 2), "%.2f" % y, "%.2f" % (z + 2)],
                   capture_output=True)
    time.sleep(1)
    if "(driver)" not in run(h, "chassis car enter shitbox"):
        print("FAIL setup: host not in the driver seat")
        return 1
    if "(passenger)" not in run(c, "chassis car enter shitbox 1"):
        print("FAIL setup: client not in the passenger seat")
        return 1

    hs, cs = both(h, c)
    if hs.get("auto") == "off":
        ign(h, "auto")
        hs, cs = both(h, c)
    results.append(("both-on", hs.get("engine") == "on" and cs.get("engine") == "on" and cs.get("auto") == "on",
                    "host " + fmt(hs) + " / client " + fmt(cs)))

    ign(c, "key")
    hs, cs = both(h, c)
    ok = all(s.get("auto") == "off" and s.get("key") == "off" and s.get("engine") == "off" for s in (hs, cs))
    results.append(("client-key", ok, "host " + fmt(hs) + " / client " + fmt(cs)))

    run(h, "chassis drive 1 0 2")
    time.sleep(1.5)
    hs = ign(h)
    results.append(("no-throttle", abs(hs.get("speed", 99)) < 1.5, "%.1f km/h after 1.5 s full throttle" % hs.get("speed", -1)))
    time.sleep(1)

    ign(h, "key")
    hs, cs = both(h, c)
    ok = all(s.get("auto") == "off" and s.get("key") == "on" and s.get("engine") == "on" for s in (hs, cs))
    results.append(("host-key", ok, "host " + fmt(hs) + " / client " + fmt(cs)))

    ign(c, "auto")
    hs, cs = both(h, c)
    ok = all(s.get("auto") == "on" and s.get("engine") == "on" for s in (hs, cs))
    results.append(("client-auto", ok, "host " + fmt(hs) + " / client " + fmt(cs)))

    run(c, "chassis bind Ignition button <Gamepad>/select")
    run(c, "chassis bind pad select", until=r"virtual gamepad released")
    hs, cs = both(h, c)
    ok = all(s.get("auto") == "off" and s.get("engine") == "off" for s in (hs, cs))
    results.append(("client-bind", ok, "host " + fmt(hs) + " / client " + fmt(cs)))
    run(c, "chassis bind Ignition button none")
    ign(h, "auto")

    # The host paints the car; the fob on the client's copy takes the new body colour.
    run(h, "chassis tune set paint 1E5AC8,classic")
    time.sleep(2.5)
    fob, body = tag(c)
    results.append(("client-tag", fob == body == "1E5AC8", "host painted 1E5AC8: client fob %s, body %s" % (fob, body)))
    run(h, "chassis tune clear")
    time.sleep(2)
    fob, body = tag(c)
    results.append(("client-tag-back", fob is not None and fob == body and fob != "1E5AC8",
                    "paint removed: client fob %s, body %s" % (fob, body)))

    run(c, "chassis exit")
    run(h, "chassis exit")
    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
