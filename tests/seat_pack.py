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
  solids-listed         - `chassis items solids` names every item whose held model has a solid collider; they go
                          into the emptied hotbar in batches of 8 for the rest checks
  rest-baseline         - with empty hands the car stands still on the flat spot (else the spot is the fault)
  rest-<peer>-<item>    - (#129) each of those items held for 3 s in the first-person view: it is in the hand, the
                          standing car does not move or turn, and the held item has no solid collider left
  give-<peer>-<item>    - only on failure: `items give` added nothing (no retry)
  solids-back           - after getting out, the held item's solid colliders are on again
  errors                - no Chassis exception or item error on either peer

Mouse presses go through `chassis items press`, not the real mouse.

Usage: python -I tests/seat_pack.py --host mptest3 [--client mptest4] [--vehicle cheetah] [--skip items,steer,refl,rest]
Without --client only the host's checks run.
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


REST_HOLD = 3.0
REST_MOVE = 0.15
REST_YAW = 1.0
POSE = r"speed (-?[\d.]+) steer \S+ at \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\) yaw (\d+)"
SOLID_FALLBACK = ["energydrink"]


def car_pose(slot):
    """(speed km/h, x, y, z, yaw) of the nearest car, from `chassis car`."""
    m = re.search(POSE, run(slot, "chassis car", until=r"profile "))
    return tuple(float(g) for g in m.groups()) if m else None


def solid_items(slot):
    """Item IDs whose first-person model has a solid collider, or None when the build has no `items solids`."""
    m = re.search(r"with solid colliders: ([^\r\n]*)", run(slot, "chassis items solids", until=r"items solids"))
    if not m:
        return None
    s = m.group(1).strip()
    return [] if s == "-" else [re.sub(r"\(\d+\)$", "", x) for x in s.split()]


def still(p0, p1):
    """(car stayed put, text) for two car poses."""
    if not (p0 and p1):
        return False, "no car pose (before %s, after %s)" % (p0, p1)
    moved = ((p1[1] - p0[1]) ** 2 + (p1[2] - p0[2]) ** 2 + (p1[3] - p0[3]) ** 2) ** 0.5
    turn = abs(p1[4] - p0[4]) % 360
    turn = min(turn, 360 - turn)
    ok = moved <= REST_MOVE and turn <= REST_YAW and abs(p1[0]) < 0.5
    return ok, "car moved %.2f m, turned %.0f deg, speed %.1f km/h" % (moved, turn, p1[0])


def rest(car_slot, slot, peer, results, batch):
    """Holds every item of the batch in turn (each is in the hotbar alone with the rest of the batch); the standing
    car must keep its place and heading, and the held item must be in the hand with no solid collider left."""
    run(slot, "chassis view 0", until=r"view=")
    time.sleep(1.0)
    for item in batch:
        # A push by an earlier item lets the car roll on; each item starts from a standing car.
        p0 = car_pose(car_slot)
        for _ in range(6):
            if p0 and abs(p0[0]) < 0.3:
                break
            time.sleep(1.0)
            p0 = car_pose(car_slot)
        run(slot, "chassis items pick " + item, until=r"items (slot|pick)")
        time.sleep(REST_HOLD)
        p1 = car_pose(car_slot)
        st = status(slot)
        eq = field(st, "equippable") or "-"
        solids = field(st, "heldSolids") or "?"
        ok, how = still(p0, p1)
        held = eq != "-"
        results.append(("rest-%s-%s" % (peer, item), ok and held and solids in ("0", "?"),
                        "%s held %.0f s (equippable %s): %s, solid colliders %s" % (item, REST_HOLD, eq, how, solids)))


def clear(slot, host):
    """Empties the inventory: `chassis items clear`, or vanilla's host-only clearinventory on an older build."""
    text = run(slot, "chassis items clear", until=r"items (clear|status)")
    if "items clear: hotbar" in text:
        return True
    if host:
        run(slot, "clearinventory", max_wait=3000)
        return True
    print("  [" + slot + "] no `items clear` in this build and vanilla clearinventory is host-only; hotbar not cleared")
    return False


def give(slot, peer, item, qty, results):
    """Gives once. Nothing added is a FAIL right away; only a command that never answered is sent again once."""
    for attempt in range(2):
        text = run(slot, "chassis items give %s %d" % (item, qty), until=r"items give: ", max_wait=4000)
        m = re.search(r"items give: (.*)", text)
        if m:
            if re.match(r"added [1-9]", m.group(1)):
                return True
            results.append(("give-%s-%s" % (peer, item), False, m.group(1)))
            return False
    results.append(("give-%s-%s" % (peer, item), False, "no answer to `items give` twice"))
    return False


def calm(slot, host):
    """Closes an open arrest notice (it blocks the hotbar) and, on the host, keeps the police off the players."""
    run(slot, "chassis items notice", until=r"items (notice|status)")
    if host:
        run(slot, "setpoliceignoreplayers true", max_wait=3000)


def tp(slot, pos):
    import subprocess
    from hand_items_coop import S1
    subprocess.run([sys.executable, "-I", S1, "--slot", slot, "tp", "%.2f" % pos[0], "%.2f" % pos[1], "%.2f" % pos[2],
                    "--yaw", "%d" % SPOT_YAW], capture_output=True)


# A flat car park in town; the template's own spawn is on a slope, where a standing car rolls off by itself.
SPOT_HOST = (-133.0, -3.6, 54.2)
SPOT_CLIENT = (-133.0, -3.6, 51.5)
SPOT_CAR = (-137.0, -3.6, 54.2)
SPOT_YAW = 270
BATCH = 8


def renders(slot):
    text = run(slot, "chassis tune shine", until=r"tune shine")
    m = re.search(r"renders: .*? x(\d+)( disabled)?", text)
    return (int(m.group(1)), bool(m.group(2))) if m else (-1, False)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--client", default="")
    ap.add_argument("--vehicle", default="cheetah")
    ap.add_argument("--skip", default="", help="comma list of parts to skip: items,steer,refl,rest")
    ap.add_argument("--rest-items", default="", help="comma list of item IDs for the rest part instead of every listed one")
    args = ap.parse_args()
    h, c = args.host, args.client
    peers = [(h, "host")] + ([(c, "client")] if c else [])
    skip = set(s for s in args.skip.split(",") if s)
    results = []

    if c:
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

    for slot, peer in peers:
        calm(slot, peer == "host")
        clear(slot, peer == "host")

    run(h, "chassis car put %s %.2f %.2f %.2f %d" % ((args.vehicle,) + SPOT_CAR + (SPOT_YAW,)), max_wait=5000)
    time.sleep(2)
    tp(h, SPOT_HOST)
    if c:
        tp(c, SPOT_CLIENT)
    time.sleep(2)
    a = run(h, "chassis car enter " + args.vehicle)
    b = run(c, "chassis car enter %s 1" % args.vehicle) if c else "(passenger)"
    results.append(("seated", "(driver)" in a and "(passenger)" in b, "host drives" + (", client beside" if c else "")))
    time.sleep(3.0)

    if "rest" not in skip:
        solids = solid_items(h)
        results.append(("solids-listed", solids is not None,
                        ("items with a solid held model: " + (" ".join(solids) or "none")) if solids is not None
                        else "no `items solids` in this build, holding %s" % " ".join(SOLID_FALLBACK)))
        items = solids if solids is not None else SOLID_FALLBACK
        if args.rest_items:
            items = [s for s in args.rest_items.split(",") if s]
        # Empty hands first: a car that moves without any item means the spot or the seat, not a held item.
        run(h, "chassis view 0", until=r"view=")
        p0 = car_pose(h)
        time.sleep(REST_HOLD)
        ok, how = still(p0, car_pose(h))
        results.append(("rest-baseline", ok, "empty hands %.0f s: %s" % (REST_HOLD, how)))
        for i in range(0, len(items), BATCH):
            batch = items[i:i + BATCH]
            for slot, peer in peers:
                clear(slot, peer == "host")
                held = [item for item in batch if give(slot, peer, item, 1, results)]
                rest(h, slot, peer, results, held)
        for slot, peer in peers:
            clear(slot, peer == "host")

    # The hotbar checks drink the cucumber; the energy drink stays for solids-back.
    for slot, peer in peers:
        for item, qty in ((DRINK, 4), ("energydrink", 1)):
            give(slot, peer, item, qty, results)
        calm(slot, peer == "host")

    if "steer" not in skip:
        line = steer_watch(h, 5)
        results.append(("steer-still", wheels_still(line), line or "no steerwatch line"))
        if c:
            line_c = steer_watch(c, 5)
            results.append(("steer-still-client-view", wheels_still(line_c), line_c or "no steerwatch line"))
        run(h, "chassis drive 0 1 2", until=r"drive:")
        time.sleep(0.3)
        line = steer_watch(h, 1)
        m = re.search(r"steer (-?[\d.]+)\.\.(-?[\d.]+)", line)
        results.append(("steer-input", bool(m) and float(m.group(2)) > 5.0, line or "no steerwatch line"))
        time.sleep(2.5)

    if "items" not in skip:
        for slot, peer in peers:
            for view in ("chase", "fp", "fpdash"):
                drink(slot, peer, view, results)
        for slot, peer in peers:
            print("  %s shot: %s" % (peer, shot(slot)))
            set_view(slot, "chase")

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
    for slot, _ in peers:
        errs += [slot + ": " + l for l in log_lines(slot, r"\[Items\].*failed|Exception.*Chassis|\[Chassis\].*Exception|\[Reflections\].*failed")]
    results.append(("errors", not errs, errs[0] if errs else "no Chassis exception or item error on either peer"))

    back = r"\[Items\] \d+ solid collider\(s\) of the held item back on"
    if "rest" not in skip:
        run(h, "chassis view 0", until=r"view=")
        run(h, "chassis items pick energydrink", until=r"items (slot|pick)")
        time.sleep(1.5)
    n_back = len(log_lines(h, back))
    # hand_items.py, which runs next, starts from the chase view.
    for slot, _ in peers:
        set_view(slot, "chase")
    if c:
        run(c, "chassis items exit")
    run(h, "chassis items exit")
    if "rest" not in skip:
        # The energy drink is in the host's hand; getting out must switch its collider back on.
        # Either Chassis switched them back on (log line), or the exit re-equipped the item and the new copy has
        # vanilla's solid collider; both leave the status with the drink in the hand and at least one solid collider.
        time.sleep(1.5)
        got = log_lines(h, back)
        st = status(h)
        eq, solids = field(st, "equippable") or "-", field(st, "heldSolids") or "?"
        on = eq != "-" and solids.isdigit() and int(solids) >= 1
        results.append(("solids-back", on or len(got) > n_back,
                        "out of the car: equippable %s, solid colliders %s%s" % (eq, solids, ("; " + got[-1]) if len(got) > n_back else "")))
    run(h, "chassis car remove", max_wait=5000)
    # hand_items.py runs next in the same save and needs free hotbar slots.
    for slot, peer in peers:
        clear(slot, peer == "host")

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    print("===== %d PASS, %d FAIL" % (len(results) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
