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

**Status: abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen.** Alle Zahlen aus den
Fachdokumenten der Pakete übernommen; Details in `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-OBJECTS.md`,
`docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`.

**Umgesetzt:**

- **Welt-Layer-Nahtstelle:** `RenderServices`/`IWorldLayer`/`LiveMatchHost`/`StrideConvert` trennen die Stride-Szene in
  unabhängige Layer, die ausschließlich Simulationssnapshots lesen; `--match <Karte>` startet ein steuerbares
  Live-Skirmish, `--smoke-test --capture` erzeugt headless ein PNG.
- **Terrain:** `Layers/Terrain` rendert Heightfield, Blend-/3-Wege-Texturen, Wasser und Kriegsnebel (Fog-Textur
  0/127/255) sowie die Minimap (`MinimapImage.Compose`) direkt aus `TerrainRenderModel`/`FogOfWarGrid`; 11 von 150
  Karten stichprobenhaft geprüft, 6 davon mit Screenshot verifiziert (z. B. Alpine Assault: 239×259 Zellen,
  156 Chunks, 26.630 Blend-Zellen, 370 Diagonalabweichungen(4)); 0,266 ms/Frame CPU-Mittel (Fortress Avalanche,
  900 Frames); Screenshot-SHA-256 zweier unabhängiger Läufe identisch (`CFF6C66C4708683A…`).
- **Objekte:** `Layers/Objects` teilt sich ein Stride-Model+Skelett je Rigid-Part-Modell und baut skinned Teile
  (169 im Vollbestand) pro Instanz; Teamfarbe über ZHC-Hue-Shift-Texturen und HOUSECOLOR-Vertexfärbung. Beispiel
  Alpine Assault (America gegen China): 590 Drawables, 10 im Sichtkegel gezeichnet, 78 geladene Modelle,
  2.080 Dreiecke, 0,19–0,27 ms/Frame, 0 zusätzliche Allokationen, SHA-256 über 300 und 1.500 Frames identisch.
  Beispiel GLA gegen America (6.000 Frames): 612 Drawables, 16 gezeichnet, 9 skinned Infanteristen, 124 Modelle,
  6.133 Dreiecke, 0,38–0,60 ms/Frame, 168 Byte/Frame, identischer Hash in beiden Läufen.
- **FX:** `Layers/Fx` rendert Partikel (kamera-/bodenausgerichtete Sprites, Streaks), Tracer und Bodendecals
  (`DefaultMaxQuads = 32768`, vier Materialien); Demoring-Test (400 Frames): letzter Frame 2.457 aktive Partikel,
  1.516 gezeichnet, 9 Streaks, 4 Decals, 7 Batches, 0,23 ms CPU (Mittel 0,50 ms), SHA-256 über zwei Läufe identisch;
  Realdaten-Skirmish (12.000 Frames) 0 verworfene Quads, 0 unbekannte Batches, SHA-256 identisch; Bench mit 10.000
  aktiven Partikeln: 1,735 ms je Build (Mittel über 400 Wiederholungen).
- **Audio:** `CnCNew.Game/Audio` (`AudioDirector`, `EvaCatalog`, `MusicPlaylist`) wählt Musik, EVA-Ansagen (u. a.
  `BuildingLost`, `UnitLost`, `BaseUnderAttack`, `UpgradeComplete`, `LowPower`, `InsufficientFunds`) und Sound über
  fünf Busse (Music/Sfx/Voice/Ambient/Ui) deterministisch aus Snapshot-Ereignissen aus; `Audio/AudioOutput` speist
  `Stride.Audio`-`DynamicSoundSource`-Instanzen aus dem rendererunabhängigen Mixer (Blockgröße 882 Frames bei
  44.100 Hz, vier Puffer, ~80 ms Latenz); Realdaten-Test über 600 gerenderte Frames (20 s) liefert über zwei Läufe
  byteidentische SHA-256.
- **Simulationsgrundlage (Paket E1):** Chinook-/Transport-/Tunnel-KI und Projektil-Flugzeit sind ergänzt; Modulkatalog
  jetzt 85 von 205 Typen implementiert, 1.470 von 2.115 Objekten instanziierbar (69,5 %), Kompatibilitätsbasislinie
  749 Einträge, Snapshot v7/Replay v7/`CommandTypes.Version` 9; Retail-Referenzmatch: Sieger FactionAmerica bei
  Tick 13.800, Endhash `4DCB46FE3D2E8214`.

**Abnahme:**

- *Komplettes headless Match live visualisierbar und steuerbar:* erfüllt – `--match`/`--smoke-test --capture` zeigen
  und steuern ein laufendes Skirmish-Match, mehrfach deterministisch reproduziert (identische Hashes über
  wiederholte Läufe in Terrain-, Objekt- und FX-Layer).
- *Alle sichtbaren Objektzustände wechseln korrekt:* erfüllt mit Einschränkung – `ModelConditionState`-Wechsel
  (Bewegung, Angriff, Schaden, Bau-/Verkaufsfortschritt) werden dargestellt, aber Türme bleiben in Ruhepose, weil
  `DrawableInstance.TurretYaw/TurretPitch` von der Präsentation aktuell immer `null` liefert
  (`W3DModelDraw::handleClientTurretPositioning` ist nicht nachgebildet).
- *Definierte Performance-/Speicher-/Golden-Image-Budgets eingehalten:* erfüllt für die gemessenen Layer (Terrain
  0,266 ms, Objekte 0,19–0,60 ms, FX ≤ 1,735 ms für 10.000 Partikel – deutlich unter dem ursprünglich genannten
  Richtwert von 4 ms für ~600 Drawables); Golden-Image-/Drawlist-Hashes sind über wiederholte Läufe byteidentisch.
  Gemessen auf der Entwicklungsmaschine, nicht auf separat definierter Zielhardware.
- *Videos ohne proprietäre Miles-/Bink-SDK-Abhängigkeit wiedergeben:* **nicht erfüllt.** Es gibt keinen
  Bink-Decoder; jeder Aufruf wird gezählt (`Stats.SkippedMovies`) und tut sonst nichts. Laut `docs/GAME-AUDIO.md`
  ist das eine bekannte, dauerhafte Grenze des Projekts (keine legale Dekodierung ohne die offizielle Bink-SDK),
  kein Implementierungsfehler dieser Phase.

**Bekannte Grenzen:**

- Kein Bink-Videodecoder (siehe oben) – dauerhafte Grenze, keine SDK verfügbar.
- Türme rendern in Ruhepose (siehe oben).
- Einige dünne, zweiseitige W3D-Meshes (`mesh.IsTwoSided` → `CullMode.None`) zeigen von der Rückseite eine
  unbeleuchtete (schwarze) Fläche, weil `MaterialDiffuseLambertModelFeature` die Normalen für Rückseiten nicht
  spiegelt – ein Verhalten der geteilten Stride-Materialpipeline, nicht des Objektlayers selbst.
- Kein Blob-/Kontaktschatten: aus Zeitgründen in dieser Phase nicht umgesetzt.
- `WaterAppearance.AdditiveBlending` ungeprüft (keine der elf sondierten Karten nutzt es); `WaterAppearance.TransparentWaterDepth`
  wird nicht ausgewertet (Wasser hat konstante Deckkraft unabhängig von der Tiefe).
- `FxFrame`-Inhalte werden nicht vollständig gezeichnet: `ParticleKind.Drawable` (1 von 1.087 Partikelsystemen im
  Retail-Bestand betroffen, kein W3D-Partikelmodell-Renderpfad), `ParticleKind.Smudge` (kein Postprocessing-Zugriff),
  `LightPulseInstance` (gezählt, nicht gezeichnet), `RayEffectRequest` (0 Retail-Verwendungen).
- Kamera-ausgerichtete Billboards/HLOD-Aggregate und -Proxies werden nicht aufgelöst; `AnimationMode.Manual` rendert
  immer Frame 0; `StartFrameFirst/Last`/`MaintainFrameAcrossStates*` werden nicht ausgewertet; Environment-Mapping
  nutzt eine feste Blickrichtung statt echter Pro-Pixel-Reflexion.
- Weiterhin offen aus Phase 6/Paket E1: echte Kollision (`ToppleUpdate`/`CrushDie`), Brücken, Formationen, lokale
  Vermeidung, Wirkung von `CommandSet`/Radar, veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl; 645 der 2.115
  Retail-Objekte bleiben nicht instanziierbar.

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

**Status: abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen.** Alle Zahlen aus den
Fachdokumenten der Pakete übernommen; Details in `docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`,
`docs/GAME-HUD.md`, `docs/CLIENT-UI-RENDERING.md`.

**Umgesetzt:**

- **Eingabe:** `CnCNew.Presentation.Input` (`GameInputController`, `SelectionModel`, `CommandModeController`,
  `CommandBarModel`, `HotkeyMap`, `CameraInputController`) übersetzt Ereignisse in der Reihenfolge von
  `GameClient.cpp` (Metatasten 20, Command-Bar-Hotkeys 25, Platzierung 30, GUI-Befehl 40, Auswahl 50, Kamera 60,
  Kontextbefehle 70) in typisierte Simulationsbefehle. `Data/English/CommandMap.ini`: 96 Einträge, 0 Probleme,
  0 Konflikte; 820 Befehlsschaltflächen (7 ohne gebundenen Befehl), 472 Befehlssätze, 0 Probleme; 14 synthetische
  Tests plus ein Realdatentest, zwei Läufe byteidentisch (`tests/CnCNew.Presentation.Tests`).
- **Menüs (`CnCNew.Game/Shell`):** Zustandsautomat Boot → Menüs (MainMenu/Options/SkirmishSetup+MapSelect/
  Credits/QuitConfirm) → MatchLoading → InMatch → Results → zurück, 8 Screens aus echten WND-Layouts, 0 nicht
  registrierte Callbacks/0 unaufgelöste Labels über alle Screens, 54 Golden-Hashes (8 Screens × 6 Auflösungen von
  800×600 bis 2560×1080), 0 Dateinamen-Rückfälle unter 64 gelisteten Mehrspielerkarten.
- **HUD (`CnCNew.Game/Hud`):** `MatchController` bindet Command Bar (14 Slots + 9 Warteschlangen-Slots), Minimap
  (Klick/Ziehen/Rechtsklick-Befehl), Ziehauswahl, Cameo-/Hotkey-/Kosten-/Cooldown-Overlays, Geld-/Energiebalken und
  einen EVA-Text-Ersatz-Ticker aus echten `ControlBar.wnd`/`QuitMenu.wnd`-Daten, ausschließlich über
  `LiveMatch.Commands`. Golden-Drawlist-Hash über vier Auflösungen (800×600 bis 2560×1080), alle 14
  Befehlsschaltflächen vollständig im Bild.
- **Einstellungen:** `OptionsScreen` bearbeitet eine Arbeitskopie von `GameSettings` und schreibt sie erst bei
  „Accept“ persistent über `SettingsStore` (`settings.json`, atomar); verdrahtet sind Auflösung, die drei
  Lautstärkeregler und die Scrollgeschwindigkeit.
- **UI-Rendering/Lokalisierung:** `CnCNew.Client.Stride/Ui/UiRenderer` zeichnet die von Shell/Hud erzeugten
  `UiDrawList`s auflösungsunabhängig per `SpriteBatch`; Text kommt aus FreeType (`Stride.SharpFont`), nicht aus
  `SpriteFont`/GDI. Letzter Verifikationslauf (32 PNGs über vier Auflösungen): `commands=5778 sprites=12542
  drawCalls=854 batches=361 missingImages=8 missingTextures=0 missingLabels=12 missingGlyphs=0`; zwei unabhängige
  Läufe liefern für alle 32 PNGs identische SHA-256.
- Build unverändert 21 Warnungen (bekannte Stride-Assetcompiler-Basislinie), 0 neu.

**Abnahme:**

- *Ohne Entwicklerargumente vom Startmenü in ein Match und zurück bedienbar:* erfüllt für Skirmish (MainMenu →
  SkirmishSetup → MatchLoading → InMatch → Results → MainMenu); Mehrspieler-, Lade-/Wiedergabe- und
  Kampagnen-Einstieg bleiben ohne Funktion (Buttons sichtbar, aber deaktiviert – außerhalb des Produktscopes,
  siehe `CLAUDE.md`).
- *Alle benötigten Texte auflösbar:* erfüllt mit Einschränkung – 0 gemessene Fallbacks bei Kartennamen und
  Shell-Labels; `ShellTextMeasure` liefert aber synthetische statt echter Glyphenmetrik, und FreeType-Glyphen sind
  pixelgenau zur FreeType-Rasterung, nicht zur originalen GDI-Rasterung (`CreateFont`/`ExtTextOut`) – Abweichungen
  zeigen sich höchstens bei sehr kleinen Schriftgrößen.
- *UI-Tests decken mehrere Auflösungen/Seitenverhältnisse ab:* erfüllt – Golden-Hashes für Shell (6 Auflösungen),
  HUD (4 Auflösungen) und den UI-Renderer (4 Auflösungen) sind über wiederholte Läufe byteidentisch.
- *Auflösung, Seitenverhältnis, Skalierung, Audio-/Grafikoptionen persistent:* **nicht vollständig erfüllt.**
  Auflösung, Lautstärke und Scrollgeschwindigkeit sind verdrahtet und persistent; Anzeigemodus (Fenster/Vollbild),
  Sprache, Seitenverhältnis und Barrierefreiheits-Textskalierung existieren zwar in `GameSettings`, haben aber in
  der ausgelieferten `OptionsMenu.wnd` kein eigenes Gadget und sind von diesem Screen aus nicht erreichbar.

**Bekannte Grenzen:**

- Kein Mehrspieler-, Lade-/Wiedergabe- oder Kampagnen-Einstieg vom Hauptmenü aus (Buttons sichtbar, deaktiviert);
  die zugehörigen Multiplayer-/Kampagnen-WND-Dateien (`GameSpyGameOptionsMenu.wnd`, `LanGameOptionsMenu.wnd`,
  `GameSpyLoadScreen.wnd`, `MultiplayerLoadScreen.wnd`, `ChallengeLoadScreen.wnd`) werden nicht geladen.
- Anzeigemodus/Sprache/Seitenverhältnis/Barrierefreiheits-Textskalierung sind nicht über die ausgelieferte
  `OptionsMenu.wnd` erreichbar (siehe oben).
- `RadioButtonUserMaps` liefert eine leere Liste (kein Importpfad für eigene Karten).
- Kein Mausklick beendet den Abspann (nur Escape) – das Nachrichtenmodell kennt bislang keine „beliebiger Klick auf
  ein Nicht-Gadget-Fenster“-Nachricht.
- Keine Formations-/Gruppenversatzlogik (`groupMoveToPosition` gibt allen Einheiten denselben Zielpunkt); kein
  Befehl „Feuer auf Position“; Kontextbefehle für Entführen, Autobombe, Sabotage, Bergen, Gebäude einnehmen und
  Hacken haben keine Simulationsbefehle und bleiben unübersetzt (`FeedbackKind.Unsupported`).
- `docs/PRESENTATION-INPUT.md` §10 listet sechs fehlende Simulations-Lese-/Schreib-APIs (u. a.
  Baukosten-Trockenlauf, Produktionswarteschlangen-Lesezugriff, `SpecialPowerModuleBase.AvailableOnFrame`,
  `UpgradeMuxModule.Executed`) sowie fehlende Befehle für Feuer-auf-Position, dauerhaften Angriffsmarsch, Wächter,
  Wegpunkte-als-KI-Zustände, Formationsbewegung, Einnehmen/Entführen/Sabotage/Hacken, Fallschirmabwurf,
  Waffenwechsel, Overcharge, Leuchtfeuer, Upgrade-Abbruch, Spezialkraft-Bau und Linienbau.
- FreeType-Glyphen sind pixelgenau zu FreeType, nicht zur originalen GDI-Rasterung (siehe oben).

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

**Status: Teil A Punkte 2–5 (Skriptausführung, Kampagnenlogik, Savegames, Headless-Prüfung) und Teil B Punkt 11 umgesetzt am 1. Oktober 2026; die Phase
ist damit noch nicht im Sinne der Abnahme abgeschlossen (siehe „Bekannte Grenzen“).** Alle Zahlen frisch gemessen; Details, Quellverweise und
Abweichungen in `docs/SCRIPT-ENGINE.md` und `docs/CAMPAIGN-AND-SAVEGAMES.md`.

**Phase-10-Zwischenstand (1. Oktober 2026, Teil der Durchspielbarkeits-Abnahme, noch nicht abgeschlossen):** Befehlsknopf-Fähigkeiten
(`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, die vier häufigsten der Familie) lösen jetzt echte `UseSpecialPowerCommand`-Befehle aus;
`SpecialAbilityUpdate` ist eine ehrliche Hülle (Nachlademechanismus ohne die inszenierte Fähigkeitswirkung) und macht 36 vorher blockierte
Objekte instanziierbar (Ranger, Jarmen Kell, GLA-Rebellin-Fähigkeiten). Lokale Seite ohne `playerIsHuman`: `MissionOptions.HumanSideName` plus
`CampaignTool --human-side` lösen das API-seitig; die General-Auswahl bleibt Shell-Arbeit. Zahlen: 1.530 von 2.115 Objekten instanziierbar
(72,3 %, vorher 70,6 %), 88 von 205 Modultypen, Modulverwendungen abgedeckt 15.730 von 19.553 (80,4 %), Kompatibilitätsbasislinie 747 → 751.
`CampaignTool audit`: fehlende Aktionsausführungen 5,7 % → 4,3 %, Endproben weiterhin Sieg 10/24, Niederlage 15/24 in Summe (Zusammensetzung
verschoben, `docs/SCRIPT-ENGINE.md` §9). Garnison, Kartenaufdeckung, KI-Teambau, Sichtbedingungen, `SKIRMISH_*` und Skirmish-Setup (Teil A
Punkt 1) bleiben offen; Details in `docs/HANDOFF-PHASE11.md`.

**Phase-11-Stand (1. Oktober 2026, frisch gemessen; Auftrag `docs/HANDOFF-PHASE11.md`, Vorbereitung für `docs/HANDOFF-PHASE12.md`):** die Skript-VM führt jetzt auch die
`SKIRMISH_*`-Bedingungen (Entscheidung „Portierung“ statt `SkirmishAi`-Ersatz, `docs/SCRIPT-ENGINE.md` §5c), Sicht-/Spezialkraft-Bedingungen, Kartenaufdeckung, Garnison/Behälter, Bauschalter,
`BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM`, die Wegpunkt-Varianten der Befehlsknopf-Fähigkeiten, **Sequenzskripte** (§5a) und **Teamereignis-Skripte** (§5b) aus; Upgrade-/`FIRE_WEAPON`-
Knöpfe haben einen eigenen Pfad. Bedingungen **80** ausgeführt / 29 fehlend von 109, Aktionen **131** ausgeführt / 129 weitergereicht / **2 aufgezeichnet** / **82** fehlend von 344
(vor der Phase: 63/46 und 73/129/0/142). Die Shell wählt die menschliche Seite einer Challenge-Karte selbst (`ChallengeSides`), `CampaignController.StartCurrentMission` hat den
Latenz-Parameter, und eine Kampagnenmission spielt hinter der vollen Skirmish-Befehlsleiste (`LiveGame`/`LiveMission`/`MissionController`, `docs/GAME-HUD.md` §14).
**Neuer Beleg für „die Mission endet auf ihrem eigenen Skriptweg“:** `CampaignTool reachability` prüft statisch, ob die `VICTORY`/`DEFEAT`-Skripte jeder Karte mit den implementierten Typen
überhaupt feuern können (Ketten über Flags, Zähler, Timer, Freischaltungen und Teamereignisse). Vorher (Stand von Phase 10, gleiche Analyse): Sieg sauber 4 / eingeschränkt 18 / blockiert 2,
Niederlage sauber 0 / eingeschränkt 24; **jetzt: Sieg sauber 21 / eingeschränkt 3 / blockiert 0, Niederlage sauber 24 / 24 Karten** (Tabelle: `docs/SCRIPT-ENGINE.md` §9). „Sauber“ heißt:
jede Bedingung und Aktion auf der Kette ist implementiert; es heißt **nicht**, dass ein Spieler die Mission gewinnen kann (Zustandsbedingungen sind Blätter). Die Ausschalt-Endproben des
`CampaignTool audit` sind unverändert Sieg 10/24, Niederlage 15/24 – sie fahren keine Ziele ab. Der neue Zielfahrer (`CampaignTool drive`, `MissionObjectiveDriver`) erfüllt die Weltzustands-Bedingungen der Skripte und zählt nur, wenn eine `VICTORY`/`DEFEAT`-Aktion des Skripts selbst feuert: **Sieg auf dem Skriptweg 10/24, Niederlage 16/24** (`docs/SCRIPT-ENGINE.md` §9, mit Befund, woran die übrigen scheitern). Weiterhin offen: `TEAM_HUNT_WITH_COMMAND_BUTTON`, `TEAM_SET_ATTITUDE`,
`TEAM_LOAD_TRANSPORTS`, die `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*`-Familie, die Überschreibungs-Beziehungen, Verstärkung mit Transport, `PLAYER_COMPLETED/MIDWAY_SPECIAL_POWER`, die Persona-Fraktion
der Challenge-Wahl (`docs/SCRIPT-ENGINE.md` §9, Blockerliste).

**Verlauf (Stand vor Phase 11, nur noch zur Nachvollziehbarkeit; die Zahlen unten stammen aus Phase 9):**

- **Skript-VM** (`CnCNew.Simulation/Scripting`): deterministische Ausführung der dekodierten World-Builder-Skripte (Zähler, Flags, Timer, Zufallstimer,
  Ein-Schuss-Skripte, Schwierigkeitsflags, verzögerte Auswertung, Unterprogramme, Teams mit Instanziierung, benannte Objekte, Bereichs- und
  Schadensbedingungen, Wegpunktpfade, Befehle als validierte Spielerbefehle, Angriffsprioritäten, Verstärkung, Sieg/Niederlage). Bedingungen 63 ausgeführt
  / 46 fehlend von 109, Aktionen 69 ausgeführt / 129 an den Client weitergereicht / 146 fehlend von 344; fehlende Typen werden zur Laufzeit gezählt
  (`MissingActionHits`/`MissingConditionHits`), unauflösbare Verweise als `Diagnostics`.
- **Missionssitzung** (`MissionSession`): Welt aus Kartendaten (Seiten, Beziehungen, platzierte Objekte, KI-Baulisten, Startbasis menschlicher Challenge-Spieler),
  Skripte + automatisches Gefecht + Weltschritt je Tick; `MissionSnapshot` (Version 1) wrappt den `WorldSnapshot` (Version 7) und speichert Skriptzustand und
  Befehlssequenzen. Objekte entstehen wie im Original sofort (`SimulationWorld.MaterializePendingSpawns`). Neue Weltzugriffe: `SetMoney`,
  `SetRelationship`, `TransferOwnership`.
- **Kampagne** (`CnCNew.Game/Campaign`): `CampaignCatalog` (Campaign.ini, 17 Kampagnen/85 Missionen, unbekannte Schlüssel gemeldet), `CampaignProgress`
  (Sieg rückt vor, Niederlage wiederholt, Versuchszähler für `MISSION_ATTEMPTS`), `CampaignController`/`MissionRun`.
- **Savegame** (`CnCNew.Game/Save`): `SaveGame` Version 1 (Kopf + Fortschritt + eingebetteter Missionszustand, SHA-256-Prüfsumme, kanonische Bytes,
  verständliche Ablehnungsgründe), `SaveGameStore` (atomar, listet beschädigte Dateien mit Grund).
- **Prüfung:** `CnCNew.CampaignTool` (`list`, `audit`, `playthrough`) und `SimTool mission-run`. 24 eindeutige Kampagnenkarten: 24/24 deterministisch (zwei Läufe),
  24/24 identisch nach Speichern/Wiederherstellen, Endproben durch Ausschalten der Gegner Sieg 10/24 und durch Ausschalten des Betrachters Niederlage 15/24;
  eine Challenge-Kampagne wurde über fünf Missionen mit Speichern/Laden je Mission durchgespielt. Neue Tests: 11 Skript-/Missionstests in
  `Tests.Scripting.cs`, `CampaignTests.cs` (7 + Realdaten).
- **Teil B:** Punkt 11 (Kartenskripte) siehe oben; Punkt 12 war bereits erledigt. `CommandButtonHuntUpdate` und `LockWeaponCreate` sind als ehrliche Hüllenmodule
  gebunden (`docs/SCRIPT-ENGINE.md` §8): 1.494 von 2.115 Objekten instanziierbar (70,6 %, vorher 69,5 %), 87 von 205 Modultypen, Modulverwendungen
  abgedeckt 15.639 von 19.553 (80,0 %), Kompatibilitätsbasislinie 749 → 747. Punkte 9 (Kollision) und 10 (Bau-/Regelgrenzen) sind **offen**.

**Bekannte Grenzen:**

**Bekannte Grenzen (Stand nach Phase 11):**

- **Nicht jede Mission ist als vollständig durchspielbar belegt.** Belegt ist, dass die Endskripte jeder Karte statisch erreichbar sind (21/24 Siege, 24/24 Niederlagen „sauber“) und dass jede Karte
  deterministisch läuft und nach Speichern/Laden identisch fortsetzt; **nicht** belegt ist, dass ein Spieler alle Ziele erfüllen kann. Die dynamischen Endproben (Auslöschen) erreichen weiterhin nur
  10/24 Siege und 15/24 Niederlagen, der Zielfahrer 10/24 Siege und 16/24 Niederlagen auf dem Skriptweg, weil die Siegskripte von Zielen (Zähler, Bereiche, Teams, Spezialkräfte) abhängen, die eine Auslöschung nicht abfährt. Was zur Einlösung der Abnahme
  „jede Kampagnenmission besteht ihre wesentlichen Erfolgs- und Fehlerpfade“ noch fehlt, ist ein Zielfahrer (Spieler-Agent oder Skript-Zeuge), der die Missionsziele erfüllt – Auftrag von
  `docs/HANDOFF-PHASE12.md`.
- Noch fehlende Skriptaktionen/-bedingungen mit Treffern (statisch, Skripte/Karten): `TEAM_HUNT_WITH_COMMAND_BUTTON` 53/12, `TEAM_SET_ATTITUDE` 42/11, `TEAM_LOAD_TRANSPORTS` 33/8,
  `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_OBJECTTYPE` 30/5, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 27/5, `PLAYER_SET_OVERRIDE_RELATION_TO_TEAM` 19/6, `TEAM_PANIC` 18/4, …; fehlende Bedingungen:
  `PLAYER_COMPLETED/MIDWAY_SPECIAL_POWER`, `SUPPLY_SOURCE_SAFE/ATTACKED`, `PLAYER_BUILT_UPGRADE`, `TEAM_STATE_IS`, `PLAYER_POWER_COMPARE_PERCENT`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`
  (`docs/SCRIPT-ENGINE.md` §9). Verstärkung mit Transport (`teamTransport`, `teamStartsFull`, `CINE_*`-Flugzeuge, Fallschirm) und Formationen/Angriffsmarsch sind nicht simuliert.
- Die Fraktion der Persona einer Challenge-Wahl (`ChallengeMode.ini` `PlayerTemplate`) ersetzt die Fraktion der Kartenseite nicht (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).
- Das Skirmish-Setup (Teil A Punkt 1) ist seit Phase 10 abgeschlossen (alle 64 Skirmish-Karten mit ≥2 Startpositionen starten und enden, `SimTool skirmish-sweep`); ein Skirmish-Speicherstand ist
  bewusst nicht unterstützt (`SkirmishMatchSession.CreateSave()` wirft; Replay/Checkpoint deckt Skirmish-Partien ab).
- Objekte, deren Vorlage im Retail-Basisbestand fehlt (Kartenüberlagerungs-Definitionen wie Straßen, Zäune, Bäume), werden übersprungen und gezählt; `*_HUNT`/`*_ATTACK_*` sind Bewegungsaufträge plus
  automatisches Gefecht (keine Formation, kein Angriffsmarsch); `PLAYER_AFFECT_RECEIVING_EXPERIENCE`/`OBJECT_ALLOW_BONUSES` werden nur aufgezeichnet.
- Freischaltungen (Kampagnenreihenfolge der Challenge-Kampagnen, Profil) sind nicht modelliert; Menü, Briefing, Ladeliste, Speichern im Spiel und Missionsabschluss sind angeschlossen
  (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7), der Stride-Client verarbeitet die Shell-Ausgaben (`StartMissionOutput`) noch nicht (`--mission` startet eine Karte direkt).
- Missionen sind keine Replays (Skriptaktionen sind keine aufgezeichneten Befehle); Wahrheit ist der Snapshot.
- Referenzwerte nach Phase 11: Retail-Referenzmatch Sieger FactionGLA bei Tick 19.200, Endhash `28BE35C1174E369F` (unverändert gegenüber Phase 10); Stresstest `56882F763EABEF67` (die frühere Angabe
  `ED6F9329F59662E3` war schon vor dieser Phase nicht mehr reproduzierbar).

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
jedoch separat begründet und versioniert. Die Schritte 1 bis 8 sind abgeschlossen, Schritt 9 ist weit fortgeschritten, aber nicht im Sinne seiner Abnahme beendet (siehe oben „Bekannte Grenzen“).
Der nächste Einstieg ist `docs/HANDOFF-PHASE12.md` (Zielfahrer für die Missionen, restliche Skriptfamilien, Verstärkungstransport, Client-Verdrahtung der Shell).
