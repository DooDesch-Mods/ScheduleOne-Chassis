"""
Pictures of the ignition lock with the key in it, for the visual check before a merge.

    python ignition_shots.py --slot mptest2
    python ignition_shots.py --slot mptest2 --vehicles shitbox cheetah veeper

Per car: puts it at a fixed spot facing -Z, finds its Chassis_IgnitionLock and shoots three views with the HUD
hidden: from the driver's head, close up from the driver's side, and from above between wheel and centre stack.
Prints PASS or FAIL per car (lock found and three pictures taken) and the picture paths; exit code 1 on any FAIL.
The pictures still need a look by eye.
"""
import argparse
import json
import re
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
X, Y, Z = 60.0, 4.4, -70.0
fails = 0


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def check(name, ok, detail):
    global fails
    print("%s %s: %s" % ("PASS" if ok else "FAIL", name, detail))
    fails += 0 if ok else 1


def lock_position(slot):
    out = s1(slot, "find", "Chassis_IgnitionLock", "--exact", "--rect", str(X - 4), str(Z - 4), str(X + 4), str(Z + 4),
             "-n", "1")
    try:
        objs = json.loads(out[out.index("{"):])["objects"]
        return objs[0]["position"] if objs else None
    except (ValueError, KeyError) as e:
        print("INFO find answer not readable (%s): %s" % (e, out.strip()[-200:]))
        return None


def shot(slot, eye, target, fov):
    out = s1(slot, "cam", "shot", "--eye", *[str(v) for v in eye], "--target", *[str(v) for v in target],
             "--fov", str(fov), "--hud", "hide")
    m = re.search(r'"path": "([^"]+)"', out)
    return json.loads('"' + m.group(1) + '"') if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--vehicles", nargs="+", default=["shitbox", "cheetah", "veeper"])
    a = ap.parse_args()
    slot = a.slot

    for code in a.vehicles:
        s1(slot, "run", "chassis exit")
        s1(slot, "run", "chassis car remove")
        s1(slot, "tp", str(X + 40), str(Y), str(Z + 40))
        out = s1(slot, "run", "chassis car put %s %s %s %s 180" % (code, X, Y, Z))
        if "car put:" not in out:
            check(code, False, "car put failed: " + out.strip()[-160:])
            continue
        time.sleep(2.5)
        p = lock_position(slot)
        if p is None:
            check(code, False, "no Chassis_IgnitionLock in the car")
            continue
        lx, ly, lz = p
        # The car faces -Z: behind the dashboard is +Z, the driver sits at +X of the lock, the passenger at -X.
        views = [
            ("driver", (lx + 0.3, ly + 0.38, lz + 0.6), (lx + 0.05, ly - 0.05, lz - 0.2), 70),
            ("close", (lx + 0.12, ly + 0.1, lz + 0.22), (lx, ly - 0.01, lz), 40),
            ("above", (lx - 0.05, ly + 0.32, lz + 0.42), (lx + 0.08, ly, lz), 55),
        ]
        paths = []
        for name, eye, target, fov in views:
            path = shot(slot, eye, target, fov)
            print("INFO %s %s: %s" % (code, name, path))
            paths.append(path)
        s1(slot, "cam", "release")
        check(code, all(paths), "lock at (%.2f, %.2f, %.2f), %d of 3 pictures" % (lx, ly, lz, sum(1 for x in paths if x)))

    s1(slot, "run", "chassis car remove")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
