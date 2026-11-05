"""Cabin light and glovebox on a vanilla car.

Spawns a car next to the player, takes the driver seat and checks:
  auto-in     - getting in turns the cabin light on (door light)
  fade-in     - it swells in over about half a second instead of a hard on (the log names the time)
  drive-off   - rolling at 40 km/h ends the door light
  fade        - it fades out over 1.5 s instead of a hard off
  dark-moving - it stays off while the car drives
  stand-on    - it comes back after 3 s standing with a player aboard
  label-auto  - the label under the dot names the switch position
  click-on, click-off, click-auto - clicks step the switch Auto -> On -> Off -> Auto (aim + click, as the mouse)
  dome-press  - every click presses the lamp in (its press counter rises by 3)
  lid-open, lid-shut - the glovebox lid swings open while the glovebox is open and shuts after
  glove-own   - a click on the glovebox lid opens the own glovebox with its slots
  glove-keep  - an item put into the glovebox is still there after close and open
  glove-trunk - in trunk mode the same click opens the vanilla trunk
  exit-reset  - a switch left on On goes back to Auto once the car is empty
  exit-door   - getting out lights the cabin
  exit-dark   - the door light is out after its 15 s

Usage: python -I tests/cabin.py --slot mptest5 [--code shitbox]
Needs a Debug build of Chassis (the `chassis` console command) in a loaded save.
"""
import argparse
import os
import re
import subprocess
import sys
import time

S1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Workspace", "tools", "bridge", "s1.py")


def run(slot, words, until=None, max_wait=8000):
    cmd = [sys.executable, "-I", S1, "--slot", slot, "run"] + words.split() + ["--max-wait", str(max_wait)]
    if until:
        cmd += ["--until", until]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = out.stdout + out.stderr
    print("  > " + words)
    for line in text.strip().splitlines()[-8:]:
        print("    " + line)
    return text


def light(slot):
    return run(slot, "chassis cabin", until=r"cabin light (on|off)")


def label(slot):
    return run(slot, "chassis label", until=r"label:")


def level(text):
    m = re.findall(r"cabin light (?:on|off) \(.*?level (\d+\.\d+)", text)
    return float(m[-1]) if m else -1.0


def switch(slot, name):
    text = run(slot, "chassis telemetry", until=r"switch " + name + " ")
    m = re.search(r"switch " + name + r" .*", text)
    return m.group(0) if m else ""


def presses(text):
    m = re.search(r"pressed (\d+)x", text)
    return int(m.group(1)) if m else -1


def lid(text):
    m = re.search(r"lid open (\d\.\d\d)", text)
    return float(m.group(1)) if m else -1.0


def count(text):
    m = re.findall(r"(\d+) items?", text)
    return int(m[-1]) if m else -1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--code", default="shitbox")
    args = ap.parse_args()
    results = []
    s = args.slot

    run(s, "chassis cabin mode own")
    run(s, "spawnvehicle " + args.code, max_wait=4000)
    # The spawn lands a moment after the command returns, right after a boot more so.
    enter = ""
    for _ in range(5):
        enter = run(s, "chassis car enter " + args.code)
        if "(driver)" in enter:
            break
        time.sleep(1)
    if "(driver)" not in enter:
        print("FAIL setup: not in the driver seat of " + args.code)
        return 1
    car = re.search(r"car enter: (\S+ net \d+)", enter)
    results.append(("auto-in", "cabin light on (switch Auto, door" in light(s), "getting in turns the cabin light on"))
    time.sleep(0.5)
    log = subprocess.run([sys.executable, "-I", S1, "--slot", s, "log", "--grep", "light fully in", "-n", "40"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    took = [float(t) for t in re.findall(re.escape(car.group(1)) + r": light fully in after (\d+\.\d+) s", log)] if car else []
    results.append(("fade-in", bool(took) and took[-1] >= 0.4, "the light swells in (took %s s)" % took[-1:]))

    # Driving ends the door light at once; it fades out over a second and comes back after 3 s of standing.
    run(s, "chassis coast 40 3", max_wait=3000)
    time.sleep(0.4)
    levels = [level(light(s)) for _ in range(4)]
    results.append(("drive-off", levels[-1] < 1.0, "driving ends the door light (levels %s)" % levels))
    results.append(("fade", any(0.0 < v < 1.0 for v in levels), "the light fades instead of a hard off (levels %s)" % levels))
    time.sleep(2.5)
    results.append(("dark-moving", "cabin light off" in light(s), "the light stays off while the car drives"))
    time.sleep(5)
    results.append(("stand-on", "cabin light on (switch Auto, standing" in light(s), "the light comes back after 3 s standing"))

    # A click steps the switch Auto -> On -> Off -> Auto; the label under the dot names the position.
    run(s, "chassis view 10")
    run(s, "chassis aim dome")
    time.sleep(0.3)
    results.append(("label-auto", "Cabin light: Auto" in label(s), "the label names the position Auto"))
    pressed_before = presses(switch(s, "dome"))
    run(s, "chassis click", until=r"click:")
    time.sleep(0.5)
    on = light(s)
    results.append(("click-on", "cabin light on (switch On" in on and "Cabin light: On" in label(s), "one click: On"))
    run(s, "chassis click", until=r"click:")
    time.sleep(2.5)
    results.append(("click-off", "cabin light off (switch Off" in light(s) and "Cabin light: Off" in label(s), "two clicks: Off, dark although the car stands"))
    run(s, "chassis click", until=r"click:")
    time.sleep(0.5)
    results.append(("click-auto", "cabin light on (switch Auto" in light(s), "three clicks: back to Auto, lit while standing"))
    pressed_after = presses(switch(s, "dome"))
    results.append(("dome-press", pressed_before >= 0 and pressed_after == pressed_before + 3, "each click presses the lamp in (%d -> %d)" % (pressed_before, pressed_after)))

    run(s, "chassis aim glovebox")
    run(s, "chassis click", until=r"glovebox opened|trunk opened|refused")
    time.sleep(1)
    info = run(s, "chassis cabin", until=r"glovebox")
    results.append(("glove-own", re.search(r"own glovebox, \d slots, open", info) is not None, "the lid opens the own glovebox"))
    opened = lid(switch(s, "glovebox"))
    results.append(("lid-open", opened == 1.0, "the lid stands open while the glovebox is open (lid %.2f)" % opened))

    run(s, "chassis cabin close")
    time.sleep(1)
    shut = lid(switch(s, "glovebox"))
    results.append(("lid-shut", shut == 0.0, "the lid shuts after the glovebox closes (lid %.2f)" % shut))
    before = count(run(s, "chassis cabin", until=r"glovebox"))
    if before > 0:
        # A glovebox left full by an earlier run: empty one slot, so the put has room.
        run(s, "chassis cabin open", until=r"glovebox")
        run(s, "chassis cabin take", until=r"take")
        run(s, "chassis cabin close")
        before = count(run(s, "chassis cabin", until=r"glovebox"))
    put = run(s, "chassis cabin put", until=r"put")
    run(s, "chassis cabin open", until=r"glovebox")
    kept = count(run(s, "chassis cabin", until=r"glovebox"))
    results.append(("glove-keep", "put:" in put and kept == before + 1, "an item stays in the glovebox (%d -> %d)" % (before, kept)))
    run(s, "chassis cabin close")

    run(s, "chassis cabin mode trunk")
    run(s, "chassis aim glovebox")
    trunk = run(s, "chassis click", until=r"trunk opened|refused")
    results.append(("glove-trunk", "trunk opened" in trunk, "in trunk mode the lid opens the trunk"))
    run(s, "chassis cabin close")
    run(s, "chassis cabin mode own")

    # Switch on On, then out: the empty car goes back to Auto, and the door light runs its 15 s.
    run(s, "chassis aim dome")
    run(s, "chassis click", until=r"click:")
    time.sleep(0.5)
    run(s, "chassis exit")
    time.sleep(2)
    out = light(s)
    results.append(("exit-reset", "(switch Auto" in out, "the switch goes back to Auto when the car is empty"))
    results.append(("exit-door", "cabin light on (switch Auto, door" in out, "getting out lights the cabin"))
    time.sleep(16)
    results.append(("exit-dark", "cabin light off" in light(s), "the door light is out after its 15 s"))

    failed = 0
    for name, ok, what in results:
        print(("PASS " if ok else "FAIL ") + name + ": " + what)
        failed += 0 if ok else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
