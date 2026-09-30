# N+1-Handoff: Phase 7 – Weltpräsentation und offene Simulationsschulden

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§6 „Status“ und §7), `docs/SIMULATION-CORE.md` (besonders
§13–§20), `docs/GAME-RULES.md` (§10 und §11), `docs/W3D-MATERIALS.md` und `docs/CONTENT-PIPELINE.md`. `docs/HANDOFF-PHASE5.md` bis
`docs/HANDOFF-PHASE7.md` bleiben als Verlauf erhalten; ihre Zahlen sind veraltet.

Die Nummerierung: Dieser Auftrag deckt Schritt 7 des Gesamtplans ab (`IMPLEMENTATION-PLAN.md` §7, „Vollständige audiovisuelle
Weltpräsentation“). Er trägt zusätzlich die Simulationsschulden aus Phase 6 (Teil B), die für eine lesbare Präsentation vorrangig sind.

## Ehrlicher Status von Phase 6 (Ausgangslage)

Phase 6 ist abgeschlossen (Commits `c62332a`, `d1a0bdd`, `964150a`, `894a270`, `18c5b46`, `582fafb`), aber mit bewussten Grenzen
(vollständig: `IMPLEMENTATION-PLAN.md` §6, `SIMULATION-CORE.md` §20):

- **Abnahme erfüllt mit Einschränkungen:** 150 von 150 Retail-Karten dekodieren und bauen Gelände; Navigation ist deterministisch, aber punktförmig
  ohne Kollision (6 von 462 Startpaaren nur teilweise begehbar); 16 KI-gegen-KI-Läufe sind deterministisch, 15 enden mit Sieger, 1 im Patt
  (Alpine Assault, America gegen China). **America gewinnt nie**, weil `ChinookAIUpdate`/`TransportAIUpdate` fehlen und damit keine Versorgungswirtschaft entsteht.
- **Nicht umgesetzt:** Brücken, Formationen, lokale Vermeidung/Kollision, Kartenskripte (`SidesList`/`PlayerScriptsList` werden übersprungen),
  Höhen-Sichtlinie, Detektoren, Flugzeuge in der KI, Prüfung von Bauvoraussetzungen in `QueueProduction`, `MaxSimultaneousOfType`, Eroberung
  von Kraftwerken, Umschaltbefehl für `Overcharge`, Wirkung von `CommandSet`/Radar, veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl.
- **Block 0.4 offen:** Die RNG-Folge wurde nie gegen ein kompiliertes Original verglichen; nicht raten.
- Die Skirmish-KI ist eine eigene Neuentwicklung (`SkirmishAi`), kein Port der Originalskripte; Gegnerstartpositionen sind öffentliches Wissen.

## Reproduktionsbasis (Stand Ende Phase 6, 30. September 2026; vor dem Weiterarbeiten neu messen)

- Vollinventar mit allen Kartenoverlays: 228 INI-Dateien geladen (28 ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module,
  333.061 Schlüssel, 18.138/19.688 Referenzen aufgelöst (1.550 sichtbare Fallbacks), Snapshot 144.832.736 Byte, SHA-256
  `AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`.
- Retail-Basisdaten (`--maps exclude`): 12.550 Definitionen, 2.115 Objekte, Snapshot 144.221.861 Byte, SHA-256
  `1248585D0BAAB79F5202FDB81F26A2B00962A9D393B0CE1FF93C5FFDF6A5C6E7`.
- Kompatibilitätsbasislinie 741 Einträge; Modulkatalog 205 Typen, 77 implementiert; 1.393 von 2.115 Objekten instanziierbar (65,9 %),
  722 blockiert; Modulverwendungen abgedeckt 15.382 von 19.553 (78,7 %); 77 Module der Kategorie „Phase 5“, 29 „Phase 6“, 22 „Phase 7“ offen.
- Befehlstabelle 25 Typen (`CommandTypes.Version = 8`), Snapshot Version 6, Replay-Format v6, Ablehnungscodes bis 34.
- Retail-Referenzmatch: 13 Fraktionen, Sieger Spieler 12 bei Tick 26.880, Endhash `5C370ED383EC9F6B`, Lauf-2-Kettenhash `EB8BDC1482BC20DF`.
- Stresstest (216.000 Ticks, 13,0 s): Endhash `D1521974C861A4B9`, Kettenhash `C298F5F74993D6CD`, 914.394 Befehle, Snapshot bei Tick 108.000
  11.862.400 Byte.
- `MapTool audit`: 150/150 Karten, 30.391.581 Höhenpunkte, 281.480 Objekte, 16.475.736 Pfadzellen, Bodenpfade 462 Paare (456 vollständig, 6 teilweise, 0 blockiert).
- Asset-Runtimebestand 25.682 Dateien; `IniTool audit` 256/256 INI-Dateien, 66.625 Blöcke.

## Teil A – Weltpräsentation (Plan §7)

Die Präsentation liest ausschließlich immutable Simulationssnapshots und schreibt nur typisierte Inputbefehle; Stride-Typen bleiben in
`CnCNew.Client.Stride`. Vor dem Bau ist zu klären, welche Sichten die Simulation heute liefert (`IWorldServices`, `EntitiesVisibleTo`,
`VisionMap`, `TerrainMap`) und ob ein schreibgeschützter Snapshot-Leser (`SimulationSnapshot`/Render-Projektion) ergänzt werden muss.

1. **Terrain-Rendering:** Höhenfeld, Blend-Tiles/Texturlayer (`MapData`, `TextureClasses`), Wasser aus den Polygontriggern (`TerrainMap.Water`),
   Kliffs; Kartenvorschau aus einer der 150 Karten (Vertical-Slice-Karte festlegen und dokumentieren).
2. **Objekte:** W3D-Hierarchien, Skelette, Animationen, Condition States und Turrets (Grundlagen in `CnCNew.W3D`, `docs/W3D-MATERIALS.md`);
   `ModelConditionState`-Wechsel aus Snapshotzuständen (Bewegung, Angriff, Schaden, Bau-/Verkaufsfortschritt, `UNDER_CONSTRUCTION`).
3. **Kamera, Auswahl, Kommandofeedback:** Auswahlrechteck, Bewegungs-/Angriffs-/Baubefehle als typisierte Befehle (`ConstructCommand` mit
   Bauplatzvorschau über die Bauplatzprüfung), Sammelpunkte; Sicht/Fog aus `VisionMap` (sichtbar/erkundet).
4. **FX:** Partikel, Tracer, Projektile, Decals, Beleuchtung und Schatten; Projektile brauchen zuerst die Flugzeit-Simulation (Teil B).
5. **Audio und Video:** freier Runtimepfad, keine Miles-/Bink-SDK-Abhängigkeit; Ereignisse aus Snapshots ableiten.
6. **Profilierung:** Streaming, Ressourcenlebensdauer, Speicher- und Golden-Image-Budgets; `--smoke-test` des Stride-Clients erweitern.

**Abnahme von §7:** Ein komplettes headless Match (`SimTool ai-match`, Replay) kann live visualisiert und gesteuert werden, alle sichtbaren
Objektzustände wechseln korrekt, definierte Performance-, Speicher- und Golden-Image-Budgets werden auf Zielhardware eingehalten.
Bleiben Grenzen, benenne sie ehrlich in `IMPLEMENTATION-PLAN.md`.

## Teil B – Simulationsschulden, die die Präsentation blockieren oder verfälschen

Miss vor jedem Schritt mit `SimTool audit-coverage .local/phase2-import-a`, was ein Modul allein freischaltet. Häufigste Blocker (Objekte
insgesamt / nur durch dieses Modul blockiert): `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3,
`JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15,
`DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23.

7. **`ChinookAIUpdate`/`TransportAIUpdate`** (Amerika-Versorgung), `TunnelContain`, `HelixContain`; danach die 16-Lauf-Auswertung wiederholen
   (`SIMULATION-CORE.md` §19) und prüfen, ob Amerika gewinnt und das Patt verschwindet.
8. **Projektil-Flugzeit:** `DumbProjectileBehavior`, `MissileAIUpdate` (44/29); Schaden greift dann beim Einschlag.
9. **Kollision:** `ToppleUpdate` (76/66), echtes `CrushDie`/`SquishCollide`, `HeightDieUpdate`; Kollisions- oder Vermeidungsmodell an
   `NavigationGrid` anbinden (Formationen, lokale Vermeidung sind unerledigter Planumfang aus §6).
10. **Bau- und Regelgrenzen:** Voraussetzungs- und Fähigkeitsprüfung in `QueueProduction`, `MaxSimultaneousOfType`, Reparaturkosten,
    Kraftwerkseroberung, Overcharge-Befehl, `CommandSet`-/Radarwirkung, veteranenbasierte `ArmorSet`/`WeaponSet`, `CommandButtonHuntUpdate`,
    `SpecialAbilityUpdate`, Detektoren und Höhen-Sichtlinie.
11. **Kartenskripte** (`SidesList`/`PlayerScriptsList`) sind der Weg zu Missionen und Kampagne (Plan §9); sie zu lesen ist Voraussetzung, nicht Teil von §7.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt,
  dokumentiert und gegen `baselines/compatibility.json` abgesichert.
- Determinismus: kanonische Sortierung, keine Wandzeit, keine `Dictionary`-/`HashSet`-Reihenfolgeabhängigkeit; zwei unabhängige
  Läufe byteidentisch. `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab (`SourceRules`-Test); jedes `IUpdateModule` speichert `nextWake`.
- `TreatWarningsAsErrors=true`; nur die zehn bekannten NU1901/NU1903-Warnungen sind erklärt.
- Bestandszahlen immer neu messen, nie aus Docs oder diesem Handoff übernehmen.
- Fremder Code darf gelesen, nicht unbesehen kopiert werden; jeder portierte Algorithmus braucht einen Quellverweis (Datei, Funktion).
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 8) und bekommen eigene Codes; bestehende ändern sich nie. Änderungen am
  Snapshot- oder Replay-Format erhöhen deren Version (aktuell Snapshot 6, Replay 6); ältere werden mit klarer Meldung abgewiesen.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber;
  am Ende dieses Durchlaufs `docs/HANDOFF-PHASE9.md` (oder einen neuen Fortsetzungsauftrag) schreiben und committen.
- Praktisch: Große Editskripte mit dem Write-Werkzeug in eine `.js`-Datei im Scratchpad legen statt komplexer Heredocs; ein laufender
  SimTool-Prozess sperrt die Release-DLL.

## Abschlussbericht

Zahlen (mit Commit-Hashes), implementierte Funktionen/Module, Determinismus-, Test-, Stress- und Replayergebnisse, Performance- und
Golden-Image-Messungen, Modulabdeckung und Instanziierungsquote vorher/nachher mit Blockerliste, bekannte Grenzen und technische
Schulden, `git status` und Bestätigung „kein Remote, nichts gepusht“.
