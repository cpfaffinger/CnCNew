# Spielregeln (Phase 5, in Arbeit)

Stand: 28. September 2026. Dieses Dokument beschreibt die auf dem deterministischen Simulationskern
(`docs/SIMULATION-CORE.md`) aufsetzenden Spielregeln, die Phase 5 bisher implementiert hat: Wirtschaft, Upgrades,
direkter Kampf, einfache Bewegung, Produktion und Sieg/Niederlage. Phase 5 ist **nicht abgeschlossen** – der
Fortsetzungsauftrag mit dem vollständigen Restumfang steht in `docs/HANDOFF-PHASE5.md`.

Alle Regeln laufen innerhalb der bestehenden Tickphasen (`SIMULATION-CORE.md` §4): neue Befehle werden in Phase 1
ausgeführt, neue Module (`StealthUpdate`, `AutoHealBehavior`, die elf `*Upgrade`-Module, `AIUpdateInterface`,
`ProductionUpdate`) ticken in Phase 3 wie jedes andere Updatemodul. Es gibt keine zusätzliche Tickphase.

## 1. Spieler und Wirtschaft

- `SimulationWorld` hält pro Spieler ein Guthaben (`Fixed`, `Money::withdraw`-Semantik: `WithdrawMoney` klemmt auf das
  verfügbare Guthaben und gibt den tatsächlich abgehobenen Betrag zurück) und eine inkrementell gepflegte
  Energiebilanz (`PlayerState.EnergyProduction`, Summe von `ObjectTemplate.EnergyProduction` aller eigenen Objekte,
  analog zu `Energy::objectEnteringInfluence`/`objectLeavingInfluence` – bei Spawn/Zerstörung fortgeschrieben, nie pro
  Frame neu berechnet). Negative Werte sind Nettoverbraucher.
- `GiveMoneyCommand` (nur System) schreibt Guthaben direkt gut; für ein Referenzmatch/Szenario mit Startkapital, nicht
  für reguläres Spielergeschehen.
- **Bekannte Lücke:** Energie hat noch keinen Verbrauchseffekt (z. B. Produktionsverlangsamung bei Energiemangel,
  `ProductionUpdate::calcTimeToBuild`); die Bilanz wird geführt, aber nirgends gelesen.

## 2. Upgrades

- `UpgradeCatalog` (`src/CnCNew.Simulation/Templates/UpgradeCatalog.cs`) vergibt jedem `Upgrade`-Definitionseintrag
  einen stabilen Bitindex (128-Bit-`UpgradeMask`, analog zur `UpgradeMaskType` des Originals). Ein Upgrade ist entweder
  spielergebunden (`Type = PLAYER`, `Player::addUpgrade`) oder objektgebunden (`Type = OBJECT`,
  `Object::getObjectCompletedUpgradeMask`); der wirksame Schlüssel eines Objekts ist die Vereinigung beider Masken
  (`SimulationWorld.UpgradeKeyMaskOf`).
- `PurchaseUpgradeCommand` prüft Eigentümerschaft, bereits abgeschlossenes Upgrade (`AlreadyOwned`) und Guthaben
  (`InsufficientFunds`), bucht die Kosten sofort vollständig ab (kein Baufortschritt – anders als bei Produktion wird
  ein Upgrade in Phase 5 augenblicklich abgeschlossen, sobald es bezahlbar ist; eine Bauzeit über die
  Produktionswarteschlange ist eine Phase-6-Erweiterung) und ruft `CompleteUpgrade`, das die Maske setzt und jedes
  `IUpgradeModule` des betroffenen Objekts (bzw. aller Objekte des Spielers bei einem spielergebundenen Upgrade) über
  `TryActivate` neu bewertet.
- **`UpgradeMuxState`** (`src/CnCNew.Simulation/Modules/UpgradeMux.cs`) ist die wortgetreue Portierung von
  `UpgradeModule.cpp` (`wouldUpgrade`, `attemptUpgrade`, `testUpgradeConditions`, `resetUpgrade`): Aktivierung
  erfordert eine nichtleere `TriggeredBy`-Maske im Schlüssel (alle oder irgendeine, je `RequiresAllTriggers`), keine
  gesetzte `ConflictsWith`-Maske und noch nicht ausgeführt; `AttemptUpgrade` entfernt zuerst die in `RemovesUpgrades`
  genannten Upgrades, wendet dann den Moduleffekt an und markiert sich als ausgeführt.
- **Elf `*Upgrade`-Module**, alle gegen den echten Bestand (`.local/phase2-import-a`) geprüft:

  | Modul | Effekt | Quelle |
  |---|---|---|
  | `ArmorUpgrade` | schaltet Schadensauflösung auf `ArmorSet Conditions=PLAYER_UPGRADE` um | `ArmorUpgrade.cpp` |
  | `WeaponSetUpgrade` | schaltet auf `WeaponSet Conditions=PLAYER_UPGRADE` um, füllt das Magazin neu | `WeaponSetUpgrade.cpp` |
  | `WeaponBonusUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (kein `WeaponBonus`-System) | `WeaponBonusUpgrade.cpp` |
  | `LocomotorSetUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (nur `SET_NORMAL` modelliert) | – |
  | `StealthUpgrade` | setzt den `CAN_STEALTH`-Statusbit | `StealthUpgrade.cpp` |
  | `MaxHealthUpgrade` | hebt `MaxHealth` um `AddMaxHealth`, vier `ChangeType`-Varianten (§3) | `MaxHealthUpgrade.cpp`, `ActiveBody.cpp::setMaxHealth` |
  | `RadarUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (kein Radar/Shroud) | `RadarUpgrade.cpp` |
  | `CommandSetUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (UI-Präsentation) | `CommandSetUpgrade.cpp` |
  | `ExperienceScalarUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (kein XP-System) | `ExperienceScalarUpgrade.cpp` |
  | `ModelConditionUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (reine Präsentation) | `ModelConditionUpgrade.cpp` |
  | `ObjectCreationUpgrade` | fordert die Erzeugungsliste per Event an (wie `CreateObjectDie` in Phase 4) | `ObjectCreationUpgrade.cpp` |

  Jeder „bewusste No-Op" ist ein Modul, dessen Auslöse-/Konflikt-/Entfernungslogik korrekt läuft (andere Module, die
  vom selben Upgradenamen abhängen, sehen den richtigen Zustand), dessen Wirkung aber ein noch nicht existierendes
  Teilsystem beträfe – niemals ein unbekannter, still verworfener Schlüssel.

## 3. Waffen, Schaden, Rüstung

- `WeaponCatalog`/`ArmorCatalog` (`src/CnCNew.Simulation/Templates/WeaponCatalog.cs`) lösen `Weapon`- und
  `Armor`-Definitionen in Simulationswerte auf. **Bewusste Phase-5-Vereinfachung:** nur Volltreffer auf ein
  explizites Ziel (`PrimaryDamage`), keine Flächenwirkung (`PrimaryDamageRadius`/`SecondaryDamage` bleiben kompiliert,
  aber ungenutzt), keine Projektil-Flugzeit (`WeaponSpeed` ungenutzt), keine `WeaponBonus`-Multiplikatoren.
- `ArmorInfo.Adjust` portiert `ArmorTemplate::adjustDamage`: `Schaden *= Koeffizient[Schadensart]`, außer bei
  `DAMAGE_UNRESISTABLE`/`DAMAGE_SUBDUAL_UNRESISTABLE` (umgehen die Rüstung vollständig), Ergebnis nie negativ.
- **`ArmorSet`/`WeaponSet`-Auswahl:** Das Original wählt über einen `SparseMatchFinder` die beste zu den aktuellen
  Bedingungen (Veteranenstufe, Upgrades, Second-Life, Kistenupgrades, …) passende Menge. Phase 5 modelliert nur zwei
  Bedingungen: `Conditions = None` (Standard) und `Conditions = PLAYER_UPGRADE` (die einzige Bedingung, die
  `ArmorUpgrade`/`WeaponSetUpgrade` tatsächlich setzen); die volle veteranenbasierte Bestauswahl ist eine
  Fortsetzungsaufgabe.
- **`FireWeaponCommand`:** direkter, expliziter Schuss (kein automatisches Zielsuchen, keine Sichtlinie). Ablehnungen:
  `NoWeapon` (kein `DefaultWeapon`/`PlayerUpgradeWeapon`), `WeaponNotReady` (Tick < `WeaponReadyTick` – Nachladezeit
  oder Schusspause), `OutOfRange` (quadrierte Distanz > quadrierte `AttackRange`, damit keine Wurzel nötig ist).
  Magazin: `Entity.WeaponClip` startet beim Spawn (bzw. bei `WeaponSetUpgrade`) mit `ClipSize` (`ClipSize = 0` bedeutet
  unbegrenzt, kodiert als `int.MaxValue`); nach jedem Schuss sinkt es um eins, bei 0 beginnt `ClipReloadTime`, sonst
  greift `DelayBetweenShots`. Ein Treffer ruft `SimulationWorld.Damage` mit dem rüstungsbereinigten Betrag,
  `DamageType`/`DeathType` der Waffe.
- **`MaxHealthUpgrade`-Rechenregeln** (`ActiveBody.cpp::setMaxHealth`, alle vier Fälle portiert):

  | `ChangeType` | Wirkung auf die aktuelle Gesundheit |
  |---|---|
  | `SAME_CURRENTHEALTH` | unverändert, außer sie überschreitet die neue Obergrenze |
  | `PRESERVE_RATIO` | `neu = alt · (neueObergrenze / alteObergrenze)` (Verhältnis bleibt gleich) |
  | `ADD_CURRENT_HEALTH_TOO` | steigt um denselben Betrag wie die Obergrenze |
  | `FULLY_HEAL` | wird auf die neue Obergrenze gesetzt |

- **Furchtsignal (Block 0.3):** `ActiveBody::attemptDamage`s Zufallsverbrauch ist portiert – beim Übergang von über
  25 % auf unter 25 % Gesundheit (und noch am Leben) zieht die Simulation `GameLogicRandomValue(0,99)`; bei < 25 wird
  ein `VoiceFearRequested`-Event ausgelöst (reine Präsentationsanforderung, der Tonabspielung obliegt Phase 7). Der
  Zug selbst erfolgt immer bei Schwellenüberschreitung, unabhängig vom Ergebnis, damit der Zufallsstrom mit dem
  Original synchron bleibt (Test: `Tests.Phase5FearSoundRng`, vergleicht den Rohzustand des `logic`-Streams vor/nach).
- **Bekannte Lücken:** keine Erfahrungs-/Veteranenvergabe aus Treffern, keine `IS_FIRING_WEAPON`/Retaliate-Logik.

## 4. Einfache Bewegung

- `SetMoveTargetCommand` setzt `Entity.HasMoveTarget`/`MoveTargetX`/`MoveTargetY`; nur Objekte mit `AIUpdateInterface`
  akzeptieren den Befehl (sonst `CannotMove`).
- `AIUpdateInterface` (`src/CnCNew.Simulation/Modules/BuiltIn/Phase5Modules.cs`) bewegt geradlinig zum Ziel:
  Restdistanz und „nah genug" (`max(CloseEnoughDist, aktuelle Geschwindigkeit)`, damit kein Objekt das Ziel für immer
  überschießt) werden ohne Wurzelziehen über quadrierte Distanzen geprüft; für die tatsächliche Normalisierung beim
  Bewegen steht `Fixed.Sqrt` zur Verfügung – eine deterministische Ganzzahl-Newton-Iteration (kein `Math.Sqrt`, keine
  Gleitkommazahl, siehe `Numerics/Fixed.cs`), die Geschwindigkeit nähert sich der Zielgeschwindigkeit über
  `Acceleration`/`Braking` an (Bremsen einsetzend, sobald die Restdistanz die Bremsdistanz `v²/(2·Braking)`
  unterschreitet), und ein Tick bewegt nie weiter als bis zum Ziel.
- **Bewusste Vereinfachung:** keine drehratenbegrenzte Ausrichtung (`Entity.Angle` bleibt beim Bewegen unverändert;
  `LocomotorInfo.TurnRatePerTick` wird kompiliert, aber nicht angewendet), kein Terrain, keine Kollision, keine
  Pfadsuche – alles Phase 6. Ohne Drehrate/Trigonometrie bleibt die Bewegung vollständig ganzzahlig/festkomma-basiert
  und damit plattformunabhängig deterministisch (vergleiche die Begründung für `Fixed` in `SIMULATION-CORE.md` §1).

## 5. Produktion und Konstruktion

- `QueueProductionCommand` reiht ein Template in die Warteschlange eines Objekts mit `ProductionUpdate` ein: Kosten
  werden **sofort vollständig** abgebucht (`ProductionUpdate.h`-Semantik, wie `Money::withdraw`), Ablehnungen sind
  `CannotProduce` (kein `ProductionUpdate`-Modul), `QueueFull` (`MaxQueueEntries` erreicht) und `InsufficientFunds`.
  `CancelProductionCommand` entfernt einen Platz und erstattet die Kosten vollständig.
- `ProductionUpdate::update()`: nur der Kopf der Warteschlange zählt pro Tick einen Tick weniger `RemainingTicks`
  (`BuildTime` in Sekunden, aufgerundet auf ganze Ticks); bei Erreichen von 0 entsteht die Einheit an der Position des
  Produzenten (`RequestSpawn`, keine Ausgangs-/Türgeometrie) im Besitz von dessen Spieler, und der nächste
  Warteschlangenplatz beginnt neu.
- **Bewusste Vereinfachung:** kein Energie-Verlangsamungsfaktor (`ProductionUpdate::calcTimeToBuild`), keine
  Bauplatz-/Kollisionsprüfung, kein Rallypunkt, `QuantityModifier` und die Türanimationsfelder sind typisiert, aber
  ungenutzt (siehe Modulkatalog-Kommentar).

## 6. Stealth und Selbstheilung (Block 0.2)

- `StealthUpdate` tarnt ein Objekt (`STEALTHED`-Statusbit), sobald `StealthDelay` Ticks lang keine seiner
  `StealthForbiddenConditions` zutraf; jede Verletzung bricht die Tarnung sofort und setzt den Countdown zurück
  (wortgetreu zum Original). Phase 5 prüft mechanisch `ATTACKING`, `MOVING` (`Speed` gegen `MoveThresholdSpeed`),
  `USING_ABILITY`, `FIRING_PRIMARY`/`_SECONDARY`/`_TERTIARY` (`LastFireTick == aktueller Tick`) und `TAKING_DAMAGE`
  (`LastDamageTick == aktueller Tick`); `NO_BLACK_MARKET` und `RIDERS_ATTACKING` sind kompiliert, aber niemals wahr
  (kein Schwarzmarkt-/Mitfahrersystem). Der gesetzte Statusbit hat noch keinen Verbraucher: Phase 5 hat weder
  automatische Zielerfassung noch Sichtlinie, die ihn auswerten könnten.
- `AutoHealBehavior` heilt periodisch (`HealingDelay`) alle passenden Objekte desselben Spielers: `KindOf`/
  `ForbiddenKindOf`-Filter, `Radius` oder (`AffectsWholePlayer`) ohne Entfernungsprüfung, `SkipSelfForHealing`. Die
  Fähigkeit startet aktiv (`StartsActive`) oder wird durch ein Upgrade freigeschaltet (`IUpgradeModule`, derselbe
  `UpgradeMuxState` wie die `*Upgrade`-Module).

## 7. Sieg und Niederlage

- `SimulationWorld.IsPlayerDefeated(player)` entspricht `VictoryConditions::hasSinglePlayerBeenDefeated` mit den
  Standardflaggen `NOBUILDINGS|NOUNITS`: ein Spieler ohne ein einziges Objekt ist besiegt.
- `SimulationWorld.TryGetWinner()` ist „letzter Überlebender" (**bewusste Vereinfachung**: keine Teams/Allianzen in
  Phase 5 – ein Team-Sieg im Original zählt mehrere Spieler als eine Einheit): genau ein Nicht-System-Spieler mit
  mindestens einem Objekt, sonst `null`.

## 8. Befehlstabelle

Siehe `docs/SIMULATION-CORE.md` §6 für die vollständige, versionierte Tabelle (Codes 1–12) und die Ablehnungsgründe.

## 9. Modulabdeckung

Siehe `docs/SIMULATION-CORE.md` §11: 623 von 2.115 Objekten sind heute instanziierbar (29,5 %, gegenüber 26/2.113 in
Phase 4); die verbleibenden Blocker (`FlammableUpdate`, `PhysicsBehavior`, `TransitionDamageFX`, `SlowDeathBehavior`,
`GarrisonContain`, `CreateCrateDie`, `SquishCollide`, `PoisonedBehavior`, …) sind der Reststoff der Phase-5-Fortsetzung.

## 10. Bekannte Grenzen (Zusammenfassung)

Jede Zeile ist eine bewusste, dokumentierte Vereinfachung, keine stille Lücke:

- Kein Flächenschaden, keine Projektil-Flugzeit, keine `WeaponBonus`-Multiplikatoren (§3).
- Nur zwei `ArmorSet`/`WeaponSet`-Bedingungen statt der vollen veteranenbasierten Bestauswahl (§3).
- Keine Erfahrungs-/Veteranenvergabe aus Kämpfen (§3). Das Furchtsignal (Block 0.3) ist implementiert.
- Keine drehratenbegrenzte Ausrichtung, kein Terrain, keine Pfadsuche, keine Kollision (§4).
- Kein Energie-Verlangsamungsfaktor, keine Bauplatzprüfung, kein Rallypunkt (§5).
- Der `STEALTHED`-Statusbit hat noch keinen Verbraucher (kein Sicht-/Zielsystem, §6).
- Keine Teams/Allianzen, keine Sciences, keine `CommandSet`/`CommandButton`-Wirkung (reine Präsentation), kein
  Garrison/Transport/Supply/Radar/Reparatur/Verkauf (Rest von HANDOFF-PHASE5.md Punkt „Vollständiger Umfang").
- Kein headless Referenzmatch aller Fraktionen, keine Golden-Master-Szenarien gegen Originalwerte (HANDOFF-PHASE5.md
  Punkte 7/8 stehen aus); die hier beschriebenen Mechaniken sind stattdessen durch `tests/CnCNew.Simulation.Tests/
  Tests.Phase5.cs` (gezielte Integrationstests: Wirtschaft, Kampf, Upgrade-Effekte, Bewegung, Produktion, Stealth/Heilung,
  Sieg) und den 216.000-Tick-Stresstest mit den neuen Modulen an echten Objekten abgesichert.
