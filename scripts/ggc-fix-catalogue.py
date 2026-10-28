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
        'adopted - GameFix.NoPowerEdgeForUnfinishedPlant (after the fidelity correction F-56, Phase 41)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp#GameLogic::tryStartNewGame':
        'equivalent - random colour, position and faction are drawn from the match description\'s own seed (MatchDescription.Resolve)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate.cpp#AIUpdateInterface::xfer':
        'not applicable - this port\'s saves are its own format (state writers per module), not the original\'s xfer',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp#GameLogic::xfer':
        'not applicable - this port\'s saves are its own format, not the original\'s xfer',
}

# Phase 32: the review of the CRC-gated markers, one entry per place (file:line) - by line, because several markers share a function.
REVIEW_BY_LINE = {
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:4204': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:7187': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:7265': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:7333': 'not applicable - the port has no moveAlliesAwayFromDestination search (allies step to the nearest free place, SimulationWorld.ExitGoals.cs)',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:8368': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:9492': 'adopted - GameFix.ContactWeaponNotViewBlocked (after the fidelity correction F-55, Phase 41)',
    'Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp:11282': 'not applicable - CRC or save bookkeeping of the C++ engine; the port hashes and saves its own state',
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:859': 'not applicable - a guard of the C++ game flow against a new-game message during a game; the port starts a match from its description',
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:962': "equivalent - a rally point is only set by the producer's owner (SimulationWorld.Construction.cs, MayCommand)",
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:1142': "equivalent - a special power is only used by its source's owner (SimulationWorld.cs, UseSpecialPowerCommand check)",
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:1202': 'equivalent - as for the special power at a location (same check)',
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:1258': 'equivalent - as for the special power at an object (same check)',
    'Core/GameEngine/Source/GameLogic/System/GameLogicDispatch.cpp:1668': 'not applicable - the port has no command that overrides a delivery destination',
    'Core/GameEngine/Source/GameNetwork/Network.cpp:587': "not applicable - network message validation; the port's relay protocol admits only player orders (CommandTypes.IsPlayerOrder)",
    'Core/GameEngineDevice/Source/W3DDevice/GameClient/W3DView.cpp:743': 'not applicable - camera (presentation)',
    'GeneralsMD/Code/GameEngine/Source/Common/NameKeyGenerator.cpp:128': 'not applicable - CRC or save bookkeeping of the C++ engine; the port hashes and saves its own state',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/ActionManager.cpp:462': 'equivalent - resuming construction needs the same owner (SimulationWorld.Construction.cs, CheckResume)',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/ActionManager.cpp:493': 'not applicable - the port lets any own builder resume at once (no wait for the dead builder); a fidelity gap of its own, not this defect',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/Energy.cpp:212': 'not applicable - the port has no power loss of disabled power plants, so no double removal; a fidelity gap of its own',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp:720': 'adopted - GameFix.TunnelHealOncePerPlayer',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp:2139': 'not applicable - upgrades are bought and completed at once in the port; there is no upgrade in progress to transfer',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp:2471': 'adopted - GameFix.BountyRounding',
    'GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp:3688': 'adopted - GameFix.BattlePlanAggregate',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIGroup.cpp:2179': 'not applicable - the port has no group attack that orders garrisoned occupants',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIGuard.cpp:186': 'adopted - GameFix.GuardAnswersOnlyWhenIdle (after the fidelity correction F-58, Phase 41)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIGuardRetaliate.cpp:190': 'adopted - GameFix.GuardAnswersOnlyWhenIdle (after the fidelity correction F-58, Phase 41)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIPlayer.cpp:125': "equivalent - the AI player's built structure keeps its health (ScriptEngine.AiPlayer.cs)",
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIPlayer.cpp:468': 'equivalent - the AI player spawns a structure at full health (ScriptEngine.AiPlayer.cs)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIStates.cpp:2693': "rejected - the port has no approach state that fails on stealth: every later attack order is checked against the player's sight (CanPlayerSee) and a lost target is dropped (IsAttackable), so the fix is no switch here; recorded as fidelity debt F-40",
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIStates.cpp:6379': 'adopted - GameFix.EnterNeedsVerticalOverlap (after the fidelity correction F-38, Phase 34)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIStates.cpp:6498': 'not applicable - an exit is immediate in the port; there is no exit state that retries on a busy door',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/BunkerBusterBehavior.cpp:169': 'adopted - GameFix.BunkerBusterOnlyWhenKillingSelf',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/InstantDeathBehavior.cpp:163': 'adopted - GameFix.NoDeathWeaponFromScaffold',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/InstantDeathBehavior.cpp:183': 'equivalent - every InstantDeathBehavior of an object runs in the port (no dead-state guard)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/PropagandaTowerBehavior.cpp:212': 'adopted - GameFix.PropagandaTowerEnclosingContainer',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Body/UndeadBody.cpp:87': 'adopted - GameFix.UndeadLethalByArmour (after the fidelity correction F-48, Phase 41)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Collide/CrateCollide/CrateCollide.cpp:170': 'equivalent - a collision stops on a destroyed crate (SimulationWorld.Collision.cs)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:181': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:305': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:385': 'not applicable - garrison points are not modelled',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:449': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:1554': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp:1676': 'adopted - GameFix.DoorExitToRally (after the fidelity correction F-34, Phase 34)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/RiderChangeContain.cpp:250': 'adopted - GameFix.BikeTrainableFromRider (after the fidelity correction F-39, Phase 34)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp:477': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp:582': 'adopted - GameFix.NoExitFromContainedTransport',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp:675': 'not applicable - the port never ejects passengers on capture; a fidelity gap of its own (the original ejects on a non-allied capture)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Damage/TransitionDamageFX.cpp:391': 'not applicable - decouples particle effects from the logic CRC; the port draws no logic random number for effects',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Object.cpp:3137': 'adopted - GameFix.NoVeterancyEffectsWhenDead',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/PartitionManager.cpp:2128': 'not applicable - collisions use exact footprints, not a coarse fill of partition cells',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate.cpp:3560': 'not applicable - the port has no attack-position command',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate.cpp:3838': 'not applicable - the container to leave is always the one holding the rider',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate.cpp:3876': 'not applicable - as above',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1088': 'not applicable - healing of a Chinook at an airfield is not simulated',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1099': 'not applicable - as above',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1294': 'not applicable - as above',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1326': 'equivalent - entering oneself is refused in the pre-check (SimulationWorld.cs, CheckMoveAndEnter)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1330': 'equivalent - an enter order the container cannot take is refused in the pre-check',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/ChinookAIUpdate.cpp:1345': 'equivalent - the port lands the Chinook and lets riders out one by one through the door',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/DeployStyleAIUpdate.cpp:146': 'equivalent - deploying starts only from a fire attempt that passed the range check',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/DozerAIUpdate.cpp:2358': 'equivalent - a builder keeps its task over a disable in the port',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/HackInternetAIUpdate.cpp:132': 'not applicable - the port has no hack command for a unit that is already hacking',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/JetAIUpdate.cpp:1467': 'adopted - GameFix.TakeoffWaitsForOneJet (CRC-gated by the #if around the whole update, which this scan does not see; Phase 41)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/JetAIUpdate.cpp:2026': 'adopted - GameFix.HealedHelicopterToRally',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/JetAIUpdate.cpp:2150': 'equivalent - the attack locomotor ends with its timer (JetAIUpdate.cs)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/MissileAIUpdate.cpp:557': 'not applicable - the port has no diving-missile distance logic',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/WorkerAIUpdate.cpp:981': 'equivalent - a worker keeps its task over a disable',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/BattlePlanUpdate.cpp:255': 'adopted - GameFix.BattlePlanFollowsCapture',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/BattlePlanUpdate.cpp:380': 'not applicable - the bombardment turret is not enabled by the plan in the port',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/DockUpdate/SupplyCenterDockUpdate.cpp:82': 'adopted - GameFix.ProportionalSupplyBoost',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/EMPUpdate.cpp:308': 'not applicable - decouples particle effects from the logic CRC; the port draws no logic random number for effects',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/HordeUpdate.cpp:84': 'equivalent - dead units are not horde members (Phase12Modules.cs)',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/MissileLauncherBuildingUpdate.cpp:205': 'not applicable - a defect of the C++ code itself (memory, an uninitialised value, a crash guard); the port is managed code without it',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/NeutronMissileSlowDeathUpdate.cpp:366': 'adopted - GameFix.NeutronBlastBoundingSphere',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/OCLUpdate.cpp:129': 'not applicable - the port has no disabled-timer shift of OCLUpdate',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/PhysicsUpdate.cpp:1382': 'not applicable - collision damage of physics is not simulated',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/PhysicsUpdate.cpp:1411': 'not applicable - as above',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/ProductionUpdate.cpp:1091': 'not applicable - one unit per production entry; there are no batches',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp:1404': 'not applicable - decouples particle effects from the logic CRC; the port draws no logic random number for effects',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/SpectreGunshipUpdate.cpp:619': 'not applicable - decouples particle effects from the logic CRC; the port draws no logic random number for effects',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/StickyBombUpdate.cpp:290': 'adopted - GameFix.BoobyTrapClearedOnRemoval',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Weapon.cpp:611': 'not applicable - the port has no sniper-against-empty-structure check; a fidelity gap of its own',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp:3813': 'not applicable - the port has no disabled mask and no central update gate: each module checks the disable kinds it knows and stops on any of them, which is what the fix makes the original do',
    'GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp:3861': 'not applicable - as above',
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
                        'review': REVIEW_BY_LINE.get(f'{relative}:{index + 1}') or REVIEW.get(f'{relative}#{function}', ''),
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
    unmatched = sorted((set(REVIEW) - {f"{row['path']}#{row['function']}" for row in rows}) | (set(REVIEW_BY_LINE) - {f"{row['path']}:{row['line']}" for row in rows}))
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
