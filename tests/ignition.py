"""
Optional ignition: the key, auto ignition and what an engine that is off still does.

    python ignition.py --slot mptest6
    python ignition.py --slot mptest6 --vehicle cheetah

Spawns a car on flat ground, sits in it and goes through the ignition the way a player does (the key as the key
binding turns it, the auto ignition button as a click; both through the host). Reads `chassis ign` for the state
and drives with `chassis drive` to see whether the throttle works. Then compares the key fob with the body colour
(`chassis ign tag`) as spawned, with a custom paint and after a repaint in Marco's menu. Prints PASS or FAIL per check; exit code 1 on any
FAIL. Debug build of Chassis needed.
"""
import argparse
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
X, Y, Z = 60.0, 4.4, -70.0
STATE = re.compile(r"ign: .*?: auto ignition (on|off), key (on|off), key shows (\d), driver (\w+), engine (on|off), "
                   r"rpm (\d+), speed (-?[0-9.]+) km/h")
fails = 0


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def run(slot, cmd):
    return s1(slot, "run", cmd)


def check(name, ok, detail):
    global fails
    print("%s %s: %s" % ("PASS" if ok else "FAIL", name, detail))
    fails += 0 if ok else 1


def state(slot, act=""):
    out = run(slot, ("chassis ign " + act).strip())
    if act:
        time.sleep(1.0)
        out = run(slot, "chassis ign")
    m = STATE.search(out)
    if not m:
        return None, out
    return {"auto": m.group(1), "key": m.group(2), "shows": m.group(3), "driver": m.group(4), "engine": m.group(5),
            "rpm": int(m.group(6)), "speed": float(m.group(7))}, out


def drive(slot):
    """Full throttle for 2 s from standing; returns the speed read while the throttle is still held."""
    run(slot, "chassis drive 1 0 2")
    time.sleep(1.5)
    st, _ = state(slot)
    # Signed: forward is positive, so a car still rolling back from the last check does not count as driving.
    speed = st["speed"] if st else -99
    # back to the start for the next check
    time.sleep(1)
    run(slot, "chassis drive -1 0 2")
    time.sleep(3)
    return speed


TAG = re.compile(r"ign tag: fob #?([0-9A-Fa-f]{6}), body #?([0-9A-Fa-f]{6})")


def tag(slot):
    """The key fob's colour and the body colour as the car shows them, after the half-second tick."""
    time.sleep(1.5)
    m = TAG.search(run(slot, "chassis ign tag"))
    return (m.group(1).upper(), m.group(2).upper()) if m else (None, None)


def key_tag(slot, paint="1E5AC8"):
    """The fob follows the body colour: as spawned, with a custom paint, and after a repaint at Marco's."""
    fob, body = tag(slot)
    check("tag-start", fob is not None and fob == body, "fob %s, body %s as spawned" % (fob, body))

    run(slot, "chassis tune set paint %s,classic" % paint)
    fob, body = tag(slot)
    check("tag-paint", fob == body == paint, "custom paint %s: fob %s, body %s" % (paint, fob, body))

    before = fob
    run(slot, "chassis tune marco open")
    bought = run(slot, "chassis tune marco Red")
    run(slot, "chassis tune marco close")
    fob, body = tag(slot)
    check("tag-repaint", fob is not None and fob == body and fob != before,
          "repainted at Marco's (%s): fob %s, body %s" % (bought.strip().splitlines()[0][:80] if bought.strip() else "no answer", fob, body))
    run(slot, "chassis tune clear")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--vehicle", default="shitbox")
    a = ap.parse_args()
    slot, code = a.slot, a.vehicle

    run(slot, "chassis exit")
    run(slot, "chassis car remove")
    s1(slot, "tp", str(X + 4), str(Y), str(Z))
    out = run(slot, "chassis car put %s %s %s %s 180" % (code, X, Y, Z))
    if "car put:" not in out:
        check("spawn", False, out.strip()[-200:])
        sys.exit(1)
    time.sleep(2)
    run(slot, "chassis car enter " + code)
    time.sleep(1.5)

    st, out = state(slot)
    if st is None:
        check("state", False, "no ignition state: " + out.strip()[-300:])
        sys.exit(1)
    if st["auto"] == "off":
        # a car left from an earlier run: back to the default first
        st, out = state(slot, "auto")
    check("default", st["auto"] == "on" and st["engine"] == "on", "auto ignition %s, engine %s on entering" % (st["auto"], st["engine"]))
    v = drive(slot)
    check("drive-auto", v > 5, "%.1f km/h after 1.5 s full throttle with auto ignition" % v)

    st, out = state(slot, "key")
    check("key-off", st and st["auto"] == "off" and st["key"] == "off" and st["engine"] == "off",
          "after turning the key: auto %s, key %s, engine %s" % (st and st["auto"], st and st["key"], st and st["engine"]))
    check("key-off-log", "auto ignition off" in s1(slot, "log", "--grep", "Ignition", "-n", "20"),
          "host logged that the key turned auto ignition off")
    check("rpm-off", st and st["rpm"] == 0, "rev counter %s with the engine off" % (st and st["rpm"]))
    v = drive(slot)
    check("no-throttle", -99 < v < 1.5, "%.1f km/h after 1.5 s full throttle with the engine off" % v)

    lights = run(slot, "chassis ctl lights 1")
    check("lights-off-engine", "value now 1" in lights, "headlights switch with the engine off: " + lights.strip().splitlines()[0][:120] if lights.strip() else "no answer")
    run(slot, "chassis ctl lights 0")

    run(slot, "chassis exit")
    time.sleep(1)
    run(slot, "chassis car enter " + code)
    time.sleep(1.5)
    st, _ = state(slot)
    check("stays-off", st and st["engine"] == "off" and st["auto"] == "off",
          "re-entered with auto ignition off: engine %s" % (st and st["engine"]))

    st, _ = state(slot, "key")
    check("key-on", st and st["key"] == "on" and st["engine"] == "on" and st["auto"] == "off",
          "key turned on: key %s, engine %s, auto %s" % (st and st["key"], st and st["engine"], st and st["auto"]))
    v = drive(slot)
    check("drive-key", v > 5, "%.1f km/h after 1.5 s full throttle with the key on" % v)

    # The ignition binding on a controller button, pressed on a virtual gamepad.
    run(slot, "chassis bind Ignition button <Gamepad>/select")
    run(slot, "chassis bind pad select")
    time.sleep(1.5)
    st, _ = state(slot)
    check("bind-key", st and st["key"] == "off" and st["engine"] == "off",
          "ignition button <Gamepad>/select: key %s, engine %s" % (st and st["key"], st and st["engine"]))
    run(slot, "chassis bind Ignition button none")

    st, _ = state(slot, "auto")
    check("auto-on", st and st["auto"] == "on" and st["engine"] == "on",
          "auto ignition button: auto %s, engine %s" % (st and st["auto"], st and st["engine"]))

    key_tag(slot)

    run(slot, "chassis exit")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
