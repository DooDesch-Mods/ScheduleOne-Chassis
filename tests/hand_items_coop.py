"""Hotbar items in a vehicle in co-op: the host drives, the client sits beside.

Needs a joined pair from `pwsh -File tests/coop_up.ps1`. Checks:
  seated          - host in the driver seat, client in the passenger seat
  remote-item     - the client sees the pistol in the host's seated hand, rendered, with the hand off the wheel
  remote-fp-item  - with the host in first person, the client still sees the pistol in the host's hand
  client-drink    - the passenger drinks; the trash goes out of the passenger window
  host-sees-drink - the host sees the can in the passenger's hand while drinking
  client-fires    - the passenger fires the pistol in first person
  errors          - no Chassis exception or item error on either peer

Usage: python -I tests/hand_items_coop.py --host mptest5 --client mptest6
"""
import argparse
import json
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
    print("  [" + slot + "] > " + words)
    for line in text.strip().splitlines()[-4:]:
        print("    " + line)
    return text


def where(slot):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot, "where"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return json.loads(out.stdout[out.stdout.index("{"):])


def seat_entry(text, which):
    """The `items seats` entry of the remote or local player."""
    m = re.search(r"items seats: (.*)", text)
    if not m:
        return ""
    for part in m.group(1).split("; "):
        if "(" + which + ")" in part:
            return part
    return ""


def status(slot):
    text = run(slot, "chassis items status", until=r"items: ")
    m = re.search(r"items: .*", text)
    return m.group(0) if m else ""


def field(st, name):
    m = re.search(r"\b%s=(\S+)" % re.escape(name), st)
    return m.group(1) if m else ""


SLOTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "slots", "slots.py")


def log_lines(slot, pattern):
    """Matching lines of the slot's MelonLoader log, read from the file the game writes."""
    out = subprocess.run([sys.executable, "-I", SLOTS, "--json"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    root = next((s["root"] for s in json.loads(out)["slots"] if s["slot"] == slot), None)
    if root is None:
        print("  no root for slot " + slot)
        return []
    with open(os.path.join(root, "MelonLoader", "Latest.log"), encoding="utf-8", errors="replace") as f:
        return [l.rstrip() for l in f if re.search(pattern, l)]


def shot(slot):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot, "shot"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout.strip()
    return out.splitlines()[-1] if out else "none"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    args = ap.parse_args()
    h, c = args.host, args.client
    results = []

    for slot in (h, c):
        run(slot, "give m1911 1")
        run(slot, "give cuke 2")
    pos = where(h)
    x, y, z = pos["position"]
    run(h, "chassis car put shitbox %.2f %.2f %.2f 0" % (x + 4, y, z), max_wait=5000)
    time.sleep(2)
    subprocess.run([sys.executable, "-I", S1, "--slot", c, "tp", "%.2f" % (x + 2), "%.2f" % y, "%.2f" % (z + 2)], capture_output=True)
    time.sleep(2)
    a = run(h, "chassis car enter shitbox")
    b = run(c, "chassis car enter shitbox 1")
    results.append(("seated", "(driver)" in a and "(passenger)" in b, "host drives, client beside"))
    time.sleep(1.5)

    run(h, "chassis items pick m1911", until=r"items slot")
    time.sleep(2.0)
    e = seat_entry(run(c, "chassis items seats", until=r"items seats"), "remote")
    m = re.search(r"renderers (\d+)/(\d+)", e)
    results.append(("remote-item", "M1911" in e and "@Invisible" not in e and m is not None and int(m.group(1)) > 0 and "rightHandIK=0.00" in e,
                    "client sees the host's pistol in the seated hand: " + e))
    print("  client shot (host holds the pistol):", shot(c))

    run(h, "chassis view 0", until=r"view=")
    time.sleep(1.5)
    e = seat_entry(run(c, "chassis items seats", until=r"items seats"), "remote")
    results.append(("remote-fp-item", "M1911" in e and "@Invisible" not in e, "host in first person, client still sees the pistol: " + e))
    run(h, "chassis view 10", until=r"view=")

    run(c, "chassis items pick cuke", until=r"items slot")
    time.sleep(1.0)
    run(c, "chassis items press primary 2", until=r"items press")
    time.sleep(0.8)
    e = seat_entry(run(h, "chassis items seats", until=r"items seats"), "remote")
    results.append(("host-sees-drink", "Cuke" in e and "@Invisible" not in e, "host sees the can in the passenger's hand: " + e))
    time.sleep(3.0)
    trash = log_lines(c, r"\[Items\] trash .* thrown out of the (left|right) window")
    results.append(("client-drink", any("right window" in l for l in trash), "passenger's trash out of the right window: " + (trash[-1] if trash else "no line")))

    run(c, "chassis items pick m1911", until=r"items slot")
    run(c, "chassis view 0", until=r"view=")
    time.sleep(1.5)
    before = field(status(c), "gun")
    run(c, "chassis items press secondary 90", until=r"items press")
    time.sleep(0.8)
    run(c, "chassis items press primary 2", until=r"items press")
    time.sleep(0.8)
    after = field(status(c), "gun")
    nb = int(before) if before.isdigit() else -1
    na = int(after) if after.isdigit() else -1
    results.append(("client-fires", nb > 0 and na == nb - 1, "passenger fires in first person (ammo %s -> %s)" % (before, after)))
    run(c, "chassis view 10", until=r"view=")

    errs = []
    for slot in (h, c):
        errs += [slot + ": " + l for l in log_lines(slot, r"\[Items\].*failed|Exception.*Chassis|\[Chassis\].*Exception")]
    results.append(("errors", not errs, errs[0] if errs else "no Chassis exception or item error on either peer"))

    run(c, "chassis items exit")
    run(h, "chassis items exit")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    print("===== %d PASS, %d FAIL" % (len(results) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
