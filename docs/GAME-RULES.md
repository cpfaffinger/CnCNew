# Spielregeln (Phase 5, in Arbeit)

Stand: 29. September 2026 (nach §6d). Dieses Dokument beschreibt die auf dem deterministischen Simulationskern
(`docs/SIMULATION-CORE.md`) aufsetzenden Spielregeln, die Phase 5 bisher implementiert hat: Wirtschaft, Upgrades,
direkter Kampf, einfache Bewegung, Produktion, Sieg/Niederlage sowie die Blockermodule aus §3/§6/§6a (Schadenszustände,
Brand, Gift, Tod, Stealth-Erkennung, Garnison-Containment) sowie das Spezialkraft-Subsystem (§6d). Phase 5 ist **nicht abgeschlossen** – der
Fortsetzungsauftrag mit dem vollständigen Restumfang steht in `docs/HANDOFF-PHASE5.md`.

Alle Regeln laufen innerhalb der bestehenden Tickphasen (`SIMULATION-CORE.md` §4): neue Befehle werden in Phase 1
ausgeführt, neue Module ticken in Phase 3 wie jedes andere Updatemodul. Es gibt keine zusätzliche Tickphase.

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

### Spieler, Fraktionen und Sciences

- `PlayerConfiguration.Faction` benennt einen `PlayerTemplate` (`FactionCatalog`, Block 0.1 fortgesetzt); ist er gesetzt,
  schreibt `SimulationWorld` einmalig bei der Konstruktion `StartMoney` und `IntrinsicSciences` in den Spielerzustand
  (`Player::Player()`). `StartingBuilding`/`StartingUnit0`–`9` bleiben Namen (keine aufgelösten Indizes): ein
  Referenzmatch spawnt sie über gewöhnliche `SpawnCommand`s, statt den Fraktionskatalog an die Objektvorlagenliste zu
  koppeln.
- `ScienceCatalog` gibt jeder `Science`-Definition einen stabilen Bitindex (128-Bit-`ScienceMask`, strukturell wie
  `UpgradeMask`, aber ein getrennter Indexraum – Sciences und Upgrades sind getrennte Referenzdomänen im Original).
  `PurchaseScienceCommand` portiert `Player::attemptToPurchaseScience`/`isCapableOfPurchasingScience` in dieser
  Reihenfolge: bereits im Besitz (`AlreadyOwned`) → fehlende direkte Voraussetzung (`MissingPrerequisite`,
  `Player::hasPrereqsForScience`) → Kaufpreis 0 bedeutet **nicht direkt kaufbar** (`ScienceNotPurchasable` – das
  Original nutzt denselben Sentinelwert für „nur per `grantScience` vergebbar“, z. B. intrinsische Fraktionssciences)
  → zu wenig Punkte (`InsufficientSciencePurchasePoints`). Bei Erfolg werden die Punkte abgebucht und die Maske gesetzt.
- **Bewusste Vereinfachung:** „Science Purchase Points“ sind im Original an Rang/Erfahrung gekoppelt
  (`Player::resetRank`, `RankInfo::m_sciencePurchasePointsGranted`) – ein Fortschrittssystem, das Phase 5 nicht
  modelliert. `GiveSciencePointsCommand` (nur System) schreibt Punkte direkt gut, analog zu `GiveMoneyCommand`.
  `IsGrantable` ist typisiert, aber ungenutzt (kein `grantScience`-Aufrufer existiert außer der intrinsischen
  Fraktionsvergabe, die die Maske direkt setzt statt über `grantScience` zu prüfen — wie im Original).
- **Bekannte Lücke:** keine Teams/Allianzen/Diplomatie als Datenmodell (siehe §7); `CommandSet`/`CommandButton` bleiben
  reine Präsentation ohne Simulationswirkung.

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

### Schadenszustände, Brand, Gift und Tod (die nächsten Blockermodule)

- **Schadenszustände** (`ActiveBody.CalcDamageState`): `PRISTINE`/`DAMAGED`/`REALLYDAMAGED`/`RUBBLE` nach dem
  Gesundheitsverhältnis gegen `GameData.ini`s `UnitDamagedThreshold`/`UnitReallyDamagedThreshold` (Retail-Standard
  0,7/0,35 ohne `GameData`-Block). `TransitionDamageFX` fordert beim Wechsel in einen **schlechteren** Zustand die
  passende `DamagedOCL`/`ReallyDamagedOCL`/`RubbleOCL` an (`CreationListRequested`-Event), aber nur mit auflösbarer
  Schadensquelle (`DamageInfo.Source` gültig) – die zugehörigen FX-Listen und Partikelsysteme sind reine Präsentation.
- **`FlammableUpdate`:** Flammenschaden (`DAMAGE_FLAME`/`DAMAGE_PARTICLE_BEAM`) sammelt sich gegen `FlameDamageLimit`
  (verfällt nach `FlameDamageExpiration` ohne neuen Treffer); ist das Limit erreicht, setzt `AFLAME`, schädigt
  periodisch (`AflameDamageDelay`/`AflameDamageAmount`), wird nach `BurnedDelay` dauerhaft `BURNED` und löscht sich
  nach `AflameDuration` (außer es ist bereits `BURNED` – dann bleibt es aus, kann aber nicht neu entzündet werden).
- **`PoisonedBehavior`:** ein `DAMAGE_POISON`-Treffer merkt sich den tatsächlich zugefügten Schaden
  (`IDamageModule.OnDamage`s `actualDamageDealt`) und wendet ihn alle `PoisonDamageInterval` erneut an (als
  `DAMAGE_UNRESISTABLE`, nicht wieder `DAMAGE_POISON` – sonst würde jeder Tick die Vergiftung neu starten), bis
  `PoisonDuration` abgelaufen ist. Eine erneute Vergiftung während des Zyklus verlängert die Dauer, verschiebt den
  nächsten Tick aber nur nach vorne, nie nach hinten. Heilung (`IDamageModule.OnHealing`, neu: feuert nur, wenn die
  Gesundheit tatsächlich gestiegen ist, wortgetreu zu `ActiveBody::attemptHealing`) heilt die Vergiftung vollständig.
- **`SlowDeathBehavior`:** beim passenden Tod treten alle zutreffenden Geschwister-Module (bis zu sieben pro Objekt in
  echten Daten) in eine gewichtete Lotterie (`ProbabilityModifier`, mindestens 1); nur das Gewinnermodul plant die
  verzögerte Zerstörung (`DestructionDelay`/`-Variance`). `Entity.SlowDeathStarted` verallgemeinert die
  Original-Sperre (die dort ein `AIUpdateInterface` voraussetzt) auf jede Entität. Absichtlich nicht modelliert:
  Überkill-Bonus (braucht den tatsächlichen Schaden am Todesschlag, noch nicht durch `OnDie` durchgereicht), Physik
  beim Wegschleudern, LOD-Zeitskalierung, die 3-Phasen-Zufallseffektlisten.
- **`FireWeaponWhenDeadBehavior`:** feuert `DeathWeapon` beim passenden Tod, sobald aktiv (`StartsActive` oder ein
  passendes `TriggeredBy`-Upgrade). Einzigartig unter den Upgrade-Modulen: `ConflictsWith` wird beim Tod noch einmal
  geprüft, weil der `StartsActive`-Pfad nie durch die Aktivierungs-Konfliktprüfung lief (so funktionieren
  ChemicalGenerals sich gegenseitig ausschließende Sprengsatz-Upgrades: der Standard startet aktiv, ein späteres
  Konflikt-Upgrade bringt ihn zum Schweigen). Das Feuern selbst löst nur ein `WeaponFireRequested`-Event aus – echter
  Flächenschaden an Umstehenden ist die bereits bestehende, projektweite Vereinfachung (`WeaponCatalog`: nur
  Volltreffer, siehe oben).

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
- `StealthDetectorUpdate` durchsucht periodisch (`DetectionRate`) alle anderen Objekte nach einem gerade `STEALTHED`en,
  gegnerischen, zum `KindOf`-Filter passenden Objekt innerhalb `DetectionRange` (Standard: `VisionRange` des eigenen
  Templates) und markiert es `DETECTED` (`StealthUpdate.MarkDetected`: setzt das Bit, verlängert nie verkürzt seine
  Ablaufzeit). `DETECTED` besteht neben `STEALTHED` (enttarnt es, ohne die Tarnung selbst aufzuheben) und läuft von
  selbst ab, sobald nichts mehr nachmarkiert. **Bewusste Vereinfachung, projektweit vorbestehend:** ohne Team-/
  Diplomatiesystem (§7) gilt „gleicher Besitzer = Verbündeter" statt einer echten Beziehungsprüfung; ohne
  Garrison/Transport-System (`CanDetectWhileGarrisoned`/`CanDetectWhileContained` sind typisiert, aber wirkungslos,
  ein Objekt ist nie „enthalten").

### 6a. Containment (Garrison)

Der größte Einzelblocker nach gemessenem Freischaltungsgewinn (167 von 200 Objekten mit `GarrisonContain` hingen nur
noch an diesem Modul, siehe `docs/HANDOFF-PHASE5.md`). Neue Basisinfrastruktur, nicht nur ein weiteres Modul:
`Entity.ContainedBy`/`Entity.Container` (ein Container pro Entität, absichtlich eng gehalten – `IContainerModule` ist
bewusst schmal, bis ein zweiter Implementierer, `TransportContain`, ihn tatsächlich erweitert) sowie
`EnterContainerCommand`/`ExitContainerCommand` (`CommandTypes.Version` 3 → 4).

- **Eintritt/Austritt:** sofort, ohne Pfadsuche/Distanzprüfung (dieselbe „sofort, keine echte räumliche Prüfung"-
  Vereinfachung wie Wirtschaft/Produktion). Eintritt erfordert `IsValidContainerFor`; Austritt kann vom Besitzer der
  Passagiereinheit oder vom Besitzer des Containers angeordnet werden.
- **`GarrisonContain`:** `ContainMax` (Kapazität), `AllowInsideKindOf`/`ForbidInsideKindOf` (Standard: nur `INFANTRY`,
  `GarrisonContainModuleData`s eigener, engerer Standard gegenüber `OpenContain`s generischem „alles erlaubt"),
  `AllowAlliesInside`/`AllowEnemiesInside` (beide Standard `Yes` – **bewusste Vereinfachung**: ohne Team-/
  Diplomatiesystem gilt „gleicher Besitzer = Verbündeter", dieselbe Näherung wie bei `StealthDetectorUpdate`; es gibt
  kein „neutral"). Ein Gebäude in `REALLYDAMAGED` kann nicht mehr betreten werden und wirft beim Übergang **in**
  diesen Zustand sofort alle Insassen aus (beides entfällt bei `KindOf = GARRISONABLE_UNTIL_DESTROYED`).
- **Heilung** (`HealObjects`): ein Splitter `MaxHealth / TimeForFullHeal` pro Tick, ab der vollen Verweildauer ein
  harter Sprung auf volle Gesundheit (`GarrisonContain::healSingleObject`).
- **Tod des Containers:** `DamagePercentToUnits` wird auf die aktuelle Gesundheit jedes Insassen angewendet, danach
  werden Überlebende ausgeworfen (nicht getötet – `OpenContain::onDie` ruft `removeAllContained`, nie
  `killAllContained`, solange `killRidersWhoAreNotFreeToExit` beim Basisverhalten bleibt, wie bei `GarrisonContain`).
- **Bewusst nicht modelliert:** `InitialRoster` (Startbesatzung – bräuchte synchrones verschachteltes Erzeugen beim
  Konstruieren, eine größere Änderung an der Spawn-Pipeline, als dieser erste Zuschnitt rechtfertigt), Feuern aus dem
  Container heraus, Garnisonspunkte/-positionen (art-abgeleitet, keine INI-Daten), das
  Apparent-Controlling-Player-/Team-Tausch-System (kein Teamsystem), Eroberungsbehandlung (kein Eroberungssystem).
- **`TransportContain`** (der zweite Implementierer, der `ContainModuleBase` gerechtfertigt hat – geteilte
  Gating-/Ein-/Austritts-/Todeslogik, siehe die Klassenkommentare): Kapazität ist **gewichtet**
  (`ObjectTemplate.TransportSlotCount`, nicht einfach gezählt) – ein Objekt mit 0 Plätzen (der Standard) ist nie
  transportierbar, und die Summe der Platzkosten aller Insassen plus der Bewerber darf `Slots` nicht überschreiten.
  `HealthRegen%PerSec` heilt kontinuierlich (`MaxHealth · Prozent / 100` pro Sekunde), nicht rampenartig wie
  `GarrisonContain`s `HealObjects`. Kein `REALLYDAMAGED`-Sonderfall (die Referenz hat dafür auch keinen). Bewusst
  nicht modelliert: `InitialPayload`, Feuern aus dem Container, Austrittspfade/-verzögerung/-streuung (Präsentation
  oder brauchen echte Bewegung/Positionierung), Eroberungsbehandlung.

## 6b. Erzeugungslisten und Kisten

**`ObjectCreationList`** (295 Definitionen, streng typisiert in `CnCNew.Data`: `ObjectCreationListDefinition.Sections`; die
Nugget-Zahlen im Bestand: `CreateObject` 184, `CreateDebris` 473, `DeliverPayload` 46, `ApplyRandomForce` 10, `FireWeapon` 4,
`Attack` 1) wird jetzt ausgeführt, nicht mehr nur als `CreationListRequested`-Event gemeldet.
`SimulationWorld.CreateFromList(source, liste, anforderer, transferPreviousHealth)` entspricht
`ObjectCreationList::create` und wird von `CreateObjectDie` und `InstantDeathBehavior` aufgerufen. Das Event bleibt als
Protokoll bestehen. Der Katalog (`ObjectCreationListCatalog`) bindet jede Liste beim Aufbau der Vorlagen; nur ein unbekannter
Nugget-Typ ist ein `TemplateBuildException`, alles andere wird zur Laufzeit als `CreationSkipped`-Event mit Grund gemeldet.

- **`CreateObject`** (`GenericObjectCreationNugget::create/reallyCreate/doStuffToObj`): Pro `Count` wird ein Objekt aus
  `ObjectNames` gewählt (ein `Range(0, n−1)`-Zug je Exemplar), danach – in dieser Reihenfolge – Mindestabstand und
  Startwinkel bei `SpreadFormation`, dann Gesundheitsanteil (`Range(MinHealth, MaxHealth)`) und bei `OnGroundAligned`/
  `SendItOut` ein Orientierungszug. `Offset` wird um die Quellausrichtung gedreht; `Disposition` `LIKE_EXISTING` übernimmt
  die Ausrichtung. `PutInContainer` erzeugt den Container zuerst (im Original `newObject`), positioniert ihn aber erst nach
  dem Inhalt. `ContainInsideSourceObject` steckt das neue Objekt in die Quelle, `InheritsVeterancy` überträgt den Rang
  (nur auf trainierbare Vorlagen), `MinLifetime`/`MaxLifetime` überschreiben `LifetimeUpdate` ohne zusätzlichen Zug.
  `SkipIfSignificantlyAirborne` prüft `Thing::isSignificantlyAboveTerrain` (`Höhe > −9·Gravity`, Gravity jetzt typisiert aus
  `GameData`; Terrain ist bei Z = 0 flach).
- **Verzögertes Erzeugen:** Spawns laufen weiter über die Warteschlange (`ProcessSpawns` nach `ProcessDestroys`). Alles,
  was das Original nach `newObject` am Objekt einstellt, reist als `SpawnSetup` mit und wird in `SpawnEntity` angewandt:
  Startgesundheit (`ActiveBody::setInitialHealth`), Rang, Lebensdauer, Container, Insasse der Quelle und
  `TransferPreviousHealth` (`CreateObjectDie`: unresistabler Schaden `MaxHealth − PreviousHealth`). Ein in `SpawnEntity`
  ausgelöstes `RequestDestroy` wirkt daher erst im folgenden Tick. Der Spawn-Containment meldet `EntityContained`.
- **Zufallsreihenfolge:** Alle Züge finden zur Listenzeit statt (die Gesundheits- und Orientierungszüge des Originals liegen
  nach `newObject`, hier nach der Auswahl, aber vor dem tatsächlichen Erzeugen). Das ist die einzige bewusste Abweichung in der
  Zugreihenfolge; nach ihr richten sich die Golden-Master-Tests. `InstantDeathBehavior` zieht wie das Original genau eine
  Liste pro Modul.
- **Nicht simuliert, aber nie still verworfen:** `CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack` und
  `DeliverPayload` erzeugen `CreationSkipped` (Grund `NuggetNotSimulated`), ihre Zufallszüge (Kraft, Drehung, Physik) werden
  nicht verbraucht; `InvulnerableTime` und `DiesOnBadLand` erscheinen als `SettingNotSimulated`. Weitere Gründe:
  `UnknownList`, `UnknownTemplate`, `TemplateNotInstantiable`, `ContainerNotInstantiable`, `NoObjectNamed`. Sichtbar ist damit,
  welche Listen des Bestands erst mit Physik (Phase 6) vollständig werden.
- **Positionsfindung:** `PartitionManager::findPositionAround` ohne Terrain und Hindernisse wählt den innersten Ring: ein
  Startwinkelzug, Abstand `MinDistance`; ist `MaxDistance < MinDistance`, wird die Formationsposition übersprungen. Sinus und
  Cosinus liefert `FixedTrig` (Taylor-Reihe, Fehler unter 2·10⁻⁵, `Cos(0)` exakt 1) statt der binary32-Funktionen des Originals.
- **`RequiresLivePlayer`** prüft `IsPlayerDefeated` des Besitzers; da die Quelle selbst mitzählt, ist der Zweig praktisch
  nie aktiv (im Original ebenso) und daher ungetestet. Objekte, die als nicht instanziierbar übersprungen werden, verbrauchen
  keine `doStuff`-Züge.
- **`LifetimeUpdate`** zieht einmal (das Original zieht nach `setLifetimeRange` erneut).
- **Fixtures:** Die Fixture-Kompilierung ist streng referenzgeprüft, ein unbekannter Objektname in einer Erzeugungsliste ist
  dort nicht darstellbar; `UnknownTemplate` ist nur mit Realdaten prüfbar.

**`CreateCrateDie`** (`CreateCrateDie.cpp onDie`, `CrateSystem.cpp`; Katalog `CrateCatalog`, 9 `CrateData`-Definitionen):
Nach dem Todesfilter (`DieMux`) wird ein Kill durch denselben Besitzer als „ALLIES" nicht belohnt. Je `CrateData` folgt
ein `Range(0,1)`-Zug gegen `CreationChance`, dann `VeterancyLevel`, `KilledByType` (alle Arten müssen vorliegen) und
`KillerScience`; erst wenn alle Bedingungen erfüllt sind, wählt ein weiterer Zug gewichtet ein `CrateObject`
(Summe < Zug → keine Kiste, wie im Original), ein letzter zieht die Ausrichtung. Die Kiste erscheint mangels Terrain am Ort
des Opfers (`findPositionAround` ist Phase 6); Besitzer ist der Verursacher (`OwnedByMaker`) oder neutral. Alle Kistenobjekte
des Bestands nutzen `*CrateCollide`-Module, die nicht implementiert sind: die Anforderung wird immer als `CrateRequested`
gemeldet, das Objekt aber nur erzeugt, wenn es instanziierbar ist. Kein Teamsystem: „Verbündeter" ist „gleicher Besitzer"
(Präzedenzfall `GarrisonContain`), der KI-Hook `notifyCrate` fehlt.

## 6c. Module für das Retail-Match

Vierzehn Module schalten Retail-Objekte frei, die das Referenzmatch braucht. Alle sind streng typisiert, im Katalog mit
Schema geführt und in `Tests.RetailModules.cs` geprüft; Aktualisierungsmodule speichern ihren Weckzeitpunkt
(`nextWake`, vom `SourceRules`-Test erzwungen), weil `SimulationWorld.Register` sonst nach einem Restore ein neues
Intervall beginnt und die Snapshotfortsetzung divergiert.

| Modul | Referenz | Verhalten |
| --- | --- | --- |
| `BaseRegenerateUpdate` | `BaseRegenerateUpdate.cpp` | Strukturen heilen `BaseRegenHealthPercentPerSecond` (`GameData`) ihrer Maximalgesundheit je Sekunde, sobald sie `BaseRegenDelay` unbeschädigt waren (Schub aus drei Ticks alle drei Ticks wie im Original; schläft bei voller Gesundheit, endet bei `SOLD`). |
| `PhysicsBehavior` | `PhysicsUpdate.cpp` | Ruhezustand des Originals: reagiert nur auf Kräfte und Terrainkontakt, schläft für immer. Beides gibt es noch nicht (Phase 6); das Modul verschiebt nie ein Objekt. |
| `SquishCollide` | `SquishCollide.cpp` | Ohne Kollisionserkennung wird `onCollide` nie gerufen; zustandslos. |
| `GenerateMinefieldBehavior` | `GenerateMinefieldBehavior.cpp` | Legt Minenfelder bei Tod/Upgrade (`placeMines`, `SmartBorder`, kreisförmig) mit Zugreihenfolge des Logikstroms; Geländeveto, `setScootParms` und Rechteckränder werden als `CreationSkipped` gemeldet. |
| `DefaultProductionExitUpdate`, `SupplyCenterProductionExitUpdate` | gleichnamige `.cpp` | Ausgang immer frei; kein Tor, kein Rallypunkt, keine Tarnungsvergabe (`GrantTemporaryStealth`). |
| `QueueProductionExitUpdate` | `QueueProductionExitUpdate.cpp` | Burst-Ausgang: `InitialBurst` Einheiten in Folge, danach `ExitDelay` Sperrzeit; der Produzent hält fertige Einheiten zurück, bis der Ausgang frei ist. |
| `HealContain` | `HealContain.cpp` | Insassen heilen je Tick `MaxHealth / TimeForFullHeal`, danach voll geheilt entlassen (ohne KI am Container platziert). |
| `EjectPilotDie` | `EjectPilotDie.cpp` | Erzeugt bei Tod die Boden- bzw. Luftliste (`IsSignificantlyAboveTerrain`); `InvulnerableTime` ist nicht modelliert. |
| `VeterancyGainCreate` | `VeterancyGainCreate.cpp` | Setzt bei Erzeugung `StartingLevel`, wenn die geforderte Science vorhanden ist. Das Original speichert es als Mindeststufe; ohne Erfahrungssystem wird es einmal gesetzt. |
| `PreorderCreate` | `PreorderCreate.cpp` | Zeigt nur eine Vorbestellerdekoration; inerter Platzhalter ohne Schlüssel. |
| `RadarUpdate` | `RadarUpdate.cpp` | Passiv; `RadarUpgrade` startet die Ausfahranimation (`RadarExtendTime`). Es gibt kein Radar/keine Minikarte in der Simulation. |
| `RebuildHoleExposeDie` | `RebuildHoleExposeDie.cpp` | Sterbende GLA-Strukturen hinterlassen ein Loch (Besitzer, Position, Ausrichtung, `HoleMaxHealth`); nicht für neutrale oder im Bau befindliche. Ausgelassen: `isPlayerActive`, Geometrieübernahme, `TransferAttackers`. |
| `RebuildHoleBehavior` | `RebuildHoleBehavior.cpp` | Das Loch regeneriert `HoleHealthRegen%PerSecond`; nach `WorkerRespawnDelay` würde ein Arbeiter neu bauen – Arbeiter und Bauauftrag sind KI (Phase 6), daher `CreationSkipped` und Ende des Countdowns. |

`HealContain` gehört zu den Containern (§6a); `ProductionUpdate` fragt den Ausgang über die Schnittstelle
`IProductionExit` ab. `ModuleFramework.cs` kennt zusätzlich `ISpawnOriginReceiver` (Lochursprung) und die
Welt-Dienste `IsSignificantlyAboveTerrain` und `BaseRegen*`.

**Nicht modelliert (bewusst, nicht still):** Rallypunkte und `UseSpawnRallyPoint`, `GrantTemporaryStealth`, Lochgeometrie,
`TransferAttackers`, Arbeiter-/Wiederaufbau-KI, dauerhafte Mindest-Veteranenstufe, Radarsystem, Vorbestellungen,
`InvulnerableTime` von `EjectPilotDie`.

## 6d. Spezialkräfte

Zehn Module und ein Befehl bilden das Spezialkraft-Subsystem. Es schaltet die **echten Kommandozentralen aller 13
Fraktionen** frei (vorher Kasernen als Ersatz, §7a). Referenz: `SpecialPower.cpp` (`SpecialPowerTemplate`, Feldtabelle),
`SpecialPowerModule.cpp`, `OCLSpecialPower.cpp`, `CashBountyPower.cpp`, `CashHackSpecialPower.cpp`,
`SpectreGunshipDeploymentUpdate.cpp`, `Player.cpp` (`addScience`, `grantScience`, `getOrStartSpecialPowerReadyFrame`,
`expressSpecialPowerReadyFrame`), `ActionManager.cpp` (`canDoSpecialPower`) und `Object.cpp` (`scoreTheKill`).

**Definition und Katalog.** `SpecialPower` ist streng typisiert (`SpecialPowerSchema`): `ReloadTime`, `RequiredScience`
(Referenz auf `Science`), `PublicTimer`, `Enum` (Namenstabelle `SpecialPowerType`, 67 Einträge), `SharedSyncedTimer`,
`DetectionTime`, `ViewObject*`, `RadiusCursorRadius`, `ShortcutPower`; `InitiateSound`, `InitiateAtLocationSound` und
`AcademyClassify` sind Präsentation bzw. Statistik und stehen als `deferred-field` in der Kompatibilitätsbasislinie (79
Verwendungen). Der `SpecialPowerCatalog` der Simulation vergibt dichte Indizes (Datenreihenfolge) und rechnet `ReloadTime` in
Ticks um; ohne `ReloadTime` gilt der Standard des Originals (1 s = 30 Ticks, 3 von 80 Definitionen, ausdrücklich gezählt).
Bestand: 80 Definitionen, davon 36 mit `SharedSyncedTimer`.

**Bereitschaftsmaschine** (`SpecialPowerModuleBase`, portiert wie geschrieben): `availableOnFrame`, `pausedCount`,
`pausedOnFrame`. Eine Kraft ist bereit, wenn sie nicht pausiert ist und `Tick >= availableOnFrame` gilt; ein Befehl in Tick
N sieht also die Bereitschaft von Tick N (ein bei Tick 0 erzeugter Träger mit 300 Ticks Nachladezeit ist bei Tick 300 bereit,
nicht früher). Beim Erzeugen (`OnSpawn`, nicht im Bau) startet das Nachladen, `StartsPaused` pausiert zusätzlich. Eine
`SharedSyncedTimer`-Kraft ignoriert ihre eigene Pause und liest den geteilten Bereitschaftsframe des Spielers
(`PlayerState.SharedReadyFrames`, sortiert, Teil von Snapshot und Hash); ein noch nicht vorhandener Timer beginnt beim ersten
Zugriff. `SpecialPowerCreate` (am Ende des Baus, im Tick nach dem Spawn) meldet die Kraft als „entstanden“
(`onSpecialPowerCreation`: Nachladen neu starten, geteilte Kraft sofort bereit, `StartsPaused` erneut pausieren – die
doppelte Pause bei `StartsPaused` + `SpecialPowerCreate` ist wie im Original portiert und wird von
`UnpauseSpecialPowerUpgrade` einzeln aufgehoben).

**Befehl.** `UseSpecialPowerCommand` (Code 17, `CommandTypes.Version = 5`, im Replay kodiert): Quellobjekt, Kraftname,
Zielart (`None`, `Object`, `Location`), Zielobjekt, Koordinaten. Die Prüfung (`CheckSpecialPower`, gleiche Regeln bei Einreichung
und Ausführung, weil sich die Bereitschaft dazwischen ändert) lehnt explizit ab: `UnknownEntity`, `NotPermitted`,
`UnknownSpecialPower` (25; Kraft unbekannt oder das Objekt trägt kein Modul dafür), `MissingScience` (26),
`SpecialPowerNotReady` (27; pausiert, nachladend oder im Bau), `InvalidSpecialPowerTarget` (28; Zielart passt nicht oder das
Zielobjekt existiert nicht) und `SpecialPowerHandlerMissing` (29; `UpdateModuleStartsAttack` verlangt einen Handler im
Updatemodul, der fehlt – die Kraft wird verweigert statt still nichts zu tun). Ausgeführt emittiert der Befehl
`SpecialPowerUsed`, ruft den ersten passenden Handler (`ISpecialPowerHandler`, Modulreihenfolge), startet das Nachladen
(sofern nicht `StartsAttack`) und wendet die kraftspezifische Wirkung an.

| Modul | Verhalten |
| --- | --- |
| `SpecialAbility` | Reine Kraft, deren Wirkung ein Handler trägt (Einheitenfähigkeiten, Kanonenboote). Ohne Handler und mit `UpdateModuleStartsAttack` wird abgelehnt (29). |
| `OCLSpecialPower` | Erzeugt eine `ObjectCreationList` (§6b): die erste `UpgradeOCL`, deren Science der Besitzer hat, sonst `OCL`. `CREATE_AT_LOCATION`/`USE_OWNER_OBJECT` erzeugen am Ziel (ohne Ziel am Quellobjekt), `CREATE_ABOVE_LOCATION` 300 Einheiten darüber (`ON_GROUND_ALIGNED` setzt z wieder auf 0, `LIKE_EXISTING` behält es). Die drei Kartenrand-Orte brauchen Terrain: die Kraft löst aus und lädt nach, die fehlende Lieferung meldet `CreationSkipped(NoTerrainEdge)`. |
| `CashBountyPower` | Hebt die Kopfgeldrate des Besitzers auf `Bounty` (nur wenn er die Science der Kraft hat, beim Erscheinen und bei Science-Erwerb). Auszahlung: `ScoreTheKill` (`Object::scoreTheKill`/`Player::doBountyForKill`) zahlt dem Besitzer des Töters `ceil(BuildCost · Rate)` für den Kill eines fremden Objekts (nicht `IGNORED_IN_GUI`, nicht im Bau, kein Systemspieler). |
| `CashHackSpecialPower` | Stiehlt `min(Betrag, Guthaben des Opfers)` vom Besitzer des Zielobjekts; Betrag = erste `UpgradeMoneyAmount`, deren Science der Dieb hat, sonst `MoneyAmount`. Ein Ortsziel bewirkt wie im Original nichts. |
| `SpecialPowerCreate` | Meldet die Kräfte des Objekts als entstanden (siehe oben). |
| `GrantUpgradeCreate` | Schließt das benannte Upgrade (spieler- oder objektgebunden) einmal ab, wenn das Objekt fertig ist. |
| `GrantScienceUpgrade` | Vergibt eine Science (`Player::grantScience`; eine nicht vergebbare Science, `IsGrantable = No`, wird verweigert). |
| `UnpauseSpecialPowerUpgrade` | Hebt eine Pause der benannten Kraft am Objekt auf. |
| `SpecialPowerCompletionDie` | Meldet dem Skript-System das Ende des Trägers; Skripte gibt es nicht, das Modul ist ausdrücklich inert. |
| `SpectreGunshipDeploymentUpdate` | Handler der Gunship-Kraft (erster Treffer nach Modulreihenfolge, `RequiredScience`). Das Kanonenboot ist eine fliegende Einheit mit eigener KI, die vom Kartenrand einfliegt; beides fehlt, daher `CreationSkipped(NoTerrainEdge)` und nur der Nachladestart des Trägers. |

**Science-Vergabe** (`SimulationWorld.AddScience`, gemeinsam für Kauf und `GrantScienceUpgrade`): nach dem Eintragen werden
alle Kräfte der Objekte des Spielers, die diese Science verlangen, geweckt (`onSpecialPowerCreation` und sofort bereit,
`Player::addScience`).

**Nicht modelliert (bewusst, nicht still):** kein Bauvorgang (jedes Objekt ohne `UnderConstruction` ist im Tick nach dem Spawn
fertig, `onBuildComplete`), kein Deaktiviert-Zustand (`isDisabled`-Prüfung), `createOwner = false` (der Besitzerwechsel von
`CreateFromList`), `OCLAdjustPositionToPassable`, Kanonenboot-KI und Kanteneinflug, Sichtobjekte (`ViewObject*`), EVA-Ansagen,
Sounds, öffentliche Timer und Skript-Benachrichtigungen; die Kopfgeldbeziehung ist „anderer Besitzer“ (kein Teammodell). Die
Referenzmatch-Erzeugung setzt keine Spezialkräfte ein (der Befehlsgenerator erzeugt kein `UseSpecialPowerCommand`); die Module
laufen an den echten Kommandozentralen mit (Nachladeuhren, Science-Wecker), ihre Wirkung ist durch `Tests.SpecialPower.cs` belegt
(Fixture mit neun Kräften: Ablehnungen, Nachladen, OCL-Ursprünge, Science-Umschaltung, Pause/Unpause, geteilte Timer,
Kopfgeld/Cash-Hack-Beträge, Snapshotfortsetzung, Replay-Rundlauf des Befehls; Realdatenprüfung der 80 Definitionen).

## 7. Sieg und Niederlage

- `SimulationWorld.IsPlayerDefeated(player)` entspricht `VictoryConditions::hasSinglePlayerBeenDefeated` mit den
  Standardflaggen `NOBUILDINGS|NOUNITS` = `!Player::hasAnyObjects`. `Team::hasAnyObjects` (`Team.cpp`) überspringt
  effektiv tote oder zur Zerstörung angemeldete Objekte sowie Projektile, `INERT`- und `MINE`-Objekte; genau diese
  Ausnahmen gelten hier (`CountsForSurvival`). Ohne sie überlebt jede Fraktion durch Minen, Trümmer und Projektile ewig –
  der Referenzmatch endete dann nie.
- `SimulationWorld.TryGetWinner()` ist „letzter Überlebender" (**bewusste Vereinfachung**: keine Teams/Allianzen in
  Phase 5 – ein Team-Sieg im Original zählt mehrere Spieler als eine Einheit): genau ein Nicht-System-Spieler mit
  mindestens einem Objekt, sonst `null`.

## 7a. Referenzmatch und Golden-Master-Szenarien

**Szenario** (`src/CnCNew.Simulation/Testing/ReferenceMatchScenario.cs`, Aufruf `SimTool reference-match <Inhalt>`): ein
scriptloser, seedbasierter Befehlsgenerator spielt alle teilnahmefähigen Fraktionen gegeneinander auf einer hindernisfreien
Karte, die nur als Koordinaten existiert, und zeichnet die Befehle als Replay (Format v2, mit Fraktionen und Checkpoints) auf.
Das Match endet mit dem Sieg (§7) plus `GraceTicks` Nachlauf, spätestens bei `MaxTicks` (Standard 216.000); `MinTicks`
füllt das Replay bei Bedarf mit Leerticks auf. Der Generator schreibt ausschließlich typisierte Befehle: Startobjekte per
`SpawnCommand`, Einkommen per `GiveMoneyCommand` (Ersatz für Supply, das es noch nicht gibt), Produktion per
`QueueProductionCommand`, Bewegung per `SetMoveTargetCommand`, Feuer per `FireWeaponCommand`.

- Teilnehmer ist jede spielbare Fraktion (`PlayableSide`), deren Seite mindestens eine instanziierbare, bewaffnete,
  baubare Einheit stellt und die ein instanziierbares Startgebäude oder einen **ausdrücklich protokollierten Ersatz** hat
  (ein instanziierbares Gebäude derselben Seite mit `ProductionUpdate`; der Grund steht in `MatchParticipant.Substitution`).
- Ausgeschlossene Fraktionen erscheinen mit Begründung im Bericht; nichts wird still übergangen. Bei weniger als zwei
  Teilnehmern verweigert `Record` die Aufnahme, `SimTool reference-match` liefert Exitcode 3 und den Abdeckungsbericht.
- Zwei Aufnahmen desselben Szenarios liefern byteidentische Replays; zwei frische Läufe und ein Lauf mit Snapshot bei einem
  Viertel der Länge sind tickweise hashgleich; alle Checkpoints verifizieren; kein aufgezeichneter Befehl wird abgelehnt.

**Nachweis** (`Tests.ReferenceMatch`): synthetische Fixture mit zwei Seiten, drei teilnehmenden Fraktionen (eine mit
Ersatzgebäude, weil ihre Startbasis ein ungebundenes Modul enthält), einer wegen fehlender Einheit ausgeschlossenen und einer
unspielbaren Fraktion. Das Match endet mit einem Sieger, das Replay läuft durch Datei, zwei Läufe und Snapshot identisch.

**Retail-Daten.** Neu gemessen (`SimTool reference-match .local/phase2-import-a`, Basisdaten ohne Kartenoverlays,
Fingerabdruck `7110C5BA69917279`): **alle 13 spielbaren Fraktionen nehmen teil**, jede mit ihrer **echten Kommandozentrale**
(`AmericaCommandCenter`, `Infa_ChinaCommandCenter`, `GLACommandCenter`, …; seit §6d gibt es keinen protokollierten Ersatz mehr),
der Match läuft deterministisch. Übersprungen werden 13 Startobjekte (9 Bauarbeiter-Fahrzeuge, 4 GLA-Arbeiter; Dozer-/
Arbeiter-KI fehlt). Ergebnis mit `GraceTicks` 300: Sieger Spieler 1 (FactionAmerica) bei Tick 11.385 (6,3 Spielminuten),
Replay 11.686 Ticks (462.194 Byte, 11 Checkpoints), 13.978 Befehle (625 Produktions-, 2.153 Bewegungsaufträge, 10.875 Schüsse), alle
Checkpoints verifiziert, keine Ablehnung außer 43 `UnknownEntity` (Befehle an inzwischen zerstörte Objekte), Endhash
`83EBD54549BB1DB5`, Kettenhash `928D78E6DD172836`; ein zweiter frischer Lauf und der Lauf ab Snapshot bei Tick 5.843 sind
tickweise identisch. Endzustand: 25 Objekte, 382 erzeugt, 357 zerstört; Überlebende: 21 America, je 1 der vier GLA-Seiten. Der Realdatentest `Tests.RealReferenceMatch` prüft Teilnehmerzahl,
Sieger, Determinismus und Snapshotfortsetzung. Die Abdeckungslage je Seite liefert `--diagnose`; die Aufgabe „Referenzmatch
aller Fraktionen auf Retail-Daten“ ist damit **erfüllt, mit echten Kommandozentralen**. Grenzen: kein Dozer/Arbeiter, kein Supply
(Einkommen per `GiveMoneyCommand`), keine Spezialkräfte im Befehlsstrom (§6d).

**Golden Master** (`tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs`): die erwarteten Werte berechnet ein unabhängiges
Modell im Test, sie werden nicht aus der Simulation zurückgelesen.

- Schadenskette: `Armor.cpp ArmorTemplate::adjustDamage` (Schaden · Koeffizient, unter 0 auf 0, `UNRESISTABLE` umgeht die
  Rüstung), `Weapon.cpp Weapon::privateFireWeapon` (nächster Schuss nach `DelayBetweenShots`, nach leerem Magazin nach
  `ClipReloadTime`), `ActiveBody.cpp attemptDamage` (Tod bei 0 Lebenspunkten). Geprüft werden die Schussticks und der Tod
  mit und ohne Rüstung; Millisekunden werden mit 30 Ticks je Sekunde umgerechnet.
- Produktionsuhr: `ThingTemplate::calcTimeToBuild` = `BuildTime · LOGICFRAMES_PER_SECOND`; aufeinanderfolgende Einheiten der
  Warteschlange werden genau eine Bauzeit nacheinander fertig; die Kosten verlassen das Konto beim Einreihen.
- Die Koeffizienten der Tests (100 %, 50 %) sind in Fließkomma und Festkomma exakt; die dokumentierte Abweichung
  (Festkomma statt `Real`) wird dadurch nicht maskiert, aber auch nicht gegen das Original gemessen – es gibt keine
  Originalläufe als Referenz, nur die aus dem Quelltext abgeleitete Rechnung.
- **Nicht erreicht:** Supply-Einnahmen und Erfahrung (kein Supply-/Erfahrungssystem, §10).

## 8. Befehlstabelle

Siehe `docs/SIMULATION-CORE.md` §6 für die vollständige, versionierte Tabelle (17 Befehlscodes, `CommandTypes.Version = 5`) und die Ablehnungsgründe (Codes 25–29 für Spezialkräfte, §6d); das Replay-Format v2 (`docs/SIMULATION-CORE.md` §8) kodiert alle 17.

## 9. Modulabdeckung

Siehe `docs/SIMULATION-CORE.md` §11: 1.224 von 2.115 Objekten sind heute instanziierbar (57,9 %, gegenüber 26/2.113 in
Phase 4, 863/2.115 vor §6c und 1.199 vor §6d); 60 von 205 Modultypen sind implementiert und decken 15.078 von 19.553
Modulverwendungen (77,1 %). Nach einer frischen `audit-coverage`-Messung sind die größten verbleibenden Blocker nach Objekten
(davon nur durch dieses Modul blockiert): `CommandButtonHuntUpdate` 79/10, `ToppleUpdate` 76/66 (braucht Kollisionsphysik,
Phase 6), `SpecialAbilityUpdate` 64/2, `JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CrushDie` 42/39,
`CountermeasuresBehavior` 40/0, `FireWeaponUpdate` 36/26, `SpawnBehavior` 35/6, `AutoFindHealingUpdate` 34/10,
`JetAIUpdate` 34/2, `HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27,
`RailroadBehavior` 25/25, `BaikonurLaunchPower` 23/23, `ReplaceObjectUpgrade` 20/16, `RepairDockUpdate` 17/16. Das
Spezialkraft-Subsystem (§6d) ist damit vollständig; die Fähigkeiten selbst (`SpecialAbilityUpdate` und die Handler der
Einheitenfähigkeiten) fehlen noch.

## 10. Bekannte Grenzen (Zusammenfassung)

Jede Zeile ist eine bewusste, dokumentierte Vereinfachung, keine stille Lücke:

- Kein Flächenschaden, keine Projektil-Flugzeit, keine `WeaponBonus`-Multiplikatoren (§3).
- Nur zwei `ArmorSet`/`WeaponSet`-Bedingungen statt der vollen veteranenbasierten Bestauswahl (§3).
- Keine Erfahrungs-/Veteranenvergabe aus Kämpfen (§3). Das Furchtsignal (Block 0.3) ist implementiert.
- Keine drehratenbegrenzte Ausrichtung, kein Terrain, keine Pfadsuche, keine Kollision (§4).
- Kein Energie-Verlangsamungsfaktor, keine Bauplatzprüfung, kein Rallypunkt (§5); Kollision, Kräfte und Terrain fehlen,
  die Module `PhysicsBehavior`/`SquishCollide` sind daher Ruhezustand (§6c).
- Der `STEALTHED`-Statusbit hat noch keinen Verbraucher außer `StealthDetectorUpdate`s `DETECTED`-Markierung (kein
  Zielsystem, das `DETECTED` auswertet, §6).
- Keine Teams/Allianzen/Diplomatie als Datenmodell (`GarrisonContain`/`StealthDetectorUpdate` behelfen sich mit
  „gleicher Besitzer = Verbündeter"), keine `CommandSet`/`CommandButton`-Wirkung (reine Präsentation), kein
  Transport/Supply/Radar/Reparatur/Verkauf/Bau (Rest von HANDOFF-PHASE5.md Punkt „Vollständiger Umfang"). Garrison
  (§6a) ist implementiert. Sciences (Kauf, Voraussetzungen) sind implementiert; „Science Purchase Points" werden
  mangels Rang-/Erfahrungssystem nur per Systembefehl gutgeschrieben (§1a).
- Spezialkräfte (§6d): kein Bauvorgang, kein Deaktiviert-Zustand, keine Kartenrand-Lieferung (Kanonenboote, Randerzeugung nur als
  `CreationSkipped`), keine Kanonenboot-KI, keine Sichtobjekte/EVA/Skriptmeldungen, Kopfgeld ohne Teammodell.
- Referenzmatch auf Retail-Daten mit den echten Kommandozentralen (ohne Dozer/Arbeiter, ohne Supply, ohne Spezialkräfte im
  Befehlsstrom; Einkommen per `GiveMoneyCommand`), 13 von 13 Fraktionen (§7a); Golden-Master nur für
  Schadenskette und Produktionsuhr, nicht für Supply und Erfahrung. Die hier beschriebenen Mechaniken sind zusätzlich durch
  `tests/CnCNew.Simulation.Tests/Tests.Phase5.cs` (gezielte Integrationstests: Wirtschaft, Kampf, Upgrade-Effekte, Bewegung,
  Produktion, Stealth/Heilung, Sieg) und den 216.000-Tick-Stresstest mit den neuen Modulen an echten Objekten abgesichert.
