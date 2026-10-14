#!/usr/bin/env python3
"""Catalogue the fixes of the community source GeneralsGameCode (docs/GGC-FIXES.md).

Reads references/generals-game-code at the commit scripts/get-references.ps1 records, finds every comment marked
"@bugfix" or "@fix" in the code of Zero Hour (GeneralsMD/) and in the code both games share (Core/), and writes
docs/GGC-FIXES-CATALOGUE.md: one row per marker with its place (file, line, enclosing function), its subsystem, whether
the community source compiles it only when it may change the game's logic CRC (inside "#if !RETAIL_COMPATIBLE_CRC" or
the like), the first line of its comment as the community wrote it, and what this port did with it (REVIEW below).

Only names, places and the one-line description of each fix are written - no code. The output is deterministic: rows in
path and line order, no dates of the run.

Usage: python scripts/ggc-fix-catalogue.py [--check]   (--check: exit 1 when the committed catalogue differs)
Exit code 3: the reference checkout is missing or at another commit (the step lacks its environment; never a pass).
"""
import io
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, 'references', 'generals-game-code')
COMMIT = 'd6837ff7adeb9b40787db49085d96e1fc1180424'
OUTPUT = os.path.join(ROOT, 'docs', 'GGC-FIXES-CATALOGUE.md')
TREES = ('GeneralsMD', 'Core')
MARKER = re.compile(r'@(bugfix|fix)\b')
FUNCTION = re.compile(r'^(?:[A-Za-z_][\w<>,:*&\s]*?\s)?([A-Za-z_]\w*::~?[A-Za-z_]\w*)\s*\(')
GATE = re.compile(r'#\s*(if|ifdef|ifndef|elif)\b.*\b(RETAIL_COMPATIBLE_CRC|RETAIL_COMPATIBLE_XFER_SAVE|PRESERVE_[A-Z_]+)\b')

# What this port did with a fix, by "path#function" (the function as the catalogue names it) - the outcome of a review, written by hand.
# adopted: rewritten as an option of the rule profile (src/CnCNew.Simulation/Rules/RuleProfile.cs); not-applicable: this port has no counterpart
# of the code the fix corrects; equivalent: this port already behaves as the fix makes the original behave.
REVIEW = {
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/PoisonedBehavior.cpp#PoisonedBehavior::startPoisonedEffects':
        'adopted - GameFix.PoisonKillCredit',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/FlammableUpdate.cpp#FlammableUpdate::onDamage':
        'adopted - GameFix.FlameKillCredit',
    **{f'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Collide/CrateCollide/Sabotage{kind}CrateCollide.cpp#Sabotage{kind}CrateCollide::isValidToExecute':
        'adopted - GameFix.NoSabotageOfUnfinishedOrSold'
       for kind in ('CommandCenter', 'InternetCenter', 'MilitaryFactory', 'PowerPlant', 'Superweapon', 'SupplyCenter', 'SupplyDropzone')},
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Collide/CrateCollide/SabotageFakeBuilding.cpp#SabotageFakeBuildingCrateCollide::isValidToExecute':
        'adopted - GameFix.NoSabotageOfUnfinishedOrSold',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Body/ActiveBody.cpp#ActiveBody::internalAddSubdualDamage':
        'not applicable - this port does not simulate subdual damage (a body reports it as no effect, docs/SIMULATION-CORE.md)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Object.cpp#Object::onDisabledEdge':
        'not applicable - this port has no disabled-power path of power plants (energy follows ownership and construction only)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp#GameLogic::tryStartNewGame':
        'equivalent - random colour, position and faction are drawn from the match description\'s own seed (MatchDescription.Resolve)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate.cpp#AIUpdateInterface::xfer':
        'not applicable - this port\'s saves are its own format (state writers per module), not the original\'s xfer',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp#GameLogic::xfer':
        'not applicable - this port\'s saves are its own format, not the original\'s xfer',
}


def subsystem(path):
    p = path.replace('\\', '/')
    if '/GameLogic/AI/AIPathfind' in p:
        return 'pathfinding'
    if '/GameLogic/AI/' in p or '/AIUpdate/' in p or p.endswith('/AIUpdate.cpp'):
        return 'AI'
    if '/GameLogic/ScriptEngine/' in p:
        return 'scripts'
    if '/GameLogic/' in p:
        return 'simulation'
    if '/GameNetwork/' in p:
        return 'network'
    if '/GameClient/GUI/' in p or '/GUI/' in p:
        return 'UI'
    if '/GameClient/' in p or 'W3DDevice' in p or 'WWVegas' in p or '/WW3D' in p:
        return 'rendering'
    if '/Tools/' in p:
        return 'tools'
    return 'engine'


def function_of(lines, index):
    depth_guard = 0
    for i in range(index, -1, -1):
        match = FUNCTION.match(lines[i])
        if match and not lines[i].rstrip().endswith(';'):
            return match.group(1)
        depth_guard += 1
        if depth_guard > 4000:
            break
    return ''


def gated(lines, index):
    for i in range(max(0, index - 8), min(len(lines), index + 4)):
        if GATE.search(lines[i]):
            return True
    return False


def summary(lines, index):
    text = lines[index].strip()
    text = re.sub(r'^(//+|/?\*+)\s*', '', text)
    text = re.sub(r'^\d+:\s*', '', text)
    text = re.sub(r'^TheSuperHackers?\s+', '', text)
    text = MARKER.sub('', text, count=1).strip()
    text = re.sub(r'^[\w\-/]+(\s*/\s*[\w\-]+)*\s+\d{2}/\d{2}/\d{4}:?\s*', '', text)
    text = text.replace('|', '/').strip()
    return text[:140] + ('...' if len(text) > 140 else '')


def author_date(line):
    match = re.search(r'@(?:bugfix|fix)\s+([\w\-/ ]+?)\s+(\d{2})/(\d{2})/(\d{4})', line)
    if not match:
        return '', ''
    return match.group(1).strip(), f'{match.group(4)}-{match.group(3)}-{match.group(2)}'


def collect():
    rows = []
    for tree in TREES:
        for directory, _, files in os.walk(os.path.join(SOURCE, tree)):
            for name in files:
                if not name.lower().endswith(('.cpp', '.h', '.inl')):
                    continue
                full = os.path.join(directory, name)
                relative = os.path.relpath(full, SOURCE).replace('\\', '/')
                with io.open(full, encoding='utf-8', errors='replace') as stream:
                    lines = stream.read().split('\n')
                for index, line in enumerate(lines):
                    if '//' not in line and '*' not in line:
                        continue
                    found = MARKER.search(line)
                    if not found:
                        continue
                    function = function_of(lines, index)
                    author, date = author_date(line)
                    rows.append({
                        'path': relative, 'line': index + 1, 'tag': found.group(1), 'function': function, 'author': author, 'date': date,
                        'subsystem': subsystem(relative), 'gated': gated(lines, index), 'summary': summary(lines, index),
                        'review': REVIEW.get(f'{relative}#{function}', ''),
                    })
    rows.sort(key=lambda row: (row['path'], row['line']))
    return rows


def render(rows):
    out = []
    out.append('# GeneralsGameCode fixes - the generated catalogue')
    out.append('')
    out.append('Generated by `python scripts/ggc-fix-catalogue.py` from `references/generals-game-code` at commit `' + COMMIT + '` (the commit')
    out.append('`scripts/get-references.ps1` fetches). Do not edit by hand: the reading and the decisions are in [GGC-FIXES.md](GGC-FIXES.md), the review column')
    out.append('comes from the table `REVIEW` of the script. One row per comment marked `@bugfix` or `@fix` in the code of Zero Hour (`GeneralsMD/`) and in the')
    out.append('code both games share (`Core/`); the description is the first line of the community\'s comment, shortened.')
    out.append('')
    out.append('**CRC-gated** means the community source compiles the fix only when it may change the game\'s logic CRC (`#if !RETAIL_COMPATIBLE_CRC` and')
    out.append('the like): by its own judgement, a fix that changes the outcome of a match. Ungated fixes are, by the same judgement, free of logic changes')
    out.append('(a crash, a leak, a read of uninitialised memory, rendering, user interface) - unless the community misjudged one.')
    out.append('')
    by = {}
    for row in rows:
        key = (row['subsystem'], row['gated'])
        by[key] = by.get(key, 0) + 1
    subsystems = sorted({row['subsystem'] for row in rows})
    out.append('| Subsystem | Markers | CRC-gated | Reviewed here |')
    out.append('| --- | ---: | ---: | ---: |')
    for name in subsystems:
        total = sum(1 for row in rows if row['subsystem'] == name)
        gated_count = sum(1 for row in rows if row['subsystem'] == name and row['gated'])
        reviewed = sum(1 for row in rows if row['subsystem'] == name and row['review'])
        out.append(f'| {name} | {total} | {gated_count} | {reviewed} |')
    out.append(f'| **all** | **{len(rows)}** | **{sum(1 for row in rows if row["gated"])}** | **{sum(1 for row in rows if row["review"])}** |')
    out.append('')
    for name in subsystems:
        out.append(f'## {name}')
        out.append('')
        out.append('| Place | Function | Tag | Author, date | CRC-gated | Description (community) | This port |')
        out.append('| --- | --- | --- | --- | --- | --- | --- |')
        for row in rows:
            if row['subsystem'] != name:
                continue
            place = row['path'].split('/Source/')[-1].split('/Include/')[-1] + ':' + str(row['line'])
            who = (row['author'] + ' ' + row['date']).strip()
            out.append(f"| `{place}` | `{row['function']}` | {row['tag']} | {who} | {'yes' if row['gated'] else ''} | {row['summary']} | {row['review']} |")
        out.append('')
    return '\n'.join(out)


def main():
    check = '--check' in sys.argv[1:]
    if not os.path.isdir(os.path.join(SOURCE, 'GeneralsMD')):
        print(f'NOT RUN: {SOURCE} is missing - fetch it with pwsh scripts/get-references.ps1 -Only generals-game-code', file=sys.stderr)
        return 3
    head = subprocess.run(['git', '-C', SOURCE, 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
    if head != COMMIT:
        print(f'NOT RUN: {SOURCE} is at {head or "?"}, the catalogue is made from {COMMIT}', file=sys.stderr)
        return 3
    rows = collect()
    text = render(rows) + '\n'
    unmatched = sorted(set(REVIEW) - {f"{row['path']}#{row['function']}" for row in rows})
    if unmatched:
        print('A reviewed place matches no marker (the function is named differently):\n  ' + '\n  '.join(unmatched), file=sys.stderr)
        return 1
    if check:
        with io.open(OUTPUT, encoding='utf-8', newline='') as stream:
            if stream.read() != text:
                print(f'{OUTPUT} differs from what the script makes - run it without --check and commit the result.', file=sys.stderr)
                return 1
        print(f'ok: {len(rows)} markers, the committed catalogue is current')
        return 0
    with io.open(OUTPUT, 'w', encoding='utf-8', newline='') as stream:
        stream.write(text)
    print(f'{len(rows)} markers, {sum(1 for row in rows if row["gated"])} CRC-gated, {sum(1 for row in rows if row["review"])} reviewed -> {OUTPUT}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
