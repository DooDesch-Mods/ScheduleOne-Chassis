"""
Own car in the side mirrors: spawns a vanilla car (the shitbox, or `--vehicle <code>`) at a fixed spot at noon,
gets in, turns first person on and shows the own car in the mirrors (`chassis mirror own on`). Looks at each side
mirror and takes a screenshot, then the same with the own car hidden for comparison. Checks that each side mirror's
first-render line reports its near plane past the glass, so the housing rim stays out of the picture. The rim itself
is judged on the screenshots. Prints PASS or FAIL per check.

    python own_car_side.py --slot mptest5 --label fix

Needs a Debug build of Chassis in the slot and a loaded save. The car is removed at the end.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run, SPOT, YAW, part  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicle", default="shitbox", help="vanilla vehicle code")
    a = ap.parse_args()
    code = a.vehicle.lower()
    r = Run(a.slot, "own-side-" + a.label + ("" if code == "shitbox" else "-" + code))
    g = r.g

    start = r.mark()
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    g.run("spawnvehicle " + code, settle_ms=3000, max_wait_ms=8000)

    entered = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
    r.check("got into the " + code, any("driver" in l for l in entered), entered[-1] if entered else "no answer")
    view = r.say("chassis view 0", "view=")
    r.check("first person on", any("first person" in l for l in view), view[-1] if view else "no answer")

    r.say("chassis mirror own on", "mirror ")
    time.sleep(1.0)
    now = r.info()
    r.check("own car shown", "own car shown" in now, now)

    for side in ("left", "right"):
        look = r.say("chassis mirror look " + side, "looking at")
        r.check("view turned to the %s mirror" % side, bool(look), look[-1] if look else "no answer")
        time.sleep(1.5)
        mine = part(r.info(), side)
        r.check("info: %s rendering" % side, mine.startswith(side + " rendering"), mine or "no %s part" % side)
        r.shot("own-shown-" + side)

    for side in ("left", "right"):
        first = r.lines(start, r"\[Mirror\] first mirror render \(%s\).*near plane [0-9.]+ m past the glass" % side)
        clear = re.search(r"near plane ([0-9.]+) m", first[0]) if first else None
        r.check("%s mirror near plane past the housing rim" % side,
                bool(clear) and float(clear.group(1)) > 0, first[0] if first else "no first-render line")

    r.say("chassis mirror own off", "mirror ")
    time.sleep(1.0)
    for side in ("left", "right"):
        r.say("chassis mirror look " + side, "looking at")
        time.sleep(1.5)
        r.shot("own-hidden-" + side)

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Mirror\].*failed")
    r.check("no Chassis exception or mirror error", not errors, errors[0] if errors else "")

    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
