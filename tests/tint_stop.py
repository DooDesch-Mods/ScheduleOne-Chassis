"""
Tinted windows lower the chance that an officer pulls the car over (#106).

    python tint_stop.py --slot mptest --label fix

In a car on a street, for no tint and the three tints of Marco's Bodyshop:
- `chassis stop tint` reports the factor the formula gives (none 1.0, Limo 0.5, the others in proportion).
- `chassis stop roll` over many forced rolls hits at chance x factor, within 4 standard deviations.
- With the Limo tint and an officer in front of the car, the host's real roll logs factor 0.500.
Debug build of Chassis, loaded save. Prints PASS or FAIL per check; exit code 1 on FAIL.
"""
import argparse
import math
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run  # noqa: E402
from traffic_stop import car_on_street  # noqa: E402

# Marco's Bodyshop, Lights & Looks (MarcosBodyshop/Builtin/LooksShop.cs).
TINTS = [("none", None), ("Light Smoke", "0E1012CC"), ("Dark Smoke", "08090BE6"), ("Limo", "030304F8")]
STOCK_ALPHA = 0.55
DARKEST_ALPHA = 0xF8 / 255.0
ROLLS = 40000
CHANCE = 0.5


def expected_factor(rgba):
    if rgba is None:
        return 1.0
    a = int(rgba[6:8], 16) / 255.0
    darkness = min(1.0, max(0.0, (a - STOCK_ALPHA) / (DARKEST_ALPHA - STOCK_ALPHA)))
    return 1.0 - 0.5 * darkness


def num(line, name):
    m = re.search(r"\b%s[= ](-?[\d.]+)" % re.escape(name), line)
    return float(m.group(1)) if m else None


def set_tint(r, rgba):
    if rgba is None:
        r.say("chassis tune unset tint", "tune", settle_ms=800)
    else:
        r.say("chassis tune set tint " + rgba, "tune", settle_ms=800)


def factors(r):
    for name, rgba in TINTS:
        set_tint(r, rgba)
        said = r.say("chassis stop tint", "stop tint", settle_ms=300)
        line = said[-1] if said else ""
        want = expected_factor(rgba)
        got = num(line, "factor")
        r.check("%s: factor %.3f" % (name, want), got is not None and abs(got - want) < 0.002, line or "no answer")

        said = r.say("chassis stop roll %d %s" % (ROLLS, CHANCE), "stop roll", settle_ms=1500)
        line = said[-1] if said else ""
        rate, expected = num(line, "rate"), num(line, "expected")
        p = CHANCE * want
        sigma = math.sqrt(p * (1 - p) / ROLLS)
        ok = rate is not None and expected is not None and abs(expected - p) < 0.001 and abs(rate - p) < 4 * sigma
        r.check("%s: %d forced rolls hit %.3f +- %.4f" % (name, ROLLS, p, 4 * sigma), ok, line or "no answer")


def real_roll(r, g):
    """The host's own once-a-second roll goes through the tint factor while an officer sees the tinted car."""
    set_tint(r, "030304F8")
    g.run("setpoliceignoreplayers false", settle_ms=500, max_wait_ms=4000)
    mark = r.mark()
    # Measured from the camera behind the car: 10 m puts the officer about 6 m in front of it, facing it, within
    # the 8 m vanilla rolls in. Tracking puts him back on that spot when his patrol walks him on.
    placed = r.say("chassis items target 10 cop", "items target", settle_ms=800)
    r.note("cop: " + (placed[-1] if placed else "no answer"))
    r.say("chassis items track on", "items", settle_ms=300)
    line, end = None, time.time() + 45
    while not line and time.time() < end:
        probe = r.say("chassis stop probe", "stop probe", settle_ms=300)
        r.note(probe[-1] if probe else "no probe answer")
        line = r.wait_line(mark, r"\[Stop\] roll: officer .* in .* tint factor 0\.500", 5)
    r.check("Limo: the officer's real roll uses factor 0.500", bool(line), line or "no roll line in 45 s")
    r.say("chassis items track off", "items", settle_ms=300)
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)

    # Without tint vanilla rolls: no roll line of ours.
    set_tint(r, None)
    time.sleep(1.5)
    mark = r.mark()
    time.sleep(6)
    ours = r.lines(mark, r"\[Stop\] roll: .* in ")
    r.check("no tint: vanilla rolls, no tint roll line", not ours, ours[0] if ours else "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    a = ap.parse_args()
    r = Run(a.slot, "tint-stop-" + a.label)
    g = r.g
    start = r.mark()
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    r.say("chassis stop vanilla off", "stop vanilla", settle_ms=300)
    if car_on_street(r, g, "tint"):
        factors(r)
        real_roll(r, g)
    errors = r.lines(start, r"\[Stop\].*failed|\[Chassis\].*(Exception|\[Error\])")
    r.check("no tint roll error", not errors, errors[0] if errors else "")
    set_tint(r, None)
    r.say("chassis exit", "exit", settle_ms=1500)
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    r.say("chassis car remove", "car remove", settle_ms=2500)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
