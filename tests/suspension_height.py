"""
Suspension stages must lower the body over the wheels, never raise it.

    python suspension_height.py --slot mptest5
    python suspension_height.py --slot mptest5 --vehicles cheetah,shitbox

Spawns each car on flat ground, installs suspension_1..4 and measures the clearance between the body collider's
underside and the tyre bottoms. Prints PASS or FAIL per car and stage; exit code 1 on any FAIL.
"""
import argparse
import json
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
# vehicle code -> root object name and the collider that is the body's underside
CARS = {"cheetah": ("Coupe(Clone)", "Main_Collider")}
X, Y, Z = 60.0, 4.4, -70.0  # flat terrain, ground 3.98
STAGES = ["stock", "suspension_1", "suspension_2", "suspension_3", "suspension_4"]


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout


def run(slot, cmd):
    return s1(slot, "run", cmd)


def measure(slot, root, body_suffix):
    d = json.loads(s1(slot, "find", "", "--kind", "both", "--under", root, "--sort", "none", "-n", "600"))
    body, tires = None, []
    for o in d["objects"]:
        c = o["bounds"]["center"]
        if abs(c[0] - X) > 5 or abs(c[2] - Z) > 5:
            continue
        p = o["path"]
        if body_suffix and p.endswith(body_suffix):
            body = o
        if p.endswith("/Tire") and o["type"].startswith("MeshRenderer"):
            tires.append(o)
    if not tires:
        return None
    tire_bottom = sum(t["bounds"]["min"][1] for t in tires) / len(tires)
    tire_centre = sum(t["bounds"]["center"][1] for t in tires) / len(tires)
    if body is not None:
        return body["bounds"]["min"][1] - tire_bottom
    # no body collider named: use the wheel centre against the ground-fixed tyre bottom (not a body measure)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--vehicles", default="cheetah")
    a = ap.parse_args()
    fails = 0
    for code in [c.strip() for c in a.vehicles.split(",") if c.strip()]:
        if code not in CARS:
            print("FAIL %s: no body collider known in this script" % code)
            fails += 1
            continue
        root, suffix = CARS[code]
        run(a.slot, "chassis car remove")
        out = run(a.slot, "chassis car put %s %s %s %s 0" % (code, X, Y, Z))
        if "car put:" not in out:
            print("FAIL %s: spawn failed: %s" % (code, out.strip()[-200:]))
            fails += 1
            continue
        time.sleep(5)
        prev = None
        for st in STAGES:
            run(a.slot, "chassis tune remove suspension" if st == "stock" else "chassis tune part " + st)
            time.sleep(4)
            clr = measure(a.slot, root, suffix)
            if clr is None:
                print("FAIL %s %s: body or tyres not found" % (code, st))
                fails += 1
                continue
            ok = prev is None or clr <= prev + 0.002
            print("%s %s %s: clearance %.3f m (previous %s)" % ("PASS" if ok else "FAIL", code, st, clr,
                                                                 "-" if prev is None else "%.3f" % prev))
            fails += 0 if ok else 1
            prev = clr
        run(a.slot, "chassis car remove")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
