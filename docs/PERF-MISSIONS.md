# Leistung der Kampagnenmissionen (Phase 19, Teilauftrag Skripte, Zielerwerb, Suchmodule)

Stand 6. Oktober 2026. Gegenstand: was einen Missionstick teuer macht, soweit es nicht Bewegung/Wegsuche, Sicht oder der Zustands-Hash ist –
die Skript-VM (`src/CnCNew.Simulation/Scripting`), der Zielerwerb (`SimulationWorld.Acquisition.cs`) und die Module, die je Update die Welt
absuchen (`PointDefenseLaserUpdate`, `CheckpointUpdate`, `DemoTrapUpdate`, `StealthDetectorUpdate`, die Docksuche der Versorger).

**Regel der Arbeit:** kein Ergebnis ändert sich. Jeder Commit besteht `SimTool reference-manifest check … --set quick` unverändert
(darin `mission_MD_USA05` und `mission_MD_GLA05`), der Endstand zusätzlich `--set full`, `input-manifest check`, die Prüfung des inkrementellen
Hashs und Kampagnen-Audit und -Fahrer (§6). Die Manifestdateien sind nicht angefasst.

**Alle Zeiten in diesem Dokument sind nicht ruhig gemessen.** Der Rechner (AMD Ryzen 7 PRO 6850U, 16 logische Prozessoren) lief während der
ganzen Arbeit mit vier weiteren Agenten; dieselbe Messung streut um den Faktor zwei bis drei (`AIUpdateInterface`, das hier nicht angefasst
wurde, lag je Lauf zwischen 388 und 1.228 µs je Aufruf). Belastbar sind deshalb die **Zähler** (Aufrufe, ausgewertete Bedingungen, allozierte
Bytes – alle deterministisch) und Zeiten **im Verhältnis innerhalb desselben Laufs**. Die ruhige Vergleichsmessung nach
`docs/PERFORMANCE.md` §4 steht aus (§7).

## 1. Messwerkzeug

`SimTool perf-mission` (docs/PERFORMANCE.md §3) kann seit dieser Arbeit sagen, *wo* Zeit und Speicher bleiben:

- **Allokation je Tickphase** („Allocated bytes per tick by phase“) und **je Modultyp** (Spalten `bytes/tick`, `bytes/call` der Modultabelle):
  `GC.GetAllocatedBytesForCurrentThread` an denselben Stellen, an denen schon die Uhr gelesen wird.
- **`--script-detail`**: die Skript-VM meldet einem `IScriptObserver` (`Scripting/ScriptObserver.cs`, das Gegenstück zu `ITickPhaseObserver`)
  die Abschnitte von `ScriptEngine.Update` (`ScriptSection`: Housekeeping, Scripts, Followers, ButtonHunts, TeamEvents, Sequences, TeamBuilds,
  TeamQueues, WorldOrders) sowie Beginn und Ende jeder Bedingungsauswertung und jeder Aktion. Das Werkzeug (`ScriptRecorder.cs`) führt je
  Bedingungs- und Aktionstyp Aufrufe, Eigenzeit, Maximum und Eigen-Bytes (verschachtelte Aufrufe abgezogen). Die Uhr steht im Werkzeug; die
  Simulation hält keine (`SourceRules`). Der Beobachter ist kein Zustand; ohne ihn läuft die VM dieselben Aufrufe in derselben Reihenfolge.
  Die Option ist nicht Standard, weil zwei Uhr- und zwei Zählerzugriffe je Bedingung sonst in jedem Profil der Phase `MissionScripts` stünden.
- **Erster Schritt**: wie viel des ersten Ticks die Laufzeit mit dem Übersetzen von Methoden verbringt (`System.Runtime.JitInfo`).

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-mission .local/content --map "Maps/MD_GLA05/MD_GLA05.map" --ticks 3000 --script-detail --top 30
```

## 2. Gemessene Aufschlüsselung des Ausgangsstands (`74ccdde`)

### 2.1 Die 4 MB je Tick sind nicht die Skripte

`docs/PERFORMANCE.md` §6.3 Punkt 5 schreibt die 4 MB Allokation je Tick von `MD_GLA05` den Skripten zu. Die Messung je Phase widerlegt das
(`MD_GLA05`, 1.500 Ticks):

| Phase | Bytes je Tick |
|---|---:|
| Updates | 5.174.216 |
| davon `PointDefenseLaserUpdate` | 5.155.999 (191.090 je Aufruf) |
| Acquisition | 232.842 |
| MissionScripts | 97.321 |
| alle anderen zusammen | 7.200 |
| **Summe** | **5.511.603** |

Ursache im Laser: `ScanRate = 0` in den Retail-Daten (`AirforceGeneral.ini`), also sucht jeder der 27 Laser **jeden Tick** ein Ziel, und die
Suche ging über alle rund 2.250 Objekte mit `PrimaryTargetTypes.Any(other.Template.IsKindOf)` – zwei Delegates und ein LINQ-Aufruf je Objekt,
dazu vier Arrays je Suche. 40.473 Aufrufe in 1.500 Ticks, 110,8 µs je Aufruf, 28,1 % der Update-Phase.

Die 233 KB im Zielerwerb waren die Skript-VM auf Umwegen: `IAcquisitionAdvisor.AttitudeOf` → `ScriptEngine.TeamOfEntity` baute beim ersten
Aufruf jedes Ticks das Wörterbuch Objekt → Team neu auf und lief bei **jedem** Aufruf über alle Teams (Enumerator eines `SortedDictionary`).

### 2.2 Skript-VM nach Abschnitt und Bedingungstyp

`MD_GLA05`, 3.000 Ticks, `--script-detail`. 690.622 Bedingungsauswertungen (230,2 je Tick) in 33 Typen, 471 Aktionen (0,2 je Tick) in 66 Typen;
102 Skripte führten ihre Aktionen aus.

| Abschnitt von `ScriptEngine.Update` | µs je Tick | Bytes je Tick |
|---|---:|---:|
| Scripts (alle Skriptlisten: Bedingungen und Aktionen) | 4.392,2 | 88.869 |
| TeamEvents (Feind gesichtet/Entwarnung, Team zerstört, untätig) | 1.898,1 | 4.652 |
| Housekeeping (`PruneTeams`, Zeitgeber) | 100,4 | 9.049 |
| Followers (stehende Befehle: Pfade, Jagd, Angriff) | 50,1 | 6.101 |
| ButtonHunts, Sequences, TeamBuilds, TeamQueues, WorldOrders | zusammen 4,9 | 0 |

| Bedingungstyp | Aufrufe je Tick | Eigenzeit µs je Tick | µs je Aufruf | Bytes je Aufruf |
|---|---:|---:|---:|---:|
| `PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA` | 40,4 | 2.367,4 | 58,60 | 824 |
| `BUILT_BY_PLAYER` | 5,0 | 401,6 | 80,33 | 1.699 |
| `PLAYER_ALL_DESTROYED` | 4,2 | 236,8 | 56,91 | 336 |
| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` | 11,1 | 198,1 | 17,90 | 160 |
| `PLAYER_LOST_OBJECT_TYPE` | 2,5 | 173,3 | 68,43 | 1.091 |
| `PLAYER_HAS_OBJECT_COMPARISON` | 2,1 | 131,5 | 61,98 | 932 |
| `SKIRMISH_SPECIAL_POWER_READY` | 2,0 | 130,7 | 65,32 | 1.171 |
| `PLAYER_ALL_BUILDFACILITIES_DESTROYED` | 1,0 | 84,4 | 84,36 | 2.991 |
| `PLAYER_HAS_N_OR_FEWER_BUILDINGS` | 1,0 | 62,2 | 62,24 | 881 |
| `PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA` | 1,0 | 61,1 | 61,09 | 951 |
| `TIMER_EXPIRED` | 67,9 | 38,0 | 0,56 | 64 |
| `FLAG` | 33,7 | 18,5 | 0,55 | 64 |
| `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` | 14,8 | 13,6 | 0,92 | 120 |
| übrige 20 Typen | 43,5 | 86,4 | – | – |
| **alle** | **230,2** | **4.003,6** | | 70.574 je Tick |

Befunde:

1. **Alles über 50 µs je Aufruf ist dieselbe Stelle:** `PlayerEntities(player)` filterte je Bedingung alle Objekte der Welt mit einem LINQ-`Where`
   über den geboxten Tabellen-Enumerator und sortierte das Ergebnis nach Id. Zehn Bedingungstypen, 60 bis 70 Aufrufe je Tick, 3,7 von 4,0 ms.
2. **Jede Bedingung allozierte 64 Bytes**, auch `TIMER_EXPIRED` und `CONDITION_TRUE`: im großen `switch` von `EvaluateCondition` lesen Lambdas
   die Parameterliste, also legt der Compiler das Closure-Objekt beim Eintritt in die Methode an – ob ein Lambda erreicht wird oder nicht.
   Dasselbe in `ExecuteScriptBody` (einmal je Skript und Tick, auch für inaktive Skripte: die 38 KB je Tick des Abschnitts Scripts außerhalb
   der Bedingungen) und im `switch` der `SKIRMISH_*`-Bedingungen (48 Bytes).
3. **TeamEvents** ist fast so teuer wie die Hälfte aller Bedingungen: `EnemyInSight` lief für jedes lebende Mitglied jedes Teams mit einem
   Feind-gesichtet- oder Entwarnungsskript über die ganze Welt, bis es einen Feind in Sichtweite fand – im Normalfall (keiner da) also ganz.
4. **Aktionen kosten nichts** (33,8 µs je Tick im Mittel, fast alles im ersten Tick: Kartenaufdeckung 6 bis 13 ms je Aktion).
5. **Housekeeping**: `PruneTeams` fragte jeden Tick für jedes benannte Objekt, ob sein Name in der sortierten Menge der je existierten Namen
   steht, und baute vier gefilterte Arrays und ein Delegate je Team.

### 2.3 Module, die die Welt absuchen

| Modul | Karte | Aufrufe in 3.000 Ticks | µs je Aufruf | Bytes je Aufruf | Was es je Aufruf tut |
|---|---|---:|---:|---:|---|
| `PointDefenseLaserUpdate` | `MD_GLA05` | 80.973 (27 Laser) | 93,9 | 191.090 | alle Objekte, Arten über LINQ (§2.1) |
| `CheckpointUpdate` | `MD_GLA05` | 2.999 (1 Posten) | 236,3 | 13.101 | zweimal alle Objekte; je Kandidat Randabstand = bis zu drei 128-Bit-Wurzeln |
| `StealthDetectorUpdate` | `MD_GLA05` / `MD_USA05` | 6.081 / 12.608 | 68,0 / 72,9 | 356 / 104 | alle Objekte; je Objekt zuerst die Spielerbeziehung (drei Tabellenzugriffe), dann erst das Tarn-Bit |
| `DemoTrapUpdate` | `MD_USA05` | 44.985 (15 Fallen) | 80,5 | 227 | alle Objekte, je Objekt zwei 128-Bit-Produkte für das Abstandsquadrat |
| `WorkerAIUpdate` | `MD_USA05` | 11.992 | 190,7 | 956 | u. a. Docksuche über alle Objekte mit Modulsuche je Objekt; der Rest ist Bewegung |

Zwei Ursachen darunter gelten für die ganze Simulation:

- **`ObjectTemplate.IsKindOf` allozierte 24 Bytes je Aufruf.** Es war `ImmutableArray<string>.Contains(kind, StringComparer.OrdinalIgnoreCase)`;
  dieser Aufruf alloziert in .NET 10 und braucht rund viermal so lange wie eine Schleife über die Handvoll Namen (einzeln gemessen, Release:
  24 B und 40–84 ns gegen 0 B und 9–17 ns). Gefunden über den Kontrollposten: seine 12,9 KB je Aufruf waren 540 solche Aufrufe.
- **`Fixed.Sqrt` begann Newtons Iteration bei `max(raw, scale)`**, für ein Abstandsquadrat also beim Quadrat selbst, und brauchte etwa eine
  128-Bit-Division je Bit, um das der Start zu groß war (einzeln gemessen: 80–225 ns je Wurzel).

### 2.4 Zielerwerb

`StepAcquisition` läuft je Tick über alle Objekte, deren KI im Leerlauf Ziele sucht; ein Blick (`FindMoodTarget`) lief über **alle** Objekte der
Welt und verwarf die eigenen und die des Systemspielers einzeln. Dazu die Team-Abfrage aus §2.1. Ruhig gemessen (PERFORMANCE.md §6.2):
503–541 µs je Tick in `MD_GLA05`, 276–312 in `MD_USA05`; unter der Last dieser Arbeit 974–2.329 µs.

### 2.5 Der erste Tick

Der längste Tick einer Mission ist Tick 0 (ruhig 169 ms in `MD_GLA05`, davon 47 % Skripte). Gemessen am Endstand in `MD_USA05`: erster Schritt
293,0 ms, **davon 218,0 ms Übersetzen von Methoden** (JIT) auf diesem Thread – die großen `switch`-Methoden der VM werden beim ersten Durchlauf
übersetzt. Der Rest sind die Aktionen des ersten Ticks, voran die Kartenaufdeckung. Das ist eine Eigenschaft des Prozesses, nicht der Mission
(ein zweiter Missionsstart im selben Prozess zahlt sie nicht), und hier nicht geändert (§7).

Tick 4.657 von `MD_GLA05` (93 ms, 94 % Updates, 8 MB) liegt in der Update-Phase außerhalb dieses Teilauftrags; sein Speicheranteil war zum
größten Teil der Laser (5 MB in jedem Tick).

## 3. Gemeinsames Mittel: Listen statt Suche über alle Objekte

`SimulationWorld.Indexes.cs` gibt drei Sichten auf die Objekttabelle, jeweils in aufsteigender Id-Reihenfolge und mit **genau den Mitgliedern,
die eine Suche über `Entities` mit dem jeweiligen Filter besucht**:

| Abfrage | Filter der ersetzten Suche |
|---|---|
| `EntitiesOwnedBy(PlayerId)` | `entity.Owner == owner` |
| `EntitiesOfKind(string)` | `entity.Template.IsKindOf(kind)` (ohne Rücksicht auf Groß-/Kleinschreibung) |
| `EntitiesWithModule<T>()` | ein Modul des Objekts ist ein `T` (Klasse oder Schnittstelle) |

- **Kein Zustand:** weder gehasht noch gespeichert. Jede Liste wird beim ersten Fragen in einem Durchgang über die Tabelle gebaut – eine Welt,
  die nie fragt (Stresstest), zahlt nichts, und eine wiederhergestellte Welt baut aus derselben Tabelle dieselben Listen.
- **Exakte Pflege** an den drei Stellen, an denen sich die Antwort ändern kann: `Register` (Objekt kommt in die Tabelle), `ProcessDestroys`
  (geht hinaus) und `SetOwner` – jetzt die eine Stelle, die `Entity.Owner` eines registrierten Objekts setzt (`TransferOwnership`,
  `ExecuteCapture`). Arten und Module eines Objekts ändern sich nie.
- **Reihenfolge:** die Tabelle zählt nach Id auf (`EntityTable`), die Listen auch. Wo ein Aufrufer mehrere Listen nacheinander liest (die Listen
  der Feindspieler, die Listen mehrerer Arten), ist bei der jeweiligen Änderung begründet, warum das Ergebnis nicht von der Reihenfolge abhängt.
- Wer eine Liste durchläuft, darf ihre Mitgliedschaft dabei nicht ändern; Skriptaktionen, die Objekte töten oder übertragen, bekommen wie
  bisher eine eigene Kopie (`ScriptEngine.PlayerEntities`).

## 4. Die Änderungen

Jede Änderung ist ein Commit mit einer Ursache; „Manifest quick“ heißt `reference-manifest check --set quick` auf genau diesem Stand.

| Nr. | Commit | Inhalt |
|---:|---|---|
| 1 | `b4a0598` | Messwerkzeug: `IScriptObserver`, `--script-detail`, Allokation je Phase und Modul |
| 2 | `432abae` | Listen nach Besitzer und Art; `PointDefenseLaserUpdate` liest die Listen seiner Zielarten |
| 3 | `4b1c3a9` | Objektbedingungen der Skripte lesen die Besitzerliste |
| 4 | `3426c0c` | `EnemyInSight` der Team-Ereignisse sieht nur die Objekte der Feindspieler an |
| 5 | `c51b37d` | Tabelle Objekt → Team wird nur nach einer Änderung neu gefüllt (`TeamTable`, `TeamMembers`, `MembershipStamp`) |
| 6 | `9c16bd1` | Zielerwerb: `FindMoodTarget` sieht nur die Objekte der Feindspieler an |
| 7 | `0926b7f` | `WorldEffectModules.Closest` (Kontrollposten, `EnemyNearUpdate`): Spieler mit der gesuchten Beziehung, Reichweitentest vor den Wurzeln |
| 8 | `cc39b54` | Skriptschleife und Housekeeping ohne Allokation |
| 9 | `da6a4ed` | `Fixed.Sqrt`: Startwert nahe der Wurzel |
| 10 | `9b1abf0` | `ObjectTemplate.IsKindOf` als Schleife |
| 11 | `43f1370` | `DemoTrapUpdate`: Reichweitentest vor den Produkten |
| 12 | `cde8162` | `StealthDetectorUpdate`: Tarn-Bit zuerst |
| 13 | `66a2f07` | Tests (§5) |
| 14 | `3c4fb10` | Liste nach Modultyp; Docksuche der Versorger und Sammelpunkt-Suche lesen Listen |

Warum jede davon das Ergebnis nicht ändert:

**2 – Laser.** Jede der vier Auswahlen (primär/sekundär, in/außer Reichweite) ist das Minimum von (Abstand, Id) über ihre Kandidaten – `Closer`
bricht Gleichstände selbst nach Id –, und eine Auswahl außer Reichweite wird nur benutzt, wenn ihre Liste keinen Kandidaten in Reichweite hat;
dann wurde sie auch nie übersprungen. Also hängt das Ergebnis nicht von der Reihenfolge ab, und ein Objekt mit zwei gelisteten Arten, das
zweimal kommt, ändert beim zweiten Mal nichts. Ob eine Vorlage primär, sekundär oder keins ist, wird je Vorlage einmal nachgesehen (im Modul,
nicht in den Moduldaten: der Katalog-Hash des Eingangsmanifests umfasst Felder). In der Suche wird keine Zufallszahl gezogen.

**3 – Objektbedingungen.** `PlayerEntities` war `Entities.Where(Owner == p && CountsAsObject).OrderBy(Id)`; die Tabelle zählt schon nach Id auf,
das Sortieren änderte die Reihenfolge nie. Die Bedingungen laufen jetzt über `EntitiesOwnedBy(p)` mit demselben Filter; dessen Vorlagenteil
(nicht `PROJECTILE`/`INERT`/`MINE`, `STRUCTURE`, `MP_COUNT_FOR_VICTORY`, Baueinrichtung) wird je Vorlage einmal nachgesehen. Die Typliste einer
Zählung (`objectTypesFromParam`) wird einmal je Zählung aufgelöst statt je Objekt – in der Zählung ändert sie sich nicht. Auswertungsreihenfolge,
Zähler fehlender Typen, Flankengedächtnisse, Einmal-Abschaltung: unberührt. Dieselben 690.622 Auswertungen vorher und nachher.

**4 – Feind gesichtet.** Ohne Beziehungsüberschreibung eines Skripts ist „Feind“ die Beziehung der beiden Besitzer (`AreEnemies` →
`RelationshipOf`); nur Objekte der Feindspieler des Mitglieds können antworten. Gefragt ist „gibt es eines“, die Reihenfolge ist gleichgültig.
Der Systemspieler und unbekannte Besitzer sind niemandes Feind. Mit Überschreibung (`_world.RelationshipOverride`) bleibt die Suche je Objekt.

**5 – Objekt → Team.** Die Regel, *wann* die Tabelle aufgefrischt wird (anderer Tick oder andere Mitgliedersumme), ist unverändert – sie
entscheidet, was ein Aufrufer nach einem Mitgliederwechsel innerhalb eines Ticks sieht, und eine „immer frische“ Tabelle wäre eine
Verhaltensänderung. Gespart wird nur die Arbeit: jede Änderung einer Mitgliedschaft oder der Teammenge zählt einen Stempel hoch
(`TeamMembers`, `TeamTable`); steht er noch, ergäbe Zählen dieselbe Summe und Füllen dieselbe Tabelle. `ScriptTeam.Members` ist dafür ein
`TeamMembers` (`IReadOnlyList<uint>`) statt einer `List<uint>`; ändern kann es nur die VM.

**6 – Zielerwerb.** Wie 4: ohne Überschreibung muss ein Opfer einem Feindspieler gehören (`IsAttackable` → `AreEnemies`). Die Prüfungen je
Kandidat sind derselbe Code (`ConsiderMoodTarget`). Die Auswahl ist das Maximum einer strengen Ordnung – (wirksame Priorität, Priorität, näher,
kleinere Id) für eine Einheit mit Angriffsprioritäten, sonst (näher, kleinere Id); welche gilt, hängt nur von der Einheit ab –, also
reihenfolgeunabhängig. Keine Prüfung zieht eine Zufallszahl oder ändert Zustand.

**7 – Kontrollposten.** Die Beziehung ist eine der Besitzer; Kandidaten sind die Objekte der Spieler mit der gesuchten Beziehung. Auswahl =
Minimum von (Randabstand, Id). Der Systemspieler ist nie Feind und für Verbündete ausgenommen; ein Besitzer, den die Welt nicht kennt, ist nur
sein eigener Verbündeter (so sagt es `RelationshipOf`, und so ist es nachgebaut). Reichweitentest: liegt ein Kandidat auf einer Achse weiter
weg als Reichweite + beide Radien (nach oben abgeschätzt: großer + kleiner Halbmesser + 1, ohne Wurzel) + 1 Einheit, dann ist sein Randabstand
größer als die Reichweite, was die andere Achse auch sagt; die eine Einheit ist weit mehr, als die Rundung der Quadrate und der Wurzel wegnehmen
kann (zusammen unter 2⁻¹⁵). Der Abstandstest selbst ist unverändert.

**8 – Skriptschleife.** Team-Bedingungen sind Schleifen über die Mitglieds-Ids statt LINQ mit Lambdas (ein lebendes Mitglied ist wie in
`LiveMembers` eines, das existiert und nicht zum Entfernen vorgemerkt ist). Die Skripte und Gruppen je Liste stehen als Arrays in
Programmreihenfolge fest (das Programm ändert sich nie); ob ein Skript oder eine Gruppe aktiv ist, wird weiterhin erst gefragt, wenn sie an der
Reihe ist. Die Instanzen des Bedingungsteams eines Skripts werden wie zuvor vor der ersten Auswertung in Id-Reihenfolge gesammelt, nur ohne
Closure und ohne Array im Normalfall (keine oder eine). `PruneTeams` sieht nur noch die Namen an, die noch nicht als „hat existiert“ bekannt sind
(eine aus den beiden Tabellen abgeleitete Menge, beim Laden neu gebaut). Gedächtnisschlüssel der Betreten/Verlassen-Bedingungen, das Argument
eines stehenden Befehls und der Text einer „unknown …“-Diagnose sind Funktionen ihrer Eingabe und werden je Eingabe einmal gebaut; gezählt wird
die Diagnose weiterhin bei jedem Lauf.

**9 – Wurzel.** Newtons Iteration über die ganzen Zahlen fällt von jedem Startwert ≥ Wurzel auf ⌊√Ziel⌋ und hält dort; der Startwert
2^⌈Bits/2⌉ liegt über der Wurzel. Derselbe Wert für jede Eingabe, nur weniger Divisionen.

**10 – `IsKindOf`.** Dieselbe Vergleichsregel (`OrdinalIgnoreCase`) über dieselben Namen. Kein neues Feld an der Vorlage.

**11 – Sprengfalle.** Liegt ein Objekt auf einer Achse mehr als eine Einheit jenseits der Auslösereichweite, ist sein Abstandsquadrat mindestens
Reichweite² + 2·Reichweite + 1; die Rundung der beiden Produkte (je eine halbe Rohwert-Einheit) bringt das nicht unter die Grenze. Der Kreistest,
die nach Id sortierte Liste, die Reihenfolge der Prüfungen darauf und die frühe Rückkehr bei einem Freund in Reichweite sind unverändert.

**12 – Tarnerkennung.** Die Prüfungen sind voneinander unabhängig und wirkungslos; ihre Reihenfolge entscheidet nur, was vergeblich angesehen
wird. Die Objekte, die bestehen, und die Reihenfolge, in der sie als entdeckt markiert werden, sind dieselben.

**14 – Docksuche.** Nur ein Objekt mit einem Dock-Modul hat Kosten (`Cost` fragt zuerst `DockOf`); die Objekte mit einem `IDockModule` sind also
alle Kandidaten, die es gibt (eine Obermenge, auf die derselbe Test angewandt wird). Auswahl = Minimum von (Kosten, Id). Die Sammelpunkt-Suche
(`Closest` nach Art) läuft über die Artliste in derselben Id-Reihenfolge wie die Suche, die sie ersetzt.

**Nicht benutzt** (wie vom Auftrag ausgeschlossen): Skripte seltener auswerten, Staffeln, Budgets, geänderte Reihenfolge, Näherungen, Threads,
Wandzeit, `float`.

## 5. Tests

`tests/CnCNew.Simulation.Tests/Tests.Indexes.cs`, alle über die öffentliche Schnittstelle, auf Welten, die mit festem Seed zufällig verändert
werden (Erzeugen, Töten, Entfernen, Besitzerwechsel, Gefechte):

| Test | Vergleich |
|---|---|
| `WorldEntityListsMatchScan` | Besitzer-, Art- und Modullisten gegen die gefilterte Suche über alle Objekte (Mitglieder und Reihenfolge): vom ersten Tick an gefragt, erst mitten im Lauf und erst am Ende gefragt, und in aus dem Snapshot wiederhergestellten Sitzungen (viermal im Lauf); Fragen ändert den Welt-Hash nicht |
| `ScriptObjectQueriesMatchScan` | die Objektbedingungen der Skripte (über `ExplainScript` gelesen: alles zerstört, Baueinrichtungen, Gebäudezahlen, Zählung nach Typ und Typliste, im Gebiet nach Typ und Art, Einheiten und Wert im Gebiet, gebaut von Spieler) gegen die Suche mit dem alten Filter und Sortieren; auch direkt nach einem Töten oder Besitzerwechsel, den noch kein Tick verarbeitet hat, und nach dem Wiederherstellen |
| `EnemyOwnerSearchesMatchPerObjectSearch` | Zielerwerb und „Feind gesichtet“ nach Feindspieler gegen die Suche je Objekt: eine zweite Sitzung bekommt eine Beziehungsüberschreibung, die keine Beziehung ändert, und muss Tick für Tick denselben Welt-Hash und dieselben Skriptzähler haben |
| `CheckpointScanMatchesWorldScan` | was Kontrollposten beider Spieler und des Systemspielers sehen, gegen die Suche über die Welt mit Randabständen, in allen vier Lagen |
| `FixedSqrtMatchesReferenceIteration` | über 40.000 Werte (Zweierpotenzen, Quadratzahlen und ihre Nachbarn, Kartenabstände, Zufallsmuster) gegen die alte Iteration und gegen die Definition der abgerundeten Wurzel |
| `KindLookupMatchesComparerLookup` | `IsKindOf` gegen `ImmutableArray.Contains` mit dem Vergleicher |

Die Sprengfalle, die Tarnerkennung, der Laser und die Docksuche haben keinen eigenen Vergleichstest gegen ihre alte Suche (ihre Suche ist nicht
von außen aufrufbar); sie stehen unter ihren vorhandenen Verhaltenstests und unter dem Manifest (`MD_USA05`: 15 Fallen; `MD_GLA05`: 27 Laser).

## 6. Ergebnis

### 6.1 Vorher/Nachher (nicht ruhig gemessen)

Stand `74ccdde` gegen den Endstand, `perf-mission`, 3.000 Ticks, je Karte abwechselnd alt, neu, alt, neu; während der Läufe liefen eigene
Prüfläufe und die Arbeit von vier weiteren Agenten. Zeiten in µs. Der Maßstab für die Last ist `AIUpdateInterface` je Aufruf (hier nicht geändert).

**`MD_GLA05`** (589×589 Zellen, 12 Seiten, 2.249–2.304 Objekte; Endhash `826CCD82C8536DC2`, Kette `10330D7D179054FD` in allen vier Läufen)

| Größe | alt 1 | neu 1 | alt 2 | neu 2 |
|---|---:|---:|---:|---:|
| Tick, Mittel | 54.152 | 33.420 | 49.529 | 24.647 |
| Tick, Median | 40.229 | 17.289 | 35.414 | 18.145 |
| Phase `MissionScripts` je Tick | 13.469 | 953 | 12.130 | 962 |
| Phase `Acquisition` je Tick | 2.325 | 208 | 2.015 | 229 |
| Phase `Updates` je Tick | 21.167 | 16.007 | 20.255 | 11.243 |
| `PointDefenseLaserUpdate` je Aufruf | 217,9 | 1,80 | 216,4 | 1,43 |
| `CheckpointUpdate` je Aufruf | 522,5 | 66,4 | 461,8 | 60,6 |
| `StealthDetectorUpdate` je Aufruf | 169,8 | 96,3 | 147,1 | 96,0 |
| *Maßstab* `AIUpdateInterface` je Aufruf | 857,8 | 1.009,1 | 812,0 | 639,3 |
| **Bytes je Tick** | **4.093.975** | **15.501** | 4.093.968 | 15.516 |
| GC Gen 0/1/2 in 3.000 Ticks | 1.435/19/2 | 6/2/0 | 1.436/18/2 | 7/3/1 |

**`MD_USA05`** (527×521 Zellen, 10 Seiten, 1.835–1.909 Objekte; Endhash `1CB28E6AA923BDEF`, Kette `0816C3438CB7F689` in allen vier Läufen)

| Größe | alt 1 | neu 1 | alt 2 | neu 2 |
|---|---:|---:|---:|---:|
| Tick, Mittel | 21.626 | 24.353 | 26.603 | 14.201 |
| Tick, Median | 14.478 | 11.233 | 16.361 | 9.853 |
| Phase `MissionScripts` je Tick | 1.901 | 725 | 1.521 | 527 |
| Phase `Acquisition` je Tick | 1.340 | 133 | 1.520 | 106 |
| Phase `Updates` je Tick | 8.811 | 10.481 | 10.928 | 5.384 |
| `DemoTrapUpdate` je Aufruf | 139,6 | 64,5 | 196,6 | 41,0 |
| `StealthDetectorUpdate` je Aufruf | 154,1 | 60,5 | 151,6 | 43,2 |
| `WorkerAIUpdate` je Aufruf | 356,3 | 378,0 | 438,6 | 254,0 |
| *Maßstab* `AIUpdateInterface` je Aufruf | 719,9 | 1.390,8 | 931,2 | 515,5 |
| **Bytes je Tick** | **209.110** | **20.147** | 208.752 | 20.150 |
| GC Gen 0/1/2 in 3.000 Ticks | 75/6/0 | 8/2/0 | 75/6/0 | 7/1/0 |

Lesart: Der Rechner war in diesen Läufen etwa viermal langsamer als in der ruhigen Messung (alt: 49–54 ms je Tick gegen ruhig 12,6–13,5 ms),
und die Last schwankte von Lauf zu Lauf (Maßstab 516 bis 1.391 µs). Aussagen, die das trägt:

- Die **Allokation** ist deterministisch: `MD_GLA05` von 4,09 MB auf 15,5 KB je Tick (Faktor 264), `MD_USA05` von 209 KB auf 20 KB. Gen-0-Sammlungen
  in 3.000 Ticks von 1.435 auf 6 bis 7.
- **Skriptphase** `MD_GLA05`: rund ein Dreizehntel (13.469/953, 12.130/962), `MD_USA05`: rund 2,6 bis 2,9.
- **Zielerwerb**: rund ein Neuntel bis ein Zwölftel auf beiden Karten.
- Laser 1/120 bis 1/150, Kontrollposten rund 1/8, Sprengfalle 1/2 bis 1/5, Tarnerkennung `MD_USA05` 1/2,5 bis 1/3,5.
- Der **ganze Tick** von `MD_GLA05` wird in beiden Paaren deutlich kürzer (Median 40 → 17 und 35 → 18 ms); wie viel davon ruhig übrig bleibt,
  sagt erst die ruhige Messung. Rechnet man die ruhigen Ausgangswerte (PERFORMANCE.md §6.2: Skripte 2,8–3,0 ms, Zielerwerb 0,50–0,54 ms, Laser
  30,6 % von 4,6–5,3 ms Updates) mit den Verhältnissen oben um, fallen in `MD_GLA05` etwa 4,5 bis 5 ms von 12,6–13,5 ms weg. Das ist eine
  Rechnung, keine Messung.

### 6.2 Prüfungen auf dem Endstand

| Prüfung | Ergebnis |
|---|---|
| `reference-manifest check --set quick` nach jedem der Commits 1–12 (Tabelle §4), jeweils auf genau diesem Stand gebaut | 9 Läufe identisch, zwölfmal |
| `reference-manifest check --set full` | 50 Läufe identisch (3.297 s unter Last, `--jobs 5`); zugleich die Prüfung des Commits 14 |
| `input-manifest check` | identisch |
| `reference-manifest check --set quick --verify-incremental` | 60.896 Ticks geprüft, 0 Abweichungen, 9 Läufe identisch |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministisch, 7/7 identisch nach Speichern/Laden; Ausgabe bis auf die Zeiten gleich der des Ausgangsstands; Endproben Sieg 7/7, Niederlage 6/7 (wie vorher) |
| `CampaignTool audit --ticks 900 --campaign GLA` | 5/5 und 5/5; Ausgabe bis auf die Zeiten gleich der des Ausgangsstands; Endproben Sieg 1/5, Niederlage 2/5 (wie vorher) |
| `CampaignTool drive --campaign CHALLENGE_0` (60.000 Ticks) | Sieg und Niederlage auf 7 von 7 Karten über das Skript der Mission |
| `tests/CnCNew.Simulation.Tests` (Debug) | alle bestanden |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | 370 Tests bestanden (368 im Lauf aus einer Kopie des Ausgabeverzeichnisses; die zwei Quelltextregel-Tests finden von dort die Repo-Wurzel nicht und wurden an Ort und Stelle wiederholt: bestanden) |
| `tests/CnCNew.Game.Tests -c Release -- --only CampaignTests,CampaignShellTests,MissionPresentationTests,MissionHudTests,MissionEffectsTests,MissionTextTests` | bestanden (728 s) |
| sauberer Solution-Build | `dotnet build CnCNew.slnx` nach Löschen von `bin`/`obj` des Clients: 0 Warnungen, 0 Fehler |

**Dauer des Kampagnen-Audits je Karte** (900 Ticks; nicht ruhig, alter und neuer Stand nacheinander unter verschiedener Last):

| Kampagne | alt, s je Karte | neu, s je Karte |
|---|---|---|
| `CHALLENGE_0` (7 Karten) | 32, 67, 23, 30, 35, 108, 106 (Summe 401) | 15, 26, 10, 22, 13, 54, 59 (Summe 199) |
| `GLA` (5 Karten) | 45, 41, 84, 122, 168 (Summe 460) | 76, 43, 98, 117, 146 (Summe 480) |

Das Ziel „unter einer Minute je Karte“ ist für `CHALLENGE_0` auch unter Last erreicht. Für `GLA` zeigt diese Messung **keinen** Gewinn und
drei von fünf Karten über einer Minute; der neue Lauf lief zusammen mit dem vollen Manifest. Ob die Karten ruhig unter einer Minute bleiben
(der Handoff nennt 20–60 s als ruhigen Ausgangswert), ist nicht belegt.

## 7. Was bleibt und warum

Gemessen am Endstand (`MD_GLA05`, `--script-detail`, unter Last; Größenordnungen):

| Rest | Größe | Warum er bleibt |
|---|---|---|
| Bedingungen: 230 Auswertungen je Tick | rund 260 µs je Tick unter Last, 1 µs je Auswertung | Das Original wertet jedes aktive Skript jeden Frame aus; seltener auswerten oder überspringen ist ausgeschlossen. Was noch ginge: `TIMER_EXPIRED`/`FLAG`/`COUNTER` (102 je Tick) suchen ihren Zähler über den Namen in einem sortierten Wörterbuch; den Zähler je Bedingung zu merken verlangt eine Entwertung bei `Load` und nach `ExplainScript` und bringt geschätzt 10–30 µs je Tick. Nicht gemacht. |
| Abschnitt TeamEvents | 100–220 µs | Je Team mit Ereignisskript mehrere Durchgänge über die Mitglieder (lebend, beweglich, untätig) mit kleinen Arrays (3,1 KB je Tick). |
| Abschnitt Followers | 60–150 µs, 5,5 KB je Tick | Stehende Befehle; die Jagd (`NearestEnemy`) sortiert noch alle Objekte, Befehle entstehen über Closures. |
| Abschnitt Housekeeping | 55–105 µs | `PruneTeams` fragt je Tick für jedes Teammitglied, ob sein Objekt noch existiert. Überspringen ließe sich das nur mit einem Beweis, dass seit dem letzten Mal nichts entfernt und nichts hinzugefügt wurde (Zähler der Welt, Stempel der Mitgliedschaften, vier weitere Tabellen); der Gewinn steht in keinem Verhältnis zum Risiko. |
| Zielerwerb | 105–230 µs je Tick unter Last | Der Sockel je Objekt, das Ziele sucht, und die Blicke über die Objekte der Feindspieler. |
| `DemoTrapUpdate`, `CheckpointUpdate`, `StealthDetectorUpdate`, Suche der Team-Ereignisse | 41–96 µs je Aufruf | Sie gehen weiter über eine Liste von Objekten und verwerfen fast alle. Richtig abgestellt wird das erst mit einem **räumlichen Index** der Welt. Der fehlt, weil `Entity.X/Y` einfache Eigenschaften sind, die die Bewegung setzt – ein Index, der bei jeder Positionsänderung mitgeführt wird, greift in `Entity` und in die Bewegungsmodule ein, an denen parallel andere arbeiten. Vorschlag für danach: ein gleichmäßiges Gitter in `SimulationWorld`, gepflegt in einem Positions-Setter; die Aufrufer hier sind darauf vorbereitet (jeder begründet, warum sein Ergebnis nicht von der Reihenfolge der Kandidaten abhängt). |
| `StealthDetectorUpdate` in `MD_GLA05` | 96 µs je Aufruf | Dort ist es die Höhen-Sichtlinie zu den getarnten Objekten in Reichweite (`TerrainMap.HasHeightLineOfSight`), Gebiet der Sicht. |
| `WorkerAIUpdate`, `SupplyTruckAIUpdate`, `TransportAIUpdate` | 250–1.000 µs je Aufruf | Bewegung und Wegsuche, nicht Gegenstand dieses Teilauftrags. |
| Erster Tick | 290–390 ms unter Last, davon 220–280 ms JIT | Eine Sache des Prozessstarts (ReadyToRun oder ein Aufwärmlauf wären Entscheidungen über die Auslieferung, keine Änderung der Simulation); der Rest sind die Aktionen des ersten Ticks, voran die Kartenaufdeckung (Sicht). |
| `SimulationWorld.IsPlayerDefeated` | selten | Noch ein LINQ-Durchgang über alle Objekte je Aufruf (Siegprüfung, `MULTIPLAYER_*`-Bedingungen); kam in keinem Profil nach oben, und die Datei gehört in dieser Phase anderen. |
| Hash, Sicht, Sicht-Hash, `AIUpdateInterface` | zusammen der größte Teil des Ticks | Andere Teilaufträge der Phase. |

Offen:

- **Die ruhige Messung** (`scripts/perf-profile.ps1`, PERFORMANCE.md §4) auf dem zusammengeführten Stand der Phase. Erst sie sagt, wie viel Kern
  ein Missionstick bei 60 Hz noch braucht; alle Zeiten hier sind unter Last entstanden.
- **Audit-Dauer der `GLA`-Kampagne** und der übrigen Kampagnen ruhig nachmessen (§6.2).
- **Zusammenführen:** Diese Arbeit ändert außerhalb ihres Gebiets je eine Stelle in `ObjectTemplate.IsKindOf` (Schleife) und `Fixed.Sqrt`
  (Startwert), fügt `SimulationWorld.Indexes.cs` hinzu, hängt vier Zeilen in `SimulationWorld.cs` (`Register`, `ProcessDestroys`) und je eine in
  `SimulationWorld.Scripting.cs`/`.PowerAndCapture.cs` (`SetOwner`) ein und erweitert `IWorldServices` um vier Mitglieder. Mit den parallelen
  Arbeiten an Bewegung, Sicht und Hash kann es dort Konflikte geben; `tools/CnCNew.SimTool/PerfCommand.cs` wird ebenfalls parallel bearbeitet.
- `docs/PERFORMANCE.md` §6.3 Punkt 5 (4 MB den Skripten zugeschrieben) ist durch §2.1 überholt; das Dokument selbst ist in dieser Arbeit nicht
  angefasst worden.
