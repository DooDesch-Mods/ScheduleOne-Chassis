"""
Runs mirrors.py once per vanilla car in one game and prints a PASS/FAIL line per car plus the failed checks.

    python mirrors_all.py --slot mptest5 --label dev
    python mirrors_all.py --slot mptest5 --label dev --vehicles bruiser,veeper

Each car's full output goes to out/<label>-<code>.txt.
"""
import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
# BaseInterior.VanillaCodes
CODES = ["shitbox", "shitbox_police", "cheetah", "dinkler", "hounddog", "bruiser", "bruiser_police", "veeper", "hotbox"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--slot", required=True)
    ap.add_argument("--label", default="run")
    ap.add_argument("--vehicles", default=",".join(CODES))
    ap.add_argument("--seconds", type=float, default=3.0)
    a = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    os.makedirs(OUT, exist_ok=True)
    total_fail = 0
    for code in [c.strip() for c in a.vehicles.split(",") if c.strip()]:
        cmd = [sys.executable, os.path.join(HERE, "mirrors.py"), "--slot", a.slot, "--label", a.label,
               "--vehicle", code, "--seconds", str(a.seconds)]
        done = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        text = done.stdout + ("\n[stderr]\n" + done.stderr if done.stderr.strip() else "")
        with open(os.path.join(OUT, "%s-%s.txt" % (a.label, code)), "w", encoding="utf-8") as handle:
            handle.write(text)
        fails = [l for l in done.stdout.splitlines() if l.startswith("FAIL")]
        passes = sum(1 for l in done.stdout.splitlines() if l.startswith("PASS"))
        verdict = "PASS" if done.returncode == 0 and not fails else "FAIL"
        if verdict == "FAIL":
            total_fail += 1
        print("%s  %s  %d PASS, %d FAIL%s" % (verdict, code, passes, len(fails),
                                             "" if done.returncode in (0, 1) else "  exit %d" % done.returncode), flush=True)
        seen = set()
        for line in fails:
            if line not in seen:
                seen.add(line)
                print("      " + line, flush=True)
        if done.returncode not in (0, 1):
            print("      " + (done.stderr.strip().splitlines() or ["no stderr"])[-1], flush=True)
    print("\n===== %d car(s) with FAIL" % total_fail)
    return 1 if total_fail else 0


if __name__ == "__main__":
    sys.exit(main())
