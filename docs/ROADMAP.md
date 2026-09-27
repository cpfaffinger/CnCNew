# Roadmap

## 0. Reproduzierbare Ausgangsbasis

- Installation unverändert kopieren und per SHA-256 inventarisieren.
- EA-Quellcode, GeneralsGameCode und GeneralsGamePatch als lokale Referenzen festhalten.
- Original, installierte Modifikationen und künftige Engine-Ausgaben strikt trennen.

## 1. Asset- und Dateisystem

**Status: abgeschlossen am 27. September 2026.**

- BIG-Archive lesen, validieren und getrennt extrahieren.
- Virtuelles Overlay-Dateisystem mit originalgetreuer Suchpriorität bauen.
- Overlay einmalig als normale Dateien mit Herkunfts- und SHA-256-Manifest materialisieren.
- Laufzeitzugriff ausschließlich über das archivfreie `CnCNew.Content` bereitstellen.
- INI-, STR-, MAP-, WND-, W3D-, DDS/TGA-, Audio- und Videoformate katalogisieren.
- Asset-Konverter nur dort einsetzen, wo Laufzeit-Decoding nicht sinnvoll ist.
- Schema-2-Manifest, deterministischer Abhängigkeitsgraph, atomarer inkrementeller Import und vollständiger Format-/Referenzaudit sind umgesetzt; Bestandsmatrix und Fallbacks stehen in `CONTENT-PIPELINE.md`.

## 2. Headless-Simulation

- **Datengrundlage abgeschlossen am 27. September 2026:** 256 INI-Dateien werden deterministisch
  zu unveränderlichen Definitionen, expliziten Moduldeskriptoren und einem typisierten
  Referenzgraphen kompiliert; lose Parserknoten überschreiten die Daten-/Simulationsgrenze nicht.
- deterministischen Simulations-Tick und reproduzierbare Zufallszahlen implementieren.
- den registrierten Template-/Module-/Behavior-Vertrag im Simulationskern instanziieren.
- Spieler, Fraktionen, Upgrades, Sciences, Waffen, Schaden, Produktion und Wirtschaft.
- Terrain, Navigation, Sichtweite, Shroud/Fog, Zielwahl und Skirmish-KI.
- Golden-Master- und Replay-basierte Vergleichstests gegen das Original.

## 3. Windows-Client

- Fenster, Eingabe und Direct3D-Renderer auf aktuellem Windows-Unterbau.
- **Erster Vertical Slice erreicht:** archivfreies W3D-Modell wird samt Materialgruppen, UVs und Original-DDS-Texturen über Stride/Direct3D 11 gerendert.
- **W3D-Materialzustands-Slice erreicht:** Shader-/Vertexmaterialtabellen, IDs und DCG werden über den Vollbestand validiert; Alpha-Test, Blend, Depth-Zustände, Vertexfarben und Two-Sided-Culling sind im Direct3D-11-Client aktiv.
- **W3D-Materialdomäne abgeschlossen:** geordnete Mehrfachpässe, beide Texture-Stages, DCG/DIG/SCG, alle Shaderfunktionen des ausgelieferten Bestands, Environment-Mapping sowie typisierte und zeitabhängige Linear-/Grid-Mapper laufen rendererunabhängig getestet und im Direct3D-11-Client.
- Lokale deterministische PNG-Baselines decken transparente, additive, zweistufige und animierte Materialpfade ab; Originalassets und abgeleitete Bilder bleiben außerhalb von Git.
- W3D-Modelle, Animation, Terrain, Partikel, Decals, Schatten und UI.
- Audio-Ersatz für Miles und Video-Ersatz für Bink ohne proprietäre SDK-Abhängigkeit.
- Auflösungsunabhängige UI unter Beibehaltung der ursprünglichen Datenformate.

## 4. Spielmodi

- zuerst eine kleine Skirmish-Karte als vertikaler Slice.
- anschließend vollständiger Skirmish samt KI.
- danach Missionsskripte, Briefings, Zwischensequenzen und Kampagnenfortschritt.
- Savegames erst nach Stabilisierung des Simulations-Datenmodells.

## 5. Patch-Integration und Qualität

- GeneralsGameCode-Fixes nach Subsystem katalogisieren und mit Regressionstests übernehmen.
- GeneralsGamePatch als eigene Overlay-Schicht importieren; kontroverse Balanceänderungen konfigurierbar halten.
- Crash-, Speicher-, Pfadfindungs-, KI-, Grafik- und UI-Fehler priorisieren.
- Profiler, strukturierte Logs, deterministische Testläufe und Diagnose-Dumps.

## Nicht im ersten Scope

- Netzwerkcode, Matchmaking, NAT, GameSpy-Kompatibilität und Anti-Cheat.
- binäre Kompatibilität zu alten Mods oder Savegames, solange der Kern noch im Aufbau ist.

