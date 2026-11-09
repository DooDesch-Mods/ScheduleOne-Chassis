"""
Hotbar items in a vehicle: spawns a vanilla car (the shitbox, or `--vehicle <code>`) at a fixed spot at noon, gives a
pistol, an energy drink and a placeable, gets in and walks through the seat rules with `chassis items`:

- the vehicle state keeps equipping on and the pistol is taken into the hand
- chase view: no first-person viewmodel, the right hand leaves the wheel, the pistol does not aim or fire
- first person: viewmodel shown, the seated body's copy hidden, the pistol fires at the nearest NPC
- reloading and drinking throw their trash out of the window next to the seat
- a placeable stays out of the hand while seated and comes back after getting out

Mouse presses go through `chassis items press`, not the real mouse. Prints PASS or FAIL per check.

    python hand_items.py --slot mptest3 --label fix

Needs a Debug build of Chassis in the slot and a loaded save. The car is removed at the end.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run, SPOT, YAW  # noqa: E402

GUN = "m1911"
MAG = "m1911mag"
DRINK = "cuke"
PLACEABLE = "growtent"


def field(status, name):
    m = re.search(r"\b%s=(\S+)" % re.escape(name), status)
    return m.group(1) if m else ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicle", default="shitbox", help="vanilla vehicle code")
    a = ap.parse_args()
    code = a.vehicle.lower()
    r = Run(a.slot, "items-" + a.label + ("" if code == "shitbox" else "-" + code))
    g = r.g

    def status():
        said = r.say("chassis items status", "items: ", settle_ms=600)
        return said[-1] if said else ""

    def ammo(st):
        gun = field(status() if st is None else st, "gun")
        return int(gun) if gun.isdigit() else -1

    start = r.mark()
    g.world("teleport", {"x": SPOT[0], "y": SPOT[1], "z": SPOT[2], "yaw": YAW}, timeout_ms=8000)
    g.world("set_time", {"time": 1200}, timeout_ms=8000)
    time.sleep(1.0)
    for item in (GUN, DRINK, PLACEABLE):
        g.run("give %s 1" % item, settle_ms=800, max_wait_ms=6000)
    g.run("give %s 2" % MAG, settle_ms=800, max_wait_ms=6000)
    g.run("give %s 1" % DRINK, settle_ms=800, max_wait_ms=6000)
    g.run("spawnvehicle " + code, settle_ms=3000, max_wait_ms=8000)

    entered = r.say("chassis car enter " + code, "car enter", settle_ms=2500)
    r.check("got into the " + code, any("driver" in l for l in entered), entered[-1] if entered else "no answer")
    st = status()
    r.check("vehicle state keeps equipping on", field(st, "stateEquipping") == "Enabled", st)

    # ---- chase view -------------------------------------------------------------------------------------------
    r.say("chassis items pick " + GUN, "items slot", settle_ms=1500)
    time.sleep(1.0)
    st = status()
    r.check("pistol in the hand while seated", "RangedWeapon" in field(st, "equippable") or "M1911" in field(st, "equippable"), st)
    r.check("chase view: no first-person viewmodel", field(st, "viewmodel") == "False", st)
    r.check("chase view: item in the seated hand", field(st, "avatarItem") not in ("", "-") and "@Invisible" not in field(st, "avatarItem"), st)
    r.check("right hand off the wheel", field(st, "rightHandIK") == "0.00", st)
    r.shot("chase-pistol")

    before = ammo(st)
    r.say("chassis items press secondary 40", "items press", settle_ms=300)
    time.sleep(0.4)
    r.say("chassis items press primary 2", "items press", settle_ms=800)
    time.sleep(0.6)
    after = ammo(None)
    r.check("chase view: pistol does not fire", before >= 0 and after == before, "ammo %d -> %d" % (before, after))
    kept = r.lines(start, r"\[Items\] mouse input kept from .*weapon in the chase view")
    r.check("chase view: weapon input blocked in the log", bool(kept), kept[0] if kept else "no line")

    # Consumables work in both views; the chase view drinks the first of the two cans.
    r.say("chassis items pick " + DRINK, "items slot", settle_ms=1500)
    time.sleep(1.0)
    mark = r.mark()
    r.say("chassis items press primary 2", "items press", settle_ms=500)
    hit = r.wait_line(mark, r"\[Items\] trash .* thrown out of the (left|right) window", 8)
    # The driver sits on the left; the chase camera behind the car must not decide the side.
    r.check("chase view: drink trash out of the driver's window", bool(hit) and "left window" in hit, hit or "no line within 8 s")
    r.say("chassis items pick " + GUN, "items slot", settle_ms=1500)
    time.sleep(1.0)

    # ---- first person -----------------------------------------------------------------------------------------
    view = r.say("chassis view 0", "view=")
    r.check("first person on", any("first person" in l for l in view), view[-1] if view else "no answer")
    time.sleep(1.0)
    st = status()
    r.check("first person: viewmodel shown", field(st, "viewmodel") == "True", st)
    r.check("first person: seated body's copy hidden", "@Invisible" in field(st, "avatarItem"), st)
    r.check("first person: own right hand stays on the wheel", field(st, "rightHandIK") == "1.00", st)
    def blocker(line):
        """What stops the bullet before the aimed NPC (a parked or passing car), or "" when the line of fire is clear."""
        stop = re.search(r"bullet stops at (\S+) at ([\d.]+) m", line)
        npc = re.search(r"aimed NPC at \([^)]*\) ([\d.]+) m", line)
        if not stop or not npc:
            return ""
        return "%s at %s m" % (stop.group(1), stop.group(2)) if float(stop.group(2)) < float(npc.group(1)) - 1.0 else ""

    # The NPC walks on after the warp, so the view follows it every frame until the shot is out. A car of the save's
    # world can stand or drive in the line of fire; the NPC is placed again until the line is clear.
    blocked = ""
    for attempt in range(4):
        aim = r.say("chassis items target 8", "items target", settle_ms=800)
        r.note("aim: " + (aim[-1] if aim else "no answer"))
        r.say("chassis items track", "items track", settle_ms=300)
        time.sleep(1.0)
        fix = r.say("chassis items aimfix", "items aimfix", settle_ms=300)
        r.note("aim error with tracking: " + (fix[-1] if fix else "no answer"))
        ray = r.say("chassis items ray", "items ray", settle_ms=600)
        r.note("ray: " + (ray[-1] if ray else "no answer"))
        blocked = blocker(ray[-1] if ray else "")
        if not blocked:
            break
        r.note("line of fire blocked by " + blocked + ", attempt %d" % (attempt + 1))
        r.say("chassis items track off", "items track", settle_ms=300)
        time.sleep(4.0)

    def hp():
        said = r.say("chassis items hp", "items hp", settle_ms=500)
        m = re.search(r"health (\d+)", said[-1]) if said else None
        return int(m.group(1)) if m else -1

    hp_before = hp()
    before = ammo(st)
    # Vanilla scatters every shot by 5 to 15 degrees, less the longer the weapon is aimed, so a single shot at 8 m can
    # miss; the aim is held until the accuracy is up and up to three shots are fired.
    r.say("chassis items press secondary 600", "items press", settle_ms=300)
    time.sleep(2.0)
    r.note("before the shot: " + status())
    r.shot("fp-aimed")
    r.say("chassis items press primary 2", "items press", settle_ms=800)
    time.sleep(0.6)
    after = ammo(None)
    r.check("first person: pistol fires", before > 0 and after == before - 1, "ammo %d -> %d" % (before, after))
    hp_after = hp()
    for _ in range(2):
        if 0 <= hp_after < hp_before:
            break
        time.sleep(1.0)
        r.say("chassis items press primary 2", "items press", settle_ms=800)
        time.sleep(0.6)
        hp_after = hp()
    after = ammo(None)
    r.say("chassis items track off", "items track", settle_ms=300)
    r.check("shot from inside the car hits the NPC", hp_before > 0 and 0 <= hp_after < hp_before,
            "health %d -> %d" % (hp_before, hp_after) + (", line of fire blocked by " + blocked if blocked else ""))
    let = r.lines(start, r"\[Items\] shot from inside .* body collider")
    r.check("own body lets the bullet through", bool(let), let[0] if let else "no line")

    # Vanilla drops the reload trash only when the spare magazine runs empty, so the gun is emptied first.
    left = after
    r.say("chassis items press secondary 900", "items press", settle_ms=300)
    time.sleep(0.6)
    for _ in range(12):
        if left <= 0:
            break
        r.say("chassis items press primary 2", "items press", settle_ms=500)
        time.sleep(0.5)
        left = ammo(None)
    r.check("magazine emptied for the reload", left == 0, "ammo %d" % left)
    mark = r.mark()
    r.say("chassis items press reload 2", "items press", settle_ms=500)
    hit = r.wait_line(mark, r"\[Items\] trash .* thrown out of the (left|right) window", 6)
    r.check("reload trash thrown out of the window", bool(hit), hit or "no line within 6 s")
    time.sleep(1.5)
    r.shot("fp-reload-trash")

    # ---- drink ------------------------------------------------------------------------------------------------
    r.say("chassis items pick " + DRINK, "items slot", settle_ms=1500)
    time.sleep(1.0)
    mark = r.mark()
    r.say("chassis items press primary 2", "items press", settle_ms=500)
    hit = r.wait_line(mark, r"\[Items\] trash .* thrown out of the (left|right) window", 8)
    r.check("drink trash thrown out of the window", bool(hit), hit or "no line within 8 s")

    # ---- placeable --------------------------------------------------------------------------------------------
    r.say("chassis items pick " + PLACEABLE, "items slot", settle_ms=1500)
    time.sleep(0.8)
    st = status()
    r.check("placeable stays out of the hand", field(st, "placeable") == "True" and field(st, "equippable") == "-", st)
    refused = r.lines(start, r"\[Items\] .* is a placeable, it stays out of the hand")
    r.check("placeable refusal in the log", bool(refused), refused[0] if refused else "no line")

    mark = r.mark()
    r.say("chassis items exit", "items exit", settle_ms=2500)
    back = r.wait_line(mark, r"\[Items\] .* back in the hand after getting out", 5)
    r.check("placeable back in the hand after getting out", bool(back), back or "no line within 5 s")
    st = status()
    r.check("viewmodel shown again on foot", field(st, "viewmodel") == "True", st)

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Items\].*failed")
    r.check("no Chassis exception or item error", not errors, errors[0] if errors else "")

    gone = r.say("chassis car remove", "car remove", settle_ms=3500)
    r.note("removed: " + (gone[-1] if gone else "no answer"))
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
