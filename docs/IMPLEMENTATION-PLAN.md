# Implementierungsplan bis zum abgeschlossenen ersten Scope

Stand: 27. September 2026

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

## 3. Typisierte Spieldaten und Modulregistrierung

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

## 4. Deterministischer Simulationskern

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

## 5. Spielregeln, Kampf, Wirtschaft und Produktion

**Ergebnis:** Die vollständige Kernschleife eines RTS-Matches funktioniert headless:
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
jedoch separat begründet und versioniert. Der nächste Implementierungseinstieg ist ausschließlich
Schritt 1, beginnend mit der roten W3D-Testassertion und danach der vollständigen Materialtreue.
