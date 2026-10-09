# 30-Hz-Fehler: die verlorene Weckbitte des langsamen Todes

Stand 8. Oktober 2026, gemessen am Stand `71e41d7` (Korrektur) über `37b36b8` (`main`). Dritte Messrunde der Phase 23, Fall `timed` der
Raten-Äquivalenz-Suite (`docs/rate-invariance/RATE-INVARIANCE-TIMING.md` Abschnitt 5, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/TICKRATE-MIGRATION.md` §15.4).
Die Korrektur ändert das Verhalten bei 30 Hz und ist nach der Entscheidung des Projektinhabers vom 8. Oktober 2026 eine Ursache in einem eigenen Commit;
das Referenzmanifest ist in diesem Zweig **nicht** neu geschrieben.

## 1. Befund

`timed/lifetime CINE_BShipWeaponDetonationObject`, Größe „creation to end“: – / 0,0667 / 0,0667 s bei 30 / 60 / 120 Hz. Bei 30 Hz verlässt das Objekt die
Welt nie. Die Raten unterscheiden sich, weil 30 Hz falsch ist.

## 2. Mechanismus

Das Objekt (`Data/INI/Object/AmericaMiscUnit.ini`) hat `LifetimeUpdate` mit 1 ms (ein Bild) und `SlowDeathBehavior` mit `DestructionDelay = 1` (ein Bild).
Ablauf bei 30 Hz, T = Tick, in dem das Objekt entsteht:

1. `SimulationWorld.AttachModules` (`src/CnCNew.Simulation/World/SimulationWorld.cs:1750`) weckt jedes Update-Modul, das keinen eigenen Wunsch gesetzt hat,
   für T + 1. Das `SlowDeathBehavior` hat keinen (`_nextWake = -1`), es ist also in T + 1 fällig. Das `LifetimeUpdate` hat sich in `OnSpawn` selbst für T + 1
   (ein Bild) gestellt.
2. Tick T + 1, `RunUpdates` (`SimulationWorld.cs:1348`): beide Module desselben Objekts sind fällig; die Reihenfolge ist Weckzeit, Objekt-ID, Modulindex
   (`CompareUpdates`), also zuerst `LifetimeUpdate` (Modul 2), dann `SlowDeathBehavior` (Modul 3).
3. `LifetimeUpdate` → `world.Kill` → `OnEntityDied` → `SlowDeathBehavior.OnDie` → `SlowDeathLottery.Run` → `BeginSlowDeath`
   (`src/CnCNew.Simulation/Modules/BuiltIn/Phase5BlockerModules.cs:288`): `_destructionTick = T + 2`, `world.WakeAt(Entity, this, T + 2)`.
4. `SimulationWorld.WakeAt` (`SimulationWorld.cs:1373`), Zeile 1382: das Modul ist im laufenden Tick noch fällig (`module.NextWake == _tick`) und die Bitte
   lautet „nächster Tick“ (`requested == _tick + SleepNone`) – die Bitte wird verworfen („already awake“).
5. Noch in T + 1 läuft das Update des `SlowDeathBehavior`. Vor der Korrektur: Frist nicht erreicht, Rückgabe `SleepForever`. `RunUpdates` setzt `NextWake`
   auf „nie“ und nimmt das Modul aus dem Heap. Niemand weckt es wieder: das tote Objekt bleibt in der Welt.

Bei 60 und 120 Hz ist die Frist T + 1 + 2 bzw. T + 1 + 4, also nicht „der nächste Tick“; `WakeAt` verschiebt das Modul auf die Frist, das Objekt endet
zwei Bilder nach dem Entstehen (0,0667 s).

Dieselbe Lücke ohne Lebensdauer: ein Objekt wird in dem Tick nach seinem Erscheinen getötet, bevor sein `SlowDeathBehavior` zum ersten (und einzigen) Mal
gelaufen ist – durch einen Befehl (`ExecuteCommands` liegt vor `RunUpdates`), durch einen Welteingriff zwischen zwei Ticks (`_tick` zeigt dann schon auf den
Tick, in dem das Modul fällig ist) oder durch das Update eines Objekts mit kleinerer ID – und der gezogene `DestructionDelay` ist höchstens ein Bild
(`AtLeastOneFrame`: auch ein Wert von 0). Nach dem ersten Update schläft ein `SlowDeathBehavior` für immer, dann greift jede Weckbitte.

`BattleBusSlowDeathBehavior` trug für den echten Tod eine Kopie desselben Updates (`VehicleModules.cs`, Zweig `_realDeath`). Dort ist das Modul nicht nur im
ersten Tick fällig, sondern in jedem Tick des Wurfs (erster Tod) und alle 15 Bilder beim Blick auf die leere Hülle.

## 3. Das Original

- `GameLogic::friend_awakenUpdateModule` (`GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp`): dieselben drei Sonderfälle wie `WakeAt`, in
  derselben Reihenfolge – ein Aufruf aus dem eigenen `update()` wird ignoriert („it will be ignored, in favor of the return code from update“), gleiche Zeit ist
  nichts zu tun, und „if we already awake, and someone calls setWakeFrame(self, UPDATE_SLEEP_NONE), we don't want to reset our wake frame, since that would
  prevent us from getting called THIS frame“. Der Vertrag: ein Modul, das in diesem Bild noch läuft, entscheidet mit dem Rückgabewert seines `update()`
  (`GameLogic::update`, Schleife über `m_sleepyUpdates`: `u->friend_setNextCallFrame(now + sleepLen)`).
- `SlowDeathBehavior::SlowDeathBehavior` (`…/Object/Behavior/SlowDeathBehavior.cpp`): `setWakeFrame(getObject(), UPDATE_SLEEP_FOREVER)` – das Modul schläft
  ab dem Konstruktor und ist im ersten Bild des Objekts **nicht** fällig.
- `SlowDeathBehavior::beginSlowDeath`: `m_destructionFrame = d->m_destructionDelay + GameLogicRandomValue(0, variance)`, dann
  `setWakeFrame(obj, UPDATE_SLEEP(whenToWakeTime))` mit dem kleinsten der drei Zähler (Sinken, Mitte, Zerstörung), danach `m_destructionFrame += now`.
- `SlowDeathBehavior::update`: gibt bis zur Zerstörung in jedem Zweig `UPDATE_SLEEP_NONE` zurück, läuft also ab dem Beginn des langsamen Todes jedes Bild;
  `if (now >= m_destructionFrame) { doPhaseStuff(SDPHASE_FINAL); TheGameLogic->destroyObject(obj); }`.
- `LifetimeUpdate::LifetimeUpdate` / `update` (`…/Object/Update/LifetimeUpdate.cpp`): `setWakeFrame(UPDATE_SLEEP(delay))` mit `delay >= 1`, dann
  `getObject()->kill()`.
- `BattleBusSlowDeathBehavior::update` (`…/Object/Behavior/BattleBusSlowDeathBehavior.cpp`): „If a real death, we aren't allowed to do anything“ –
  `return SlowDeathBehavior::update();`.

Im Original entsteht die Lücke also nicht: das Modul schläft bis `beginSlowDeath`, wird dort geweckt und bleibt bis zur Zerstörung wach. Für das Objekt des
Befunds: Tod ein Bild nach dem Entstehen, Zerstörung ein Bild später – zwei Bilder, 0,0667 s, der Wert der Suite bei 60 und 120 Hz.

## 4. Ursache und Korrektur

Der Scheduler ist nicht die Ursache. `WakeAt` ist Zeile für Zeile der des Originals, und dessen Vertrag („das Update dieses Bildes entscheidet“) gilt für
alle Module. Die Ursache liegt im Modul: es modelliert vom langsamen Tod nur den Zerstörungszeitpunkt, schläft deshalb bis dahin (das Original bleibt wach),
hielt die Frist aber **nur** als Weckbitte und antwortete aus einem Update vor der Frist mit „für immer“. Dazu kommt, dass es – anders als im Original – im
ersten Tick des Objekts überhaupt fällig ist.

Korrektur (Commit `71e41d7`, eine Ursache): das Update gibt den Rest bis zur Frist zurück.

```csharp
internal static class SlowDeathTimer
{
    public static long Update(IWorldServices world, Entity entity, bool activated, long destructionTick)
    {
        if (!activated) return SimulationTiming.SleepForever;
        if (world.Tick < destructionTick) return destructionTick - world.Tick;
        world.RequestDestroy(entity.Id);
        return SimulationTiming.SleepForever;
    }
}
```

`SlowDeathBehavior.Update` und der Zweig des echten Todes in `BattleBusSlowDeathBehavior.Update` rufen sie auf (im Original ist das eine Funktion). Ein Update
vor der Frist gab es bisher nur im verlorenen Fall; alle anderen Abläufe sind unverändert (gemessen: Abschnitt 7).

**Verworfen: eine allgemeine Änderung an `WakeAt`** (die verworfene Bitte merken und nach dem Update als Obergrenze anwenden, oder die Bitte immer
annehmen). Gründe: (a) sie wiche vom Scheduler des Originals ab, und zwar für alle Module – „schon wach“ schützt gerade den Aufruf in diesem Tick; (b) sie
änderte `nextWake` (Teil des Zustands-Hashes) überall, wo heute eine Bitte verworfen wird, also das Verhalten und alle Referenzwerte bei 30 Hz ohne
benannten Fehler; (c) die Durchsicht aller Aufrufer und die Messung (Abschnitt 5) finden außer dem langsamen Tod keinen Verlust. Ebenfalls verworfen:
das Modul wie im Original ab dem Entstehen schlafen zu lassen – das behebt den ersten Tick, nicht den Bus, und ändert den Zustand jedes Objekts mit
langsamem Tod in seinem ersten Tick (alle Hashes) ohne Verhaltensgewinn.

## 5. Alle Aufrufer von `WakeAt`

Eine Bitte kann nur verloren gehen, wenn (1) das Zielmodul im laufenden Tick fällig und noch nicht gelaufen ist, (2) die Bitte genau „nächster Tick“ lautet
und (3) das Update des Moduls in diesem Tick eine längere Pause zurückgibt, ohne das Erbetene getan zu haben. 68 Aufrufe im Quelltext (64 in Modulen, 4 in
der Welt):

| Art der Bitte | Aufrufer | Verlust möglich? |
|---|---|---|
| `world.Tick` („jetzt“) | `AIUpdateInterface` (`OrderMove`, `OrderIdle`, `OrderDock`, `OrderEvacuate`, `StartTask`), `HackInternetAIUpdate` (`StartHacking`, `OrderMove`), `DockUpdateBase` (2), `GarrisonContain`, `HealContain`, `HelixContain`, `OverlordContain`, `TunnelContain`, `DeliverPayloadAIUpdate`, `SupplyCarrierAI`; Welt: Produktion (`SimulationWorld.cs:1020`), Bauabschluss (`SimulationWorld.Construction.cs:274`) | nein – „jetzt“ fällt nie unter „schon wach“; ein schon gelaufenes Modul läuft im selben Tick noch einmal |
| `SleepForever` (schlafen legen) | `BaseRegenerateUpdate`, `AutoHealBehavior.StopHealing`, `PoisonedBehavior.OnHealing`, `ToppleUpdate`, `StealthDetectorUpdate.SetEnabled(false)`, `OverchargeBehavior.Enable(false)`, `SpyVisionUpdate` (Dauer 0) | nein |
| `Min(NextWake, tick + Turmbild)` | Welt: Zielerwerb (`SimulationWorld.Acquisition.cs:221`, `:317`) | nein – läuft nach `RunUpdates`; ist das Modul fällig, ist die Bitte gleich seiner Weckzeit |
| „nächster Tick“ nach einem Zustandswechsel, den das Update sofort liest | `NeutronMissileUpdate`, `SpectreGunshipUpdate`, `BridgeBehavior`, `BridgeScaffoldBehavior`, `CleanupHazardUpdate`, `AssistedTargetingUpdate`, `RiderChangeContain`, `HijackerUpdate`, `JetSlowDeathBehavior`, `HelicopterSlowDeathBehavior`, `NeutronMissileSlowDeathBehavior`, `BattleBusSlowDeathBehavior` (erster Tod), `CountermeasuresBehavior`, `ParticleUplinkCannonUpdate`, `HackInternetAIUpdate.DisableUntil`, `AIUpdateInterface.ParalyzeUntil`, `DemoTrapUpdate`, `FireWeaponPower`, `FireWeaponWhenDamagedBehavior`, `FlammableUpdate`, `StealthDetectorUpdate.SetEnabled(true)`, `StealthUpdate`, `PhysicsBehavior`, `DumbProjectileBehavior`, `MissileAIUpdate`, `OverchargeBehavior.Enable(true)`, `SlavedUpdate`, `SpecialAbilityUpdate`, `StickyBombUpdate`, `ProjectileStreamUpdate`, `StructureCollapseUpdate`, `StructureToppleUpdate`, `DeployStyleAIUpdate` (2), `ToppleUpdate`, `WaveGuideUpdate`, `SpyVisionUpdate.SetDisabledUntilFrame` (Weckruf) | nein – wird die Bitte verworfen, läuft das Update in diesem Tick, sieht den neuen Zustand und bleibt mit seinem Rückgabewert wach (einen Tick früher als erbeten: „so bald wie möglich“, wie im Original) |
| Frist, die genau ein Tick sein kann; das Update rechnet seine Pause aus dem Zustand | `PoisonedBehavior.OnDamage` (`CalcSleepTime`), `FlammableUpdate` (`CalcSleep`) | nein |
| Frist, die genau ein Tick sein kann; das Update handelt beim Aufruf | `SupplyWarehouseCripplingBehavior.OnDamage`, `BaseRegenerateUpdate.OnDamage`, `AutoHealBehavior.TryActivate`, `FireSpreadUpdate.StartFireSpreading` | kein Verlust; bei einer Frist von einem Bild und einem im selben Tick fälligen Modul wirkt es einen Tick früher (wie im Original, dort dieselbe Bauart) |
| Frist, die genau ein Tick sein kann; das Update vor der Frist antwortete „für immer“ | **`SlowDeathBehavior.BeginSlowDeath`**, **`BattleBusSlowDeathBehavior.BeginSlowDeath` (echter Tod)** | **ja – der Fehler; behoben** |
| dieselbe Bauart, im Original genauso | `SpyVisionUpdate.ActivateSpyVision` (ohne `SelfPowered`: das Update vor `m_deactivateFrame` gibt `UPDATE_SLEEP_FOREVER` zurück), `SpyVisionUpdate.SetDisabledUntilFrame` mit einem Bild | theoretisch, mit einer Dauer von genau einem Bild und einem im selben Tick fälligen Modul; die Daten kennen nur 20.000 und 240.000 ms. Originalgetreu, nicht geändert |

Gegenmessung statt Durchsicht: ein Messfühler im Scheduler (nur für diese Messung gebaut, nicht im Repo) zählte über das volle Referenzmanifest (50 Läufe) jede verworfene Bitte, nach der das Update desselben Ticks länger als einen Tick schlafen wollte. Nach der Korrektur: **kein einziges
Vorkommen**, bei keinem Modultyp. Der zweite Zähler – Updates des langsamen Todes vor der Frist, also genau die früher verlorenen Fälle – steht in Abschnitt 7.

## 6. Betroffene Objekte in den Retail-Daten

Zensus (`Tests.RealLostWakeCensus`, alle instanziierbaren Objekte mit `SlowDeathBehavior` oder `BattleBusSlowDeathBehavior`, je Objekt eine eigene kleine
Welt): 616 Objekte, davon 230 mit einem `DestructionDelay` von höchstens einem Bild in mindestens einem Modul. „Tot und nie entfernt“:

| Staging | 30 Hz vorher | 30 Hz nachher | 60 / 120 Hz nachher |
|---|---|---|---|
| sich selbst überlassen (10 s, dann längste Frist + 5 s) | 6 | 0 | 0 |
| im Tick nach dem Erscheinen getötet | 73 | 0 | 0 |
| dort und im nächsten Tick noch einmal getötet | 73 (dieselben) | 0 | 0 |

Vorher ist der Zensus nur bei 30 Hz gemessen (der Test bricht an der ersten Rate mit Befund ab).

- **Von selbst** (Lebensdauer von einem Bild): `CINE_BShipWeaponDetonationObject`, `CINE_CruiseMissileDetObject`, `CINE_DemoTrapDetonationObject`,
  `CINE_MiniNukeExplosion`, `CINE_SandExplosionDetonationObject`, `DemoTrapDetonationObject`. Keines wird von einer Waffe oder Erzeugungsliste der INI-Daten
  genannt; die `CINE_`-Objekte erzeugen die Kampagnenskripte (Abschnitt 7).
- **Im ersten Tick getötet** (73, die sechs eingeschlossen): Humvee, Sanitäter, POW-Truck, Truppentransporter, Kampfrad, Raketenbuggy, Radarwagen jeweils
  mit ihren General-Varianten, die fünf Schein-Gebäude der GLA mit Varianten, `ConvoyTruckArmedWithNuke`, `RemoteC4Charge`, `TimedC4Charge`,
  `TNTStickyBomb` – alle mit `DestructionDelay` 0 oder einem Bild. Im Spiel verlangt das einen Treffer genau im Tick nach dem Erscheinen; in den 50 Läufen
  des Manifests kommt es nicht vor.
- **Battle Bus:** in den Retail-Daten (kein `DestructionDelay`, also ein Bild) vom Zensus nicht getroffen; der Fall ist auf der synthetischen Fixture
  nachgestellt (`LostWakeABattleBusKilledWhileItLooksAtItsHulkEnds`), ein Vorkommen im Spiel ist nicht belegt.

## 7. Was sich bei 30 Hz ändert

`SimTool reference-manifest check … --set full --jobs 5` am Stand der Korrektur: **3 von 50 Läufen weichen ab**, 47 sind identisch (Referenzmatch,
Stresstest, alle 16 KI-Läufe, die drei 8-Spieler-Läufe, die Skirmish-Übersicht und 24 von 27 Missionsläufen):

| Lauf | Kettenhash Manifest | Kettenhash nachher (die erste gemeldete Abweichung je Lauf) |
|---|---|---|
| `mission_MD_GLA01` | `E764DA1050962851` | `0121F74708E56C61` |
| `mission_MD_USA02` | `2993A73F066BEDDE` | `CE9CE37430A7C4AD` |
| `mission_MD_USA05` | `628D7208A23B10AB` | `B944F0ECC6A49186` |

Ergebnisgrößen (`reference-manifest results`, Binärdateien von `main` gegen die der Korrektur, alle 50 Läufe): 11 Werte verschieden, alle in diesen drei
Läufen, alle von derselben Art – mehr zerstörte, weniger verbliebene Objekte:

| Lauf | `EntityDestroyed` | Objekte am Ende | je Spieler |
|---|---|---|---|
| `mission_MD_GLA01` | 374 → 381 | 1.093 → 1.086 | Spieler 1: 42 → 36, Spieler 9: 11 → 10 |
| `mission_MD_USA02` | 138 → 148 | 1.780 → 1.770 | Spieler 14: 10 → 0 |
| `mission_MD_USA05` | 68 → 79 | 1.869 → 1.858 | Spieler 1: 1.092 → 1.084, Spieler 5: 156 → 153 |

Ausgang, Endskript, ausgeführte Skripte und Aktionen, Schusszahlen, alle anderen Ereigniszahlen und die Befehlsströme (22 Dateien, bytegleich) sind
gleich. Der Messfühler nennt die Objekte (84 Updates vor der Frist gezählt; `MissionJob` rechnet die erste Hälfte jeder Missionswelt dreimal): in den drei
Missionsläufen zusammen 28 Zwischensequenz-Objekte – 11
`CINE_CruiseMissileDetObject`, 9 `CINE_DemoTrapDetonationObject`, 4 `CINE_BShipWeaponDetonationObject`, 4 `CINE_MiniNukeExplosion` –, die bisher tot in der
Welt blieben und jetzt zwei Bilder nach dem Entstehen entfernt werden (7 + 10 + 11 = 28).

Das Referenzmanifest ist damit in genau diesen drei Läufen neu zu schreiben (eigener Commit des Projektinhabers). Das Eingangsmanifest ist identisch.

Zielfahrer (`CampaignTool drive`, 24 Karten, 30 Hz, Binärdateien von `main` gegen die der Korrektur): Sieg 24/24 und Niederlage 24/24 vorher wie nachher,
auf jeder Karte dasselbe Endskript beim selben Tick (Abweichung 0,00 s).

## 8. Messungen

| Messung | vorher (`37b36b8`) | nachher (`71e41d7`) |
|---|---|---|
| Suite, Fall `CINE_BShipWeaponDetonationObject`, „creation to end“ | – / 0,0667 / 0,0667 s | 0,0667 / 0,0667 / 0,0667 s |
| Suite gesamt außerhalb der Toleranz (60 / 120 Hz), 4.307 Größen | 35 / 33 | 34 / 32 |
| … `locomotion`, `flight`, `projectiles`, `turrets` | 2/2, 0/3, 4/4, 4/0 | 2/2, 0/3, 4/4, 4/0 |
| … `timed` | 1 / 1 | **0 / 0** |
| … `ai` | 24 / 23 | 24 / 23 |
| … `weapons`, `overtime`, `economy`, `scripts` | 0 / 0 | 0 / 0 |
| Berichtsdatei der Suite (4.307 Zeilen) | – | genau eine Zeile anders (die des Befunds); alle Werte bei 30, 60 und 120 Hz sonst gleich, auch die der KI-Matches |
| Tests `Lost wake: …` (synthetische Fixture, 30/60/120 Hz) | 3 von 3 scheitern bei 30 Hz („never left the world“) | bestehen |
| Zensus der Retail-Daten | 6 / 73 / 73 bei 30 Hz | 0 / 0 / 0 bei 30, 60 und 120 Hz |
| Simulationstests ohne Inhalt, 30 Hz, `--tick-rate 60`, `--tick-rate 120` | nicht neu gemessen (am Stand `3b44506` grün) | grün |
| Simulationstests mit Inhalt, 30 Hz | – | 520 bestehen, 5 scheitern: die fünf Gruppen der Suite oben |
| Simulationstests mit Inhalt, `--tick-rate 60` / `120` | drei bzw. zwei Realdatentests scheitern (Referenzmatch ohne Sieger bei 60 Hz, Crusader-Turm, Original-Skirmish-KI) | dieselben mit denselben Meldungen, sonst nur die fünf Gruppen der Suite |
| `dotnet build CnCNew.slnx` | 0 Warnungen | 0 Warnungen |
| `input-manifest check` | – | identisch |
| `reference-manifest check --set full` | 50 von 50 | 47 von 50 (Abschnitt 7) |
| `reference-manifest check --set quick --verify-incremental` | – | 63.386 Ticks geprüft, 0 Abweichungen zwischen inkrementellem und vollem Hash |

## 9. Nicht geprüft

- Der `FINAL`-Effekt des langsamen Todes (`Weapon = FINAL …`, `OCL`, `FX`) ist in der Simulation nicht modelliert; die Korrektur entfernt die Objekte, die
  Waffe des Objekts aus dem Befund (`CINE_u02_BattleshipTargetDamagedWeapon`) feuert weiterhin nicht.
- Welche Skripte in `MD_GLA01`, `MD_USA02` und `MD_USA05` die 28 Objekte erzeugen, ist nicht zurückverfolgt; die Aufteilung der vier Typen auf die drei
  Karten ist nicht einzeln erhoben (nur die Summen je Typ und je Karte).
- Ob eine Karte die Objekte `CINE_SandExplosionDetonationObject` und `DemoTrapDetonationObject` erzeugt, ist nicht geprüft.
- Der Battle Bus ist nur auf der synthetischen Fixture nachgestellt; warum das Staging des Zensus den Bus der Retail-Daten nicht trifft, ist nicht untersucht.
- Die Determinismus-Matrix (Linux, Kulturen, JIT-Schalter) und das Audit der Kampagne (`CampaignTool audit`) sind für diesen Commit nicht gelaufen.
- Die Simulationstests mit Inhalt bei `--tick-rate 60` und `120` scheitern in den genannten Realdatentests schon auf `main`; das ist hier nur festgestellt.
