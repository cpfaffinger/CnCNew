N+1-Handoff (Phase 22): Festkomma-Auflösung – von Q47.16 auf Q39.24 (erste geplante Änderung aller Referenzwerte, bei 30 Hz)

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/SIMULATION-CORE.md` (Numerik, Snapshot, Replay, Referenzwerte), `docs/DETERMINISM.md` und die Abschlussberichte der Phasen 20 und 21 (insbesondere die Zähler „auf null gerundet“ aus dem Probelauf bei 60 und 120 Hz).
Voraussetzung: Phase 20 ist abgeschlossen. Phase 21 darf parallel laufen (eigener Worktree); gemergt wird 21 vor 22.

## Warum und warum jetzt

`Fixed` ist ein `long` mit 16 Nachkommabits (Q47.16): Auflösung 1/65.536, Bereich ±1,4 · 10¹⁴. Der Bereich ist um viele Größenordnungen größer als jede Karte, die Auflösung ist für Größen je Tick knapp. Eine Beschleunigung wird durch Rate² geteilt: 1 Einheit/s² ist bei 30 Hz 73 Rohschritte (bis 0,7 % Rundungsfehler), bei 60 Hz 18 (bis 2,7 %), bei 120 Hz 4,5. Kleine Werte runden auf null. `ToppleAndFallModules.cs` behilft sich schon heute mit zusätzlichen Bits. Mit acht zusätzlichen Nachkommabits (Q39.24: Auflösung 6 · 10⁻⁸, Bereich ±5,5 · 10¹¹) hat dieselbe Beschleunigung bei 60 Hz rund 4.660 Rohschritte.

Die Umstellung ändert jeden Hash, jeden Snapshot und jedes Replay. Deshalb geschieht sie **allein, bei unveränderten 30 Hz und vor der Umstellung der Tickrate**: so bleibt nachvollziehbar, welche Verschiebung von der Auflösung kommt und welche später von der Rate. Und sie geschieht vor jeder Veröffentlichung, solange kein Spieler Speicherstände hat.

Verworfen: Q31.32 (Bereich ±2,1 · 10⁹; Abstandsquadrate über eine große Karte liegen bei 10⁷ bis 10⁸ und hätten nur noch ein bis zwei Größenordnungen Abstand zum Rand, während der Gewinn gegenüber 24 Bit für keine bekannte Größe nötig ist). Wer in Schritt 4 einen Zwischenwert findet, der schon bei Q39.24 dem Rand nahekommt, meldet das, bevor er umstellt.

## Ehrlicher Status (Ausgangslage, Stand `2a66661`; neu messen)

- `Fixed.FractionBits = 16`, `ScaleRaw = 1 << 16` (`src/CnCNew.Simulation/Numerics/Fixed.cs`); Produkte und Quotienten über `Int128`, Rundung zur nächsten Zahl (halbe weg von null), Überlauf wirft.
- `FixedTrig`: π, 2π, π/2 als fest einkodierte Q16-Rohwerte; `Atan2` als CORDIC mit 16 Tabelleneinträgen (Fehler < 10⁻³ rad); `Sin`/`Cos` als Polynome (Fehler < 2 · 10⁻⁵). Beides ist gröber als die neue Auflösung und würde sie begrenzen.
- Rund 170 Verwendungen von `FromRaw`, `.Raw`, `FractionBits`, `ScaleRaw` in 42 Dateien; darunter fest einkodierte Annahmen außerhalb der Simulation wie `X.Raw / 65536f` (`src/CnCNew.Game/Live/LiveMatch.cs`), Umrechnungen in `SimulationQueries`, `FxWorldView`, `FrameProjector`, `MinimapImage`, `Suspension`.
- Snapshots (`JsonState`) und Replays (`ReplayFormat`) schreiben Rohwerte; Befehle tragen Rohkoordinaten.
- Ganzzahl-Hashfunktionen über Rohwerte (Turm-Scan, Stimmung) in `SimulationWorld.Turrets.cs`/`.Acquisition.cs`.

**Nachtrag aus Phase 19 (6. Oktober 2026):** `Fixed.Mul`, `Fixed.Div` und `Fixed.Sqrt` sind seit `40fcbc6`/`da6a4ed` handoptimiert (keine 128-Bit-Softwaredivision, Newton-Startwert nahe der Wurzel) und mit Äquivalenztests gegen die frühere Fassung abgesichert (`Tests.PerfMovement.cs`, `Tests.Indexes.cs`). Diese Routinen und ihre Tests sind auf 16 Nachkommabits zugeschnitten und müssen beim Wechsel auf Q39.24 neu hergeleitet werden; die Messlatte für die Geschwindigkeit ist `docs/PERFORMANCE.md` §6a.

**Nachtrag aus Phase 20 (6. Oktober 2026, Stand `9299a2d`):** die Zähler des Probelaufs stehen in `docs/TICKRATE-MIGRATION.md` §11. Katalogbau (`SimTool rate-audit .local/phase2-import-a --tick-rate N`): im benutzten Katalog rundet bei 60 und 120 Hz kein Wert auf null (einzig `Acceleration = 0.1` des von keinem Objekt benutzten `NapalmBombLocomotor` bei 120 Hz); über 1 % Quantisierungsfehler 1 Wert bei 60 Hz und 41 Werte in 5 Feldern bei 120 Hz (schlechtester `LocomotorInfo.Braking`, 9,59 %), über 10 % keiner; unter acht signifikanten Bits 46 Werte bei 60 Hz und 321 bei 120 Hz (`LocomotorInfo.Braking/Acceleration/Lift/TurnRatePerTick`, `TurretInfo.PitchRate/TurnRate`, `WeaponInfo.WeaponSpeed`). Laufzeit (`--time-probe`): 0 Rundungen auf null bei bis zu 5,9 Millionen Umrechnungen je Lauf; **nicht gezählt** sind Beträge je Tick, die Module ohne den Zeitdienst bilden. `TemplateCatalog.GravityPerTickSquared` rechnet je Zugriff in Dezimal. Die Rate ist jetzt ein Wert der Welt; Kataloge, Snapshot und Replay tragen sie – die Umstellung der Auflösung geschieht weiter bei 30 Hz. Referenzwerte vor der Umstellung: Referenzmatch Tick 14.490 `23B4B25DF62765D8`/`DE5849AF9F8493C4`, Stresstest `56882F763EABEF67`/`B8E663493F2C73D5`, `WorldSnapshot` 9, Replay 11, `CommandTypes.Version` 14, `MissionSnapshot` 2.

**Nachtrag aus Phase 21 (7. Oktober 2026, Stand `2a0a6f5`):** Phase 21 ist gemergt. Für diese Phase wichtig: `SaveGame` ist Version 2 (Rate in der Hülle) und wird bei der Umstellung erneut erhöht; außerhalb der Simulation sind die `Fixed`→`float`-Umrechnungen unberührt bis auf zwei neue Verwendungen von `ToFloat(…) * world.Time.TicksPerOriginalFrame` in `Suspension.cs` und eine `Fixed.FromInt(…)`-Multiplikation in `UnitConditionTracker.cs`; Rohwert-Erwartungen in Tests sind bewusst stehen geblieben (`Tests.Turrets.cs`: `TurnRate.Raw is > 6860 and < 6866`; `Tests.JetFlight.cs`: `FromRaw(64)`, `FromRaw(2)`; `JetOrderTests`: `FromRaw(16)`); die Simulationstests haben `world.Frames(n)`/`world.InFrames(ticks)`/`world.PerFrame(v)` (`tests/CnCNew.Simulation.Tests/TestTime.cs`) und den Probeschalter `--tick-rate N`.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** Build, Tests, `reference-manifest check --set full`, Leistungsprofil. Zusätzlich für alle Läufe des Manifests **Ergebnisgrößen** sichern, die keine Hashes sind: Sieger, Endtick, Objektzahl je Spieler am Ende, Geld, Zahl der Schüsse/Produktionen/Bauten; für Missionen der Ausgang und der Tick des Endskripts. Sie sind der Vergleichsmaßstab nach der Umstellung.
2. **Vorarbeit ohne Ergebnisänderung (Manifest besteht weiter):**
   - Jede Stelle, die 16 Bit annimmt, auf die Konstanten bzw. auf Umrechnungsfunktionen umstellen (`65536`, `<< 16`, `>> 16`, `/ 65536f`). Außerhalb der Simulation eine einzige Umrechnung `Fixed` → `float` anbieten (in `CnCNew.Presentation` oder `CnCNew.Game`, nicht in der Simulation – dort bleibt `float` verboten).
   - Ein Quelltexttest verbietet diese Literale in allen Projekten.
   - `FixedTrig` so umschreiben, dass Konstanten und Tabellen aus `FractionBits` folgen (Konstanten mit genug Stellen fest einkodiert, nie zur Laufzeit aus `Math.*` erzeugt) und die Genauigkeit zur Auflösung passt (mehr CORDIC-Schritte, höhere Polynomordnung). Bei 16 Bit müssen die Ergebnisse bitgleich zu heute bleiben – sonst ist schon das eine Ergebnisänderung und gehört in Schritt 3.
   - Ganzzahl-Hashfunktionen und alles, was Rohwerte direkt verrechnet, prüfen.
3. **Die Umstellung: ein Commit, eine Ursache.** `FractionBits = 24`. Keine andere Verhaltensänderung im selben Commit. Formatversionen erhöhen: `WorldSnapshot`, Replay, `MissionSnapshot`, `SaveGame`, `CommandTypes.Version` (Befehle tragen Rohwerte). Alte Stände werden mit klarer Meldung abgelehnt, nie umgedeutet.
4. **Überlaufprüfung.** Der Bereich schrumpft um den Faktor 256. Größte Zwischenwerte suchen (Abstandsquadrate über die größte Karte, Flächen, Geld × Rate, Schadenssummen, die Multiplikation `Fixed * long` ohne Zwischenrundung). Da jede Operation `checked` ist, wirft ein Überlauf – das volle Manifest, der Stresstest und alle Kampagnenkarten (`CampaignTool audit`, `drive`) müssen ohne `OverflowException` laufen.
5. **Neu vermessen und vergleichen.** Manifest neu schreiben (mit Begründung im Commit). Jede Ergebnisgröße aus Schritt 1 neben den neuen Wert stellen. Erwartung: Ergebnisse verschieben sich nur durch Rundung – gleiche Sieger in der großen Mehrheit der Läufe, Endticks in der Nähe. Jede Abweichung, die darüber hinausgeht (anderer Sieger, Mission mit anderem Ausgang), wird untersucht, bis ihre Ursache benannt ist: chaotische Verstärkung einer Rundung ist zulässig und wird so benannt; ein Fehler der Umstellung nicht.
6. **Determinismus und Leistung.** Die Determinismus-Matrix aus Phase 18 mit dem neuen Manifest (mindestens die Zeilen des eigenen Rechners). Leistungsprofil: die Rechenkosten dürfen nicht steigen (dieselben `Int128`-Operationen); Snapshotgröße wächst (längere Zahlen) – messen.
7. **Probelauf bei 60 und 120 Hz wiederholen:** die Zähler „auf null gerundet“ aus Phase 20 neu erheben; Sollwert bei 60 Hz ist null Fälle, jeder verbleibende Fall wird benannt.
8. **Doku:** `docs/SIMULATION-CORE.md` (Numerik, Formate, Referenzwerte), `docs/ARCHITECTURE.md` (Q39.24), `docs/DETERMINISM.md`, `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md` (Versionen, Referenzwerte – die alten als überholt kennzeichnen), README, ROADMAP, IMPLEMENTATION-PLAN.

## Engstellen und Showstopper dieser Phase

- **Versteckte 16.** Ein einziges übersehenes Literal erzeugt keinen Absturz, sondern um den Faktor 256 falsche Werte in der Darstellung oder – schlimmer – in der Simulation. Deshalb erst die Vorarbeit mit Quelltexttest, dann die Umstellung.
- **`decimal` → `Fixed` beim Laden** behält jetzt mehr Stellen; Werte aus den Daten ändern sich im Rohwert (gewollt). Die Rundungsregel bleibt.
- **Quantisierte Eingänge** (Knochentabelle auf 1/16 Einheit und 1/1024 rad, Geländehöhen) bleiben unverändert genau; nur ihr Rohwert skaliert.
- **Dauer der Neuvermessung.** Kampagnenaudit und Zielfahrer über alle Karten sind lang; die Leistungsarbeit aus Phase 19 ist die Voraussetzung, dass das in vertretbarer Zeit geht. Hintergrundläufe mit begrenztem Timeout, je Kampagne einzeln.

## Abnahme

Die Vorarbeit lässt das alte Manifest bestehen; die Umstellung ist ein einzelner Commit; alle Läufe sind ohne Überlauf neu vermessen; die Vergleichstabelle der Ergebnisgrößen liegt vor und jede auffällige Abweichung hat eine benannte Ursache; die Matrixzeilen des eigenen Rechners bestehen. Der Abschlussbericht nennt alte und neue Referenzwerte mit Commit-Hashes und was nicht belegt ist. Am Ende die „Ausgangslage“ in `docs/handoffs/HANDOFF-PHASE23.md` aktualisieren.
