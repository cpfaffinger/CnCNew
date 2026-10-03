# Spielregeln (Phase 5, abgeschlossen; Ergänzungen der Phase 6 in §11)

Stand: 30. September 2026 (Phase 6 ergänzt Gelände, Bau und KI, siehe §11 und `docs/SIMULATION-CORE.md` §13–§20; der übrige Text beschreibt den Stand von Phase 5, soweit §11 ihn nicht ersetzt). Dieses Dokument beschreibt die auf dem deterministischen Simulationskern
(`docs/SIMULATION-CORE.md`) aufsetzenden Spielregeln, die Phase 5 implementiert hat: Wirtschaft, Upgrades, direkter Kampf
mit Flächenschaden, einfache Bewegung, Produktion, die Blockermodule aus §3/§6/§6a (Schadenszustände, Brand, Gift, Tod,
Stealth-Erkennung, Garnison-Containment), das Spezialkraft-Subsystem (§6d), die Versorgungswirtschaft mit Andockmaschine
und Spawnern (§6e), Reparatur (§6f), Erfahrung und Veteranenstufen (§6h) sowie Teams und Diplomatie (§7). Phase 5 gilt als
abgeschlossen, weil Supply, Erfahrung und Teams stehen und der Referenzmatch auf Retail-Daten mit 0 übersprungenen
Startobjekten läuft (§7a); die bewussten Grenzen stehen in §10, der Folgeauftrag stand in `docs/HANDOFF-PHASE7.md` (Phase 6 ist erledigt, der nächste Auftrag ist `docs/HANDOFF-PHASE8.md`).

Alle Regeln laufen innerhalb der bestehenden Tickphasen (`SIMULATION-CORE.md` §4): neue Befehle werden in Phase 1
ausgeführt, neue Module ticken in Phase 3 wie jedes andere Updatemodul. Es gibt keine zusätzliche Tickphase.

## 1. Spieler und Wirtschaft

- `SimulationWorld` hält pro Spieler ein Guthaben (`Fixed`, `Money::withdraw`-Semantik: `WithdrawMoney` klemmt auf das
  verfügbare Guthaben und gibt den tatsächlich abgehobenen Betrag zurück) und eine inkrementell gepflegte
  Energiebilanz (`PlayerState.EnergyProduction`, Summe von `ObjectTemplate.EnergyProduction` aller eigenen Objekte,
  analog zu `Energy::objectEnteringInfluence`/`objectLeavingInfluence` – bei Spawn/Zerstörung fortgeschrieben, nie pro
  Frame neu berechnet). Negative Werte sind Nettoverbraucher.
- `GiveMoneyCommand` (nur System) schreibt Guthaben direkt gut, für Szenarien mit Startkapital und für Tests; das Einkommen des
  Referenzmatches kommt seit §6e aus der Versorgungswirtschaft.
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
- Teams und Diplomatie stehen seit §7 als Datenmodell; **Bekannte Lücke:** `CommandSet`/`CommandButton` bleiben reine Präsentation
  ohne Simulationswirkung (auch `CommandSetUpgrade` ist ein No-Op).

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
  | `WeaponBonusUpgrade` | setzt die Waffenbonusbedingung `PLAYER_UPGRADE` (§6h) | `WeaponBonusUpgrade.cpp` |
  | `LocomotorSetUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (nur `SET_NORMAL` modelliert) | – |
  | `StealthUpgrade` | setzt den `CAN_STEALTH`-Statusbit | `StealthUpgrade.cpp` |
  | `MaxHealthUpgrade` | hebt `MaxHealth` um `AddMaxHealth`, vier `ChangeType`-Varianten (§3) | `MaxHealthUpgrade.cpp`, `ActiveBody.cpp::setMaxHealth` |
  | `RadarUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (kein Radar/Shroud) | `RadarUpgrade.cpp` |
  | `CommandSetUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (UI-Präsentation) | `CommandSetUpgrade.cpp` |
  | `ExperienceScalarUpgrade` | erhöht den Erfahrungsskalar des Objekts (§6h) | `ExperienceScalarUpgrade.cpp` |
  | `ModelConditionUpgrade` | Zustandsmaschine läuft, Effekt bewusst No-Op (reine Präsentation) | `ModelConditionUpgrade.cpp` |
  | `ObjectCreationUpgrade` | fordert die Erzeugungsliste per Event an (wie `CreateObjectDie` in Phase 4) | `ObjectCreationUpgrade.cpp` |

  Jeder „bewusste No-Op" ist ein Modul, dessen Auslöse-/Konflikt-/Entfernungslogik korrekt läuft (andere Module, die
  vom selben Upgradenamen abhängen, sehen den richtigen Zustand), dessen Wirkung aber ein noch nicht existierendes
  Teilsystem beträfe – niemals ein unbekannter, still verworfener Schlüssel.

## 3. Waffen, Schaden, Rüstung

- `WeaponCatalog`/`ArmorCatalog` (`src/CnCNew.Simulation/Templates/WeaponCatalog.cs`) lösen `Weapon`- und
  `Armor`-Definitionen in Simulationswerte auf. Seit Phase 8 fliegt ein Schuss: verzögerter Treffer nach `WeaponSpeed` oder ein Projektilobjekt
  (`DumbProjectileBehavior`/`MissileAIUpdate`, §6i); Flächenschaden (`PrimaryDamageRadius`, `SecondaryDamage`, `RadiusDamageAffects`) steht seit §6g, die
  `WeaponBonus`-Multiplikatoren seit §6h.
- `ArmorInfo.Adjust` portiert `ArmorTemplate::adjustDamage`: `Schaden *= Koeffizient[Schadensart]`, außer bei
  `DAMAGE_UNRESISTABLE`/`DAMAGE_SUBDUAL_UNRESISTABLE` (umgehen die Rüstung vollständig), Ergebnis nie negativ.
- **`ArmorSet`/`WeaponSet`-Auswahl:** Das Original wählt über einen `SparseMatchFinder` die beste zu den aktuellen
  Bedingungen (Veteranenstufe, Upgrades, Second-Life, Kistenupgrades, …) passende Menge. Phase 5 modellierte nur zwei
  Bedingungen: `Conditions = None` (Standard) und `Conditions = PLAYER_UPGRADE`. Seit Phase 10 kommen `VETERAN`/`ELITE`/`HERO` als weitere,
  je für sich stehende Bedingungen hinzu (`ObjectTemplate.VeteranArmor`/`VeteranWeapon`, ausgewählt nach `Entity.VeterancyLevel` in
  `SimulationWorld.ActiveArmor`/`ActiveWeaponIndex`); nicht modelliert bleibt die Kombination mehrerer gleichzeitiger Bedingungen (ein Satz, der
  z. B. `PLAYER_UPGRADE` UND `VETERAN` zugleich verlangt) – geprüft gegen `.local/phase2-import-a`: **kein einziges `ArmorSet`/`WeaponSet` der
  Retail-Zero-Hour-Daten kombiniert eine Bedingung mit der Veteranenstufe**, die Einschränkung hat also auf den echten Bestand keine Auswirkung.
  Die Veteranenstufe wirkte zuvor nur über Waffenbonus und Gesundheit (§6h), jetzt zusätzlich über die Satzauswahl selbst.
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
- Erfahrung und Veteranenstufen aus Abschüssen stehen seit §6h. **Bekannte Lücken:** keine `IS_FIRING_WEAPON`/Retaliate-Logik; Projektile kollidieren nicht mit Objekten, an denen sie vorbeifliegen (§6i).

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
  Konflikt-Upgrade bringt ihn zum Schweigen). Das Feuern selbst löst nur ein `WeaponFireRequested`-Event aus – der Radiusschaden von §6g
  wird für diesen Todesschuss noch nicht ausgeführt (bekannte Lücke).

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
  Pfadsuche – so in Phase 5; **seit Phase 6** gilt für Einheiten mit `AIUpdateInterface` und Karte die Navigation (`SIMULATION-CORE.md` §14: A*, Drehrate, Geländehöhe), die geradlinige Bewegung bleibt für Welten ohne Gelände. Ohne Drehrate/Trigonometrie bleibt die Bewegung vollständig ganzzahlig/festkomma-basiert
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
  Kollisionsprüfung (Bauplatzprüfung und Sammelpunkt gibt es seit Phase 6 für Gebäude, §11), `QuantityModifier` und die Türanimationsfelder sind typisiert, aber
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
  selbst ab, sobald nichts mehr nachmarkiert. **Beziehungsprüfung:** seit §7 über das Beziehungsmodell (nur Feinde
  werden markiert); ohne
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
  `AllowAlliesInside`/`AllowEnemiesInside` (beide Standard `Yes`; seit §7 nach der Beziehung des Besitzers des Bewerbers zum
  Besitzer des Containers geprüft, ein neutraler Bewerber zählt weder als Verbündeter noch als Feind). Ein Gebäude in `REALLYDAMAGED` kann nicht mehr betreten werden und wirft beim Übergang **in**
  diesen Zustand sofort alle Insassen aus (beides entfällt bei `KindOf = GARRISONABLE_UNTIL_DESTROYED`).
- **Heilung** (`HealObjects`): ein Splitter `MaxHealth / TimeForFullHeal` pro Tick, ab der vollen Verweildauer ein
  harter Sprung auf volle Gesundheit (`GarrisonContain::healSingleObject`).
- **Tod des Containers:** `DamagePercentToUnits` wird auf die aktuelle Gesundheit jedes Insassen angewendet, danach
  werden Überlebende ausgeworfen (nicht getötet – `OpenContain::onDie` ruft `removeAllContained`, nie
  `killAllContained`, solange `killRidersWhoAreNotFreeToExit` beim Basisverhalten bleibt, wie bei `GarrisonContain`).
- **Bewusst nicht modelliert:** `InitialRoster` (Startbesatzung – bräuchte synchrones verschachteltes Erzeugen beim
  Konstruieren, eine größere Änderung an der Spawn-Pipeline, als dieser erste Zuschnitt rechtfertigt), Feuern aus dem
  Container heraus, Garnisonspunkte/-positionen (art-abgeleitet, keine INI-Daten), das
  Apparent-Controlling-Player-Tausch-System, Eroberungsbehandlung (kein Eroberungssystem).
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
gemeldet, das Objekt aber nur erzeugt, wenn es instanziierbar ist. „Verbündeter" folgt seit §7 dem Beziehungsmodell; der
KI-Hook `notifyCrate` fehlt.

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

Elf Module und ein Befehl bilden das Spezialkraft-Subsystem. Es schaltet die **echten Kommandozentralen aller 13
Fraktionen** frei (vorher Kasernen als Ersatz, §7a). Referenz: `SpecialPower.cpp` (`SpecialPowerTemplate`, Feldtabelle),
`SpecialPowerModule.cpp`, `OCLSpecialPower.cpp`, `CashBountyPower.cpp`, `CashHackSpecialPower.cpp`,
`SpectreGunshipDeploymentUpdate.cpp`, `SpecialAbilityUpdate.cpp`, `Player.cpp` (`addScience`, `grantScience`,
`getOrStartSpecialPowerReadyFrame`, `expressSpecialPowerReadyFrame`), `ActionManager.cpp` (`canDoSpecialPower`) und
`Object.cpp` (`scoreTheKill`).

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
| `CashBountyPower` | Hebt die Kopfgeldrate des Besitzers auf `Bounty` (nur wenn er die Science der Kraft hat, beim Erscheinen und bei Science-Erwerb). Auszahlung: `ScoreTheKill` (`Object::scoreTheKill`/`Player::doBountyForKill`) zahlt dem Besitzer des Töters `ceil(BuildCost · Rate)` für den Kill eines feindlichen Objekts (§7; nicht `IGNORED_IN_GUI`, nicht im Bau, kein Systemspieler). |
| `CashHackSpecialPower` | Stiehlt `min(Betrag, Guthaben des Opfers)` vom Besitzer des Zielobjekts; Betrag = erste `UpgradeMoneyAmount`, deren Science der Dieb hat, sonst `MoneyAmount`. Ein Ortsziel bewirkt wie im Original nichts. |
| `SpecialPowerCreate` | Meldet die Kräfte des Objekts als entstanden (siehe oben). |
| `GrantUpgradeCreate` | Schließt das benannte Upgrade (spieler- oder objektgebunden) einmal ab, wenn das Objekt fertig ist. |
| `GrantScienceUpgrade` | Vergibt eine Science (`Player::grantScience`; eine nicht vergebbare Science, `IsGrantable = No`, wird verweigert). |
| `UnpauseSpecialPowerUpgrade` | Hebt eine Pause der benannten Kraft am Objekt auf. |
| `SpecialPowerCompletionDie` | Meldet dem Skript-System das Ende des Trägers; Skripte gibt es nicht, das Modul ist ausdrücklich inert. |
| `SpectreGunshipDeploymentUpdate` | Handler der Gunship-Kraft (erster Treffer nach Modulreihenfolge, `RequiredScience`). Das Kanonenboot ist eine fliegende Einheit mit eigener KI, die vom Kartenrand einfliegt; beides fehlt, daher `CreationSkipped(NoTerrainEdge)` und nur der Nachladestart des Trägers. |
| `SpecialAbilityUpdate` | Wie `SpecialAbility`: eine reine Kraft ohne eigene Wirkung hier (Phase-10-Hülle). Im Original trägt dieses Modul die befehlsknopfausgelösten Einheitenfähigkeiten (Ranger-Granaten, Colonel Burtons Sprengsatz, Jarmen Kells Scharfschuss, der GLA-Rebellin Gebäudeeroberung/Fallenlegen, der Hacker-Geldraub) mit einer eigenen Anreise-/Verpackungs-/Vorbereitungs-/Fähigkeitsobjekt-Zustandsmaschine (`SpecialAbilityUpdate.cpp`); keine dieser Phasen ist simuliert. Das Modul hält nur den geteilten Nachlademechanismus, damit das Objekt instanziierbar ist (`docs/SIMULATION-CORE.md` §11) und `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` die Kraft auslösen und nachladen lassen kann (`docs/SCRIPT-ENGINE.md` §5/§8). |

**Science-Vergabe** (`SimulationWorld.AddScience`, gemeinsam für Kauf und `GrantScienceUpgrade`): nach dem Eintragen werden
alle Kräfte der Objekte des Spielers, die diese Science verlangen, geweckt (`onSpecialPowerCreation` und sofort bereit,
`Player::addScience`).

**Nicht modelliert (bewusst, nicht still):** kein Bauvorgang (seit Phase 6 gibt es ihn für Dozer-Bauten, §11; jedes Objekt ohne `UnderConstruction` ist im Tick nach dem Spawn
fertig, `onBuildComplete`), kein Deaktiviert-Zustand (`isDisabled`-Prüfung), `createOwner = false` (der Besitzerwechsel von
`CreateFromList`), `OCLAdjustPositionToPassable`, Kanonenboot-KI und Kanteneinflug, Sichtobjekte (`ViewObject*`), EVA-Ansagen,
Sounds, öffentliche Timer und Skript-Benachrichtigungen. Die
Referenzmatch-Erzeugung setzt keine Spezialkräfte ein (der Befehlsgenerator erzeugt kein `UseSpecialPowerCommand`); die Module
laufen an den echten Kommandozentralen mit (Nachladeuhren, Science-Wecker), ihre Wirkung ist durch `Tests.SpecialPower.cs` belegt
(Fixture mit neun Kräften: Ablehnungen, Nachladen, OCL-Ursprünge, Science-Umschaltung, Pause/Unpause, geteilte Timer,
Kopfgeld/Cash-Hack-Beträge, Snapshotfortsetzung, Replay-Rundlauf des Befehls; Realdatenprüfung der 80 Definitionen).

## 6e. Versorgungswirtschaft, Andockmaschine und Spawner

Die Wirtschaft ersetzt den Übergangsbehelf `GiveMoneyCommand`: Versorgungszentren, Lager, Lastwagen und Arbeiter laufen auf den
echten Definitionen. Referenz: `DockUpdate.cpp`, `AIDock.cpp` (`AIDockMachine`), `StateMachine.cpp`
(`internalSetState`, `friend_checkForTransitions`), `SupplyCenterDockUpdate.cpp`, `SupplyWarehouseDockUpdate.cpp`,
`SupplyCenterCreate.cpp`, `SupplyWarehouseCreate.cpp`, `AutoDepositUpdate.cpp`, `SupplyTruckAIUpdate.cpp`, `WorkerAIUpdate.cpp`,
`ResourceGatheringManager.cpp`, `SpawnBehavior.cpp`. Die Schemas der neun Module sind streng typisiert (Katalog, `ModuleCatalog.cs`);
`ValuePerSupplyBox` (`GameData`) ist als ganze Zahl typisiert (Standard des Originals 100 ohne Eintrag, ausdrücklich gezählt).

**Andockmaschine** (`DockMachine.cs`, `AIDockMachine`). Zustände wie im Original (Approach, Enter, Dock, Exit, Rally); die
Übergangssemantik von `StateMachine` ist portiert: ein Zustand, der beim Betreten sofort Erfolg oder Misserfolg meldet, führt im selben
Aufruf weiter (bis zu 20 Ebenen). Die Andockpositionen tragen keine Geometrie (keine Knochen, kein Terrain): Eintritt, Dock, Ausgang und
Rallypunkt liegen an der Position des Andockenden, diese vier Zustände melden daher beim Betreten Erfolg; nur die Anfahrtsposition ist
eine echte Bewegung. Die Maschinensperre des Originals während der Dockbewegung entfällt (nichts setzt von außen einen Zustand).

**`DockUpdate`** (Basis der Andockmodule): Anfahrtsplätze (`NumberApproachPositions`, dynamisch bei −1 bzw. `HasApproachPositions =
No`), Freigabe (`isClearToApproach`), Platzreservierung und Vorrücken (`reserveApproachPosition`, `advanceApproachPosition`) und der
eine aktive Andockende. Ohne Knochengeometrie liegt jeder Anfahrtspunkt in der Mitte des Docks, um `majorRadius / 2` zum Andockenden
hin verschoben (der knochenlose Zweig von `computeApproachPosition`, ohne `findPositionAround`). `update()` macht den Besitzer des ersten
erreichten Platzes zum aktiven Andockenden, solange keiner aktiv ist. Plätze und Andockende, deren Entität gestorben ist, werden
freigegeben (das Original verlässt sich darauf, dass die Maschine des Andockenden das selbst tut). Das Dock schläft, solange kein Platz
und kein Andockender belegt ist, und wird durch die erste Reservierung geweckt.

| Modul | Verhalten |
| --- | --- |
| `SupplyCenterDockUpdate` | Der Andockende entlädt alle Kisten zu je `ValuePerSupplyBox` (`Player::getSupplyBoxValue`) plus seinem Upgrade-Bonus auf einmal; die Summe geht an den steuernden Spieler des Zentrums (`SupplyDeposited`-Ereignis mit Betrag). Nicht modelliert: `GrantTemporaryStealth`, Punktezähler, Schwebetext. |
| `SupplyWarehouseDockUpdate` | Gibt je Aktion eine Kiste ab, solange Vorrat da ist; ein `DeleteWhenEmpty`-Lager verschwindet mit der letzten Kiste. Die Nähe-Prüfung (der Andockende zuckt zufällig nach) entfällt; `setDockCrippled` (Kraft „Lager lähmen“) ist nicht modelliert. |
| `SupplyCenterCreate`, `SupplyWarehouseCreate` | Melden Zentrum bzw. Lager beim `ResourceGatheringManager` an; hier wird das Netz aus den Entitäten abgeleitet, daher bleibt nichts zu tun (bewusst zustandslos). |
| `AutoDepositUpdate` | Alle `DepositTiming` Ticks erhält der Besitzer `DepositAmount` plus den ersten Bonus, dessen Upgrade er besitzt. Neutrale und im Bau befindliche Objekte verdienen nichts (der Zähler läuft weiter). `InitialCaptureBonus` ist typisiert, aber ohne Wirkung (keine Eroberung). |
| `SupplyTruckAIUpdate` | `SupplyTruckStateMachine` (Warten, Sammeln, Abliefern, Zurückziehen), Übergangssemantik wie die Andockmaschine. Die Supply-Maschine läuft vor der Basis-KI, der Schlaf ist der kürzere beider Werte; ein untätiger Lastwagen schläft, bis ein Befehl oder ein Erzeugungshaken ihn weckt. Ohne Upgrade-Bonus. Das `forcedBusy` des Originals (vom Halt-Befehl gesetzt) entfällt. |
| `WorkerAIUpdate` | Wie der Lastwagen, mit Bonus nach `Upgrade_GLAWorkerShoes`; das Versorgungshirn läuft weiterhin, Bauen geschieht über die Baubefehle der Welt (§11). |
| `DozerAIUpdate` | Bewegung; seit Phase 6 bauen, reparieren und verkaufen Dozer über `ConstructCommand`, `RepairCommand`, `ResumeConstructCommand` (§11). Minen (die eigene Maschine des Dozers) fehlen weiterhin. |

**Versorgungsnetz** (`ResourceGatheringManager`): jedes Lager und jedes Zentrum ist für jeden Spieler sichtbar. Der Lastwagen wählt nach
quadrierter 3D-Distanz; Kandidaten werden in Entitäts-ID-Reihenfolge gescannt, damit Gleichstände deterministisch brechen (das Original
nutzt die Registrierungsreihenfolge). Das gemerkte Dock eines Spielerbefehls gewinnt bedingungslos (`findBestSupplyWarehouse`,
`findBestSupplyCenter`); sonst zählt das nächste Lager innerhalb der Suchdistanz, beim Zentrum ohne Distanzgrenze. Nach der Ablieferung
läuft der Lastwagen bei Bedarf zurück (`SupplyTruckRegroupingState`: nächster eigener Geldgenerator, sonst Kommandozentrale, sonst
irgendein Gebäude).

**`DockCommand`** (Code 18, `CommandTypes.Version = 6`): der Spieler schickt einen Andockenden zu einem Dock (`aiDock`); das Ziel wird als
bevorzugtes Dock gemerkt (ein Gedächtnis für Lager und Zentrum). Ablehnung `CannotDock` (30): das Ziel hat kein Dockmodul oder der
Akteur keine KI.

**`SpawnBehavior`** (`SpawnBehavior.cpp`; Schema mit allen Feldern typisiert): ein Erzeuger, der `SpawnNumber` Objekte am Leben hält und
seine Vorlagenliste der Reihe nach durchläuft. Er arbeitet alle `SPAWN_UPDATE_RATE` (15) Ticks, beginnend mit dem ersten Update; jeder
Spawn verlässt das Objekt über dessen `IProductionExit` (ohne Ausgangsmodul entsteht nichts, einmal gemeldet als `NoExitInterface`).
`SpawnReplaceDelay` verzögert den Ersatz; `OneShot` und `SpawnedRequireSpawner` sind portiert. Abweichungen (alle bewusst):
der Tod eines Spawns wird im 15-Tick-Rhythmus abgefragt statt über `onSpawnDeath` (Ersatz `SpawnReplaceDelay` nach dem Fund); der
Anfangsschub staffelt nicht (das Original berechnet einen Geburtsframe und benutzt ihn nie); Spawns sind nicht an eine Erzeuger-ID oder
ein `SlavedUpdate` gebunden (die Familie fehlt); eine nicht instanziierbare Vorlage wird als `CreationSkipped` gemeldet, verbraucht
ihren Platz und wird nie wiederholt. `AggregateHealth`, `ExitByBudding`, `CanReclaimOrphans` (ohne `OneShot`) und
`PropagateDamageTypesToSlavesWhenExisting` brauchen die Slaved-Familie (Mobs, Brutstätten): ein Objekt, das sie verlangt, behält das
Modul in `MissingModules`, statt falsch simuliert zu werden. `SlavesHaveFreeWill` ist gelesen, aber ohne Wirkung.

**Referenzmatch** (`ReferenceMatchScenario`): als Ersatz für den Bau setzt jede Basis ein Versorgungszentrum und einen Lastwagen (bzw.
Arbeiter) der eigenen Seite sowie ein neutrales Lager (`SpawnSupplyInfrastructure`); in Tick 1 schickt der Generator jeden Träger per
`DockCommand` zum nächsten Lager, danach läuft die Trägermaschine selbst. Eine Seite ohne eines der drei Elemente wird in
`SupplyNotes` vermerkt. Der Generator schreibt kein `GiveMoneyCommand` mehr. Gezählt werden `SupplyDeliveries` und
`SupplyDeliveryMoney`.

**Nachweis** (`Tests.Supply.cs`): Golden Master mit unabhängiger Nachrechnung (Anfahrt, Ladung, Ablieferung, Betrag), Warteschlange
mehrerer Träger an einem Lager, Befehlsprüfung, Snapshot- und Replayfortsetzung, die `SpawnBehavior`-Varianten und Realdatenprüfung; das
Realmatch verlangt null übersprungene Startobjekte und Lieferungen.

**Nicht modelliert (bewusst, nicht still):** Geometrie (Knochen, Terrain, Wege), `GrantTemporaryStealth`, Bau/Dozer/Reparatur durch
Arbeiter, Lager-Lähmung, Eroberungsbonus, Slaved-/Mob-Spawner.

## 6f. Reparatur und Heilung

Referenz: `RepairDockUpdate.cpp` (`action`), `AutoFindHealingUpdate.cpp` (`update`, `scanClosestTarget`), `ActionManager.cpp`
(`canGetHealedAt`), `AIUpdate.cpp` (`privateGetHealed`).

- **`RepairDockUpdate`** (Reparaturdock): die erste Aktion für einen neuen Andockenden legt den Betrag je Tick fest als
  `(Max − Gesundheit) / framesForFullHeal`; jede Aktion heilt diesen Betrag (Quelle: das Dock, `DAMAGE_HEALING`), und das Andocken endet,
  sobald der Andockende volle Gesundheit hat. `TimeForFullHeal` ist `parseDurationReal` (Bruchteile von Frames, Standard des Originals ein
  Frame). Nicht modelliert: die Drohnenheilung (die Kampfdrohne des Andockenden wird bei jeder Aktion voll geheilt; Drohnen gibt es nicht).
- **`AutoFindHealingUpdate`**: nur für Computerspieler (neu: `PlayerConfiguration.IsComputer`, Standard Mensch; Teil von Zustand,
  Snapshot v3 und Replay v4). Zwischen zwei Suchläufen liegen `ScanRate` Ticks; übersprungen wird, wenn das Objekt gesünder als
  `NeverHeal` ist oder beschäftigt. Der Zweig „bleib dran über `AlwaysHeal`“ liegt im Original hinter einem frühen Return: `AlwaysHeal` ist
  gelesen, aber wirkungslos. Der Lauf nimmt das nächste Objekt mit `KINDOF_HEAL_PAD` innerhalb `ScanRange` (Mittelpunktdistanz, Gleichstand
  nach ID, gleichgültig wem es gehört) und ruft `AIUpdateInterface.OrderGetHealed`; das geht nur zu einem verbündeten, fertigen,
  lebenden Pad und für verletzte Infanterie.
- **Neuer KI-Zustand `Enter`** (`OrderEnter`, `TryEnterContainer`): erst Bewegung, bei Ankunft der Eintritt in den Container bzw. das Dock.

**Nicht modelliert:** Verkauf (`Sell`), Reparaturaufträge des Dozers, Kranfahrzeuge, Drohnenheilung, `AlwaysHeal`.

## 6g. Flächenschaden, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CrushDie`

**Flächenschaden** (`SimulationWorld.DealWeaponDamage`, `SimulationWorld.Combat.cs`; Referenz: `Weapon.cpp`
`WeaponTemplate::dealDamageInternal`). Jede Waffe ohne Projektil (`FireWeaponCommand`, `FireWeaponUpdate`) verteilt jetzt ihren Schaden über einen Radius,
statt nur das Ziel zu treffen (`FireWeaponWhenDeadBehavior` meldet den Schuss weiter nur als `WeaponFireRequested`, §3):

- Abstand nach `DAMAGE_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_3D`: Mittelpunktdistanz minus Begrenzungskugel des Opfers, nicht unter 0. Wer
  innerhalb `PrimaryDamageRadius` liegt, erhält `PrimaryDamage`, wer weiter außen (bis zum größeren der beiden Radien) liegt,
  `SecondaryDamage`. Ohne jeden Radius wird nur das Primärziel getroffen.
- **`RadiusDamageAffects`** (typisierte Bitmaske: `SELF`, `ALLIES`, `ENEMIES`, `NEUTRALS`, `SUICIDE`, `NOT_SIMILAR`, `NOT_AIRBORNE`;
  Standard `ALLIES|ENEMIES|NEUTRALS`, also ohne den Schützen). Das Primärziel umgeht alle Filter; jedes andere Opfer wird nach Selbst,
  `NOT_SIMILAR` (gleiche Vorlage und verbündet), `NOT_AIRBORNE` und der Beziehung (§7) gefiltert. `SUICIDE` tötet den Schützen mit
  `HUGE_DAMAGE_AMOUNT`.
- Opfer werden in Entitäts-ID-Reihenfolge besucht (das Original iteriert die Partitionszellen); Insassen von Containern zählen nicht (außer
  als Primärziel). Der Rüstungsfaktor gilt je Opfer (`ActiveArmor`), der Rückgabewert ist der rüstungsbereinigte Schaden am Primärziel.
- **Nicht modelliert:** `RadiusDamageAngle`-Kegel, Schockwellen, `DamageStatusType`, historischer Schaden, die Erzeugerausnahme des
  Selbstfilters (seit Phase 8 für Projektile modelliert, §6i); Projektilflugzeit steht in §6i.

| Modul | Referenz | Verhalten |
| --- | --- | --- |
| `FireWeaponUpdate` | `FireWeaponUpdate.cpp` `update`, `isOkayToFire` | Nach `InitialDelay` detoniert die private Waffe des Moduls (eigenes Magazin und Nachladen, nie das Waffenset) an der Position des Objekts, sobald bereit; `forceFireWeapon` ignoriert Reichweite und Ziel, der Radius entscheidet. Nicht im Bau und nicht innerhalb `ExclusiveWeaponDelay` Ticks nach einem Schuss des Objekts (`getLastShotFiredFrame` betrachtet nur das Waffenset, die eigene Waffe unterdrückt sich also nie). |
| `ReplaceObjectUpgrade` | `ReplaceObjectUpgrade.cpp` `upgradeImplementation` | Das Objekt wird zerstört, ein frisches der Ersatzvorlage übernimmt Besitzer, Position und Ausrichtung (volle Gesundheit, kein Rang, keine Warteschlange). Eine unbekannte oder nicht instanziierbare Vorlage ergibt nur ein `CreationSkipped`-Ereignis (die Referenz bricht vor dem Zerstören ab). Nicht modelliert: Wegekartenpflege, `Player::onStructureConstructionComplete`. |
| `CrushDie` | `CrushDie.cpp` | Reagiert nur auf `DAMAGE_CRUSH` eines Überrollers (Vorder-/Hinterende, Ton, Modellbedingungs-Flags); Überrollschaden entsteht durch `PhysicsCollide`, und es gibt keine Kollisionserkennung. Zustandslos und inert (Präzedenzfall `SquishCollide`); Kollision ist Phase 6. |

Nachweis: `Tests.Combat.cs` (Ringe und Enemies-Filter, Standardmaske und Begrenzungskugel, `FireWeaponUpdate`-Takt, Magazin,
`ExclusiveWeaponDelay`, Snapshot, `ReplaceObjectUpgrade`, `CrushDie`). **Ausdrücklich nicht freigeschaltet** sind die verbleibenden
Blocker aus §9: sie brauchen Terrain, Kollision oder KI (Phase 6) oder schalten allein kein Objekt frei.

## 6h. Erfahrung und Veteranenstufen

Referenz: `ExperienceTracker.cpp`, `Object.cpp` (`onVeterancyLevelChanged`, `scoreTheKill`), `Weapon.cpp` (`WeaponBonusSet`,
`Weapon::getDamage/Range/DelayBetweenShots/ClipReloadTime`), `WeaponBonusUpgrade.cpp`, `ExperienceScalarUpgrade.cpp`,
`VeterancyGainCreate.cpp`, `GameData.ini` (`WeaponBonus`, `HealthBonus_*`).

- **Erfahrungswert.** Ein Tod gegen einen Feind (§7; nicht neutral, nicht verbündet, nicht im Bau, nicht `IGNORED_IN_GUI`, kein
  Systemobjekt) gibt dem Töter `ExperienceValue[Stufe des Opfers]`, multipliziert mit seinem `ExperienceScalar` (`(int)(Betrag · Skalar)`,
  abgerundet). Nur trainierbare Objekte (`IsTrainable`) sammeln Erfahrung. Die Stufe folgt `ExperienceRequired[…]` (4 Stufen: 0 normal,
  1 Veteran, 2 Elite, 3 Held); mehrere Stufen auf einmal sind möglich.
- **`SetVeterancyLevel`** setzt die Erfahrung auf die Mindestmenge der Stufe (`ExperienceTracker::setVeterancyLevel`).
  `SetMinVeterancyLevel` hebt nur an (`VeterancyGainCreate`, `InheritsVeterancy` einer Erzeugungsliste).
- **Stufenwechsel** (`OnVeterancyLevelChanged`): die Maximalgesundheit wird mit dem Verhältnis der `GameData`-Werte `HealthBonus_*`
  skaliert (`PRESERVE_RATIO`, die Gesundheit behält ihr Verhältnis), der Waffenbonuszustand VETERAN/ELITE/HERO folgt der Stufe, und das
  von der Engine erzeugte `Upgrade_Veterancy_*` der neuen Stufe wird vergeben (nicht kaufbar). Rüstungs- und Waffensatzauswahl nach Stufe
  ist nicht modelliert: kein ausgelieferter Satz verlangt sie.
- **`WeaponBonus`** (`GameData` global und je Waffe; streng typisiert als `<Bedingung> <Feld> <Prozent>`, gestützt auf die
  Namenstabellen des Originals, `WeaponBonusTable`): jede Zeile ergibt ein Delta (`Verhältnis − 1`), Zeilen einer aktiven Bedingung werden
  addiert; eine wiederholte Zeile (Bedingung, Feld) überschreibt (`parseWeaponBonusSet`). Felder: Schaden, Radius, Reichweite,
  Schussrate, Vorlaufzeit (`PreAttack` ist typisiert, aber ohne Verbraucher); Bedingungen u. a. `PLAYER_UPGRADE` (5), `VETERAN` (9), `ELITE` (10), `HERO` (11). Schaden, Radius, Reichweite,
  Schussverzögerung und Magazinnachladen (`DamageWith`, `RadiusWith`, `DelayBetweenShotsWith`, `ClipReloadWith`) wenden Bonus der
  Waffe und globalen Bonus an.
- **`WeaponBonusUpgrade`** setzt die Bedingung `PLAYER_UPGRADE` am Objekt (wie im Original wird sie nie wieder gelöscht), die `WeaponBonus
  PLAYER_UPGRADE …`-Zeilen der Waffen wirken damit (vorher No-Op). **`ExperienceScalarUpgrade`** erhöht den `ExperienceScalar` des
  Objekts um `AddXPScalar` (vorher No-Op). **`VeterancyGainCreate`** setzt bei Erzeugung `StartingLevel`, wenn die Science vorhanden ist.
- **Nachweis** (`Tests.Veterancy.cs`): Golden Master mit unabhängiger Nachrechnung von Erfahrung, Stufen, skalierter Gesundheit,
  Schussticks, Schaden und Reichweite je Stufe; Vergaberegeln, `ExperienceScalarUpgrade`, `WeaponBonusUpgrade`,
  `VeterancyGainCreate`, Snapshotfortsetzung.
- **Nicht modelliert:** Rang und „Science Purchase Points“ aus Erfahrung (Punkte kommen weiter per `GiveSciencePointsCommand`), dauerhafte
  Mindeststufe (`VeterancyGainCreate` setzt sie einmal), Erfahrungssenken (Heilung, Reparatur, Ausbildung durch Gebäude), Statistik/Skill Points.

## 6i. Projektil-Flugzeit (Phase 8, Paket E1)

Referenz: `Weapon.cpp` (`WeaponTemplate::fireWeaponTemplate`, `dealDamageInternal`, `WeaponStore::setDelayedDamage`/`update`/`handleProjectileDetonation`,
`Weapon::positionProjectileForLaunch`), `DumbProjectileBehavior.cpp`, `MissileAIUpdate.cpp`, `BezierSegment.cpp`/`BezFwdIterator.cpp`,
`PointDefenseLaserUpdate.cpp`. Umsetzung: `SimulationWorld.Projectiles.cs`, `SimulationWorld.Combat.cs`, `Modules/BuiltIn/ProjectileModules.cs`.

**Abschuss** (`SimulationWorld.FireWeaponAt`, genutzt von `FireWeaponCommand`, `FireWeaponUpdate` und `PointDefenseLaserUpdate`):

1. **Streuung:** Bei `ScatterRadius > 0` oder (`ScatterRadiusVsInfantry > 0` und Infanterieziel) wird der Radius (plus Infanteriezuschlag) mit
   `LogicRandom.Range(0, r)` und der Winkel mit `Range(0, 2π)` gezogen (zwei Züge des Logikstroms, wie `GameLogicRandomValueReal`); das Zielgebäude
   wird auf seinen Geometriemittelpunkt angezielt, der Punkt landet auf dem Boden. Die Streuung lenkt nur ein Projektil um; ein verzögerter Treffer behält
   sein Opfer (im Original ist `victimObj = NULL` auskommentiert).
2. **Ohne Projektilobjekt:** `delayInFrames = |Opferposition − Schützenposition| / WeaponSpeed` (3D). Unter einem Tick trifft der Schuss sofort
   (unverändert gegenüber Phase 5), sonst nach `ceil(delay)` Ticks: der Eintrag landet in der Liste der verzögerten Treffer (Reihenfolge des Einfügens,
   Teil von Hash und Snapshot) und wird **am Ende des Fälligkeitsticks nach der Zerstörungsphase** ausgewertet (`WeaponStore::update` läuft im Original
   nach `processDestroyList`). Getroffen wird das Opfer an seiner dann aktuellen Position (`victimID`), ist es fort, der gespeicherte Punkt;
   `DamageDealtAtSelfPosition` legt den Schaden auf die Schützenposition. Existiert der Schütze nicht mehr, gelten wie im Original **keine**
   `RadiusDamageAffects`-Filter. `WeaponSpeed ≤ 0` (drei schadenlose Retail-Waffen; das Original teilt durch null) trifft sofort.
3. **Mit Projektilobjekt:** Das Objekt der Vorlage `ProjectileObject` entsteht am Ende des Schusstick (Spawn-Queue, §4 der Kernbeschreibung) am
   Geometriemittelpunkt des Schützen (die Abschussknochen sind Grafikdaten) mit dessen Besitzer und Blickrichtung; unmittelbar nach seinen
   `OnSpawn`-Aufrufen erhält sein Projektilmodul (`IProjectileModule`) Waffe, Werfer, Opfer (bei Streuung keines), Zielpunkt und die
   Waffenbonusbedingungen des Werfers (`m_extraBonusFlags`). Die Startdaten stehen bis dahin im Spawn-Queue-Eintrag (Snapshot und Hash).
   Ein Objekt ohne Projektilmodul wird auf den Zielpunkt gesetzt („ok, für Dinge wie Firestorm“).
4. **Nicht instanziierbares Projektilobjekt:** nie still: `CreationSkipped` mit Grund 10 (`ProjectileNotInstantiable`, Text = Vorlage), danach
   verhält sich der Schuss wie einer ohne Projektil (Schritt 2). Auf Retail-Daten feuern 157 Waffen ein Projektilobjekt, 117 davon ein
   instanziierbares (Realdatentest), die übrigen 40 laufen über diesen Ersatz.

**Detonation** (`DetonateProjectile`): die Waffe detoniert an der Projektilposition **ohne** Primärziel (nur Flächenschaden), mit den Bonusbedingungen
des Abschusses; Quelle des Schadens ist der Werfer (`projectileGetLauncherID`), der Selbstfilter verschont Projektil und Werfer
(`getProducerID`). Ereignisse zur richtigen Position und zum richtigen Tick: `FxRequested` mit `ProjectileDetonationFX` (mit Position),
`ProjectileDetonationOCL` wird ausgeführt, `WeaponImpact` (Entity = Projektil, Other = Werfer, Position). Beim Abschuss meldet
`ProjectileLaunched` den Zielpunkt (bei verzögerten Treffern mit Landetick in `Value`); `WeaponFired` meldet nur noch den **sofort** zugefügten Schaden
(0 bei Flugzeit). Ereignisse mit Ort tragen `HasPosition` und X/Y/Z; nur sie schreiben die Position, alle übrigen Ereignisse sind unverändert serialisiert.
Die veteranenabhängigen Varianten (`VeterancyProjectileDetonationFX/OCL`), `FireOCL`/`FireFX` und `ProjectileExhaust` sind nicht modelliert.

**`DumbProjectileBehavior`** (Granaten auf Bézierbogen): Geschwindigkeit `WeaponSpeed`, mit `ScaleWeaponSpeed` linear zwischen `MinWeaponSpeed` und
`WeaponSpeed` nach der Lage der 2D-Distanz zwischen Mindest- und unveränderter Reichweite. Kontrollpunkte: Start, Ziel (Geometriemittelpunkt des Opfers
bzw. Zielpunkt) und zwei Punkte bei `FirstPercentIndent`/`SecondPercentIndent` der Strecke auf `FirstHeight`/`SecondHeight` über dem höchsten Gelände
zwischen Start und Ziel (nie unter einem der Enden). Punktzahl `n = ceil(Länge / Geschwindigkeit)` mit der Referenzlänge
(`getApproximateLength`: Halbierung, bis das Kontrollpolygon höchstens 1 länger als die Sehne ist, dann Mittel aus beiden). Update k (k = 1 … n) setzt
das Projektil auf Punkt k − 1, das Update danach (oder nach `MaxLifespan`, Standard 300 Ticks) detoniert: **Einschlag im Tick F + n + 1** nach dem
Schusstick F. `FlightPathAdjustDistPerSecond` zieht den Endpunkt dem Opfer nach (höchstens so weit je Tick) und rechnet die Kurve mit gleicher
Punktzahl neu. `DetonateCallsKill` tötet mit `DEATH_DETONATED`, sonst Zerstörung. `TumbleRandomly` zieht drei Logikzahlen, wenn ein
`PhysicsBehavior` da ist (Drehraten sind Präsentation). **Festkomma:** Die Punkte werden aus der Bernsteinform bei `t = i/(n−1)` berechnet (das Original
summiert Vorwärtsdifferenzen in binary32, nicht reproduzierbar); Anfangs- und Endpunkt sind exakt. `n = 1` (im Original undefiniertes Verhalten:
`BezFwdIterator` lässt den Punkt im Ursprung, die Ausrichtung liest hinter das Feld) fliegt direkt zum Endpunkt. Das höchste Gelände wird alle halbe Zelle
abgetastet (`estimateTerrainExtremesAlongLine` läuft über Partitionszellen); ein Endpunkt außerhalb der Karte zerstört das Projektil.

**`MissileAIUpdate`** (gelenkte Raketen) als kinematische Zustandsmaschine statt THRUST-Lokomotor und Physik:

| Zustand | Verhalten |
| --- | --- |
| Launch | gleitet in Startrichtung (Blickrichtung des Werfers, angehoben um `2·max(0, dz)/max(1, xy)` und normiert) mit `InitialVelocity` (mit `UseWeaponSpeed` die Waffengeschwindigkeit, die dann auch Höchstgeschwindigkeit und Beschleunigung ist), bis `IgnitionDelay` Ticks vergangen sind |
| Ignition | schärft den Gefechtskopf, fordert `IgnitionFX` an, Treibstoff für `FuelLifetime` Ticks (0: unbegrenzt), weiter ohne Drehen |
| Attack (ohne/mit Drehen) | beschleunigt mit der Lokomotorbeschleunigung bis zur Lokomotorgeschwindigkeit; ohne Drehen, bis `DistanceToTravelBeforeTurning` geflogen ist (gemessen vor der Bewegung, wie das Original), danach Richtung direkt aufs Ziel (`setMaxTurnRate(BIGNUM)`): die Position des verfolgten Opfers (`TryToFollowTarget`), sonst der Zielpunkt 10 über dem Boden (`APPROACH_HEIGHT`, nur mit Sperrdistanz) |
| Kill | innerhalb der Sperrdistanz (`DistanceToTargetForLock`, Standard 75, 2D; bei einem Punktziel halbiert und wieder auf den Bodenpunkt gerichtet) direkt aufs Ziel; wer es erreicht, detoniert dort |
| KillSelf | nach der Detonation `KillSelfDelay` Ticks (Standard 3) warten, dann `DetonateCallsKill` → Tod, sonst Zerstörung |

Ohne Treibstoff detoniert die Rakete (`DetonateOnNoFuel`) oder gleitet ohne Schub und Drehung unter der Schwerkraft (`GameData.ini Gravity`, −64/s² =
−4660 Rohwert je Tick²) weiter. Ist das verfolgte Opfer fort, läuft der Treibstoff sofort aus und die Rakete verschwindet ohne Schaden
(`airborneTargetGone`). `MissileCallsOnDie` der Waffe (`m_dieOnDetonate`) tötet die Rakete bei der Detonation. **Stellvertreter für Kollisionen**
(es gibt keine, Punkt 9 des Handoffs): eine geschärfte Rakete, die ihren Zielpunkt in einem Angriffszustand erreicht oder unter den Boden geriete,
detoniert dort; eine ungeschärfte gleitet am Boden entlang; unter Höhe 0 wird sie zerstört. Nicht modelliert: Täuschkörper, Störsender
(`DistanceScatterWhenJammed`), Marschflughöhe (`DistanceToTargetBeforeDiving`), Brückenebenen, `GarrisonHitKill*` (braucht Kollision mit Gebäuden).
Alle Vektoren sind `Fixed`, Richtungen werden mit der Festkommawurzel normiert.

**`PointDefenseLaserUpdate`** (Paladin, Avenger, Air-Force-Chinook): alle `ScanRate` Ticks wählt es das nächste Feindobjekt der primären Arten
(`BALLISTIC_MISSILE SMALL_MISSILE`) in Laserreichweite, sonst der sekundären, sonst das nächste außerhalb der Reichweite, aber innerhalb `ScanRange`
(2D, `FROM_CENTER_2D`; gleiche Abstände nach Entity-ID, weil die Partitionsreihenfolge nicht reproduzierbar ist); nicht entdeckte Tarnung wird übergangen,
Bodenziele nur mit `AntiGround`. Zwischen den Scans feuert es, sobald der Laser bereit ist (`DelayBetweenShots` ohne Bonus), über `FireWeaponAt`; ein
getötetes oder entwichenes Ziel wird verworfen und der nächste Scan nach `LogicRandom.Range(0, 3)` Ticks angesetzt. `PredictTargetVelocityFactor` hat im
Original keine Wirkung (die vorhergesagte Position wird berechnet, aber nicht benutzt).

**Nachweis** (`Tests.Projectiles.cs`, Handrechnungen): gerade Granate 200 Einheiten bei 300/s = 20 Punkte, Einschlag F + 21 mit Wirkungsereignis und
Detonations-FX am Zielpunkt; Bogen 120 Einheiten (Höhen 30/30 bei 25 %/75 %, unabhängig in binary64 nachgerechnete Länge 131,43) = 14 Punkte,
Einschlag F + 15, Punkt 5 bei (44,515; 21,302), Scheitel unter dem analytischen 22,5; Rakete ohne Zündverzögerung (10 je Tick sofort) Einschlag F + 10,
Entfernung F + 13; Rakete mit 3 Ticks Zündverzögerung, 2 je Tick Anfangsgeschwindigkeit und 1 je Tick² Beschleunigung mit allen 14 Positionen und
Einschlag F + 15; verzögerter Treffer 150 Einheiten bei 600/s = F + 8, unter einem Tick sofort; Streuung deterministisch innerhalb des Radius; der
Punktverteidigungslaser schießt die Rakete ab; Snapshot mitten im Flug (Granate, Rakete, verzögerter Treffer) und zwei Läufe hashgleich.
Realdaten (`Tests.RealFlight.cs`): die erste instanziierbare Einheit mit Granate (`AmericaTankCrusader`/`GenericTankShell`, Einschlag nach 11 Ticks) und die
erste mit Rakete (`AirF_AmericaVehicleTomahawk`/`TomahawkMissile`, 45 Ticks) treffen einen Crusader, zweimal hashgleich.

## 6j. Transporter, Tunnelnetz und Chinook-Versorgung (Phase 8, Paket E1)

Referenz: `TransportAIUpdate.cpp`, `ChinookAIUpdate.cpp`, `SupplyTruckAIUpdate.cpp`, `TunnelContain.cpp`, `TunnelTracker.cpp`, `HelixContain.cpp`,
`HelicopterSlowDeathUpdate.cpp`, `OpenContain.cpp`. Umsetzung: `Modules/BuiltIn/TransportModules.cs`, `World/TunnelTracker.cs`,
`SimulationWorld.Containers.cs`, `AIUpdateInterface` (Evakuierung).

**Befehle** (`CommandTypes.Version = 9`): `MoveAndEnterCommand` (26, `aiEnter`): ein eigenes bewegliches, nicht enthaltenes Objekt läuft zum Container
und steigt ein, wenn es ihn wirklich erreicht hat (Umkreis beider Objekte plus zwei Zellen; ein Teilpfad, der zu früh endet, steigt nicht mehr
teleportierend ein) und der Container es dann noch aufnimmt (Kapazität bei Ankunft). `EvacuateCommand` (27, `MSG_EVACUATE`/`aiMoveToAndEvacuate`): der
Besitzer lässt alle Insassen aussteigen; ein beweglicher Container fährt mit Position zuerst dorthin, ein Chinook landet vorher, Gebäude und Tunnel
lassen sofort aus; ein Tunnel entlässt das **ganze Netz** durch sich. Der Evakuierungsauftrag ist ein Zustand der KI (`AI_EVACUATE`: nicht untätig),
den jeder neue Auftrag ersetzt. Das sofortige `EnterContainerCommand` (15) und `ExitContainerCommand` (16) bleiben unverändert.

**`TransportAIUpdate`:** ein `AIUpdateInterface`; die Weitergabe von Angriffsbefehlen an feuerberechtigte Insassen (`privateAttackObject`) entfällt, weil
es kein Feuern aus Containern gibt. Laden/Entladen über die Befehle oben, Aussteigen immer erlaubt (`FREE_TO_EXIT`).

**`ChinookAIUpdate`:** ein `SupplyTruckAIUpdate` (dieselbe Versorgungsmaschine, §6e) mit eigenen Dockverzögerungen und `UpgradedSupplyBoost`, sobald der
Spieler `Upgrade_AmericaSupplyLines` besitzt (`getUpgradedSupplyBoost`). Er fliegt „auch wenn er am Boden startet“: ein neuer Chinook steigt sofort auf
`PreferredHeight` über Grund (Chinook 100), Höhenänderung um eine Lokomotorgeschwindigkeit je Tick (die Referenz lässt den Lokomotor mit `Lift` steigen,
`ChinookTakeoffOrLandingState`); Versorgung läuft im Flug über dem Lager. Insassen verlassen nur einen gelandeten Chinook (`getAiFreeToExit`): eine
Evakuierung senkt ihn bis zum Boden, entlädt und lässt ihn wieder steigen. Einsteigen geht ohne Landung (Vereinfachung). Nicht simuliert:
Abseilen (Combat Drop), Heilen am Flugfeld, Wegflug vom Kartenrand, `isAvailableForSupplying` (ein beladener Chinook versorgt trotzdem).

**`HelicopterSlowDeathBehavior`:** nimmt an derselben gewichteten Geschwisterlotterie teil wie `SlowDeathBehavior` (gemeinsame Schnittstelle
`ISlowDeathCandidate`); nach dem Zuschlag (zwei Varianzzüge des Grundmoduls plus ein `Real`-Zug für den Rotorabwurf) dreht sich der Hubschrauber um
seinen Eigendrall (Pendeln zwischen `MinSelfSpin` und `MaxSelfSpin` alle `SelfSpinUpdateDelay`), fliegt entlang seines Orbitwinkels mit gedämpfter
`SpiralOrbitForwardSpeed` (der Winkel dreht um `SpiralOrbitTurnRate`) und fällt mit `FallHowFast` der Schwerkraft (Auftrieb `−g·(1 − FallHowFast)`).
Nach der gezogenen Zeit fliegt der Rotor ab (`FXBlade`, `OCLBlade`, ab Veteran `OCLEjectPilot`); eine Einheit über dem Boden schlägt er auf
(`FXHitGround`, `OCLHitGround`), und im ersten Tick mehr als `DelayFromGroundToFinalDeath` später explodiert er (`FXFinalBlowUp`, `OCLFinalBlowUp`),
hinterlässt `FinalRubbleObject` (unbekannt oder nicht instanziierbar: `CreationSkipped`) und wird zerstört. Kinematisch statt Kraft über `PhysicsBehavior`.

**`TunnelContain` mit `TunnelTracker`:** je Spieler ein Tunnelnetz (in `PlayerState`, Teil von Hash und Snapshot v7): die registrierten Tunnel in
Registrierungsreihenfolge und die gemeinsame Insassenliste in Eintrittsreihenfolge mit Eintrittstick. Jeder Tunnel meldet sich bei der Erzeugung an
(`onObjectCreated`, auch im Bau) und ab, wenn er stirbt (Todesfilter), entfernt (`onDelete`) oder verkauft wird (`onSelling` beim **Beginn** des Verkaufs;
der letzte Tunnel lässt vorher alle aus). Stirbt der letzte Tunnel, werden alle Insassen zerstört (Einsturz); sonst übernimmt der älteste verbleibende
Tunnel die Insassen, die durch den toten eingestiegen waren. Kapazität ist `GameData.ini MaxTunnelCapacity` (10) für das ganze Netz; Flugzeuge sind
ausgeschlossen (`TunnelTracker::isValidContainerFor`); stellvertretend für `ActionManager::canEnterObject` nur eigene Objekte, nur in lebende, fertige,
nicht verkaufte Tunnel. Heilung (`healObject`): je Tunnel und Tick ein Anteil `MaxHealth / TimeForFullHeal`, nach `TimeForFullHeal` im Netz voll –
bei mehreren Tunneln also mehrfach je Tick, genau wie im Original, wo jeder Tunnel `healObjects` aufruft. Aussteigen setzt den Insassen auf den Tunnel
und schickt ihn zu einem Punkt im Abstand `BoundingCircleRadius · [1; 1,5]` in zufälliger Richtung (`scatterToNearbyPosition`, zwei Logikzüge).
Nicht modelliert: Nemesis-Zielwahl der Tunnelverteidiger, `harmAndForceExitAllContained` (Bunkerbrecher), Eroberung.

**`HelixContain`:** ein `TransportContain` mit einem zusätzlichen Platz für ein `PORTABLE_STRUCTURE` (Gatling-Kanone, Propagandaturm): das erste wird
immer angenommen, belegt keinen Platz, folgt Position und Richtung des Helix in jedem Update, stirbt mit ihm (`onDie` → `kill`) und verschwindet mit ihm
(`onDelete`). `PayloadTemplateName` (im Retail-Bestand unbenutzt) wird ausdrücklich abgewiesen (Vorlage nicht instanziierbar mit
`HelixContain[PayloadTemplateName]`). Nicht modelliert: Garnisonswaffenbonus, Spiegelung des Schadenszustands (Darstellung).

**Skirmish-KI** (`SkirmishAi`): Chinooks sind Versorger (`Role.Carrier`); GLA-Einheiten unter 50 % Gesundheit laufen in einen eigenen Tunnel in
600 Einheiten Umkreis (`MoveAndEnterCommand`), und sobald alle Insassen heil sind, entleert sich das Netz durch den Tunnel, der dem Armeeziel am
nächsten liegt (`EvacuateCommand`); Transporter der Armee (Humvee, Horchposten) nehmen beim Angriff Infanterie in 250 Einheiten Umkreis auf und
lassen sie aussteigen, wenn Feinde in `EngageRadius` sind oder das Ziel erreicht ist. Projektile sind weder Ziele noch eigene Einheiten der KI.

**Nachweis** (`Tests.Transports.cs`): gemeinsames Netz, Kapazität, Eintrittsregeln, Ausstieg durch den anderen Tunnel im Streuring 20 … 30; Heilung
zwei Anteile je Tick bei zwei Tunneln und voll nach 30 Ticks, Übergabe beim Tod, Einsturz; Verkauf lässt aus; Einlaufen und Entladen an einer Position;
Chinook steigt 5 je Tick auf 100, versorgt im Flug (2 Kisten = 200, mit Supply Lines 260), landet zum Entladen und steigt wieder; Absturz handgerechnet
(−2330 Rohwert je Tick², Aufschlag nach 75 Updates, Explosion im zehnten Tick danach, Wrack); Helix-Aufbau; Snapshot v7 mit Insassen und während der
Evakuierung, Replay v7 der neuen Befehle, v6 abgewiesen. Realdaten: Chinook liefert (4 Lieferungen in 1.800 Ticks), GLA-Tunnel lässt durch den
zweiten Tunnel aus.

## 7. Teams, Diplomatie, Sieg und Niederlage

**Beziehungsmodell** (`SimulationWorld.RelationshipOf`, `Relationship.cs`; Referenz: `Player::getRelationship`, `Player::setPlayerRelationship`).
`Relationship` trägt die Indizes des Originals (`Enemies = 0`, `Neutral = 1`, `Allies = 2`; Teil von Replay und Snapshot). Die Beziehung ist
**gerichtet** (`von` sieht `zu`). Auflösung in dieser Reihenfolge: ein Spieler ist sein eigener Verbündeter; das Systemobjekt (neutrale
Objekte) ist für alle neutral; eine ausdrückliche gerichtete Überschreibung gewinnt; sonst entscheidet das Team – gleiches Team ungleich 0
bedeutet Verbündete, alles andere Feinde. Das Original startet mit NEUTRAL und schreibt jede Beziehung im Skirmish-Aufbau; diese Welt hat
keinen solchen Aufbauschritt, „kein Team, keine Beziehung“ heißt daher Feind (bewusste Abweichung).
`PlayerConfiguration(Id, Name, Faction, Team, IsComputer)` liefert das Team; `SetRelationshipCommand` (Code 19, nur System,
`CommandTypes.Version = 7`, im Replay kodiert; beide Spieler echt und verschieden) setzt eine gerichtete Überschreibung, die Gegenrichtung
ist ein eigener Befehl. Zustand: Team, Überschreibungen (sortiert) und Computerkennzeichen sind Teil von Snapshot (v3) und Hash; das
Replay-Format ist Version 4.

**Verbraucher** (vorher überall „gleicher Besitzer = Verbündeter“): Abschussverbuchung (`ScoreTheKill`: Erfahrung und Kopfgeld nur für
Feinde), Container (`GarrisonContain`/`TransportContain`: `AllowAlliesInside`/`AllowEnemiesInside`, neutral zählt weder als Verbündeter noch als
Feind), Kistenabwurf (`CreateCrateDie`: ein Abschuss durch einen Verbündeten belohnt nicht), Stealth-Erkennung (`StealthDetectorUpdate`
markiert Feinde), `RadiusDamageAffects` (§6g), Ziel- und Padwahl (`AutoFindHealingUpdate`, Referenzmatch-Zielwahl).

**Sieg** (`VictoryConditions::update` für ein Skirmish ohne „letzte Allianz“-Abwahl):
- `SimulationWorld.IsPlayerDefeated(player)` entspricht `VictoryConditions::hasSinglePlayerBeenDefeated` mit den
  Standardflaggen `NOBUILDINGS|NOUNITS` = `!Player::hasAnyObjects`. `Team::hasAnyObjects` (`Team.cpp`) überspringt
  effektiv tote oder zur Zerstörung angemeldete Objekte sowie Projektile, `INERT`- und `MINE`-Objekte; genau diese
  Ausnahmen gelten hier (`CountsForSurvival`). Ohne sie überlebt jede Fraktion durch Minen, Trümmer und Projektile ewig –
  der Referenzmatch endete dann nie.
- `SimulationWorld.TryGetWinningAlliance()` liefert die überlebenden Spieler in aufsteigender ID-Reihenfolge, sobald jeder Überlebende ein
  Verbündeter des ersten ist (das Original vergleicht jeden Überlebenden nur mit dem ersten; eine nicht transitive Matrix wird genauso
  beurteilt); leer, solange Feinde übrig sind oder niemand mehr lebt. `TryGetWinner()` ist das Mitglied mit der kleinsten ID (ohne Teams der
  letzte Überlebende).
- **Nicht modelliert:** Skript-/Missionssiege, Kapitulation, Beobachter, die „letzte Allianz“-Abwahl (`LastAlliance`).

## 7a. Referenzmatch und Golden-Master-Szenarien

**Szenario** (`src/CnCNew.Simulation/Testing/ReferenceMatchScenario.cs`, Aufruf `SimTool reference-match <Inhalt>`): ein
scriptloser, seedbasierter Befehlsgenerator spielt alle teilnahmefähigen Fraktionen gegeneinander auf einer hindernisfreien
Karte, die nur als Koordinaten existiert, und zeichnet die Befehle als Replay (Format v4, mit Spielern, Teams und Checkpoints) auf.
Das Match endet mit dem Sieg (§7) plus `GraceTicks` Nachlauf, spätestens bei `MaxTicks` (Standard 216.000); `MinTicks`
füllt das Replay bei Bedarf mit Leerticks auf. Der Generator schreibt ausschließlich typisierte Befehle: Startobjekte per
`SpawnCommand`, Produktion per `QueueProductionCommand`, Bewegung per `SetMoveTargetCommand`, Feuer per `FireWeaponCommand`
und – neu – Versorgungsaufträge per `DockCommand`. Alle Spieler sind Computerspieler (`IsComputer: true`, damit
`AutoFindHealingUpdate` läuft). Der frühere Geldersatz `GiveMoneyCommand` wird nicht mehr geschrieben: Jede Basis erhält
zusätzlich zur Kommandozentrale ein Versorgungszentrum, einen Nachschubträger der eigenen Seite und ein neutrales Lager
(`SpawnSupplyInfrastructure`, ein protokollierter Ersatz für den noch fehlenden Bauvorgang), der Träger fährt im ersten Tick per
`DockCommand` zum Lager und arbeitet danach mit seiner eigenen Zustandsmaschine (§6e). Fehlt einer Seite Zentrum, Träger oder
Lager, steht das als `supply note` im Bericht; der Generator zählt `SupplyDeliveries` und `SupplyDeliveryMoney`.

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
Fingerabdruck `9F5307957A55CD46`): **alle 13 spielbaren Fraktionen nehmen teil**, jede mit ihrer **echten Kommandozentrale**
(`AmericaCommandCenter`, `Infa_ChinaCommandCenter`, `GLACommandCenter`, …), und **kein Startobjekt wird übersprungen**
(`StartObjectsSkipped` = 0, vom Realdatentest erzwungen; die früheren 13 Bauarbeiter-Fahrzeuge und GLA-Arbeiter sind seit `DozerAIUpdate`/`WorkerAIUpdate` instanziierbar).
Ergebnis mit `GraceTicks` 300: Sieger Spieler 12 (FactionGLAStealthGeneral) bei Tick 26.880 (14,9 Spielminuten), Replay 27.181 Ticks
(949.191 Byte, 27 Checkpoints, SHA-256 `208BD746B5F4059422B72705F9723B7187AAAC9D6CA76DF8DDA3EBB52C52A5A8`), 27.607 Befehle (1.240
Produktions-, 7.194 Bewegungsaufträge, 13 Dock-Befehle, 18.591 Schüsse), alle Checkpoints verifiziert, keine Ablehnung außer 16
`UnknownEntity` (Befehle an inzwischen zerstörte Objekte), Endhash `B6E0CDECA3168EFC`, Kettenhash `BE21F2345E87EF24`; ein zweiter
frischer Lauf und der Lauf ab Snapshot bei Tick 13.590 sind tickweise identisch. Höchststand 135 lebende Objekte; Endzustand: 62
Objekte, 984 erzeugt, 922 zerstört; Überlebende: 47 FactionGLAStealthGeneral, je 2 FactionGLA, FactionGLADemolitionGeneral und
FactionGLAToxinGeneral. Die Wirtschaft läuft echt: **707 Lieferungen** an Versorgungszentren zahlten **93.075** Guthaben aus. Die vier
amerikanischen Fraktionen haben in dieser Messung keine Versorgungswirtschaft (`supply note`: Ihr Nachschubträger ist der Chinook, dessen
`ChinookAIUpdate` noch fehlt), ihr Einkommen ist das Startkapital. **Neumessung Phase 8, Paket E1:** der Referenzmatch setzt jetzt auch Chinooks als Nachschubträger ein (keine `supply note` mehr), Schüsse fliegen (§6i);
Sieger Spieler 1 (FactionAmerica) bei Tick 13.800, 36.880 Befehle, 428 Lieferungen mit 114.450 Guthaben, Werte in `docs/SIMULATION-CORE.md` §21. Der Realdatentest `Tests.RealReferenceMatch` prüft Teilnehmerzahl,
Sieger, Lieferungen, Determinismus und Snapshotfortsetzung. Die Abdeckungslage je Seite liefert `--diagnose`; die Aufgabe „Referenzmatch
aller Fraktionen auf Retail-Daten“ ist **erfüllt**. Grenzen (Stand Phase 5; der Referenzmatch nutzt weiterhin keinen Bau, die Skirmish-KI der Phase 6 baut, §11): kein Bauvorgang im Match (Dozer bauen nichts, Basen sind Startobjekte plus Ersatzinfrastruktur),
keine Spezialkräfte im Befehlsstrom (es gibt keinen `UseSpecialPowerCommand`-Generator, §6d), keine Kollision/kein Terrain/keine Pfadsuche (§4);
dass GLA-Stealth gewinnt, ist ein Effekt dieser Vereinfachungen (Stealth hat keine Wirkung auf Zielwahl), keine Aussage über Spielbalance.

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
- Supply und Erfahrung haben eigene Golden-Master-Tests mit unabhängigem Modell im Test (`SupplyEconomyGoldenMaster` in
  `Tests.Supply.cs`, `VeterancyExperienceAndHealthGoldenMaster` und `VeterancyWeaponBonusGoldenMaster` in `Tests.Veterancy.cs`).
  Sie sind aus dem Quelltext abgeleitet, nicht gegen Originalläufe gemessen.
- **Offen bleibt Block 0.4** des Handoffs (Vergleich der RNG-Folge mit einem kompilierten Original): es gibt keinen Compiler und keine
  Originalläufe in dieser Umgebung; der Wert wird nicht geraten.

## 8. Befehlstabelle

Siehe `docs/SIMULATION-CORE.md` §6 für die vollständige, versionierte Tabelle (27 Befehlscodes, `CommandTypes.Version = 9` bis Phase 9; Phase 8 ergänzt 26 `MoveAndEnterCommand`
und 27 `EvacuateCommand`, §6j; Phase 6 ergänzt Codes 20–25: `ConstructCommand`, `SellCommand`, `SetRallyPointCommand`, `ResumeConstructCommand`, `CancelConstructCommand`, `RepairCommand`; `DockCommand` = 18, `SetRelationshipCommand` = 19) und die Ablehnungsgründe (Codes 25–29 für Spezialkräfte, §6d;
Code 30 `CannotDock`, §6e); das Replay-Format v7 (`docs/SIMULATION-CORE.md` §8) kodiert alle 27, der Snapshot ist Version 7; Ablehnungscodes 31–34 (`TargetNotVisible`, `InvalidPlacement`, `CannotConstruct`, `CannotSell`) stammen aus Phase 6. Phase 10 ergänzt Codes 28/29
(`OverchargeCommand`, `CaptureCommand`, `CommandTypes.Version = 10`) und Ablehnungsgründe 35–38 (`CannotProduceTemplate`, `ProductionLimitReached`, `CannotCapture`,
`CannotToggleOvercharge`); siehe `docs/SIMULATION-CORE.md` §23.

## 9. Modulabdeckung

Stand Phase 13 (3. Oktober 2026, frisch gemessen mit `SimTool audit-coverage`/`audit-modules`): **1.735 von 2.115 Objekten instanziierbar (82,0 %)**, 107 von 205 Modultypen implementiert, Modulverwendungen abgedeckt 16.085 von
19.553 (82,3 %); neu sind neun Flugzeug-/Fahrzeug-/Uplink-Module (`docs/SIMULATION-CORE.md` §28). Die größten verbleibenden Blocker nach Objekten (davon nur durch dieses Modul blockiert): `JetAIUpdate` 34/34,
`MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `SlavedUpdate` 20/7, `PropagandaTowerBehavior` 14/13, `UpgradeDie` 13/0, `HiveStructureBody` 11/4, `RiderChangeContain` 11/11, `DeployStyleAIUpdate` 10/10,
`StructureCollapseUpdate` 10/10, `ParkingPlaceBehavior` 9/9.

Stand Phase 12 (2. Oktober 2026): 1.680 von 2.115 Objekten (79,4 %), 98 von 205 Modultypen, 15.903 von 19.553 Verwendungen (81,3 %); Blocker damals: `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0,
`JetAIUpdate` 34/5, `DeliverPayloadAIUpdate` 28/1, `ParticleUplinkCannonUpdate` 8/8 (alle fünf seit Phase 13 implementiert außer `JetAIUpdate`).

Stand Phase 8, Paket E1 (frisch gemessen): 1.470 von 2.115 Objekten instanziierbar (69,5 %), 85 von 205 Modultypen implementiert, 15.543 von 19.553
Modulverwendungen (79,5 %); Einzelheiten `docs/SIMULATION-CORE.md` §11 und §21. Verlauf (Stand nach Phase 6): 1.393 von 2.115 Objekten sind instanziierbar (65,9 %, gegenüber 26/2.113 in
Phase 4 und 1.384 am Ende von Phase 5); 77 von 205 Modultypen sind implementiert (183
Simulationsmodule, 22 Präsentationsmodule) und decken 15.382 von 19.553 Modulverwendungen (78,7 %). Nicht implementiert sind 77
Module, die der Katalog der Kategorie „Phase 5“ zuordnet und die noch offen sind,
29 der Phase 6 und 22 der Phase 7 (Präsentation). Nach einer frischen `audit-coverage`-Messung sind die größten verbleibenden Blocker
nach Objekten (davon nur durch dieses Modul blockiert): `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66 (braucht Kollisionsphysik),
`SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2,
`HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13,
`BaikonurLaunchPower` 23/23, `SlavedUpdate` 20/7, `HelicopterSlowDeathBehavior` 18/0, `PointDefenseLaserUpdate` 17/4,
`DumbProjectileBehavior` 16/12, `PropagandaTowerBehavior` 14/11, `HelixContain` 13/1, `HordeUpdate` 13/3, `UpgradeDie` 13/0,
`TunnelContain` 12/12, `AnimationSteeringUpdate` 11/0, `ChinookAIUpdate` 11/0, `HiveStructureBody` 11/3. Fast alle brauchen Terrain,
Kollision, Flugphysik, Zielwahl oder KI und stehen weiter aus (Phase 6 hat keine Kollision eingeführt); ein einzelnes Modul schaltet keine große Objektgruppe mehr frei.

## 10. Bekannte Grenzen (Zusammenfassung)

Jede Zeile ist eine bewusste, dokumentierte Vereinfachung, keine stille Lücke:

- **Block 0.4 offen:** die RNG-Folge wurde nicht gegen ein kompiliertes Original verglichen (kein Compiler, keine Originalläufe). Die
  Golden-Master-Tests (§7a) sind aus dem Quelltext abgeleitet, nicht gegen Originalläufe gemessen; Festkomma statt `Real` ist eine
  dokumentierte Abweichung.
- Projektil-Flugzeit ist seit Phase 8 modelliert (§6i), aber **ohne Kollision**: Projektile detonieren am Ende ihrer Bahn, am Ziel, am Boden oder nach
  Lebensdauer/Treibstoff, nie an einem Objekt, an dem sie vorbeifliegen; Täuschkörper, Störsender, Marschflughöhe und `GarrisonHitKill*` fehlen.
  Flächenschaden (§6g) läuft für `FireWeaponCommand`, `FireWeaponUpdate`, `PointDefenseLaserUpdate` und Detonationen, `FireWeaponWhenDeadBehavior` meldet
  weiterhin nur `WeaponFireRequested`.
- Erfahrung, Stufen, Gesundheits- und Waffenboni sind implementiert (§6h); die Rüstungs-/Waffensatzauswahl nach Stufe ist nicht
  modelliert, weil kein ausgelieferter Satz sie verlangt. Rang und „Science Purchase Points“ aus Erfahrung fehlen. Das Furchtsignal
  (Block 0.3) ist implementiert.
- Seit Phase 6 gibt es Terrain, Pfadsuche und drehratenbegrenzte Ausrichtung (§11), weiterhin **keine Kollision** (§4). `PhysicsBehavior`, `SquishCollide` und
  `CrushDie` sind daher Ruhezustand (§6c, §6g); Zerquetschen und Umstürzen (`ToppleUpdate`) folgen mit der Kollision.
- Kein Energie-Verlangsamungsfaktor (§5). Bauplatzprüfung, Rallypunkt, Bauvorgang, Verkauf und Reparatur durch Dozer gibt es seit Phase 6 (§11).
- (Phase 5) Der `STEALTHED`-Statusbit hatte keinen Verbraucher außer `StealthDetectorUpdate`s `DETECTED`-Markierung (kein Zielsystem, das
  `DETECTED` auswertet, §6); seit Phase 6 schließen Sichtabfragen nicht entdeckte `STEALTHED`-Objekte aus (§11), einen Detektor-Verbraucher für die KI gibt es nicht.
- Teams und Diplomatie sind ein Datenmodell (§7). Die **Wirkung** von `CommandSet`/`CommandButton` (Kommandoleisten) und der
  **Radareffekt** (`RadarUpdate`/`RadarUpgrade` führen nur den Zustand) sind nicht implementiert. Garnison (§6a), Sciences (§1a) und
  `TransportContain` sind implementiert, seit Phase 8 auch `TunnelContain`, `HelixContain`, `TransportAIUpdate` und `ChinookAIUpdate` (§6j; ohne Abseilen,
  Feuern aus Containern und Flugfeldheilung). „Science Purchase Points“ werden
  nur per Systembefehl gutgeschrieben (§1a).
- Versorgung (§6e): die Maschinensperre während der Dockbewegung, die Nähe-Prüfung der Lager, `setDockCrippled` und die Bindung von
  Spawns an einen `SlavedUpdate` fehlen; die Dockbewegung ist eine einfache geradlinige Bewegung ohne Geometrie.
- Spezialkräfte (§6d): kein Bauvorgang, kein Deaktiviert-Zustand, keine Kartenrand-Lieferung (Kanonenboote, Randerzeugung nur als
  `CreationSkipped`), keine Kanonenboot-KI, keine Sichtobjekte/EVA/Skriptmeldungen, Kopfgeld nur für Feinde (§7). Es gibt keinen
  Generator für `UseSpecialPowerCommand` im Referenzmatch.
- Referenzmatch auf Retail-Daten: 13 von 13 Fraktionen mit echten Kommandozentralen, 0 übersprungene Startobjekte, echte Lieferungen
  (§7a), aber ohne Bauvorgang, Spezialkräfte und Kollision; Sieger und Dauer sind ein Effekt dieser Vereinfachungen. Golden Masters
  decken Schadenskette, Produktionsuhr, Supply und Erfahrung. Die Mechaniken sind zusätzlich durch `Tests.Phase5.cs`, `Tests.Supply.cs`,
  `Tests.Veterancy.cs`, `Tests.Teams.cs`, `Tests.Repair.cs`, `Tests.Combat.cs` und den 216.000-Tick-Stresstest mit den neuen Modulen an
  echten Objekten abgesichert.

## 11. Ergänzungen der Phase 6

Die Regeln der Phase 6 stehen mit Zahlen in `docs/SIMULATION-CORE.md` §13–§20; hier nur, was die Spielregeln ändert:

- **Sicht:** Schüsse (`FireWeaponCommand`) verlangen ein für den Spieler sichtbares Ziel (`TargetNotVisible`); nicht entdeckte `STEALTHED`-Objekte und enthaltene Objekte sind unsichtbar. `STEALTHED` wird nur bei
  `CAN_STEALTH` gesetzt (Fehlerbehebung: vorher wurden alle Objekte getarnt).
- **Bau:** Dozer und Arbeiter setzen Gebäude mit `ConstructCommand`; Bauplatz nach `BuildAssistant::isLocationLegalToBuild` (Gelände, Fußabdruck, Hindernisse, erkundet), Kosten beim Start, Baufortschritt linear über
  `BuildTime`. Verkauf über `SellCommand` (`SaleDurationTicks` 179, `SellPercentage`), Reparatur **kostenlos** (keine Vereinfachung, sondern die korrekte Portierung: weder
  `DozerAIUpdate::privateRepair`/`DozerPrimaryStateMachine`'s Heilschritt noch `RepairDockUpdate::action` ziehen in der Referenz Geld ab, beide skalieren nur über Zeit/Prozentsatz;
  siehe `docs/SIMULATION-CORE.md` §23 für den Belegtext), Sammelpunkte über `SetRallyPointCommand`.
- **Energie:** `PowerPlantUpgrade`, `PowerPlantUpdate`, `OverchargeBehavior` (§6 der Kernbeschreibung, `SIMULATION-CORE.md` §17); seit Phase 10 ein echter `OverchargeCommand`
  (`TOGGLE_OVERCHARGE`) und eine reduzierte Kraftwerkseroberung (`CaptureCommand`, `SIMULATION-CORE.md` §23).
- **Grenzen (Stand Phase 9, siehe §23 für den Phase-10-Stand):** `QueueProduction` prüfte keine Voraussetzung; `MaxSimultaneousOfType` ungeprüft; kein Eroberungsverhalten für
  Kraftwerke; keine Kollision; `ToppleUpdate`/`CrushDie` bleiben offen.
