"""Carries an acceptance report into the repository: what a person accepted is not asked again.

python scripts/acceptance-accept.py <report.md> [--reopen ID[,ID...]] [--machine ID=why ...]

Reads the tables "Passed" of a report of scripts/acceptance.ps1 and merges them into
scripts/acceptance/accepted.json. --reopen takes goals out again, because what they judge has
changed since. --machine records a goal a program has proved, with what proved it. A goal never
leaves accepted.json by itself: reopening is a decision of whoever changed what it judges.
"""
import argparse
import json
import re
from pathlib import Path

ACCEPTED = Path(__file__).parent / "acceptance" / "accepted.json"


def passed_ids(report: str) -> list[str]:
    section = re.search(r"^## Passed\n(.*?)(?=^## |\Z)", report, re.S | re.M)
    if section is None:
        raise SystemExit("The report has no section 'Passed'.")
    return re.findall(r"^\| ([A-Z]+-\d+) \|", section.group(1), re.M)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", nargs="?")
    parser.add_argument("--reopen", default="")
    parser.add_argument("--machine", action="append", default=[])
    arguments = parser.parse_args()

    data = json.loads(ACCEPTED.read_text(encoding="utf-8")) if ACCEPTED.exists() else {"accepted": [], "byMachine": {}}
    accepted = set(data["accepted"])
    if arguments.report:
        accepted |= set(passed_ids(Path(arguments.report).read_text(encoding="utf-8")))
    reopened = {name for name in arguments.reopen.split(",") if name}
    accepted -= reopened
    machine = {name: why for name, why in data["byMachine"].items() if name not in reopened}
    for entry in arguments.machine:
        name, why = entry.split("=", 1)
        machine[name] = why
    data["accepted"] = sorted(accepted)
    data["byMachine"] = dict(sorted(machine.items()))
    ACCEPTED.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{len(data['accepted'])} accepted by a person, {len(data['byMachine'])} proved by a program")


if __name__ == "__main__":
    main()
