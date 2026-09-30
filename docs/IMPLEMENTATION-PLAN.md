# Implementierungsplan bis zum abgeschlossenen ersten Scope

Stand: 30. September 2026 (Phase 6 abgeschlossen)

## Ziel und Abgrenzung

Dieser Plan führt den aktuellen Prototyp bis zu einer lokal spielbaren Neuimplementierung von
*Generals: Zero Hour* für Singleplayer-Skirmish und Kampagne. Netzwerkcode, Matchmaking,
Anti-Cheat sowie binäre Kompatibilität zu alten Mods und Savegames bleiben außerhalb dieses
ersten Abschlusses.

Jeder Schritt bildet eine geschlossene Domäne. Ein Schritt gilt erst als abgeschlossen, wenn
seine öffentlichen Datenverträge feststehen, die reale Installation vollständig auditiert wird,
automatisierte Regressionstests bestehen und die zugehörige Dokumentation den Istzustand
beschreibt. Neue Domänen dürfen auf abgeschlossenen Verträgen aufbauen, aber keine bekannten
Defekte aus dem vorherigen Schritt übernehmen.

## Rekonstruierter Ausgangsstand

Bereits vorhanden und funktionsfähig sind:

- reproduzierbarer lokaler Import der Originalinstallation, BIG-Reader und materialisierter,
  archivfreier Content mit Herkunftsmanifest;
- validierender SAGE-INI-Syntaxparser;
- W3D-Chunk-, Mesh- und Materialdecoder mit Vollbestandsaudit;
- TGA-Decoder mit Vollbestandsaudit;
- ein Stride-/Direct3D-11-Viewer, der ein statisches W3D-Modell mit Originaltexturen und einem
  Teil der Materialzustände rendert;
- ein aktuell erfolgreicher Solution-Build und fünf erfolgreiche Testprogramme.

Die zuletzt bearbeitete Domäne ist eindeutig W3D-Materialtreue und deren Abbildung auf Stride.
`CnCNew.W3D.Tests` stoppt derzeit in einem neu hinzugefügten Test für per-face UVs der zweiten
Texture-Stage. Die Auswahl und der V-Flip sind implementiert; die Assertion vergleicht jedoch
binäre `float`-Ergebnisse exakt. Dahinter bleiben die bereits dokumentierten funktionalen Lücken:
gleichzeitige Materialpässe und Texture-Stages, animierte Mapper sowie seltene Gradient-Modi.

## 1. W3D-Material- und Renderingdomäne vollenden

**Status: abgeschlossen am 27. September 2026.**

**Ergebnis:** Statische W3D-Meshes werden mit allen im ausgelieferten Bestand vorkommenden
Materialvarianten rendererunabhängig korrekt beschrieben und im Stride-Client dargestellt.

Umfang:

- den fehlerhaften exakten Floatvergleich durch eine fachlich passende Toleranzprüfung ersetzen
  und alle W3D-Tests wieder grün machen;
- Semantik und Reihenfolge mehrerer Materialpässe und Texture-Stages vollständig modellieren;
- Primary-, Secondary-, Detail- und Post-Detail-Funktionen auf reproduzierbare Shaderpfade
  abbilden, einschließlich der seltenen Gradient-Modi;
- Mapper-Argumente typisieren und UV-Animationen zeitabhängig ausführen;
- Texturauflösung, Fallbacks, Alpha-Test, Blend, Depth, Culling, Vertexfarben und Opacity über
  repräsentative Modelle visuell und automatisiert absichern;
- den monolithischen Viewerpfad in testbare W3D-zu-Stride-Komponenten zerlegen.

Abgeschlossen, wenn:

- Build und alle sechs vorhandenen Testprogramme erfolgreich sind;
- der W3D-Vollbestandsaudit ohne unbekannte Materialzustände läuft;
- ein Real-Asset-Test mehrere Pässe, zwei Stages, per-face UVs und animierte Mapper nachweist;
- Golden Images für transparente, additive, zweistufige und animierte Beispielmaterialien
  freigegeben sind;
- der Smoke-Test ohne `NotSupportedException` für Daten aus dem ausgelieferten Bestand endet.

Abnahme:

- Solution-Build und alle sechs Testprogramme sind erfolgreich; der W3D-Test läuft zusätzlich gegen `.local/content`.
- Der Audit verarbeitet 8.904/8.904 Dateien und 70.587 Meshes ohne unbekannten Shader- oder Mapperzustand.
- 5.625 Zweipass-Meshes, 317 zweistufige Pässe, beide Mapper-Slots und per-face UV-Indizes sind real nachgewiesen.
- Der Stride-Pfad verwendet geordnete Materialpass-Sequenzen und dynamische Vertexpuffer für zeitabhängige Mapper.
- Deterministische lokale PNG-Captures und SHA-256-Baselines decken Mehrpass/Alpha/Additiv, zwei Stages sowie Linear-/Grid-Animation ab.
- Die implementierte Semantik und bewusst außerhalb des ausgelieferten Bestands liegende Mapper-/Bumpfälle sind in `docs/W3D-MATERIALS.md` dokumentiert.

## 2. Assetformate und Runtime-Contentvertrag abschließen

**Status: abgeschlossen am 27. September 2026.**

**Ergebnis:** Alle für Karten, UI, Lokalisierung, Modelle, Animation, Effekte, Audio und Video
benötigten Dateitypen haben einen validierenden Reader oder eine dokumentierte, reproduzierbare
Konvertierung. Laufzeitcode greift ausschließlich über einen stabilen Contentvertrag zu.

Umfang:

- STR-, MAP-, WND-, W3D-Animations-/Hierarchie-, DDS/TGA-, Partikel-, Audio- und Videoformate
  inventarisieren und nach Laufzeitdecoder beziehungsweise Importkonverter klassifizieren;
- fehlende Reader mit Größen-, Bereichs- und Referenzvalidierung implementieren;
- logische Asset-IDs, Abhängigkeiten, Overlay-Herkunft und Hashes in einem versionierten
  Runtime-Manifest zusammenführen;
- inkrementellen, reproduzierbaren Import sowie verständliche Diagnosen für defekte oder
  unbekannte Assets bereitstellen;
- Originaldaten, generierte Artefakte und Quellcode rechtlich und technisch getrennt halten.

Abgeschlossen, wenn der gesamte lokale Content ohne unbekannte benötigte Formate inventarisiert
und aus einem frischen Import reproduzierbar aufgebaut werden kann und jede Laufzeitreferenz
entweder auflösbar ist oder als bewusst unterstützter Fallback ausgewiesen wird.

Abnahme:

- `CnCNew.Formats` validiert STR, MAP/SCB/WAK, WND, DDS, ANI, WAV, MP3 und BIK; vorhandene
  INI-, TGA- und W3D-Reader sind in denselben Vollbestandsaudit integriert.
- 25.682/25.682 Dateien sind klassifiziert; keine Erweiterung bleibt unbekannt. Die produktiven
  Bestandszahlen und 119 scopefremden Installationsdateien stehen in `docs/CONTENT-PIPELINE.md`.
- Schema 2 enthält kanonische IDs und Pfade, Typ, Hash, Größe, Overlaykandidaten/-priorität,
  Importerversion/-optionen, Runtimeartefakte, direkte Abhängigkeiten und Diagnosen.
- 41.308 von 41.527 Referenzverwendungen werden aufgelöst; 219 Verwendungen sind als einer von
  drei geprüften Fallbacktypen sichtbar. Echte Zyklen und untypisierte fehlende Ziele sind Fehler.
- Zwei unabhängige Frischimporte besitzen den identischen Manifest-SHA-256
  `ACDF0B6CF98538469AA05F62E23D2A83BD3E17A911433B4EA73373FFFB1245FF`; der Folgelauf erreicht
  25.682/25.682 Cachetreffer.
- Staging und Verzeichnistausch verhindern Teilveröffentlichungen. Alle Artefakte wurden danach
  ohne BIG-Zugriff gegen Größe und SHA-256 geprüft.

## 3. Typisierte Spieldaten und Modulregistrierung

**Status: abgeschlossen am 27. September 2026.**

**Ergebnis:** Die INI-Syntax wird zu unveränderlichen, validierten Spieldefinitionen kompiliert;
Strings und lose Knoten gelangen nicht mehr in die Simulation.

Umfang:

- Includes, Vererbung, Überschreibungen, Conditions und Namensauflösung originalgetreu abbilden;
- Typen für Object-, Weapon-, Armor-, Damage-, Upgrade-, Science-, Player-, Faction-, Command-,
  FX- und Audio-Definitionen einführen;
- Modul- und Behavior-Namen über explizite Registries an Implementierungen binden;
- Einheiten, Maße, Enums, Referenzen und Pflichtfelder beim Laden validieren;
- verständliche Quelldiagnosen sowie Snapshots der kompilierten Definitionsdaten erzeugen.

Abgeschlossen, wenn der vollständige ausgelieferte INI-Bestand zu einem deterministischen
Definitionsgraphen kompiliert, alle Referenzen aufgelöst und unbekannte Schlüssel oder Module in
einer geprüften Kompatibilitätsliste erfasst sind.

Abnahme:

- 256/256 Dateien und unverändert 66.623 Syntaxblöcke ergeben 12.677 Definitionen, 16.360
  Modulinstanzen und 335.592 typisierte Schlüssel einschließlich rekursiver Konfigurationssektionen.
- Eigene unveränderliche Vertragstypen decken Objects, Weapons/WeaponSets, Armor/Damage,
  Upgrades/Sciences, Player/Factions, Commands, FX, Audio, Locomotors, Experience,
  SpecialPowers und GlobalData ab; Parserknoten bleiben compilerintern.
- Includes werden ausschließlich über virtuelle Runtimepfade aufgelöst. Zyklen, fehlende Basen,
  falsche Conditions, ungültige Werte, Registryduplikate und unbekannte Module im strikten Modus
  sind Fehler. Overlay und Add/Remove/ReplaceModule sind deterministisch.
- 12.441/12.468 typisierte Referenzverwendungen sind aufgelöst. Die 27 übrigen Verwendungen
  (25 Veterancy-Pseudoupgrades, `DieByFireUSA` und `Boss_VehicleSentryDrone`) sind explizite,
  nicht stille Kompatibilitätsfälle.
- 41 spätere Definitionsdomänen (2.801 Instanzen) und 206 noch nicht implementierte Modultypen
  (16.360 Verwendungen) sind mit Häufigkeit, Besitzerart und Implementierungsziel inventarisiert.
- Zwei unabhängige 112.664.082-Byte-Snapshots waren byteidentisch; SHA-256 jeweils
  `14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`. Vertrag, Regeln,
  Kompatibilitätsliste und Befehle stehen in `TYPED-DEFINITIONS.md` und
  `PHASE3-COMPATIBILITY.md`.

Verifikation (28. September 2026): Build, alle acht Testprogramme, die Auditwerkzeuge und eine
frische Vollkompilierung (identischer Snapshot-SHA-256) bestätigen die obigen Zahlen. Die
Abnahme gilt für Struktur, Vollständigkeit und Determinismus. Fachlich flach bleiben: keine
domänenspezifischen Definitionseigenschaften, Pflichtfelder oder Schlüsselschemata für
Definitionen; heuristische Einheitentypisierung ohne Tickumrechnung; `Enum`/`Flags`/`Color`/
`Vector` werden im Bestand nie erzeugt (9.150 Farben, 6.161 Koordinaten und 1.964 `KindOf`-Listen
sind `List`); produktive `ModuleRegistry` ist leer und die Kompatibilitätsliste nicht als
Basislinie abgesichert; Ladereihenfolge nicht gegen `GameEngine.cpp` abgeglichen; keine
inkrementelle Kompilierung. Details in `TYPED-DEFINITIONS.md` („Bekannte Grenzen“). Der
notwendige Teil wurde in Phase 4 als Block 0 nachgezogen (siehe unten und `TYPED-DEFINITIONS.md`), der Rest ist Vorleistung
für Phase 5.

## 4. Deterministischer Simulationskern

**Status: abgeschlossen am 28. September 2026 (einschließlich Block 0, Härtung der Phase 3).**

**Ergebnis:** Eine headless laufende Welt besitzt festen Tick, deterministische IDs und
Zufallszahlen, Befehlsqueue, Objektlebenszyklus und reproduzierbare Snapshots.

Umfang:

- `CnCNew.Simulation` ohne Abhängigkeit auf Stride, DirectX oder UI anlegen;
- Zeit, RNG-Streams, Entity-IDs, Update-Reihenfolge und numerische Regeln fest definieren;
- Objekt-, Modul-, Behavior-, Event-, Timer- und Command-Lebenszyklen implementieren;
- Inputbefehle von Präsentation und Echtzeit entkoppeln;
- Snapshot-, Hash- und Replay-Harness für deterministische Regressionen schaffen.

Abgeschlossen, wenn identische Definitionen, Karte, Seed und Befehlsfolge über lange Läufe
bitidentische Zustands-Hashes liefern und Speichervalidierung sowie Headless-Stresstest bestehen.

Abnahme Block 0 (Härtung der Phase 3):

- Retail-Ladeplan der Original-Engine (Reihenfolge, Ausschluss von Debug-/Werkzeug-/Fremdsprachdateien); `IniTool load-order-report`
  weist 478 mehrfach definierte Namen mit Gewinnern aus; 18 Gewinner änderten sich (16 `MappedImage`, 2 Kartenoverlays), bei
  keinem hängt die Ordnung von einer nicht belegten Annahme ab.
- `DefaultThingTemplate`-Vererbung, `Add/Remove/ReplaceModule`, `ObjectReskin` und nicht geschlossene `AddModule`-Blöcke wie im
  Original; wiederholbare Schlüssel behalten Vielfachheit (Tabelle mit Parserquelle).
- Object-Köpfe und Modulfelder streng typisiert (96 Schlüssel, 43 davon bewusst `Opaque` und gezählt); Farben, Vektoren, Enums und
  Flags über Namenstabellen aus der Referenz; kein Token fällt auf `Text` zurück.
- Modulkatalog mit 205 Typen ohne Factories (die frühere Zahl 206 enthielt ein Phantommodul); Bindungstabelle in der Simulation,
  beim Start gegen die Deskriptoren geprüft; `StrictUnknownModules` besteht auf dem Bestand; `compat-check` besteht gegen die
  eingecheckte Basislinie (344 Einträge); neuer Referenzhash `131ED730F2AD4DB85D6F50CE91A7059358CA32D6728F8EB6E91E6CF8775E58A4`
  (zwei Läufe byteidentisch, Erklärung der Abweichung in `TYPED-DEFINITIONS.md`).

Abnahme Phase 4:

- Solution-Build und alle neun Testprogramme sind grün (Warnungen: nur die zehn bekannten NU1901/NU1903); die Simulationstests
  laufen zusätzlich mit `--content-dir` und `--long`.
- 30-Hz-Tick, exakte Zeitumrechnung, Q47.16-Festkomma mit dokumentierter Rundung und Grenzwerten; die binary32-Abweichung des
  Originals ist gemessen (6.310 von 5.000.001 Dauern, frühestens ab 66 Minuten) und dokumentiert.
- Original-RNG mit `logic`/`cosmetic`/`audio`-Streams, Golden-Sequenzen aus einer unabhängigen Transkription (kein Compiler
  verfügbar), Zustand nach einer Million Züge, Übertragsketten.
- Monotone IDs (0 ungültig), kanonische Reihenfolgen, Sleepy-Update-Modell mit dokumentierten Abweichungen, acht Tickphasen,
  Mutationsqueues, definierte Eventzustellung vor der Entfernung.
- Zwölf Lebenszyklusmodule mit strengen Schemata; Auswahl mit Zahlen belegt (28,1 % der 19.529 Modulverwendungen der
  Basisobjekte); Modulabdeckungsaudit: 26 von 2.113 Objekten vollständig instanziierbar, Blockerliste je Modul.
- Versionierte Befehlstypen, Ablehnungsgründe (`Late`, `Duplicate`, `OutOfOrder`, `UnknownPlayer`, `NotPermitted`,
  `InvalidArguments`, `UnknownTemplate`, `UnknownEntity`, `UnknownTimer`) gezählt und als Events protokolliert.
- Kanonische Snapshots (SHA-256), inkrementeller 64-Bit-Zustands-Hash (Prüfung gegen die volle Neuberechnung), Replay-Format mit
  Fingerabdruck, Checkpoints und Integritätshash; beschädigte, abgeschnittene, versions- und definitionsfremde Dateien werden
  abgewiesen.
- Stresstest über 216.000 Ticks: bis zu 15.774 gleichzeitige Entities, 401.229 Spawn-/Destroy-Vorgänge, 914.771 Befehle,
  7.099 Timer; drei frische Läufe und die Snapshot-Fortsetzung sind über alle Ticks identisch; alle Tabellen kehren auf null zurück;
  22 s je Lauf, 532 MiB Peak; Budgets und Hardware in `SIMULATION-CORE.md`.

Bekannte Grenzen der Phase: kein Bewegungs-/Waffen-/Rüstungs-/Produktionsregelwerk (Phase 5/6); Update-Phasen des Originals nicht
modelliert; Zufallsverbräuche des Originals außerhalb der Lebensdauermodule fehlen; RNG-Abgleich mit kompiliertem Original steht
aus; `Restore` ist intern; Definitionsdomänen außer Object bleiben flach typisiert (Zahlen in `TYPED-DEFINITIONS.md`); 26 von 2.113
Objekten sind instanziierbar, weil vom Default geerbte Module (`StealthUpdate`, `AutoHealBehavior`) noch fehlen.

## 5. Spielregeln, Kampf, Wirtschaft und Produktion

**Status: abgeschlossen (Stand 29. September 2026) mit dokumentierten Grenzen (siehe „Bekannte Grenzen“); Folgeauftrag in `docs/HANDOFF-PHASE7.md`.**

**Ergebnis (Ziel):** Die vollständige Kernschleife eines RTS-Matches funktioniert headless:
Ressourcen sammeln, bauen, produzieren, bewegen, angreifen, Schaden anwenden und gewinnen.

Umfang:

- Spieler, Teams, Diplomatie, Fraktionen, Command Sets und Tech-Voraussetzungen;
- Ressourcen, Supply, Bauplätze, Konstruktion, Produktion, Reparatur und Verkauf;
- Waffen, Projektile, Zielwahl, Armor, Damage, Status, Erfahrung, Upgrades und Sciences;
- Energie, Spezialfähigkeiten, Transport, Garrison, Stealth und relevante Modulinteraktionen;
- Sieg-/Niederlagebedingungen und Regelereignisse.

Abgeschlossen, wenn ein headless Referenzmatch aller Fraktionen vom Start bis zum Sieg ohne
Sonderpfade durchläuft und zentrale Kampf- und Wirtschaftsabläufe durch Golden-Master-Szenarien
gegen bekannte Originalwerte abgesichert sind.

**Umgesetzt** (Details, Numerik und bewusste Vereinfachungen in `docs/GAME-RULES.md`; Zahlen frisch gemessen am 29. September 2026):

- Block 0.1: Domänenschemata für Weapon, Armor, Upgrade, Science, Locomotor und Faction (`docs/TYPED-DEFINITIONS.md`); als Nebenfund ein
  INI-Parser-Fehler behoben (`End`-Erkennung nur erstes Token, wie das Original).
- Block 0.2: `StealthUpdate` und `AutoHealBehavior` mit echten Feldern und Upgrade-Aktivierung. Block 0.3 (Furchtsignal-`GameLogicRandomValue`)
  abgeschlossen; **0.4 (RNG-Abgleich mit kompiliertem Original) bleibt offen** (kein Compiler verfügbar, der Wert wird nicht geraten).
- Wirtschaft (Guthaben, Energiebilanz) und **Versorgungswirtschaft** (`DockMachine`, `DockCommand`, Lager, Zentren, Nachschubträger, Arbeiter,
  `SpawnBehavior`, `GAME-RULES.md` §6e), Upgrade-System (128-Bit-Maske, `UpgradeMux`, `*Upgrade`-Module), Sciences (`ScienceCatalog`,
  `PurchaseScienceCommand`), direkter Kampf mit **Flächenschaden** (Waffen, Rüstungskoeffizienten, Magazin/Nachladen, §6g), **Erfahrung und
  Veteranenstufen** mit Gesundheits- und Waffenboni (`WeaponBonusTable`, §6h), **Teams und Diplomatie** (gerichtete Beziehungen,
  `SetRelationshipCommand`, Kopfgeld und Siegregel auf Teams, §7), **Reparaturdock und `AutoFindHealingUpdate`** (§6f), einfache geradlinige
  Bewegung (kein Terrain, keine Pfadsuche), eine Produktionswarteschlange, „letzter Überlebender“ als Sieg, Garnison/Transport-Containment,
  Erzeugungslisten, Kisten und das Spezialkraft-Subsystem (§6d).
- Befehlstabelle mit 19 Typen (`CommandTypes.Version = 7`), Snapshot Version 3, Replay-Format v4.
- Referenzmatch (`ReferenceMatchScenario`, `SimTool reference-match`) auf synthetischen **und** Retail-Daten deterministisch nachgewiesen (Sieger,
  Replay-Runde, Snapshot-Fortsetzung): 13 Fraktionen mit echten Kommandozentralen, **0 übersprungene Startobjekte**, 707 Lieferungen mit 93.075
  ausgezahltem Guthaben, Sieger Spieler 12 bei Tick 26.880, Endhash `B6E0CDECA3168EFC` (`GAME-RULES.md` §7a). Golden Masters mit unabhängigem
  Modell im Test decken Schadenskette, Produktionsuhr, Supply und Erfahrung (aus dem Quelltext abgeleitet, nicht gegen Originalläufe gemessen).
- 74 von 205 Modultypen implementiert (12 aus Phase 4, 62 aus Phase 5); **1.384 von 2.115 Retail-Objekten vollständig instanziierbar (65,4 %,
  vorher 26 von 2.113), 15.364 von 19.553 Modulverwendungen abgedeckt (78,6 %)**; der 216.000-Tick-Stresstest läuft mit allen Modulen deterministisch
  (Endhash `D80B367CEEF6D72D`), ohne Leck.

**Abnahme:** Das Abnahmekriterium „headless Referenzmatch aller Fraktionen vom Start bis zum Sieg“ ist erfüllt; „ohne Sonderpfade“ und „gegen
bekannte Originalwerte“ nur mit den folgenden Einschränkungen.

**Bekannte Grenzen** (Stand am Ende von Phase 5; Bauvorgang, Verkauf, Bauplatzprüfung, Rallypunkt, Terrain und Pfadsuche hat Phase 6 nachgeholt, siehe §6; vollständige Liste: `docs/GAME-RULES.md` §10):

- Block 0.4 offen; Golden Masters sind keine Originalmessungen, Festkomma statt `Real` ist eine dokumentierte Abweichung.
- Kein Bauvorgang: Dozer und Arbeiter bewegen und liefern, bauen aber nicht; Basen im Referenzmatch sind Startobjekte plus protokollierte
  Ersatzinfrastruktur (`SpawnSupplyInfrastructure`). Kein Verkauf, keine Bauplatzprüfung, kein Rallypunkt.
- Kein Terrain, keine Kollision, keine Pfadsuche, keine Projektil-Flugzeit; `ToppleUpdate`, echte `PhysicsBehavior`-Kräfte und `CrushDie` (nur inert) folgen
  mit Phase 6.
- Wirkung von `CommandSet`/`CommandButton` und Radar nicht implementiert; `ChinookAIUpdate` fehlt, deshalb hat Amerika im Referenzmatch keine
  Versorgungswirtschaft; kein `UseSpecialPowerCommand`-Generator.
- Veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl und Science Purchase Points aus Erfahrung sind nicht modelliert; `FireWeaponWhenDeadBehavior` ohne
  Flächenschaden.
- 731 von 2.115 Objekten sind weiter nicht instanziierbar; 80 Module der Katalogkategorie „Phase 5“ sind ohne Terrain/Kollision/KI nicht sinnvoll umsetzbar
  (Blockerliste in `docs/SIMULATION-CORE.md` §11).


## 6. Welt, Terrain, Bewegung, Sicht und Skirmish-KI

**Ergebnis:** MAP-Daten werden zu einer spielbaren Welt mit Terrain, Hindernissen, Navigation,
Fog/Shroud und konkurrenzfähiger Skirmish-KI.

Umfang:

- Heightfield, Texturlayer, Wasser, Bridges, Waypoints, Triggerbereiche und Props laden;
- Footprints, Kollision, Bodentypen, Formationen und getrennte Bewegungsarten abbilden;
- deterministische Pfadsuche, lokale Vermeidung und Neuberechnung implementieren;
- Sichtweite, Shroud, Fog, Tarnung und Aufdeckung simulieren;
- Build-Pläne, Wirtschaft, Angriff, Verteidigung und Fähigkeitsnutzung der Skirmish-KI portieren.

Abgeschlossen, wenn die ausgewählte Vertical-Slice-Karte sowie ein repräsentativer Kartensatz
vollständig geladen werden, Einheiten zuverlässig navigieren und mehrere lange KI-gegen-KI-Läufe
deterministisch ohne Deadlock oder unerlaubte Information enden.

**Status: abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen.** Alle Zahlen frisch gemessen; Details, Numerik und Grenzen in `docs/SIMULATION-CORE.md` §13–§20.

**Umgesetzt:**

- **Karten:** `SageMapDecoder` (`CnCNew.Formats`, Containerformate CkMp/EAR/ZL5, Blend-Tile-Versionen 6–8) und das Vertragsmodell `MapData` (`CnCNew.Data.World`); `MapTool audit` dekodiert **150 von 150** Retail-Karten
  ohne Fehler (30.391.581 Höhenpunkte, 281.480 Objekte, 25.463 Wegpunkte, 2.488 Polygontrigger), baut für alle das Gelände (16.475.736 Pfadzellen, 258 Startpositionen, größte Ausdehnung 589 Zellen).
- **Gelände und Navigation:** `TerrainMap` (Zellklassen Kliff/Wasser, Höhe, Startpositionen, Fingerabdruck in Snapshot v4 und Replay v5), `NavigationGrid` (deterministischer A*, Fußabdruckprüfung,
  Fluchtzellen, `ObstacleSum` zur Neuplanung), drehratenbegrenzte Bewegung mit Geländehöhe in `AIUpdateInterface`. Bodenpfade zwischen Startpositionen: 462 Paare, 456 vollständig, 6 teilweise, 0 blockiert.
- **Sicht:** `VisionMap` (sichtbar/erkundet je Spieler, Sicht-Hash in Snapshot v5), Verbündetenteilung, `REVEAL_TO_ALL`, Baustellen, Tarnung; `FireWeaponCommand` verlangt Sicht (`TargetNotVisible`). Nebenfund: Stealth-Fehler
  behoben (`STEALTHED` nur mit `CAN_STEALTH`).
- **Bau:** Baustellen als Entities, sechs neue Befehle (Codes 20–25, `CommandTypes.Version = 8`, Replay v6, Snapshot v6), Bauplatzprüfung, Verkauf (179 Ticks, `SellPercentage`), kostenlose Reparatur, Sammelpunkte;
  `PowerPlantUpdate`/`PowerPlantUpgrade`/`OverchargeBehavior`; `CommandCatalog` (`CommandSet` → `CommandButton`).
- **Skirmish-KI:** `SkirmishAi` (Rollen aus Modulen, Bauplanung, Produktion, Angriff/Verteidigung, Versorger, Reparatur, Science), arbeitet nur mit sichtbaren Informationen, Gedächtnis und öffentlichen Startpositionen;
  `SkirmishMatchScenario`/`SimTool ai-match`.
- **Zahlen:** 77 von 205 Modultypen implementiert (3 neue), **1.393 von 2.115 Retail-Objekten instanziierbar (65,9 %)**, 15.382 von 19.553 Modulverwendungen abgedeckt (78,7 %); Kompatibilitätsbasislinie 741 Einträge;
  Definitionssnapshot 144.832.736 Byte (`AEB5108D…D2ADB7`); Stresstest 216.000 Ticks in 13,0 s, Endhash `D1521974C861A4B9`; Retail-Referenzmatch Sieger bei Tick 26.880, Endhash `5C370ED383EC9F6B`.

**Abnahme:**

- *Karten laden:* erfüllt für alle 150 Karten (Dekodierung und Geländeaufbau). Eine einzelne, ausgezeichnete Vertical-Slice-Karte gibt es erst mit der Präsentation (Schritt 7); die KI-Läufe nutzen vier Karten
  (Alpine Assault, Armored Fury, Bitter Winter, Death Valley).
- *Einheiten navigieren zuverlässig:* erfüllt mit Einschränkung – Navigation ist deterministisch und ohne Blockade auf den geprüften Startpaaren, aber vereinfacht (punktförmig, keine Kollision, 6 von 462 Startpaaren nur teilweise begehbar).
- *KI gegen KI, deterministisch, ohne Deadlock, ohne unerlaubte Information:* erfüllt. 16 Läufe (4 Karten × 4 Paarungen, höchstens 45.000 Ticks): **15 enden mit einem Sieger, 1 im Patt** (Alpine Assault, America gegen China);
  alle 16 sind mit Zweitlauf und Snapshotfortsetzung identisch. Der Test `SkirmishAiOnlyUsesWhatItSees` sichert die Informationsregel; ein Patt ist kein Deadlock (die Simulation läuft weiter), aber ein benanntes Ergebnis.
  America gewinnt nie, weil ohne `ChinookAIUpdate` keine Versorgungswirtschaft entsteht.

**Bekannte Grenzen:**

- *Nicht umgesetzt aus dem Umfang:* Brücken, Formationen, lokale Vermeidung und Kollision, Texturlayer (rein Präsentation), Skripte der Karte (`SidesList`/`PlayerScriptsList` bleiben ungelesen), Triggerbereiche nur als Wasserflächen.
  Die Skirmish-KI ist eine eigene Neuentwicklung, kein Port der Originalskripte.
- *Navigation:* flacher A* mit Budget von 200.000 Erweiterungen, genäherter Fußabdruck, Fluchtzellen mit Zuschlag statt echter Layer; Teil- oder Fehlpfad beendet den Befehl.
- *Sicht:* kreisförmig, ohne Höhen-Sichtlinie, alle 3 Ticks, keine Detektoren (getarnte Überlebende können ein Match verlängern).
- *Bau:* `QueueProduction` prüft keine Voraussetzung und keine Gebäudefähigkeit, `MaxSimultaneousOfType` ungeprüft, Reparatur kostenlos; keine Eroberung von Kraftwerken, kein Umschaltbefehl für `Overcharge`.
- *KI:* Spezialkräfte unklassifiziert, Gegnerstartpositionen öffentlich bekannt, von der Karte nur Lagerhäuser gesetzt, Startgeld (10.000) und Science-Punkte vom System, keine Flugzeuge und Flugplätze, Armee durch
  Instanziierbarkeit begrenzt, gleichstarke Paarungen können ein Patt erreichen.
- *Weiterhin offen:* Block 0.4 (RNG-Abgleich, nicht geraten), Projektil-Flugzeit, `ToppleUpdate`/`CrushDie` mit echter Kollision, `ChinookAIUpdate`/`TransportAIUpdate`, Wirkung von `CommandSet`/Radar,
  Veteranen-`ArmorSet`/`WeaponSet`, `CommandButtonHuntUpdate`, `SpecialAbilityUpdate`; 722 Objekte bleiben nicht instanziierbar (Blocker in `docs/SIMULATION-CORE.md` §11).

## 7. Vollständige audiovisuelle Weltpräsentation

**Ergebnis:** Der Windows-Client projiziert Simulations-Snapshots zu einer lesbaren und dem
Original funktional entsprechenden 3D-Schlachtfeldansicht.

Umfang:

- W3D-Hierarchien, Skelette, Animationen, Condition States und Turrets darstellen;
- Terrain, Wasser, Props, Decals, Partikel, Tracer, Projektile und FX rendern;
- Kamera, Auswahl, Kommandofeedback, Sicht/Fog, Beleuchtung und Schatten integrieren;
- Audioereignisse, Musik und Sprachwiedergabe über einen freien Runtimepfad ausgeben;
- Videos ohne proprietäre Miles-/Bink-SDK-Abhängigkeit wiedergeben;
- Rendering, Streaming und Ressourcenlebensdauer profilieren und stabilisieren.

Abgeschlossen, wenn ein komplettes headless Match live visualisiert und gesteuert werden kann,
alle sichtbaren Objektzustände korrekt wechseln und definierte Performance-, Speicher- und
Golden-Image-Budgets auf Zielhardware eingehalten werden.

## 8. Eingabe, UI und Lokalisierung

**Ergebnis:** Menüs und Ingame-UI bilden den gesamten Singleplayer-Bedienpfad
auflösungsunabhängig auf Basis der Originaldaten ab.

Umfang:

- Maus, Tastatur, Hotkeys, Auswahl, Kontrollgruppen und Befehlsmodi implementieren;
- WND-Layouts, Fonts, Texturen, Animationen und STR-Lokalisierung anbinden;
- Hauptmenü, Optionen, Gefechtssetup, Ladebildschirm, HUD, Command Bar, Tooltips, Menüs und
  Ergebnisbildschirm umsetzen;
- Auflösung, Seitenverhältnis, Skalierung, Audio-/Grafikoptionen und persistente Einstellungen;
- Fokus, Pausenverhalten und grundlegende Barrierefreiheit absichern.

Abgeschlossen, wenn das Programm ohne Entwicklerargumente vom Startmenü in ein Match und zurück
bedienbar ist, alle benötigten Texte auflösbar sind und UI-Tests mehrere Auflösungen und
Seitenverhältnisse abdecken.

## 9. Skirmish, Missionen, Kampagnen und Savegames

**Ergebnis:** Der definierte Produktumfang ist spielbar: vollständiger Skirmish sowie alle
Singleplayer-Kampagnen mit Fortschritt und neuen Savegames.

Umfang:

- Skirmish-Setup, Startpositionen, Teams, Regeln, KI-Profile und Statistik abschließen;
- Skriptengine, Trigger, Counter, Kameraabläufe, Briefings und Zwischensequenzen portieren;
- Missionsziele, Niederlagen-/Siegespfade, Kampagnenreihenfolge und Freischaltungen abbilden;
- versioniertes eigenes Savegameformat auf stabilen Simulations-Snapshots aufbauen;
- jede Karte und Mission über automatisierte Lade- und Headless-Abläufe prüfen.

Abgeschlossen, wenn alle Skirmish-Karten start- und beendbar sind, jede Kampagnenmission ihre
wesentlichen Erfolgs- und Fehlerpfade besteht und Speichern/Laden einen identischen
Simulationszustand fortsetzt.

## 10. Patch-Integration, Qualität und auslieferbarer Abschluss

**Ergebnis:** Aus den Domänen entsteht ein reproduzierbar baubares, diagnostizierbares und lokal
auslieferbares Produkt mit klar auswählbarem Original- und Bugfix-Verhalten.

Umfang:

- GeneralsGameCode-Fixes nach Subsystem prüfen und jeweils mit Regressionstest übernehmen;
- GeneralsGamePatch als eigene Overlay-Schicht integrieren und Balanceänderungen getrennt
  konfigurierbar halten;
- Crash-Reporting, strukturierte Logs, Diagnose-Dumps, Profiler und Support-Bundle ergänzen;
- Abhängigkeitswarnungen beseitigen oder Buildwerkzeuge isolieren, Lizenzen und Attributionen
  dokumentieren;
- Clean-Room-Build, frischen Assetimport, vollständige Testmatrix, Langzeitläufe und Packaging
  automatisieren;
- Installations-, Migrations- und Troubleshooting-Dokumentation fertigstellen.

Abgeschlossen, wenn ein frischer Rechner aus dokumentierten Voraussetzungen Quellcode und lokale
Originalinstallation reproduzierbar in ein startbares Paket überführen kann, die gesamte
Regressionstestmatrix grün ist, keine ungeklärten hochprioren Defekte offen sind und der in diesem
Dokument definierte Singleplayer-Scope end-to-end abgenommen wurde.

## Reihenfolge und Änderungsregel

Die Schritte werden in der angegebenen Reihenfolge abgeschlossen. Erkenntnisse dürfen spätere
Schritte präzisieren; Änderungen an Ziel, Abgrenzung oder Abschlusskriterien dieses Plans werden
jedoch separat begründet und versioniert. Die Schritte 1 bis 6 sind abgeschlossen. Der nächste Implementierungseinstieg ist Schritt 7
(Weltpräsentation) zusammen mit den offenen Simulationsschulden aus Phase 6 (Chinook/Transport-KI, Projektil-Flugzeit, Kollision;
Auftrag: `docs/HANDOFF-PHASE8.md`).
