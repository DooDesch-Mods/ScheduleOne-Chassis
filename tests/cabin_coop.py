"""Cabin light and glovebox in co-op: host drives, client sits beside.

Needs a joined pair from `pwsh -File tests/coop_up.ps1`. Checks:
  light-both   - both peers see the automatic light after getting in
  switch-sync  - the host's click on the lamp shows on the client
  press-sync   - the client sees the lamp press in on each of the host's clicks
  put-sync     - an item the host puts in shows in the client's count
  client-open  - the client opens the glovebox with a click from the passenger seat
  lock         - while the client holds it, the host is refused
  lid-sync     - the host sees the lid open while the client holds the glovebox
  take-sync    - an item the client takes out is gone on the host
  lid-shut     - the lid shuts on the host after the client closes it
  release      - after the client closes it, the host opens it again

Usage: python -I tests/cabin_coop.py --host mptest5 --client mptest6
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
    for line in text.strip().splitlines()[-6:]:
        print("    " + line)
    return text


def where(slot):
    out = subprocess.run([sys.executable, "-I", S1, "--slot", slot, "where"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return json.loads(out.stdout[out.stdout.index("{"):])


def cabin(slot):
    return run(slot, "chassis cabin", until=r"glovebox")


def switch(slot, name):
    text = run(slot, "chassis telemetry", until=r"switch " + name + " ")
    m = re.search(r"switch " + name + r" .*", text)
    return m.group(0) if m else ""


def presses(text):
    m = re.search(r"pressed (\d+)x", text)
    return int(m.group(1)) if m else -1


def lid(text):
    m = re.search(r"lid open (\d\.\d\d)", text)
    return float(m.group(1)) if m else -1.0


def items(text):
    m = re.findall(r"(\d+) items?", text)
    return int(m[-1]) if m else -1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    args = ap.parse_args()
    h, c = args.host, args.client
    results = []

    run(h, "chassis cabin mode own")
    run(c, "chassis cabin mode own")
    pos = where(h)
    x, y, z = pos["position"]
    print("  host at", x, y, z)
    run(h, "chassis car put shitbox %.2f %.2f %.2f 0" % (x + 4, y, z), max_wait=5000)
    time.sleep(2)
    subprocess.run([sys.executable, "-I", S1, "--slot", c, "tp", "%.2f" % (x + 2), "%.2f" % y, "%.2f" % (z + 2)], capture_output=True)
    time.sleep(2)
    if "(driver)" not in run(h, "chassis car enter shitbox"):
        print("FAIL setup: host not in the driver seat")
        return 1
    if "(passenger)" not in run(c, "chassis car enter shitbox 1"):
        print("FAIL setup: client not in the passenger seat")
        return 1
    time.sleep(1)
    a, b = cabin(h), cabin(c)
    results.append(("light-both", "cabin light on (switch Auto" in a and "cabin light on (switch Auto" in b, "both peers see the automatic light"))

    # Two clicks put the switch on Off: the client's light goes dark although its own Auto would keep it lit.
    run(h, "chassis view 10")
    run(h, "chassis aim dome")
    press_before = presses(switch(c, "dome"))
    run(h, "chassis click", until=r"click:")
    time.sleep(0.5)
    run(h, "chassis click", until=r"click:")
    time.sleep(2.5)
    results.append(("switch-sync", "cabin light off (switch Off" in cabin(c), "the host's lamp clicks show on the client"))
    press_after = presses(switch(c, "dome"))
    results.append(("press-sync", press_before >= 0 and press_after == press_before + 2, "the client sees the lamp press in (%d -> %d)" % (press_before, press_after)))
    run(h, "chassis click", until=r"click:")

    before = items(cabin(c))
    run(h, "chassis cabin put", until=r"put")
    time.sleep(1.5)
    after = items(cabin(c))
    results.append(("put-sync", after == before + 1, "the host's item shows on the client (%d -> %d)" % (before, after)))

    run(c, "chassis view 10")
    run(c, "chassis aim glovebox")
    opened = run(c, "chassis click", until=r"glovebox opened|refused|not opened")
    time.sleep(1.5)
    cc = cabin(c)
    results.append(("client-open", re.search(r"own glovebox, \d slots, open", cc) is not None, "the client opens the glovebox from the passenger seat"))

    refused = run(h, "chassis cabin open", until=r"glovebox")
    results.append(("lock", "someone else has it open" in refused, "the host is refused while the client holds it"))
    log = subprocess.run([sys.executable, "-I", S1, "--slot", h, "log", "--grep", "hint shown", "-n", "5"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    results.append(("lock-hint", "Someone else has the glovebox open." in log, "the refused host sees the busy hint"))
    host_lid = lid(switch(h, "glovebox"))
    results.append(("lid-sync", host_lid == 1.0, "the host sees the lid open while the client holds it (lid %.2f)" % host_lid))
    shot = subprocess.run([sys.executable, "-I", S1, "--slot", h, "shot"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout.strip()
    print("  hint screenshot:", shot.splitlines()[-1] if shot else "none")

    host_before = items(cabin(h))
    run(c, "chassis cabin take", until=r"take")
    time.sleep(1.5)
    host_after = items(cabin(h))
    results.append(("take-sync", host_after == host_before - 1, "the client's take shows on the host (%d -> %d)" % (host_before, host_after)))

    run(c, "chassis cabin close")
    time.sleep(1.5)
    host_lid = lid(switch(h, "glovebox"))
    results.append(("lid-shut", host_lid == 0.0, "the lid shuts on the host after the client closes (lid %.2f)" % host_lid))
    again = run(h, "chassis cabin open", until=r"glovebox")
    results.append(("release", "own glovebox opened" in again, "after the client closes, the host can open it"))
    run(h, "chassis cabin close")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
