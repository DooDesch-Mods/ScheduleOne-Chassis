"""
Getting into a vehicle logs no key hints error and still shows the Chassis key hints.

    python -I tests/key_hints_enter.py --slot mptest

Spawns a car beside the player, gets in and out three times and checks:
  no-error   - no Unity error about 'chassis_vehicle_keys' while getting in or out. The game logs it from its state
               machine ("InputPromptsManager: LoadModule: No input data found ..."), so it shows in the bridge's
               Unity lines of the command, not in the MelonLoader log file.
  shown      - after each entry `chassis hints` reports the vehicle keys as shown, with First Person in the list
  loaded     - each entry writes "[KeyHints] shown in the key hints"
Prints PASS or FAIL per check; exit code 1 on any FAIL. Debug build of Chassis needed, a save loaded.
"""
import argparse
import json
import subprocess
import sys
import time

S1 = "F:/Projects/Mods/Schedule1/Workspace/tools/bridge/s1.py"
MODULE = "chassis_vehicle_keys"
SHOWN = "[KeyHints] shown in the key hints"
fails = 0


def s1(slot, *args):
    done = subprocess.run([sys.executable, "-I", S1, "--slot", slot] + list(args), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    return done.stdout + done.stderr


def run(slot, cmd, settle_ms=None):
    """Runs a console command and returns its log lines as (level, text) pairs."""
    words = ["run"] + cmd.split() + ["--json"]
    if settle_ms:
        words += ["--settle-ms", str(settle_ms)]
    out = s1(slot, *words)
    try:
        lines = [(l.get("level", ""), l.get("text", "")) for l in json.loads(out).get("lines", [])]
    except ValueError:
        lines = [("error", "no JSON from s1.py run: " + out.strip()[-300:])]
    print("  > " + cmd)
    for level, text in lines[-3:]:
        print("    [%s] %s" % (level, text.splitlines()[0] if text else ""))
    return lines


def module_errors(lines):
    return [t.splitlines()[0] for lv, t in lines if lv == "error" and MODULE in t]


def text_of(lines):
    return "\n".join(t for _, t in lines)


def count(slot, text):
    return len([l for l in s1(slot, "log", "--grep", text, "-n", "100000").splitlines() if text in l])


def check(name, ok, detail):
    global fails
    print("%s %s: %s" % ("PASS" if ok else "FAIL", name, detail))
    fails += 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--code", default="shitbox")
    ap.add_argument("--rounds", type=int, default=3)
    args = ap.parse_args()
    slot = args.slot

    run(slot, "chassis exit")
    for _ in range(3):
        run(slot, "chassis despawn car")
    run(slot, "spawnvehicle " + args.code)
    time.sleep(2)

    shown_before = count(slot, SHOWN)
    errors = []
    hint_states = []
    for i in range(args.rounds):
        # The vehicle state pushes and reloads its prompt modules in the frames after the seat change.
        enter = run(slot, "chassis car enter " + args.code, settle_ms=2000)
        if "(driver)" not in text_of(enter):
            check("setup", False, "round %d: not in the driver seat of %s" % (i + 1, args.code))
            return 1
        errors += module_errors(enter)
        hints = text_of(run(slot, "chassis hints"))
        hint_states.append("First Person" in hints and "(not shown)" not in hints and "not in a vehicle" not in hints)
        errors += module_errors(run(slot, "chassis exit", settle_ms=1500))

    shown = count(slot, SHOWN) - shown_before
    check("no-error", not errors, "%d error line(s) about %s over %d entries%s" % (
        len(errors), MODULE, args.rounds, (": " + errors[0]) if errors else ""))
    check("shown", all(hint_states), "key hints shown with First Person per entry: %s" % hint_states)
    check("loaded", shown >= args.rounds, "%d '%s' line(s) over %d entries" % (shown, SHOWN, args.rounds))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
