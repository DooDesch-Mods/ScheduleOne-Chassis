"""
The Mirrors setting (`chassis mirror mode 0-3`): spawns a car at the mirrors.py spot at noon and walks through the four
modes. On foot, + Nearby Cars must render the parked car's mirrors and Rear + Side must not. Seated, Rear + Side renders
rear and side glass, Rear Only keeps the side glass hidden, Off hides all glass. Checks the `[Mirror]` log lines and the
`chassis mirror` answers, takes a screenshot per mode and prints PASS or FAIL per check.

    python mirror_modes.py --slot mptest5 --label dev --vehicle shitbox

The car is removed and the mode set back to Rear + Side at the end.

Needs a Debug build of Chassis in the slot and a loaded save.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run, SPOT, YAW, part  # noqa: E402


def nearby_part(info, code):
    """The `; nearby <code> net ...` part of a `chassis mirror info` answer."""
    for piece in info.split(";")[1:]:
        if piece.strip().startswith("nearby " + code + " "):
            return piece.strip()
    return ""


def renders(text):
    found = re.search(r"rear \S.*? glass (\w+) renders (\d+)", text)
    return (found.group(1), int(found.group(2))) if found else ("-", 0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicle", default="shitbox", help="vanilla vehicle code")
    a = ap.parse_args()
    code = a.vehicle.lower()
    r = Run(a.slot, a.label + "-modes-" + code)
    g = r.g
    c = re.escape(code)

    start = r.mark()
    r.say("chassis mirror mode 2", "mirror ")
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    g.run("spawnvehicle " + code, settle_ms=3000, max_wait_ms=8000)
    time.sleep(1.0)

    # ---- on foot, in front of the parked car -------------------------------------------------------------------
    r.say("chassis mirror mode 3", "mirror ")
    picked = r.wait_line(start, r"\[Mirror\] nearby cars with mirrors: %s net" % c, 5)
    r.check("+ Nearby Cars picks the parked car on foot", picked is not None, picked or "no nearby line within 5 s")
    time.sleep(2.0)
    now = r.info()
    mine = nearby_part(now, code)
    glass, count = renders(mine)
    r.check("+ Nearby Cars: parked car's rear mirror renders", count > 0 and glass == "yes", mine or now)
    r.check("+ Nearby Cars: info counts the car", re.search(r"mode \+ Nearby Cars, nearby [1-3]\b", now) is not None, now)
    first = r.lines(start, r"\[Mirror\] first mirror render \(rear\).*%s" % c)
    r.check("first render line names the parked car", bool(first), first[0] if first else "no line")
    r.shot("mode3-on-foot")

    mark = r.mark()
    r.say("chassis mirror mode 2", "mirror ")
    applied = r.wait_line(mark, r"\[Mirror\] mode set to Rear \+ Side", 3)
    r.check("mode set to Rear + Side logged", applied is not None, applied or "no line")
    time.sleep(1.0)
    now = r.info()
    r.check("Rear + Side on foot: no nearby car", "nearby 0" in now and not nearby_part(now, code), now)
    r.shot("mode2-on-foot")

    # ---- seated ------------------------------------------------------------------------------------------------
    entered = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
    r.check("got into the " + code, any("driver" in l for l in entered), entered[-1] if entered else "no answer")
    r.say("chassis view 0", "view=")
    r.say("chassis mirror look left", "looking at")
    time.sleep(1.5)
    left = part(r.info(), "left")
    r.check("Rear + Side: left glass shows", left.startswith("left rendering") and "glass yes" in left, left or "no left part")
    r.say("chassis mirror look", "looking at")
    time.sleep(1.5)
    now = r.info()
    r.check("Rear + Side: rear glass shows", "rendering" in now.split(";")[0] and "glass yes" in now.split(";")[0], now)
    r.shot("mode2-seated")

    r.say("chassis mirror mode 3", "mirror ")
    time.sleep(1.0)
    now = r.info()
    r.check("+ Nearby Cars seated: own car not in the nearby list", not nearby_part(now, code), now)
    r.check("+ Nearby Cars seated: rear glass still shows", "glass yes" in now.split(";")[0], now)

    r.say("chassis mirror mode 1", "mirror ")
    r.say("chassis mirror look left", "looking at")
    time.sleep(1.5)
    now = r.info()
    left = part(now, "left")
    r.check("Rear Only: side mirrors stop", left.startswith("left side mirrors off by mode"), left or "no left part")
    r.check("Rear Only: side glass hidden", "glass hidden" in left, left)
    r.say("chassis mirror look", "looking at")
    time.sleep(1.5)
    now = r.info()
    r.check("Rear Only: rear glass shows", "glass yes" in now.split(";")[0], now)
    r.shot("mode1-seated")

    r.say("chassis mirror mode 0", "mirror ")
    time.sleep(1.0)
    now = r.info()
    head = now.split(";")[0]
    r.check("Off: rear mirror stops", "mirrors off by mode" in head, now)
    r.check("Off: rear glass hidden", "glass hidden" in head, now)
    r.check("Off: side glass hidden", all("glass hidden" in part(now, s) for s in ("left", "right")), now)
    r.shot("mode0-seated")

    r.say("chassis mirror mode 2", "mirror ")
    time.sleep(1.5)
    now = r.info()
    r.check("back to Rear + Side: rear glass shows again", "rendering" in now.split(";")[0] and "glass yes" in now.split(";")[0], now)

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Mirror\].*failed")
    r.check("no Chassis exception or mirror error", not errors, errors[0] if errors else "")

    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
