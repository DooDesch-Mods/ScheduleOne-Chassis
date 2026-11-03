"""
The Mirrors row in the game's graphics settings: goes to the main menu, opens Settings > Graphics through the event
system's raycast, checks that the Reflections and Mirrors rows are there and that Mirrors shows the current mode, picks
Rear Only from the dropdown, checks the `[Mirror] mode set to Rear Only.` line, sets Rear + Side back the same way and
loads the save again. Prints PASS or FAIL per check.

    python settings_row.py --slot mptest5 --label dev --save 5

Needs a Debug build of Chassis in the slot and a loaded save.
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mirrors import Run  # noqa: E402


def body(answer):
    return (answer.get("result") or answer) if isinstance(answer, dict) else {}


def find(g, query):
    return body(g.world("ui_find", {"query": query}, timeout_ms=8000)).get("elements", [])


def click(r, query, index=0):
    res = body(r.g.world("ui_click", {"query": query, "index": index}, timeout_ms=8000))
    ok = bool(res.get("clicked"))
    r.note("click '%s': clicked=%s hit=%s" % (query, ok, res.get("hit") or res.get("error") or res))
    return ok


def wait_menu(g, timeout):
    end = time.time() + timeout
    while time.time() < end:
        try:
            h = g.health()
            if h.get("scene") not in (None, "Main") and not h.get("inGame") and not h.get("loading"):
                return True
        except Exception as e:  # the bridge restarts its listener while scenes change
            print("INFO  health: %s" % e, flush=True)
        time.sleep(1.0)
    return False


def list_closed(g, timeout=3.0):
    """Waits until the dropdown list has faded out, so a query for the row no longer hits one of its items."""
    end = time.time() + timeout
    while time.time() < end:
        if not find(g, "Dropdown List"):
            return True
        time.sleep(0.25)
    return False


def mirrors_row(g):
    """The Mirrors dropdown itself, not an item of its open list."""
    return [e for e in find(g, "Chassis_Mirrors") if (e.get("path") or "").endswith("Chassis_Mirrors/Dropdown")]


def pick(r, option):
    """Opens the Mirrors dropdown and clicks one of its options."""
    if not list_closed(r.g):
        r.note("dropdown list still open after 3 s")
        return False
    if not click(r, "Chassis_Mirrors"):
        return False
    time.sleep(0.5)
    items = [e for e in find(r.g, option) if "Item" in (e.get("path") or "")]
    r.note("options matching '%s': %s" % (option, [e.get("path") for e in items]))
    if not items:
        return False
    return click(r, items[0]["path"].split("/")[-1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--save", type=int, default=5)
    a = ap.parse_args()
    r = Run(a.slot, a.label + "-settings")
    g = r.g

    start = r.mark()
    r.say("chassis mirror mode 2", "mirror ")
    g.world("return_to_menu", timeout_ms=8000)
    r.check("main menu reached", wait_menu(g, 60), str(g.health()))
    time.sleep(2.0)

    r.check("Settings opens", click(r, "Settings"))
    time.sleep(1.0)
    r.check("Graphics tab opens", click(r, "Graphics"))
    time.sleep(1.0)

    added = r.lines(start, r"\[Mirror\] settings row Mirrors added")
    r.check("Mirrors row added (log)", bool(added), added[-1] if added else "no line")
    refl = find(g, "Chassis_Reflections")
    r.check("Reflections row on screen", bool(refl), refl[0] if refl else "not found")
    row = mirrors_row(g)
    label = (row[0].get("label") or "") if row else ""
    r.check("Mirrors row on screen", bool(row), row[0] if row else "not found")
    r.check("Mirrors row shows Rear + Side", "Rear + Side" in label, label)
    if refl and row:
        r.check("Mirrors row sits below Reflections", row[0]["screen"][1] < refl[0]["screen"][1],
                "reflections y %s, mirrors y %s" % (refl[0]["screen"][1], row[0]["screen"][1]))
    r.shot("settings-row")

    mark = r.mark()
    r.check("Rear Only picked in the dropdown", pick(r, "Rear Only"))
    line = r.wait_line(mark, r"\[Mirror\] mode set to Rear Only\.", 3)
    r.check("mode set to Rear Only logged", line is not None, line or "no line")
    r.check("dropdown list closes", list_closed(g))
    row = mirrors_row(g)
    r.check("Mirrors row shows Rear Only", bool(row) and "Rear Only" in (row[0].get("label") or ""),
            row[0].get("label") if row else "not found")
    r.shot("settings-rear-only")

    mark = r.mark()
    r.check("Rear + Side picked in the dropdown", pick(r, "Rear + Side"))
    line = r.wait_line(mark, r"\[Mirror\] mode set to Rear \+ Side\.", 3)
    r.check("mode set to Rear + Side logged", line is not None, line or "no line")
    r.check("dropdown list closes again", list_closed(g))
    row = mirrors_row(g)
    r.check("Mirrors row shows Rear + Side again", bool(row) and "Rear + Side" in (row[0].get("label") or ""),
            row[0].get("label") if row else "not found")

    errors = r.lines(start, r"\[Chassis\].*(Exception|\[Error\]|ERROR)|Exception.*Chassis|\[Mirror\].*failed")
    r.check("no Chassis exception or mirror error", not errors, errors[0] if errors else "")

    g.world("load_save_by_slot", {"slot": a.save}, timeout_ms=90000)
    ready, last = g.wait_ready(timeout=300, say=lambda s: None)
    r.check("save %d loaded again" % a.save, ready, last)
    return r.summary()


if __name__ == "__main__":
    sys.exit(main())
