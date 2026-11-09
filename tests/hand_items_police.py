"""
Police and NPCs react to a weapon used from a vehicle as they do on foot: finds the nearest police officer on foot,
parks a car 8 m from them, gets in with a pistol, switches to first person and fires once into the air. Vanilla
then reports the shot as a discharged firearm, and the officer starts a pursuit.

    python hand_items_police.py --slot mptest3 --label fix

The slot must run without `setpoliceignoreplayers true` (session.ps1 -Then ''), with a Debug build of Chassis and a
loaded save. Prints PASS or FAIL per check. The car is removed at the end.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run  # noqa: E402

GUN = "m1911"


def field(status, name):
    m = re.search(r"\b%s=(\S+)" % re.escape(name), status)
    return m.group(1) if m else ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    a = ap.parse_args()
    r = Run(a.slot, "items-police-" + a.label)
    g = r.g

    def status():
        said = r.say("chassis items status", "items: ", settle_ms=600)
        return said[-1] if said else ""

    start = r.mark()
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    g.run("setpoliceignoreplayers false", settle_ms=500, max_wait_ms=4000)
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    cop = r.say("chassis items cop", "items cop", settle_ms=600)
    line = cop[-1] if cop else ""
    m = re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", line)
    r.check("an officer on foot exists", bool(m), line or "no answer")
    if not m:
        return r.summary()
    x, y, z = (float(v) for v in m.groups())
    # 20 m: close enough to see and hear the shot, far enough that the officer does not walk up for a search first.
    g.world("teleport", {"x": x + 20.0, "y": y + 0.5, "z": z, "yaw": 270}, timeout_ms=8000)
    time.sleep(1.5)
    r.note("after the teleport: playerHp " + field(status(), "playerHp"))
    g.run("give %s 1" % GUN, settle_ms=800, max_wait_ms=6000)
    g.run("spawnvehicle shitbox", settle_ms=3000, max_wait_ms=8000)
    r.note("after the spawn: playerHp " + field(status(), "playerHp"))
    entered = r.say("chassis car enter shitbox", "car enter", settle_ms=2500)
    r.check("got into the car beside the officer", any("driver" in l for l in entered), entered[-1] if entered else "no answer")

    r.say("chassis items pick " + GUN, "items slot", settle_ms=1500)
    r.say("chassis view 0", "view=")
    time.sleep(1.0)
    st = status()
    r.check("seated with the pistol in first person", field(st, "seated") == "True" and field(st, "fp") == "True" and field(st, "gun") not in ("", "-"), st)
    # A dead player fires no crime; a death before the shot is named here instead of as a missing police reaction.
    r.check("player alive before the shot", "Dead" not in st and field(st, "playerHp") not in ("", "-", "0"), "playerHp " + field(st, "playerHp"))
    r.check("no pursuit before the shot", field(st, "pursuit").startswith("None"), st)
    early = r.lines(start, r"\[Items\] crime recorded")
    r.check("no crime before the shot", not early, early[0] if early else "")
    shot_mark = r.mark()
    # The officer notices the shot through his vision cone, so he stands in front of the car facing it; the shot
    # goes into the sky so it does not hit him.
    placed = r.say("chassis items target 12 cop", "items target", settle_ms=800)
    r.note("cop: " + (placed[-1] if placed else "no answer"))
    r.say("chassis view -35", "view=")
    time.sleep(0.5)

    pursuit = ""
    shots = 0
    for _ in range(3):
        r.say("chassis items press secondary 240", "items press", settle_ms=300)
        time.sleep(1.5)
        r.say("chassis items press primary 2", "items press", settle_ms=500)
        shots += 1
        for _ in range(8):
            time.sleep(0.5)
            pursuit = field(status(), "pursuit")
            if pursuit and not pursuit.startswith("None"):
                break
        if pursuit and not pursuit.startswith("None"):
            break
    r.note("shots fired: %d" % shots)
    r.shot("police-after-shot")
    r.check("officer reacts to the shot from the car", bool(pursuit) and not pursuit.startswith("None"), "pursuit " + pursuit)
    crime = r.lines(shot_mark, r"\[Items\] crime recorded on the local player: .*seated=True")
    r.check("the shot is recorded as a crime while seated", bool(crime), crime[0] if crime else "no crime line")

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Items\].*failed")
    r.check("no Chassis exception or item error", not errors, errors[0] if errors else "")

    r.say("chassis items exit", "items exit", settle_ms=2000)
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
