"""Seat pack in co-op: hotbar items in every view (#110), still front wheels (#89), Reflections Off (#79).

Needs a joined pair from `pwsh -File tests/coop_up.ps1`. The host drives a Cheetah, the client sits beside.
Checks, per peer where it says so:

  hotbar-<peer>-<view>  - vanilla's hotbar gate is open (number keys and wheel select), the drink is in the hand
  wheel-<peer>-<view>   - one wheel notch changes the hotbar slot, unless the hovered control is a knob
  drink-<peer>-<view>   - a press of the left button drinks it: the can lands outside the window; while a cockpit
                          control is outlined, the control takes the click and the drink stays
  views: chase, fp (first person looking ahead), fpdash (first person looking down at the dashboard)
  steer-still           - the host's front wheels do not change direction while the car stands and nothing is pressed
  steer-input           - a console steer input still turns the wheels
  refl-off              - Reflections Off: the chrome probe is disabled and renders no more
  refl-on               - back to Medium: the probe renders again
  errors                - no Chassis exception or item error on either peer

Mouse presses go through `chassis items press`, not the real mouse.

Usage: python -I tests/seat_pack.py --host mptest3 --client mptest4 [--vehicle cheetah]
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hand_items_coop import run, status, field, log_lines, where, shot  # noqa: E402

DRINK = "cuke"
TRASH = r"\[Items\] trash .* thrown out of the (left|right) window"


def set_view(slot, view):
    """chase: first person off; fp: first person looking ahead; fpdash: first person 35 degrees down."""
    st = status(slot)
    fp = field(st, "fp") == "True"
    if view == "chase":
        if fp:
            run(slot, "chassis view", until=r"view=")
    elif view == "fp":
        run(slot, "chassis view 0", until=r"view=")
    else:
        run(slot, "chassis view 35", until=r"view=")
    time.sleep(1.2)


def drink(slot, peer, view, results):
    set_view(slot, view)
    run(slot, "chassis items pick " + DRINK, until=r"items slot")
    time.sleep(1.0)
    st = status(slot)
    gate = field(st, "hotbarGate")
    eq = field(st, "equippable")
    results.append(("hotbar-%s-%s" % (peer, view), gate == "open" and DRINK in eq.lower(),
                    "gate %s, equippable %s, hovered %s, fp %s" % (gate, eq, field(st, "hovered"), field(st, "fp"))))
    # The wheel changes the slot unless a knob is hovered; over a switch or the horn it must still scroll the hotbar.
    done = r"items scroll done: slot (-?\d+) -> (-?\d+), (?:hovering '([^']+)' \((\w+)\)|no control hovered)"
    n0 = len(log_lines(slot, done))
    run(slot, "chassis items scroll -1", until=r"items scroll")
    got_line = ""
    for _ in range(10):
        time.sleep(0.5)
        lines = log_lines(slot, done)
        if len(lines) > n0:
            got_line = lines[-1]
            break
    m = re.search(done, got_line)
    if m:
        moved = m.group(1) != m.group(2)
        knob = m.group(4) == "knob"
        ok = moved != knob
        what = "slot %s -> %s, %s" % (m.group(1), m.group(2), ("hovering %s (%s)" % (m.group(3), m.group(4))) if m.group(3) else "no control hovered")
    else:
        ok, what = False, "no scroll result within 5 s"
    results.append(("wheel-%s-%s" % (peer, view), ok, what))
    run(slot, "chassis items pick " + DRINK, until=r"items slot")
    time.sleep(0.8)
    before = len(log_lines(slot, TRASH))
    run(slot, "chassis items press primary 2", until=r"items press")
    got = False
    for _ in range(10):
        time.sleep(1.0)
        if len(log_lines(slot, TRASH)) > before:
            got = True
            break
    st2 = status(slot)
    # A hovered (outlined) control takes the click by design; then the drink must stay in the hand.
    hovered = field(st, "hovered") == "True"
    results.append(("drink-%s-%s" % (peer, view), got != hovered,
                    ("can out of the window" if got else "no trash line within 10 s") + ", hovered " + field(st, "hovered")
                    + ", input " + (re.search(r"input=ts \S+ ui \[[^\]]*\]", st2).group(0) if re.search(r"input=ts \S+ ui \[[^\]]*\]", st2) else "?")))


def steer_watch(slot, seconds):
    run(slot, "chassis car steerwatch %d" % seconds, until=r"car steerwatch")
    time.sleep(seconds + 1.5)
    lines = log_lines(slot, r"\[Console\] car steerwatch .*frames")
    return lines[-1] if lines else ""


def wheels_still(line):
    """True when every steered wheel's yaw stayed within 0.5 degrees and never changed direction."""
    found = re.findall(r"yaw (-?[\d.]+)\.\.(-?[\d.]+) flips (\d+)", line)
    if not found:
        return False
    return all(float(b) - float(a) < 0.5 and int(f) == 0 for a, b, f in found)


def renders(slot):
    text = run(slot, "chassis tune shine", until=r"tune shine")
    m = re.search(r"renders: .*? x(\d+)( disabled)?", text)
    return (int(m.group(1)), bool(m.group(2))) if m else (-1, False)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", required=True)
    ap.add_argument("--vehicle", default="cheetah")
    ap.add_argument("--skip", default="", help="comma list of parts to skip: items,steer,refl")
    args = ap.parse_args()
    h, c = args.host, args.client
    skip = set(s for s in args.skip.split(",") if s)
    results = []

    # A client joining the host's save for the first time plays the intro, then opens the character creator.
    end = time.time() + 240
    closed = False
    while time.time() < end:
        if "done with the default looks" in run(c, "chassis items creator", until=r"items creator", max_wait=3000):
            closed = True
            time.sleep(3)
            break
        time.sleep(3)
    results.append(("client-creator", closed, "client's character creator closed" if closed else "no character creator within 240 s"))
    # Side Hustle may raise the phone after the join; an open phone is a UI element and blocks consumables.
    for _ in range(5):
        run(c, "shphone down", until=r"shphone", max_wait=3000)
        time.sleep(2)
        m = re.search(r"\bui \[([^\]]*)\]", status(c))
        if m and m.group(1) == "":
            break

    for slot in (h, c):
        added = False
        for _ in range(10):
            if re.search(r"items give: added [1-9]", run(slot, "chassis items give %s 4" % DRINK, until=r"items give: ", max_wait=4000)):
                added = True
                break
            time.sleep(2)
        if not added:
            print("  [" + slot + "] give added nothing")

    pos = where(h)
    x, y, z = pos["position"]
    run(h, "chassis car put %s %.2f %.2f %.2f 0" % (args.vehicle, x + 4, y, z), max_wait=5000)
    time.sleep(2)
    import subprocess
    from hand_items_coop import S1
    subprocess.run([sys.executable, "-I", S1, "--slot", c, "tp", "%.2f" % (x + 2), "%.2f" % y, "%.2f" % (z + 2)], capture_output=True)
    time.sleep(2)
    a = run(h, "chassis car enter " + args.vehicle)
    b = run(c, "chassis car enter %s 1" % args.vehicle)
    results.append(("seated", "(driver)" in a and "(passenger)" in b, "host drives, client beside"))
    time.sleep(3.0)

    if "steer" not in skip:
        line = steer_watch(h, 5)
        results.append(("steer-still", wheels_still(line), line or "no steerwatch line"))
        line_c = steer_watch(c, 5)
        results.append(("steer-still-client-view", wheels_still(line_c), line_c or "no steerwatch line"))
        run(h, "chassis drive 0 1 2", until=r"drive:")
        time.sleep(0.3)
        line = steer_watch(h, 1)
        m = re.search(r"steer (-?[\d.]+)\.\.(-?[\d.]+)", line)
        results.append(("steer-input", bool(m) and float(m.group(2)) > 5.0, line or "no steerwatch line"))
        time.sleep(2.5)

    if "items" not in skip:
        for slot, peer in ((h, "host"), (c, "client")):
            for view in ("chase", "fp", "fpdash"):
                drink(slot, peer, view, results)
        print("  host shot:", shot(h))
        print("  client shot:", shot(c))
        set_view(h, "chase")
        set_view(c, "chase")

    if "refl" not in skip:
        run(h, "chassis tune set paint C0C0C0,chrome", until=r"tune set")
        time.sleep(4)
        n0, _ = renders(h)
        run(h, "chassis mirror level -1", until=r"mirror ")
        time.sleep(1.5)
        n1, off1 = renders(h)
        time.sleep(5)
        n2, off2 = renders(h)
        said = log_lines(h, r"\[Reflections\] set to Off")
        results.append(("refl-off", n0 > 0 and off2 and n2 == n1 and bool(said),
                        "renders before %d, after Off %d -> %d, disabled %s, log %s" % (n0, n1, n2, off2, said[-1] if said else "none")))
        run(h, "chassis mirror level 1", until=r"mirror ")
        time.sleep(5)
        n3, off3 = renders(h)
        results.append(("refl-on", not off3 and n3 > n2, "back to Medium: renders %d -> %d, disabled %s" % (n2, n3, off3)))
        run(h, "chassis tune unset paint", until=r"tune unset")

    errs = []
    for slot in (h, c):
        errs += [slot + ": " + l for l in log_lines(slot, r"\[Items\].*failed|Exception.*Chassis|\[Chassis\].*Exception|\[Reflections\].*failed")]
    results.append(("errors", not errs, errs[0] if errs else "no Chassis exception or item error on either peer"))

    run(c, "chassis items exit")
    run(h, "chassis items exit")
    run(h, "chassis car remove", max_wait=5000)

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    print("===== %d PASS, %d FAIL" % (len(results) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
