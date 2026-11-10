"""
A police body search of a player in a car becomes a traffic stop instead of an instant chase.

    python traffic_stop.py --slot mptest --label fix

Three runs in one call, each with a fresh car 12 m from an officer on foot:
- comply: the car stands; the officer walks to the window, the search screen opens, the search ends without a chase.
- flee: the car keeps moving; no chase for the first seconds of the countdown, a chase after it.
- driveoff: the car stops, then drives off before the search; a chase follows.
Debug build of Chassis, loaded save, police not ignoring players. Prints PASS or FAIL per check; exit code 1 on FAIL.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run  # noqa: E402


def field(state, name):
    m = re.search(r"\b%s=(\S+)" % re.escape(name), state)
    return m.group(1) if m else ""


def setup(r, g):
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    r.say("chassis exit", "exit", settle_ms=1500)
    r.say("chassis car remove", "car remove", settle_ms=2500)
    cop = r.say("chassis items cop", "items cop", settle_ms=600)
    line = cop[-1] if cop else ""
    m = re.search(r"pos=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)", line)
    if not r.check("an officer on foot exists", bool(m), line or "no answer"):
        return False
    x, y, z = (float(v) for v in m.groups())
    g.world("teleport", {"x": x + 12.0, "y": y + 0.5, "z": z, "yaw": 0}, timeout_ms=8000)
    time.sleep(1.5)
    g.run("spawnvehicle shitbox", settle_ms=3000, max_wait_ms=8000)
    entered = r.say("chassis car enter shitbox", "car enter", settle_ms=2500)
    return r.check("got into the car near the officer", any("driver" in l for l in entered), entered[-1] if entered else "no answer")


def state(r):
    said = r.say("chassis stop state", "stop state", settle_ms=300)
    return said[-1] if said else ""


def comply(r, g):
    if not setup(r, g):
        return
    mark = r.mark()
    started = r.say("chassis stop start", "stop start", settle_ms=500)
    r.check("comply: an officer starts the search", any("searches" in l for l in started), started[-1] if started else "no answer")
    pulled = r.wait_line(mark, r"\[Stop\] officer .* pulls over", 5)
    r.check("comply: the search becomes a traffic stop", bool(pulled), pulled or "no pull-over line")
    worst, screen, last = "None", False, ""
    end = time.time() + 45
    while time.time() < end:
        time.sleep(1)
        last = state(r)
        p = field(last, "pursuit")
        if p and p != "None":
            worst = p
            break
        if field(last, "screen") == "True" and not screen:
            screen = True
            r.shot("comply-search")
        if screen and field(last, "screen") == "False" and field(last, "searches") == "0":
            break
    r.check("comply: no chase while the car stands", worst == "None", "pursuit " + worst + " | " + last)
    # Vanilla escalates within a frame and the arrest can end before the first sample, so the crime line counts too.
    crime = r.lines(mark, r"crime recorded .*Failure to comply")
    r.check("comply: no Failure to comply", not crime, crime[0] if crime else "")
    window = r.lines(mark, r"\[Stop\] officer .* is at the window")
    r.check("comply: the officer reaches the window", bool(window), window[0] if window else "no window line")
    r.check("comply: the search screen opens from the seat", screen, last)
    r.check("comply: the search ends", field(last, "searches") == "0" and field(last, "screen") == "False", last)
    r.check("comply: still seated", field(last, "seated") == "True", last)
    over = r.lines(mark, r"\[Stop\] the body search of officer .* ended")
    r.check("comply: the stop state is dropped when the search ends", bool(over), over[0] if over else "no end line")


# A residential street with about 15 m clear ahead and 30 m behind at yaw 180. No street in town gives
# 10 s of straight driving, so the flee run rocks the car back and forth: it keeps moving, never stands
# for a second, and stays clear of the houses.
OPEN = (-133.0, -3.6, 54.2)
OPEN_YAW = 180


def car_on_street(r, g, tag):
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    r.say("chassis exit", "exit", settle_ms=1500)
    r.say("chassis car remove", "car remove", settle_ms=2500)
    x, y, z = OPEN
    g.world("teleport", {"x": x + 4.0, "y": y, "z": z, "yaw": 0}, timeout_ms=8000)
    time.sleep(1.5)
    put = r.say("chassis car put shitbox %s %s %s %s" % (x, y, z, OPEN_YAW), "car put", settle_ms=5000)
    r.note(put[-1] if put else "no car put answer")
    entered = r.say("chassis car enter shitbox", "car enter", settle_ms=2500)
    if not r.check(tag + ": got into the car on the street", any("driver" in l for l in entered), entered[-1] if entered else "no answer"):
        return False
    placed = r.say("chassis items target 12 cop", "items target", settle_ms=800)
    r.note(tag + " cop: " + (placed[-1] if placed else "no answer"))
    return True


def flee(r, g):
    if not car_on_street(r, g, "flee"):
        return
    gear, flipped = -1, time.time()
    r.say("chassis drive -1 0 3", "drive:", settle_ms=200)
    time.sleep(1.0)
    mark = r.mark()
    r.say("chassis stop start", "stop start", settle_ms=300)
    t0 = time.time()
    early, late, at = "None", "None", None
    end = t0 + 25
    while time.time() < end:
        # Reverse for 2 s, forward for 2 s: the car crosses 0 km/h for well under the 1 s a stop needs.
        if time.time() - flipped > 2.0:
            gear, flipped = -gear, time.time()
            r.say("chassis drive %d 0 3" % gear, "drive:", settle_ms=100)
        p = field(state(r), "pursuit")
        if p and p != "None":
            at = time.time() - t0
            if at < 7:
                early = p
            late = p
            break
    r.say("chassis drive 0 0 0.1", "drive:", settle_ms=100)
    r.check("flee: no chase during the countdown", early == "None", "chase after %.1f s" % at if at is not None else "no chase")
    r.check("flee: a chase once the countdown ran out", late != "None", "pursuit %s after %s s" % (late, "%.1f" % at if at is not None else "-"))
    stopped = r.lines(mark, r"\[Stop\] .* stopped after")
    r.check("flee: the rocking car never counted as stopped", not stopped, stopped[0] if stopped else "")
    why = r.lines(mark, r"\[Stop\] traffic stop of .* escalates: the car did not stop")
    r.check("flee: escalates because the car did not stop", bool(why), why[0] if why else "no matching escalate line")


def driveoff(r, g):
    if not car_on_street(r, g, "driveoff"):
        return
    mark = r.mark()
    r.say("chassis stop start", "stop start", settle_ms=300)
    stopped = r.wait_line(mark, r"\[Stop\] .* stopped after", 8)
    if not r.check("driveoff: the standing car counts as stopped", bool(stopped), stopped or "no stopped line"):
        return
    # Drive off before the officer reaches the window: reverse, the street is clear behind.
    r.say("chassis drive -1 0 4", "drive:", settle_ms=100)
    t0, worst = time.time(), "None"
    while time.time() - t0 < 10:
        time.sleep(0.7)
        p = field(state(r), "pursuit")
        if p and p != "None":
            worst = p
            break
    r.say("chassis drive 0 0 0.1", "drive:", settle_ms=100)
    r.check("driveoff: a chase once the car drives off", worst != "None", "pursuit " + worst)
    why = r.lines(mark, r"\[Stop\] traffic stop of .* escalates: the car drove off")
    r.check("driveoff: escalates because the car drove off", bool(why), why[0] if why else "no matching escalate line")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--only", choices=["comply", "flee", "driveoff"])
    ap.add_argument("--vanilla", action="store_true", help="switch the traffic stop off to reproduce the old chase")
    a = ap.parse_args()
    r = Run(a.slot, "traffic-stop-" + a.label)
    g = r.g
    start = r.mark()
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    g.run("setpoliceignoreplayers false", settle_ms=500, max_wait_ms=4000)
    # --vanilla reproduces the old behaviour on the same build: the comply run then expects a FAIL.
    r.say("chassis stop vanilla " + ("on" if a.vanilla else "off"), "stop vanilla", settle_ms=300)
    if a.only in (None, "comply"):
        comply(r, g)
    if a.only in (None, "flee"):
        flee(r, g)
    if a.only in (None, "driveoff"):
        driveoff(r, g)
    errors = r.lines(start, r"\[Stop\].*failed|\[Chassis\].*(Exception|\[Error\])")
    r.check("no traffic stop error", not errors, errors[0] if errors else "")
    r.say("chassis exit", "exit", settle_ms=1500)
    g.run("clearwanted", settle_ms=500, max_wait_ms=4000)
    r.say("chassis car remove", "car remove", settle_ms=2500)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
