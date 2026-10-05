# Stride-freie Spielhülle: Boot, Menüs, Skirmish-Setup, Ergebnis (Paket G1)

Dieses Dokument beschreibt `src/CnCNew.Game/Shell/**`: eine Stride-freie, headless testbare Zustandsmaschine für den
Rahmen um `LiveMatch` (`src/CnCNew.Game/Live/LiveMatch.cs`, nicht Teil dieses Pakets) – Programmstart, Hauptmenü,
Optionen, Skirmish-Einrichtung, Übergabe an das laufende Match und Ergebnisbildschirm. Die Schicht liest reale WND-
Layouts (`src/CnCNew.Ui/Wnd`, geparst über `WndParser`) und treibt sie über die Callback-Registry von `UiWindowTree`
(`src/CnCNew.Ui/Runtime`); kein Stride-Typ, kein Direct3D-Aufruf kommt in dieser Schicht vor. Referenzdokumente:
`docs/UI-WND.md`, `docs/UI-TEXT-AND-SETTINGS.md`, `docs/PRESENTATION-INPUT.md`.

## 1. Zustandsmaschine (`GameShell`, `ShellPhase`)

```
Boot (GameData.Load im Hintergrundthread, ShellGameLoadScreen)
  └─▶ Menus: MainMenu; darauf Options / QuitConfirm / MapSelect als Überlagerung, SkirmishSetup / Credits / ChallengeMenu / LoadGame als Ersatz (§13)
        └─▶ MatchLoading (SinglePlayerLoadScreen, genau ein gezeichneter Frame)
              └─▶ InMatch (Host besitzt LiveMatch/HUD; die Hülle zeichnet nichts)
                    └─▶ Results (ScoreScreen, aus einem echten SkirmishMatchSession)
                          └─▶ zurück zu Menus
```

`GameShell` hält einen Stapel von `ShellScreen`s (ein Screen = eine eigene `UiWindowTree`-Instanz, damit ein modaler
Dialog seine eigene Fokusreihenfolge hat und nicht in den darunterliegenden Screen durchsickert – `FocusOrder()` deckt
sonst den ganzen Baum ab). Nur der oberste Screen erhält Eingaben; `BuildDrawList()` verkettet die Zeichenlisten des
obersten Screens, der die darunterliegenden verdeckt (`ShellScreen.HidesScreensBelow`, wie `Shell::push`), und aller Screens darüber von unten nach oben und summiert deren
Statistik (§13). Das Hauptmenü scheint also nicht mehr durch Skirmish-Einrichtung, Credits, Challenge-Menü und Ladeliste.

### Host-Schnittstelle

| Methode | Zweck |
|---|---|
| `GameShell.Create(GameData, IShellEnvironment, language = "English", viewport?)` | Start bereits nach Boot (Tests, oder ein Host, der Content selbst lädt). |
| `GameShell.CreateLoading(contentDirectory, IShellEnvironment, language?, viewport?)` | Start in `ShellPhase.Boot`: öffnet den Content sofort synchron (WND/CSF brauchen keine kompilierten Definitionen), lädt `GameData` per `Task.Run` mit Fortschritts-Callback. |
| `Resize(width, height)` | Setzt den Viewport aller Screens im Stapel neu. |
| `HandleInput(InputEvent)` | Übersetzt `CnCNew.Presentation.Input`-Ereignisse in `UiWindowTree`-Aufrufe (`MouseMove/Down/Up/Wheel`, `KeyDown`, `Character`) und ruft `Dispatch()`. |
| `Update(dtSeconds)` | Bootfortschritt abfragen, Bildschirmübergänge (`MatchLoading` → `InMatch`), pro-Screen-`Update` (Credits-Scroll), UI-Uhr weiterstellen. |
| `BuildDrawList()` | Zeichenliste der sichtbaren Screens des Stapels (§13), mit `ShellCustomDraw` nachbearbeitet. |
| `DrainOutputs()` | Zieht `ShellOutput`s ab (siehe unten), wie `LiveMatch.DrainSoundCues`. |
| `MatchFinished(SkirmishMatchSession, MatchStatistics?)` | Vom Host aufgerufen, wenn das Match vorbei ist; zeigt den Ergebnisbildschirm. |

### Ausgaben (`ShellOutput`)

`StartMatchOutput(MatchSetup)`, `ExitRequestedOutput`, `SettingsChangedOutput(GameSettings)`,
`PlayUiSoundOutput(string EventName)`, `ApplyResolutionOutput(DisplayMode, Width, Height, VSync)`. Die Hülle kennt keine
Sounddateien oder Swapchains; sie benennt nur, was der Host tun soll.

## 2. Implementierte Screens und Callbacks

Jeder Screen registriert genau die `SYSTEMCALLBACK`/`INPUTCALLBACK`-Namen, die die ausgelieferte WND-Datei tatsächlich
benutzt (siehe Quellendumps unter dem Scratchpad der Sitzung). Ergebnis des Klickdurchlaufs in
`tests/CnCNew.Game.Tests/ShellTests.cs` gegen `.local/phase2-import-a` (neu messen, siehe unten): **0 nicht
registrierte Callbacks, 0 nicht aufgelöste Labels** über alle acht besuchten Screens.

| Screen (Datei) | Registrierte Callbacks | Zweck |
|---|---|---|
| `MainMenuScreen` (`MainMenu.wnd`) | `MainMenuSystem`, `MainMenuInput` | Eine gestapelte Datei mit allen Untermenüs; keine davon ist im WND-File selbst `Hidden` – das entspricht `MainMenuInit` (`MainMenu.cpp`), das die Hülle direkt im Konstruktor nachbildet: Einstiegsgruppe `MapBorder2` zeigen, die vier anderen Gruppen verstecken, dazu die sechs Fraktions-Schaltflächen „Recent Save“/„Load Game“ links neben der Spalte (`showSelectiveButtons(SHOW_NONE)`; Zero Hours `MainMenu.cpp` zeigt sie nie wieder – siehe §11). USA/GLA/China/Challenge öffnen die **Schwierigkeitsfrage in dieser Datei** (`MapBorder4`, §13). |
| `OptionsScreen` (`OptionsMenu.wnd`) | `OptionsMenuSystem`, `OptionsMenuInput` | Auflösung, drei Lautstärkeregler, Scrollgeschwindigkeit; Accept/Cancel/Defaults. Überlagerung des Hauptmenüs (`getOptionsLayout`, kein `Shell::push`). |
| `SkirmishSetupScreen` (`SkirmishGameOptionsMenu.wnd`) | `SkirmishGameOptionsMenuSystem`, `SkirmishGameOptionsMenuInput` | Karte, menschliche Fraktion/Name, bis zu 7 Computergegner, Startkapital → `MatchSetup`. |
| `SkirmishMapSelectScreen` (`SkirmishMapSelectMenu.wnd`) | `SkirmishMapSelectMenuSystem`, `SkirmishMapSelectMenuInput` | Kartenliste aus `Maps/MapCache.ini` (nur `isMultiplayer = yes`), Namen in der zweiten der zwei Spalten. Überlagerung der Einrichtung, die dafür Kartenvorschau, Armee-/Farb-/Team-Felder und Start versteckt (§13). |
| `ScoreScreenScreen` (`ScoreScreen.wnd`) | `ScoreScreenSystem`, `ScoreScreenInput` | Acht Ergebniszeilen aus `MatchResult`. |
| `CreditsScreen` (`CreditsMenu.wnd`) | `CreditsMenuInput` | Scrollender Abspann aus `Data/INI/Credits.ini`. |
| `LoadingScreen` (`ShellGameLoadScreen.wnd`, `SinglePlayerLoadScreen.wnd`) | keine | Beide Dateien deklarieren nirgends einen System-/Eingabe-Callback; nur `ProgressLoad` wird gesetzt. |
| `MessageBoxScreen` (`MessageBox.wnd`, `QuitMessageBox.wnd`) | `MessageBoxSystem`/`QuitMessageBoxSystem` | Gemeinsame Implementierung; beide Dateien deklarieren ebenfalls nirgends einen `INPUTCALLBACK`, Escape ist deshalb hier bewusst nicht verdrahtet. |

Eingebaute Callback-Namen (`PassMessagesToParentSystem`, `PassSelectedButtonsToParentSystem`,
`GameWinBlockInput`, `W3DNoDraw`, Gadget-Funktionen) brauchen keine Registrierung – `UiWindowTree` kennt sie bereits.

### Kampagnen- und Speicherstand-Screens (Phase 10, `CampaignScreens.cs`)

Fünf weitere Screens, nicht Teil des oben genannten Klickdurchlaufs/der 54 Golden-Hashes (eigene Tests in
`tests/CnCNew.Game.Tests/CampaignShellTests.cs`, `ShellTests.cs`): `ChallengeSelectScreen` (`ChallengeMenu.wnd`), `LoadGameScreen` (`SaveLoad.wnd`),
`MissionBriefingScreen` (`SinglePlayerLoadScreen.wnd` wiederverwendet), `MissionBannerScreen` (`Victorious.wnd`/`Defeat.wnd`,
keine Schaltflächen) und `MissionResultScreen` (`ScoreScreen.wnd` wiederverwendet). `CampaignFactionScreen` (`SinglePlayerMenu.wnd`) und `DifficultySelectScreen`
(`DifficultySelect.wnd`) gibt es seit Phase 16 nicht mehr: das Original öffnet beide in Zero Hour nie (§13). Details und Begründung je Screen:
`docs/CAMPAIGN-AND-SAVEGAMES.md` §7.

### Nicht in diesem Paket

`GameSpyGameOptionsMenu.wnd`, `LanGameOptionsMenu.wnd`, `GameSpyLoadScreen.wnd`, `MultiplayerLoadScreen.wnd`,
`ChallengeLoadScreen.wnd` (Multiplayer/Kampagne) sind nicht Teil des Scopes (siehe `CLAUDE.md`) und werden nicht
geladen; ihre Callback-Namen erscheinen daher folgerichtig nirgends in `UnregisteredCallbacks`, weil `ShellLayouts` sie
nie parst.

## 3. Benutzerdefinierte Zeichen-Callbacks (`ShellCustomDraw`)

Ein `DRAWCALLBACK`, den `UiWindowTree` nicht kennt, erscheint als `UiCustomDraw`-Befehl in der Zeichenliste
(`UiWindowTree.BuildDrawList`), gezählt in `UiDrawStatistics.CustomDraws`. `ShellCustomDraw` ersetzt genau die zwei
Namen, die in den implementierten Screens tatsächlich mit sichtbaren Fensterns vorkommen:

* **`W3DDrawMapPreview`** (`MapWindow` in `SkirmishGameOptionsMenu.wnd`, `WinMapPreview` in
  `SkirmishMapSelectMenu.wnd`) → Füllfläche, Rahmen, zentrierter Name der gewählten Karte (oder „No map selected“).
* **`W3DCreditsMenuDraw`** (`WinTextDraw` in `CreditsMenu.wnd`) → die Zeilen aus `ShellCredits`, vertikal scrollend
  über `CreditsScreen.Update(dtSeconds)` (deterministisch: eine Funktion der akkumulierten Simulationszeit, nie der
  Wanduhr).

**`W3DClockDraw`** (`Clock`) und **`W3DShellMenuSchemeDraw`** (`ShellMenuScheme`) aus `MainMenu.wnd` erscheinen in
keinem gemessenen Lauf: beide Fenster sind im WND-File selbst `Hidden`, und `UiWindowTree.BuildDrawList` bricht für ein
verstecktes Fenster ab, bevor es dessen Zeichen-Callback überhaupt auswertet. Die Hülle hebt diese Fenster nicht auf
(kein Uhr-Widget, kein animiertes 3D-Hintergrundschema) – eine bewusste, dokumentierte Vereinfachung, keine fehlende
Registrierung: Zeichen-Callbacks sind keine gerouteten Nachrichten, es gibt nichts zu registrieren.

## 4. Lokalisierung (`ShellTextSource`)

Jedes Label läuft über `GameData.LoadText(language)` (`LocalizedText`). `ShellTextSource.FallbackLabels` ist eine
vorsorgliche, gezählte Ersatztabelle für Labels, die laut Faktenlage einiger Menüs fehlen könnten
(`Tooltip:LoadProgress`, `GUI:LanguageFilter`, Anzeigemodus/Sprache/Seitenverhältnis/Skalierung/Kontrast, für die es in
den ausgelieferten Dateien gar keine eigenen Gadgets gibt). **Gemessen** (Klickdurchlauf über acht Screens, echte
Retail-Daten, Englisch): **0 Fallback-Treffer, 0 nicht aufgelöste Labels insgesamt** – jedes Label, das die acht
implementierten Screens tatsächlich sichtbar zeichnen, steht bereits in der ausgelieferten CSF. Die Tabelle bleibt als
Sicherheitsnetz bestehen (z. B. für den Fall, dass ein Tooltip einmal tatsächlich angezeigt wird) und wird weiter
gezählt, falls sich das mit mehr Testabdeckung ändert.

Kartennamen (`ShellMapNames.DisplayName`) lösen `MapCache.ini`s `nameLookupTag` (z. B. `MAP:AlpineAssault`) über die
gleiche Textquelle auf; **gemessen: 0 Dateinamen-Rückfälle** unter 64 gelisteten Mehrspielerkarten (von 150
Karteneinträgen/-dateien insgesamt) – jede hat einen auflösbaren Anzeigenamen.

## 5. Einstellungen (`GameSettings`, `SettingsStore`)

`OptionsScreen` bearbeitet eine Arbeitskopie von `GameSettings` und schreibt sie erst bei „Accept“ über
`SettingsStore` (`settings.json`, atomar, siehe `src/CnCNew.Ui/Settings/SettingsStore.cs`) und meldet
`SettingsChangedOutput`/`ApplyResolutionOutput`. „Cancel“ verwirft die Arbeitskopie, „Defaults“ setzt nur
Anzeige/Audio/Steuerung auf `GameSettings.Default` zurück (Sprache und Skirmish-Erinnerung bleiben unverändert).
Verdrahtet: Auflösung (`ComboBoxResolution`, gegen `Resolutions.Standard` plus die aktuelle Auflösung, falls sie nicht
in der Standardliste steht), die drei Lautstärkeregler, die Scrollgeschwindigkeit. **Nicht verdrahtet** (kein
passendes `GameSettings`-Feld, aber sichtbar und interaktiv, damit der Screen authentisch aussieht): Gamma, Detail-
/Antialiasing-Auswahl, alternative Maus, Doppelklick-Angriffsbewegung, Vergeltung, die gesamte `NetworkParent`-Gruppe,
das versteckte „erweiterte Anzeigeoptionen“-Panel. Anzeigemodus (Fenster/Vollbild), Sprache, Seitenverhältnis und
Barrierefreiheits-Textskalierung haben in der ausgelieferten Datei kein eigenes Gadget und sind von diesem Screen aus
nicht erreichbar (Anfrage: ein zukünftiges Revision bräuchte entweder eine eigene WND-Sektion oder die
Wiederverwendung eines der inerten Platzhalter).

## 6. Skirmish-Einrichtung und Anfragen an `LiveMatch`/die Simulation

`SkirmishSetupScreen` erzeugt einen `MatchSetup` aus: Karte (`SkirmishMapSelectScreen`, nur `isMultiplayer`-Karten),
menschliche Fraktion (`ComboBoxPlayerTemplate0`, inklusive „Random“, deterministisch über einen pro Slot festen Seed
aufgelöst – nicht den Match-Seed), bis zu 7 Computergegner (`ComboBoxPlayer1..7` = Offen/Computer, begrenzt durch die
Startpositionsanzahl der gewählten Karte aus `MapCache.ini`), Startkapital (`ComboBoxStartingCash` →
`SkirmishMatchOptions.StartMoney`). Start tut ohne Karte oder ohne mindestens einen Computergegner einfach nichts
(keine Inline-Fehlermeldung – bewusste Vereinfachung).

Nicht verdrahtet, weil `LiveMatch`/`SkirmishMatchOptions` es nicht unterstützen (Anfragen für eine künftige Phase):

* **Mehr als 7 Computergegner-Slots / eigene Startpositionswahl**: `LiveMatch.Create` weist Startposition 0 fest dem
  Menschen zu und iteriert die restlichen Startpositionen der Reihe nach; `ButtonMapStartPosition0..7` in beiden
  Karten-Screens sind rein dekorativ (Klicks laufen ins Leere).
* **Teams**: `ComboBoxTeam0..7` ist sichtbar und bedienbar, aber `SkirmishSlot.Team`/`MatchSetup` haben kein Feld dafür.
* **Spielerfarbe**: `ComboBoxColor0..7` zeigt nur generische „Color N“-Einträge (keine echte Farbpalette angebunden).
* **KI-Schwierigkeitsgrad**: die Skirmish-KI in diesem Stand kennt keine Schwierigkeitsachse; die Einrichtung bietet
  daher keine an (auch `GeneralSettings.DefaultAiDifficulty` wird von der KI nicht gelesen).
* **Spielgeschwindigkeit/Superwaffen-Limit**: `SliderGameSpeed`/`CheckboxLimitSuperweapons` sind sichtbar, aber ohne
  Wirkung.

## 7. Ergebnisbildschirm (`MatchResult`, `MatchStatistics`)

`LiveMatch` exponiert keine Tick-Ereignisse; der Host muss deshalb selbst eine `MatchStatistics` führen (`Record`
einmal pro `TickResult`, wie `SkirmishMatchSession.Step()` sie liefert) und sie `GameShell.MatchFinished` übergeben.
Ohne Tracker (`statistics: null`) oder wenn `HasData` nie wahr wurde, zeigt jede Zahlenspalte `-` statt einer
erfundenen Null. Gezählt werden nur `SimEventKind`-Werte mit eindeutigem Spielerbezug: `ProductionCompleted` →
Einheiten gebaut, `EntityDied`/`EntityDestroyed` → Einheiten verloren/zerstört, `ConstructionCompleted` → Gebäude
gebaut, `StructureSold` → Gebäude verloren, `SupplyDeposited` → Ressourcen. **`BuildingsDestroyed` wird nicht
gezählt** – es gibt kein Ereignis, das ein zerstörtes Gebäude von einer zerstörten Einheit unterscheidet (Anfrage an
die Simulation für ein künftiges `SimEventKind.StructureDestroyed` oder ein `IsBuilding`-Flag an `EntityDestroyed`).
Beobachter sind in `LiveMatch` nicht modelliert; `StaticTextObserverN` bleibt für jede Zeile versteckt.

## 8. Fokus, Tastatur, Barrierefreiheit

Escape schließt den obersten Dialog, sofern die zugehörige WND-Datei überhaupt einen `INPUTCALLBACK` deklariert
(`MainMenu`, `OptionsMenu`, die beiden Skirmish-Screens, `CreditsMenu`, `ChallengeMenu`, `SaveLoad` tun das; `MessageBox`/`QuitMessageBox` nicht –
das ist keine Lücke der Hülle, sondern exakt das Verhalten der ausgelieferten Datei). Hängt der Callback nicht an der Wurzel (`SaveLoad.wnd`: am Kind `SaveLoadMenu`), setzt der
Screen `UiWindowTree.KeyboardHome` (das `winSetFocus(parent)` der Layout-Init); Escape wirkt dort jetzt auch ohne fokussiertes Gadget (§13). Tab/Umschalt+Tab, Pfeiltasten,
Eingabe/Leertaste laufen unverändert durch `UiWindowTree.KeyDown` (siehe `docs/UI-WND.md`); jeder Screen hat wegen der
eigenen `UiWindowTree`-Instanz eine eigene, in sich geschlossene Fokusreihenfolge. UI-Skalierung läuft über
`UiScaleOptions`/`UiScaler` (`Mode = Uniform`, siehe `GameShell`-Konstruktor); ein Regler dafür in `GameSettings`
(`AccessibilitySettings.UiTextScalePercent`, `HighContrastUi`) existiert bereits, ist aber – siehe Abschnitt 5 – von
keinem Gadget aus erreichbar, weil die ausgelieferte `OptionsMenu.wnd` keins dafür vorsieht.

## 9. Tests und gemessene Zahlen

`tests/CnCNew.Game.Tests/ShellTests.cs` (Suite `ShellTests`, registriert in `Program.cs`), gegen
`.local/phase2-import-a`:

* **Klickdurchlauf**: Hauptmenü → Einzelspieler-Untermenü → Skirmish-Einrichtung → Kartenauswahl (Klick auf die erste
  Zeile der echten, gefilterten Kartenliste) → zurück → Start → Ladebildschirm → `StartMatchOutput` → ein echtes,
  600 Ticks laufendes `SkirmishMatchSession` (über das bereits vorhandene `LiveMatch.Create`, nicht neu geschrieben) →
  `MatchFinished` → Ergebnisbildschirm → zurück zum Hauptmenü → Credits → Escape → Beenden-Bestätigung (Nein, dann Ja).
* **Acht besuchte Screens** (`MainMenu`, `SkirmishSetup`, `SkirmishMapSelect`, `MatchLoading`, `ScoreScreen`,
  `Credits`, `Options`, `QuitConfirm`) je an sechs Auflösungen gezeichnet: 800×600, 1024×768, 1280×720, 1920×1080,
  2560×1080 und 640×960 (schmal, hochkant-ähnlich) – **54 goldene Hashes** in
  `tests/CnCNew.Game.Tests/baselines/screens.txt` (`SHA-256` von `UiDrawList.Dump()`), zwei unabhängige Läufe
  bytegleich.
* **0 nicht registrierte Callbacks, 0 nicht aufgelöste Labels** über alle 54 Messpunkte.
* Optionen: Auflösung und Musiklautstärke ändern, Accept, neue `GameShell`-Instanz mit demselben
  `IShellEnvironment` erzeugen → beide Werte kommen unverändert zurück (`settings.json`-Persistenz).
  `CNCNEW_UPDATE_BASELINES=1` schreibt die Hash-Datei neu (nach einer bewussten Änderung); Standardlauf vergleicht nur.
* Boot: `GameShell.CreateLoading` zeichnet sofort etwas (der Ladebildschirm ist ab dem ersten Frame sichtbar), erreicht
  nach dem Hintergrund-Ladevorgang zuverlässig `ShellPhase.Menus`.

* **Bildschirmstapel** (Phase 16, §13, `ShellTests`): nur der oberste ersetzende Screen wird gezeichnet (Skirmish-Einrichtung, Credits, Ladeliste, Challenge-Menü: keiner der sechs Hauptmenütexte
  erscheint), Optionen, Beenden-Dialog und Kartenwahl liegen über dem Screen darunter (Befehlszahl = Summe beider), die Einrichtung versteckt unter der Kartenwahl die Felder des Originals und
  stellt sie wieder her; Escape beendet die Ladeliste ohne Gadgetfokus; das Challenge-Menü zeigt Biografie und Play erst nach der Wahl, mit aufgelösten Texten, und startet die gewählte Kampagne mit
  der im Hauptmenü gewählten Schwierigkeit.
* **Fensterübergänge** (`ShellTransitionTests.cs`, zehn Tests gegen die echten Layouts, Einzelheiten §12): Einstieg, Eingabe bei versteckten Fenstern, Untermenüs, Fraktionslogos, Screen-Rundgang
  mit Flash-out/Fade-in/erneutem Einstieg, alle Kampagnenknöpfe, Ergebnisbildschirm mit hochzählenden Zahlen, byteidentische Wiederholung, Lauf ohne Animation.

`dotnet build CnCNew.slnx`: unverändert **21 Warnungen** (die dokumentierte Stride-Asset-Compiler-Basislinie), **0**
davon neu.

## 10. Bekannte Grenzen (Zusammenfassung)

* Kampagnenauswahl, Briefing, Ladeliste, Speichern im Spiel und Missionsabschluss sind seit Phase 10 angebunden
  (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7, `src/CnCNew.Game/Shell/Screens/CampaignScreens.cs`,
  `src/CnCNew.Game/Shell/MissionPresentation.cs`, `src/CnCNew.Game/Hud/MissionPauseMenu.cs`); nur Mehrspieler und
  Wiedergabe (Replays) bleiben außerhalb des Produktumfangs (Buttons sichtbar, deaktiviert).
* `RadioButtonUserMaps` liefert eine leere Liste (kein Importpfad für eigene Karten in diesem Stand).
* Kein Mausklick beendet den Abspann (nur Escape) – `WinTextDraw` ist ein einfaches `USER`-Fenster, und diese Laufzeit
  routet nur Gadget- und Tastaturnachrichten (siehe `docs/UI-WND.md`); eine „beliebiger Mausklick auf ein Nicht-
  Gadget-Fenster“-Nachricht existiert im Nachrichtenmodell bislang nicht.
* `ShellTextMeasure` liefert synthetische, deterministische Zeichenbreiten (keine echte Glyphenmetrik) und ist nur
  noch der Standard für kopflose Tests. Ein Host übergibt `GameShell.Create(…, textMeasure)` die Metrik der Schrift,
  mit der er zeichnet; der Stride-Client tut das seit dem Befundpaket in §11 (`ClientRuntime.ForShell` reicht
  `UiRenderer.TextMeasure` durch) – vorher saßen alle zentrierten Texte sichtbar neben der Mitte.
* Anzeigemodus/Sprache/Seitenverhältnis/Barrierefreiheits-Textskalierung sind in `GameSettings` vorhanden, aber von
  keinem Gadget der ausgelieferten `OptionsMenu.wnd` aus erreichbar.
* **Fensterübergänge (§12):** nur im Stride-Client eingeschaltet (`GameShell.Create(…, animateTransitions: true)`, `--no-transitions` schaltet sie ab); `Create` ohne den Parameter,
  alle Tests und die goldenen Hashes laufen ohne Animation. Nicht gespielt werden die Gruppen von Mehrspieler-Bildschirmen, des Schwierigkeitsmenüs (hier ein eigener Screen), des Spielmenüs
  im Spiel (`Quit*`, gehört zum HUD), `GenExpFade`, `ControlBarArrow` und der Bildschirmfade beim Spielstart (`FadeWholeScreen` nach dem Laden) – siehe §12 „Nicht umgesetzt“.
* **Phase 16 (Bildschirmstapel, §13):** das Hauptmenü bleibt unter ersetzenden Screens im Stapel (es spielt beim Zurückkehren seinen Einstieg erneut), wird aber nicht gezeichnet. Es gibt keine eigene
  Hintergrundebene: im Original liegt hinter allen Menüs die 3D-„Shell-Karte“ (zurückgestellt, §11), hier Schwarz; Rahmen und Lineal zeichnen die Layouts selbst (`MainMenuRuler` an der Wurzel von
  `SkirmishGameOptionsMenu.wnd`, `SaveLoad.wnd`, `ScoreScreen.wnd`). Escape im Hauptmenü und in der Schwierigkeitsfrage ist Zutat dieser Hülle (`MainMenuInput` des Originals kennt keine Escape-Taste).
* **Phase 12:** die Shell-Ausgaben (`StartMatchOutput`, `StartMissionOutput`, `ExitRequestedOutput`, Anzeige-/Einstellungsänderungen) und die Rückmeldungen
  (`MatchFinished`/`MissionFinished`) sind im Stride-Client angeschlossen (`ClientRuntime`, `docs/GAME-HUD.md` §15). `GameShell` liest beim Start `Data/INI/ChallengeMode.ini` und setzt über
  `CampaignCatalog.WithPersonas` die Persona-Fraktion jeder Challenge-Kampagne (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).

## 11. Befunde der ersten Sichtprüfung (Demo `shell`, 4. Oktober 2026)

Ein Mensch hat `scripts/demo-shell.ps1` gespielt und fünf Auffälligkeiten gemeldet. Ursachen, Behebung und Tests:

| Befund | Ursache | Behebung | Test | Was ein Mensch noch prüfen muss |
|---|---|---|---|---|
| Schaltflächen ohne sichtbaren Zustandswechsel | Die Auswahl der Zeichendaten war bereits richtig (`W3DGadgetPushButtonImageDrawThree/One`, `W3DGadgetPushButtonDraw`: Hilite 0/5/6 unter der Maus, Hilite 1/3/4 gedrückt, Disabled 0/5/6) und zeichnete kopflos einen Wechsel. Falsch war der Zustand selbst: (a) ein Klick gab der Schaltfläche den Tastaturfokus (im Original ruft `GadgetPushButtonInput` nie `winSetFocus`), und die Fokus-Hervorhebung überlebte das Verlassen – jede einmal geklickte Schaltfläche blieb hervorgehoben, Überfahren änderte an ihr nichts mehr; (b) eine Schaltfläche, die beim Gruppenwechsel versteckt wurde, behielt Hilite/Pressed und kam hervorgehoben zurück; (c) der von einem Dialog verdeckte Screen erfuhr nie, dass die Maus weg ist. | `UiWindowTree`: Fokus per Klick nur noch für Combo-Box, List-Box und Texteingabe; `GWM_MOUSE_LEAVING` löscht die Hervorhebung unbedingt; Verstecken löscht Hilite/Pressed; neues `PointerLeft()`, das `GameShell.Push` für den verdeckten Screen aufruft. | `WndTests.PushButtonStatesPickTheirDrawData`, `WndTests.HiliteFollowsThePointerOnly`; `ShellTests` „Buttons draw hilite and pushed images …“ | Ob Überfahren und Drücken im laufenden Client jetzt deutlich genug sind (die ausgelieferten Bilder `Buttons-HiLite-*` und `Buttons-Pushed-*` unterscheiden sich nur wenig). Ein vollständiges Ausbleiben jeder Hervorhebung ließ sich kopflos **nicht** nachstellen. |
| Regler im Optionsmenü unsichtbar | Der horizontale Regler mit `IMAGE`-Status wurde als dreiteiliger Balken aus Enabled 0/2/1 gezeichnet; diese Einträge sind `NoImage`. Das Original (`W3DGadgetHorizontalSliderImageDraw`) zeichnet keinen Balken, sondern eine zentrierte Reihe quadratischer Kästchen aus festen Plätzen: Disabled 0 = gefüllt, Disabled 1 = leer, Hilite 0 = Rahmen hinter der Reihe bei Hervorhebung. | `UiDrawBuilder.SliderBoxes`; Daumen wie der Kindknopf aus `gogoGadgetSlider` (13×16, 10 unter der Oberkante, Hilite-Bild nur bei Hervorhebung, gedrückt Eintrag 1). Ziehen funktionierte schon. | `WndTests.HorizontalSliderDrawsBoxesAndThumb`; `ShellTests` „Options: sliders draw their boxes and drag …“ | Farben und Abstand der Kästchen gegen das Original; der Rahmen (`linebox`) beim Überfahren. |
| Grüner Hintergrund hinter Überschriften beim Überfahren | Jedes Gadget mit `MOUSETRACK` wurde hervorgehoben. Statische Texte tragen in den Dateien `MOUSETRACK` und eine grelle `HILITEDRAWDATA`-Füllung, aber `GadgetStaticTextInput` behandelt `GWM_MOUSE_ENTERING` nicht – der Zustand entsteht im Original nie. | Hervorhebung und Enter/Leave-Nachrichten nur für Gadgets, deren Eingabefunktion sie behandelt (Knopf, Check-Box, Radio, Regler, List-Box, Combo-Box, Texteingabe). | `WndTests.StaticTextHasNoHiliteState`; `ShellTests` (sieben Beschriftungen des Optionsmenüs) | Nichts Offenes bekannt. |
| Graue Texte mit Schaltflächen links neben der Spalte | Die sechs Schaltflächen `Button{USA,GLA,China}{RecentSave,LoadGame}`. Sie sind in `MainMenu.wnd` nicht `HIDDEN`; `MainMenuInit` versteckt sie über `showSelectiveButtons(SHOW_NONE)`. Die Hülle hatte sie nur deaktiviert. | `MainMenuScreen` versteckt sie. | `ShellTests` „Main menu shows only what MainMenuInit leaves visible“ | Im Original erscheint ein Paar beim Überfahren von USA/GLA/China; das ist nicht umgesetzt (Speicherstände tragen keine Fraktion). |
| Text im Beenden-Dialog zu klein | `QuitMessageBox.wnd` ist bei 1920×1080 verfasst, die übrigen Shell-Screens bei 800×600. `UiScaler.ScaleFontSize` skalierte im Modus `Uniform`/`Anchor` auch Header-Template-Schriften mit der Erstellungsauflösung des Fensters; Template-Punktgrößen beziehen sich aber immer auf 800 Pixel Breite (`GlobalLanguage::adjustFontSize`). Folge bei 16:9: nur 600/1080 = 56 % der gemeinten Größe (LabelSmall 8 → 5 statt 9 bei 1280×720, 8 statt 14 bei 1920×1080). | Template-Schriften skalieren mit dem gleichförmigen Faktor gegen 800×600, `FONT` des Fensters weiter gegen die Erstellungsauflösung. | `WndTests.FontScalingFollowsModeAndAccessibilityFactor`; `ShellTests` „Quit dialog text takes the header template size …“ | Dieselbe Ursache traf **alle** bei 1920×1080 verfassten Layouts mit Template-Schrift: `ControlBar.wnd` (HUD), `QuitMenu.wnd`, `MessageBox.wnd`, `Diplomacy`, `GeneralsExpPoints`, die Kurzbefehlsleisten, `Victorious`/`Defeat`. Deren Texte sind jetzt bei 16:9 1,8-mal so groß wie vorher – im HUD nachsehen, ob etwas überläuft. |

Dazu behoben, nicht gemeldet: Die Hülle setzte ihre Texte mit `ShellTextMeasure` (synthetische Breiten), gezeichnet
wurde mit der echten Schrift; zentrierte Beschriftungen saßen deshalb rechts der Mitte (§10).

**Nicht gemacht:** die animierte Gefechtsszene hinter dem Hauptmenü (zurückgestellt). Die Übergangsanimationen des Originals
(`GameWindowTransitions`, `WindowTransitions.ini`, `TheTransitionHandler->setGroup(...)`: Einblenden der Schaltflächen, Aufklappen der Gruppen,
Aufblitzen des Logos) sind seit Phase 16 umgesetzt, siehe §12; ohne `animateTransitions` bleiben Gruppenwechsel harte Schnitte.

**Goldene Hashes:** 42 der 54 Werte in `baselines/screens.txt` haben sich geändert – alle Screens, die das Hauptmenü
enthalten oder über ihm liegen (versteckte Fraktions-Schaltflächen), dazu die Regler der Skirmish-Einrichtung und die
Schrift des Beenden-Dialogs; `MatchLoading` und `ScoreScreen` sind unverändert. `baselines/hud-drawlist-1280x720.sha256`
hat sich wegen der Template-Schriften von `ControlBar.wnd` geändert. Die Hashes belegen Reproduzierbarkeit, nicht
Richtigkeit.

**Sichtmittel:** `tools/CnCNew.UiRenderTool` zeichnet seitdem auch die Hülle selbst (`shell-*.png`, über
`GameShell.Create` und echte Eingabeereignisse): Hauptmenü, überfahrene und gedrückte Schaltfläche, Optionen mit
Reglern (überfahren, gezogen), Beenden-Dialog – siehe `docs/CLIENT-UI-RENDERING.md` §5.

## 12. Fensterübergänge (`WindowTransitions.ini`, Phase 16)

Das Original animiert die Hülle über `GameWindowTransitionsHandler` (`GameWindowTransitions.cpp/.h`, `GameWindowTransitionsStyles.cpp`): Schaltflächen blitzen nacheinander auf, Untermenüs klappen
auf und zu, das Logo blendet ein, Bildschirmwechsel gehen durch eine schwarze Blende, die Zahlen des Ergebnisbildschirms zählen hoch. Die Daten stehen in `Data/INI/WindowTransitions.ini`
(56 Gruppen, 381 Fenstereinträge); die Screens bestellen Gruppen mit `TheTransitionHandler->setGroup(name)`, `reverse(name)`, `remove(name)` und warten auf `isFinished()`.

### Datenweg

`WindowTransition` ist im Definitionsgraph weiter ein gezählter Kompatibilitätsblock (56, `baselines/compatibility.json` unverändert, `IniTool compat-check`: 751 Einträge, Definitionssnapshot unverändert).
Gelesen wird die Datei vom strengen Leser `WindowTransitionTable.Parse` (`src/CnCNew.Ui/Transitions/`), wie Credits und Kampagnen (§4/`ShellCredits`): es ist ein reines Anzeigeskript der Hülle, ohne
Verbraucher in Simulation oder Präsentationsgraph; eine Typisierung in `CnCNew.Data` hätte nur die Snapshot-Hashes verschoben. Der Leser kennt keinen Standardwert: unbekannter Schlüssel, unbekannter
Stil, fehlender Stil, fehlender `WinName` (außer bei den zwei ganzbildschirmigen Stilen), doppelte Gruppe, negative Verzögerung und nicht beendeter Block sind `WindowTransitionFormatException` mit Datei und
Zeile (das Original setzt hier still den ersten Stil bzw. den leeren Namen). Realdaten: 56 Gruppen, 381 Einträge, alle Stile bekannt, **alle Fensternamen lösen in den ausgelieferten Layouts auf** (0 offen).

### Aufbau (Stride-frei, `CnCNew.Ui`)

| Typ | Aufgabe | Original |
|---|---|---|
| `WindowTransitionTable` / `TransitionGroupSpec` | Gruppen, Fenstereinträge (`WinName`, `Style`, `FrameDelay`), `FireOnce` | `parseWindowTransitions`, `parseWindow` |
| `TransitionEffect` + 15 Klassen | ein Effekt eines Fensters: `Init`, `Update(frame)`, `Reverse`, `Skip`, `Draw`, `IsFinished`, `FrameLength` | `Transition` und Stile |
| `TransitionWindow`, `TransitionGroup` | Verzögerung je Fenster; Gruppenzähler, der vorwärts oder (nach `Reverse`) rückwärts läuft; `Reverse` setzt ihn auf die längste `Verzögerung + Länge` | `TransitionWindow`, `TransitionGroup` |
| `WindowTransitionHandler` | `SetGroup`, `Reverse`, `Remove`, `IsFinished`, `Step`, `Advance(Sekunden)`, `AppendDraw`; aktuelle, wartende, gezeichnete und zuletzt gezeichnete Gruppe | `GameWindowTransitionsHandler` |
| `UiTransitionHost`, `ITransitionWindow` | bindet die Effekte an einen `UiWindowTree`: Fenster per dekoriertem Namen, Verstecken, Bild/Text lesen und setzen, Töne | `TheWindowManager->winGetWindowFromId` und `GameWindow`-Aufrufe |

* **Zeit:** ein Übergangs-Frame je Clientframe des Originals; die Hülle läuft dort mit 30 FPS (`GameData.ini`: `UseFPSLimit = Yes`, `FramesPerSecondLimit = 30`). `Advance(Sekunden)` macht daraus ganze Frames zu
  1/30 s (Rest wird übertragen), nichts liest Wandzeit – eine Folge ist eine reine Funktion der gesteppten Frames. `ShellScreen.Update` deckelt einen Aufruf auf 0,1 s (3 Frames), damit ein Ladehänger die
  Folge nicht in einem Schritt abspielt.
* **Zeichnen:** ein Effekt versteckt sein Fenster und zeichnet an dessen Stelle einen Ersatz (`UiBorderRect`/`UiFillRect`/`UiImageDraw`/`UiTextDraw` mit Alpha), den `GameShell.BuildDrawList` hinter die Zeichenliste des
  jeweiligen Screens hängt (im Original zeichnet `winRepaint` die Übergänge zuletzt). Der Ersatz des Schaltflächen-Aufblitzens ist `PushButtonImageDrawThree` (`UiWindowTree.BuildPushButtonImages`: linkes, mittleres
  und rechtes Bild mit Alpha, ohne Text). Jeder Screen hat seinen eigenen Handler (jeder Screen hat seinen eigenen Fensterbaum); der Handler des Originals ist global.
* **Eingabe:** wie im Original gibt es keine Sperre im Handler. Ein Fenster, das der Effekt versteckt hält (Schaltflächen bis Effektframe 11, Gruppen bis zum Ende ihres Aufblitzens), nimmt keine Eingabe
  (versteckte Fenster werden nicht getroffen); darüber hinaus verweigern die Screens Navigation, solange eine Gruppe läuft (`dontAllowTransitions` im Hauptmenü: Untermenüwechsel, Kampagnen-/Skirmish-/Lade-/Credits-/
  Optionsknöpfe und Escape; „Beenden“ ist ausgenommen); ein Screen, der gerade ausblendet (`IsClosing`), bekommt keine Eingabe mehr. `GameShell.IsAnimating` sagt dem Host (und den Tests), wann alles still steht.

### Die 15 Stile (Häufigkeit in der Datei; Frames = Länge des Effekts)

| Stil | n | Frames | Verhalten (Quelle: `GameWindowTransitionsStyles.cpp`) |
|---|---|---|---|
| `FLASH` | 117 | 8 | Fenster Frames 1–3 versteckt, weißer Rahmen und Füllung mit Alpha 100/33, 150/66, 200/99, danach ab Frame 4 sichtbar mit abklingendem Weiß (75/50/25/10); Ton `GUIBoarderFadeIn` |
| `BUTTONFLASH` | 77 | 17 | 1–3 weißes Aufblitzen wachsend (Ton `GUIButtonsFadeIn`), 4–7 Schaltflächenbild unter abklingendem Weiß, 8–10 nur das Bild, 11–16 Fenster sichtbar unter abklingendem Verlauf `Gradient`; rückwärts dieselben Tabellen gespiegelt |
| `WINFADE` | 7 | 10 | das erste Bild des Fensters mit Alpha 25 · Frame (1–9) |
| `WINSCALEUP` | 47 | 6 | das Bild wächst aus der Mitte in sechs Schritten (Frames 1–5 gezeichnet); Ton `GUILogoMouseOver` |
| `SCORESCALEUP` | 7 | 6 | wie `WINSCALEUP`; Ton `GUIScoreScreenPictures` |
| `MAINMENUSCALEUP` | 8 | 5 | das Disabled-Bild fliegt und wächst in fünf Schritten in `WinGrowMarker`; Ton `GUILogoSelect` |
| `MAINMENUMEDIUMSCALEUP` | 10 | 3 | das Logo `WinFactionX` wächst um seine Mitte auf die Größe von `WinFactionXMedium`; Ton `GUILogoMouseOver` |
| `MAINMENUSMALLSCALEDOWN` | 0 | 6 | wie oben auf `…Small`; von der ausgelieferten Datei **nicht benutzt** (umgesetzt und getestet) |
| `TYPETEXT` | 4 | min(Textlänge, 30) | ein Zeichen je Frame, Ton `GUITypeText`; rückwärts löscht es |
| `SCREENFADE` | 1 | 30 | schwarze Fläche über den ganzen Bildschirm, Alpha `1/29 · 255 · Frame`; rückwärts blendet es auf |
| `COUNTUP` | 56 | min(Wert/Schritt, 30) | Zahl zählt in 1er, 100er oder 1000er Schritten von 0 hoch; Ton `GUIScoreScreenTick` |
| `FULLFADE` | 12 | 10 | Schwarz mit blauem Rahmen über der Fensterfläche, Alpha 51·Frame bis Frame 5 (volles Schwarz, das Fenster erscheint), danach wieder herunter |
| `TEXTONFRAME` | 16 | 1 | das Fenster erscheint nach seiner Verzögerung (+1); ein bereits verstecktes Fenster bleibt versteckt |
| `CONTROLBARARROW` | 1 | 22 | Pfeilbild gleitet in 16 Frames auf `ControlBar.wnd:ButtonGeneral`, blendet in 6 aus |
| `REVERSESOUND` | 18 | 2 | kein Bild, spielt `GUITransitionFade` auf Frame 1 |

Die Frame-, Alpha- und Schrittwerte stammen aus dem Quelltext (neu geschrieben, kein Code kopiert); jeder Effekt ist mit Abtastwerten getestet (`tests/CnCNew.Ui.Tests/TransitionTests.cs`).

### Welche Screens welche Gruppen spielen (23 von 56, im Test `GroupsAreKnown` gezählt)

| Screen | Gruppen | Auslöser (nach `MainMenu.cpp`, `SkirmishGameOptionsMenu.cpp`, `ChallengeMenu.cpp`, `PopupSaveLoad.cpp`, `ScoreScreen.cpp`) |
|---|---|---|
| Hauptmenü, erster Start | `FadeWholeScreen` (rückwärts: aus Schwarz einblenden), danach `MainMenuDefaultMenuLogoFade` | `MainMenuInit` mit `FirstTimeRunningTheGame` |
| Hauptmenü, jede Rückkehr | `MainMenuDefaultMenuLogoFade` | `MainMenuInit` nach dem Schließen eines Screens, der das Menü ausgeblendet hat (`OnRevealed`) |
| Untermenü Einzelspieler | `MainMenuDefaultMenu` entfernen, `MainMenuDefaultMenuBack` rückwärts, `MainMenuSinglePlayerMenu`; zurück: `…SinglePlayerMenu` entfernen, `MainMenuSinglePlayerMenuBack` rückwärts, `MainMenuDefaultMenu` | Knöpfe Einzelspieler / Zurück, Escape |
| Untermenü Laden/Wiedergabe | `MainMenuLoadReplayMenu` / `MainMenuLoadReplayMenuBack` | Knöpfe Laden/Wiedergabe / Zurück |
| Fraktionslogos | `MainMenuFactionUS`, `…GLA`, `…China`, `…Training` (für „Challenge“), `…Skirmish` | Überfahren des Knopfes (setzen), Verlassen (rückwärts); während einer Übergangs-Gruppe wird die Anfrage gemerkt (`showLogo`/`showSide`) |
| Hauptmenü → Skirmish | `MainMenuSinglePlayerMenuBackSkirmish` rückwärts (Logo entfernt), der Screen öffnet, wenn alles fertig ist (`Shell::push` wartet auf das Ende der Abschaltanimation) | Knopf der Einzelspieler-Spalte |
| Hauptmenü → USA / GLA / China / Challenge | `MainMenuFaction<X>` (gesetzt und entfernt, bei Challenge nur gesetzt), `MainMenuSinglePlayerMenuBack<X>` rückwärts, `MainMenuDifficultyMenu<X>`; zurück: `…Menu<X>Back` rückwärts, `MainMenuSinglePlayer<X>MenuFromDiff`; Wahl: `MainMenuDifficultyMenuBack` rückwärts, Kampagne `FadeWholeScreen`, Challenge `MainMenuDifficultyMenuTraining` rückwärts und dann das Challenge-Menü (§13) | Knöpfe der Einzelspieler-Spalte, `ButtonEasy/Medium/Hard/DiffBack` |
| Hauptmenü → Credits | `MainMenuDefaultMenu` rückwärts | Knopf Credits |
| Hauptmenü → Spiel laden | `MainMenuLoadReplayMenuBackTransition` rückwärts | Knopf Laden |
| Skirmish-Einrichtung | `SkirmishGameOptionsMenuFade` (Einstieg und rückwärts beim Schließen) | `SkirmishGameOptionsMenuUpdate` / `…Shutdown` |
| Challenge-Auswahl | `ChallengeMenuFade` (ebenso) | `ChallengeMenu.cpp` |
| Spiel laden | `SaveLoadMenuFade` (ebenso) | `PopupSaveLoad.cpp` |
| Ergebnisbildschirm (Skirmish) | `ScoreScreenShow` (Zeilen erscheinen nacheinander, Zahlen zählen hoch, Bilder wachsen) | `ScoreScreenInit` |

Optionen, Credits, Beenden-Dialog, Kartenwahl, Briefing und Missionsergebnis haben im Original keine Gruppe (die Optionen nutzten nur den älteren `AnimateWindowManager`, den die ausgelieferten Menüs
praktisch nicht mehr aufrufen) und bleiben harte Schnitte.

### Abweichungen vom Original (bewusst, alle benannt)

1. **Erster Start:** das Original wartet mit dem Hauptmenü, bis die Maus um mehr als 20 Pixel bewegt oder eine Taste gedrückt wird (`notShown`), und spielt dann `MainMenuFade` (Lineal) und das Menü. Hier beginnt es
   nach der Blende von selbst; das Lineal (`MainMenuRuler`) ist sofort da.
2. **Frame 0:** die Übergangsgruppe, die gerade begonnen wurde, wird schon im ersten Frame gezeichnet (`PrimeDraw`); das Original zeichnet erst nach dem ersten `update` (ein Frame nacktes Bild, z. B. vor der Blende aus Schwarz).
3. **Effekte ohne Fenster** (Name nicht im Screen) enden sofort und der Name wird gezählt (`MissingWindows`); das Original dereferenziert den Nullzeiger bzw. lässt den Zustand offen. `MAINMENUSCALEUP` ohne `WinGrowMarker` ebenso.
4. **`TYPETEXT`** beendet sich bei `Frame == Länge`; das Original nur bei Frame 30, so dass ein Text unter 30 Zeichen die Gruppe nie beendet. Bei 30 und mehr Zeichen identisch.
5. **`COUNTUP`/`TEXTONFRAME` auf verstecktem Fenster** (Zeile eines nicht belegten Spielerplatzes): das Fenster bleibt versteckt. Das Original macht es auf seinem Startframe sichtbar (`COUNTUP`), was nur gut aussieht, solange der
   Text leer ist; hier steht der Vorlagentext („Units Built“) in den leeren Zeilen, deshalb die Abweichung.
6. **Nicht endende rückwärts laufende Gruppe:** läuft der Zähler unter 0, ohne dass alle Fenster fertig melden, wird die Gruppe beendet (`Overrun`, Test: tritt bei den ausgelieferten Daten nie auf).
7. **Bild `Gradient`:** das Verlaufsbild von `BUTTONFLASH` (Frames 11–16) gibt es im Retail-Bestand nicht (`findImageByName` liefert im Original NULL, `drawImage(NULL)` zeichnet nichts). Hier ebenso nichts, der Name steht in
   `MissingImages`. Die Knöpfe blitzen also bis Frame 10 und erscheinen dann ohne Verlauf.
8. **Rückkehr:** `OnRevealed` spielt den Einstieg erneut, wenn das Menü für einen anderen Screen ausgeblendet hat; wird ein Dialog (Beenden) geschlossen, passiert das nicht.
9. **Zustand der Ruhe:** das Einzelspieler-Untermenü zeigt in der Ruhe die fünf kleinen Fraktionslogos am unteren Rand (`WINSCALEUP` lässt sie sichtbar). Die harte Variante zeigte sie bisher nicht; sie tut es jetzt, damit
   Ruhezustand mit und ohne Animation gleich sind (siehe Hashes).
10. **Hauptmenü-Init:** `initialHide()` versteckt alle fünfzehn Logofenster (fünf Fraktionen × drei Größen); ohne das blieb nach einer Kampagnenwahl das mittlere Logo stehen (im Test aufgefallen).

### Goldene Hashes

`baselines/screens.txt`: **24 von 54** Werten geändert (4 Screens × 6 Auflösungen): `MainMenu-SinglePlayer`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`; alle vier enthalten das offene
Einzelspieler-Untermenü (sichtbar oder darunter), Ursache ausschließlich die fünf Logos (Punkt 9). Unverändert: `MainMenu`, `MatchLoading`, `ScoreScreen`, `Credits`, `Options`, `QuitConfirm` (30 Werte) und der
HUD-Hash. Die Übergänge selbst ändern keinen Hash, weil die Hülle ohne `animateTransitions` keine Gruppe anlegt (`NoTransitionsNoAnimation`); die Ruhezustände der Animation sind in den Tests bytegleich mit denen ohne.

### Messwerte (30 Frames/s, `.local/phase2-import-a`, neu messen)

* Hauptmenü: erster Start 57 Frames (1,90 s): Blende 29 Frames, Rand ab Frame 34, Logo 40, erster Knopf 46, letzter Knopf 51. Längster Vorwärtslauf im Realdatentest (alle Zahlen als `1234` angenommen): 60 Frames (2,00 s, `ScoreScreenShow`).
* Alle 56 Gruppen laufen vorwärts und rückwärts ohne `Overrun` bis zum Ende (Realdatentest).
* 18 synthetische Tests plus Realdatentest in `TransitionTests` (Ui: Leser, Effekte, Handler, Uhr, echter Fensterbaum), 10 Tests in `ShellTransitionTests` (Game). Zwei Läufe derselben Folge: gleicher Hash (`1730265A0B3D8B31` für
  20 Frames Einstieg + Einzelspieler-Wechsel + Hover).
* `tools/CnCNew.UiRenderTool` schreibt die Folgen als PNG (`docs/CLIENT-UI-RENDERING.md` §7): 49 neue Bilder, alle 113 PNGs in zwei Läufen byteidentisch; angesehen: Einstieg (Schwarz, Lineal, Logo und Knöpfe im
  Aufblitzen), Einzelspieler-Wechsel mit wachsenden Logos, Hover-Logo, Skirmish-Ausblenden/Einblenden, Ergebnisbildschirm.

### Nicht umgesetzt (benannt)

* Die Gruppen von Mehrspieler- und GameSpy-Bildschirmen (elf `FULLFADE`-Gruppen, `Multi`-Untermenü), des Schwierigkeitsmenüs (`MainMenuDifficulty*`, `…FromDiff`: hier ist die Auswahl ein eigener Screen), die Spielmenü-Gruppen im
  Spiel (`QuitNoSave`, `QuitFull` und ihre `Back`-Gruppen; das Spielmenü gehört dem HUD), `GenExpFade` und `ControlBarArrow` (die Effekte sind umgesetzt und getestet, aber das HUD ruft sie nicht auf).
* Der Bildschirmfade beim Spielstart (`FadeWholeScreen` nach dem Laden zu Schwarz, beim Spielbeginn zurück): der Stride-Client hat keine Ebene dafür über Welt und HUD; der Ladebildschirm schaltet hart.
* Die Maus-Warteschleife des ersten Starts (Abweichung 1), die Bibs/Hinweise anderer Screens und die Gefechtsszene hinter dem Menü.

### Nicht belegt (ein Mensch muss hinsehen)

* **Dass die Folgen im Fenster so aussehen und sich so anfühlen wie im Original.** Geprüft sind Abtastwerte, Reihenfolgen, Zustände und einzelne PNGs eines Offscreen-Renderers; Tempo (30 Frames/s gegen die
  tatsächliche Bildrate des Fensters), Tonzeitpunkte (`GUIButtonsFadeIn` je Knopf) und Gesamteindruck sind **nicht** gesehen oder gehört.
* Ob der erste Start ohne Maus-Warten stört, ob die fehlende Verlaufsstufe (Punkt 7) auffällt, ob die 0,1-s-Deckelung bei langsamen Frames spürbar ist.
* Die Wirkung bei anderen Auflösungen als 1280×720 (die Ersatzflächen nutzen die skalierten Fensterrechtecke; die PNG-Folgen gibt es nur dort).
* (Erledigt in §13: `LoadGameScreen` und Escape ohne Tastaturfokus.)
### Phase 16: Textvergleich mit dem Original (Zusammenfassung, Tabelle in `docs/TEXT-AUDIT.md`)

Der Mensch fand die Texte „komisch“ gegenüber dem Original. Systematisch untersucht (Daten + Referenzcode, kein Originalbild vorhanden); behoben und im Schalenbereich wirksam:

| Befund | Ursache | Behebung | Test |
|---|---|---|---|
| „Generals“-Schrift (Titel, Hauptmenüknöpfe, 294 Fenstertexte) bis 85 px zu breit, Zeilenhöhen 1 px daneben | FreeType-Näherung (91-%-Stauchung, ungehintete Vorschübe) statt der GDI-Schrift des Originals (`lfWidth = 0,40·Höhe`, GDI wählt die Breite: ca. 87 %) | `GdiFontProvider` (`docs/CLIENT-UI-RENDERING.md` §2) | `UiRenderTool` Selbstprüfung + `--font-audit` (Sichtmittel) |
| „Alternate Mouse Setu“ abgeschnitten | Texte von Knöpfen, Kontrollkästchen und Radioknöpfen waren auf das Fenster geschnitten; im Original setzt nur statischer Text einen Zuschnitt | `UiDrawBuilder.Text` mit Zuschnitt je Gadgettyp | `WndTests` (Golden), `SentenceLayoutTests` |
| Zeilenumbruch und Zentrierung anders | greedy `<=`-Umbruch ohne Einzug und Überlappung statt `Build_Sentence_*` | `UiSentenceLayout` | `SentenceLayoutTests` (9 Fälle, von Hand nachgerechnet) |
| Skirmish-Einrichtung: `FactionAmerica`, `Color 1`, `None` | feste Namen statt `PlayerTemplate.DisplayName` (`INI:FactionAmerica` = „USA“), `MultiplayerColor.TooltipName` (`Color:Gold`), `Team:n` | `ShellSkirmishText` (Fallbacks gezählt in `ShellTextSource.RawNameHits`) | `ShellTests` „Skirmish setup and options show localized words …“ |
| Optionen: Eingabefelder zeigen das Wort „Entry“ | TEXT der Layoutdatei, `OptionsMenuInit` überschreibt es | beide Netzwerkfelder starten leer | dasselbe |
| Credits in Arial 24 fett / 16 fett / 12 | feste Werte statt `CreditsTitleFont`/`CreditsMinorTitleFont`/`CreditsNormalFont` aus `Language.ini` mit `adjustFontSize` | `LanguageFonts`, `CreditsFonts` | `ShellTests` „Credits lines use the Language.ini credits fonts …“ |
| Tooltips fehlten ganz | nie umgesetzt | `TooltipPainter` im Shell- und HUD-Zeichenpfad | `ShellTests` „A window with TOOLTIPTEXT shows the pointer tooltip …“ |

**Seither behoben (§13): (a) Bildschirmstapel, (b) `SinglePlayerMenu.wnd`.** Wortlaut des Befunds vor der Behebung: (a) *Bildschirmstapel*: `GameShell.BuildDrawList` zeichnete **jeden** Bildschirm des Stapels. `Shell::push` fährt im Original
zuerst den oberen Bildschirm herunter (`runShutdown`, danach versteckt), sodass Optionen, Skirmish-Einrichtung und Kampagnenbildschirme das Hauptmenü ersetzen; hier scheinen dessen Knopftexte
(„CHALLENGE“, „BACK“ …) durch die neuen Fenster, am deutlichsten in der Skirmish-Einrichtung. Dazu bräuchte der Hintergrund (Rahmen, Logo, im Original die 3D-Schattenkarte) eine eigene Ebene.
(b) *`SinglePlayerMenu.wnd`* (Zwischenseite Neu/Laden/Zurück nach „USA“) gibt es im Original nicht (USA führt direkt in den Schwierigkeitsdialog); ihre Texte standen in Times New Roman, wie
die Layoutdatei es vorgibt. Noch offen: (c) *Skirmish-Einrichtung*: „Computer“ und „(no map selected)“/„No map selected“ sind feste englische Wörter (kein Label; das Original kennt Easy/Medium/Hard
Army und eine Kartenleiste), die Farb-Einträge tragen keine Farbfläche. (d) Briefing und Ladeschirm haben im Original Filmhintergründe und Fortschrittstexte, hier ein Standbild.

## 13. Bildschirmstapel, Schwierigkeitsfrage, Ladeliste (Phase 16, Menüs)

Drei Befunde der Textprüfung (`docs/TEXT-AUDIT.md`, §11 oben) mit Quellen im Original (`Shell.cpp`: `push`, `pop`, `shutdownComplete`, `showShellMap`; `MainMenu.cpp`: `MainMenuShutdown`, `shutdownComplete`,
`GBM_SELECTED`, `setupGameStart`, `prepareCampaignGame`, `diffReverseSide`; `SkirmishMapSelectMenu.cpp`: `showSkirmishGameOptionsUnderlyingGUIElements`; `PopupSaveLoad.cpp`: `SaveLoadMenuInput`,
`SaveLoadMenuFullScreenInit`; `ChallengeMenu.cpp`: `ChallengeMenuInit`, `setGeneralBio`, `setEnabledButtons`; Code neu geschrieben, nichts kopiert).

### 13.1 Welcher Screen was verdeckt

`Shell::push` fährt den oberen Screen herunter, dessen Shutdown mit `layout->hide(TRUE)` endet (`MainMenuShutdown` → `shutdownComplete`), und legt dann erst den neuen an: das Hauptmenü ist **weg**, nicht darunter. Nicht
alles ist ein `push`:

| Öffnet | Weg im Original | Verdeckt den Screen darunter |
|---|---|---|
| Skirmish-Einrichtung, Credits, Challenge-Menü, Ladeliste (`SaveLoad.wnd`) | `TheShell->push(...)` | ja (`HidesScreensBelow = true`, der Standard) |
| Optionen | `getOptionsLayout` + `runInit` + `hide(FALSE)`, `OptionsMenuParent` ist `SEE_THRU` | nein: das Hauptmenü bleibt dahinter sichtbar (`OptionsScreen`) |
| Kartenwahl | `winCreateLayout("SkirmishMapSelectMenu.wnd")` + `hide(FALSE)` | nein, aber `showSkirmishGameOptionsUnderlyingGUIElements(FALSE)` versteckt in der Einrichtung `MapWindow`, `StaticTextTeam/Faction/Color`, die Felder `ComboBoxTeam/Color/PlayerTemplate0..7`, `TextEntryMapDisplay`, `ButtonSelectMap`, `ButtonStart`, `StaticTextMapPreview` und sperrt Reset/Back; OK und Back stellen sie wieder her (`SkirmishSetupScreen.SetMapPickerOpen`) |
| Beenden-Dialog, Fehlerdialog | `QuitMessageBoxYesNo`/`MessageBoxOk` | nein (`MessageBoxScreen`) |
| Missionsergebnis über dem Banner | – | nein (Zutat dieser Hülle: das Banner ist der Hintergrund, `MissionResultScreen`) |

`GameShell.BuildDrawList` zeichnet ab dem untersten Screen, der von oben aus gezählt der erste mit `HidesScreensBelow` ist (`FirstDrawnScreen`), bis zur Spitze; Übergangs-Ersatzflächen und Statistik zählen nur diese.
Eingabe erhielt ohnehin nur der oberste Screen. **Hintergrundebene:** nicht nötig. Rahmen, Lineal und Logo liegen nicht in einer Ebene, die stehen bleibt – das Original versteckt das ganze Hauptmenülayout
(`MainMenuParent`, `MainMenuRuler`, `Logo`, `MapBorder*`), und die ersetzenden Layouts bringen ihr Lineal selbst mit (`IMAGE: MainMenuRuler` an der Wurzel von `SkirmishGameOptionsMenu.wnd`, `SaveLoad.wnd`,
`ScoreScreen.wnd`); was dahinter liegt, ist im Original die 3D-Shell-Karte (§11, zurückgestellt: hier Schwarz). `ChallengeMenu.wnd` bringt einen eigenen Hintergrund (Weltkarte).
Wird der verdeckende Screen geschlossen, läuft das Hauptmenü seine Init erneut (`doPop` → `runInit`): `OnRevealed` setzt es auf die Wurzelgruppe zurück, jetzt auch ohne Animation (`LeaveFor` merkt sich den Weggang).

### 13.2 Warum es `SinglePlayerMenu.wnd` nicht mehr gibt

`SinglePlayerMenu.wnd`/`SinglePlayerMenu.cpp` sind Erbe des ersten Generals: die Datei wird ausgeliefert, `SinglePlayerMenuInit` & Co. sind in `FunctionLexicon` eingetragen, aber **nichts in Zero Hour öffnet sie**
(`Shell::push` nennt sie nirgends; die Treffer in `MainMenu.cpp` sind auskommentiert). Der Weg nach „USA“ im Original: `GBM_SELECTED` von `ButtonUSA/GLA/China` setzt die Kampagne, spielt `MainMenuFactionUS` (gesetzt und mit
`remove(…, TRUE)` auf den Endzustand gebracht), versteckt `WinFactionUS`, spielt `MainMenuSinglePlayerMenuBackUS` rückwärts und `MainMenuDifficultyMenuUS` – die **Schwierigkeitsfrage ist Teil von `MainMenu.wnd`**
(`MapBorder4` mit `StaticTextSelectDifficulty`, `ButtonEasy/Medium/Hard/DiffBack`, das große Fraktionslogo fliegt per `MAINMENUSCALEUP` in `WinGrowMarker`). Easy/Medium/Hard rufen `prepareCampaignGame` →
`MainMenuDifficultyMenuBack` rückwärts, `FadeWholeScreen`, dann beginnt die Kampagne; `ButtonDiffBack` (`diffReverseSide`) spielt `…MenuUSBack` rückwärts und `MainMenuSinglePlayerUSAMenuFromDiff`. Challenge fragt dasselbe
(`…Training`) und schiebt dann `ChallengeMenu.wnd` (`setupGameStart` mit `launchChallengeMenu`, die Schwierigkeit geht an `TheChallengeGenerals`). `DifficultySelect.wnd` (Radioknöpfe) ist ebenso ungenutzt. „Laden“ ist der
Knopf **Load** des Hauptmenüs (`MapBorder3`: `ButtonLoadGame` → `SaveLoad.wnd`); die Schnellspeicher-Paare der Fraktionen (`showSelectiveButtons`) zeigt Zero Hour nie. `MainMenuScreen` bildet das nach
(`OpenDifficulty`, `ChooseDifficulty`, `CloseDifficulty`; Gruppennamen als Tabelle `Factions`); `CampaignFactionScreen`/`DifficultySelectScreen` und die zwei Layoutnamen sind entfernt. Der Ruhezustand ohne Animation
entspricht dem der Gruppen (Test: `Dump` beider bytegleich, `ShellTransitionTests.CampaignButtons`). Eine Eigenheit der Daten bleibt sichtbar: `MainMenuSinglePlayerMenuBackTraining` blendet das Titellogo aus (`WINFADE`
rückwärts), und `…TrainingMenuFromDiff` bringt es nicht zurück – bei Challenge fehlt das Logo auf der Schwierigkeitsfrage und beim Zurück bis zur Rückkehr ins Hauptmenü.

### 13.3 Ladeliste und Escape

`SaveLoadMenuInput` hängt am Kind `SaveLoadMenu`, nicht an der Wurzel; die Layout-Init fokussiert es (`winSetFocus`). `UiWindowTree` fiel ohne Fokus auf die oberste Wurzel zurück und fand dort keinen Callback, Escape ging nur
nach einem Klick auf ein Gadget. Neu: `UiWindowTree.KeyboardHome` – das Fenster, das Tasten ohne Gadgetfokus bekommt, solange es sichtbar ist (anders als ein Gadgetfokus überlebt es das Verstecken durch eine Übergangsgruppe,
`FULLFADE` von `SaveLoadMenu`); `LoadGameScreen` setzt es. Alle anderen Shell-Screens haben den Callback an der Wurzel (geprüft gegen die Dateien) und brauchen es nicht. Im Original behandeln alle diese Eingabefunktionen
nur `KEY_ESC`.

### 13.4 Weitere Textbefunde auf dem Weg (nicht Gegenstand der drei Aufträge)

* **Kartenliste**: `ListboxMap` hat zwei Spalten (5 % Erfolgsbild, 95 % Name); die Namen standen in der ersten und waren nach zwei Buchstaben abgeschnitten.
* **Startplatzmarken** der Kartenvorschau (`ButtonMapStartPosition0..7`): im Original zu Beginn versteckt und von `positionStartSpots` platziert; hier standen sie als Diagonale über dem Vorschautext. Der Katalog kennt die Zahl der
  Startplätze, nicht ihre Lage: die Marken bleiben versteckt (offen).
* **Challenge-Menü**: Biografie (`GeneralsBioParent`) und Play sind bis zur Wahl versteckt (`ChallengeMenuInit`), die vier Zeilen (`BioNameEntry`, `BioDOBEntry`, `BioBirthplaceEntry`, `BioStrategyEntry` – im Original umgewidmet für
  Name, Rang, Waffengattung, Taktik) zeigen die Labels der Persona aus `ChallengeMode.ini` (`BioNameString`, `BioRankString`, `BioBranchString`, `BioStrategyString`; `CampaignCatalog.Personas`) sofort statt Buchstabe für Buchstabe
  (`updateBio`), nicht startbare Positionen sind versteckt (`setEnabledButtons`), und die Knöpfe tragen die Medaillen der Spielervorlage (`MedallionRegular/Hilite/Select`) in deren Bildgröße (`winSetSize`) – vorher war das Menü
  unsichtbar (die Knöpfe haben im Layout kein Bild) und zeigte die Platzhalter `GUI:BioNameEntry` …. Dafür neu in `CnCNew.Ui`: `UiWindow.SetDrawImage`/`DrawImage` (Bild je Zeichenzustand ersetzen), `SetDesignSize`.
  Nicht umgesetzt: Porträt (`BioPortrait`), Taunt-/Vorschautöne, die Einführungssequenz der Knöpfe.

### 13.5 Goldene Werte und Werkzeug

`baselines/screens.txt`: **24 von 54** Werten neu aufgenommen (4 Bildschirme × 6 Auflösungen): `Credits`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`. Ursachen: das Hauptmenü (Logo, Spaltenknöpfe, Fraktionslogos) wird
unter diesen Bildschirmen nicht mehr gezeichnet; dazu in Einrichtung und Kartenwahl die versteckten Startplatzmarken, unter der Kartenwahl die versteckten Felder der Einrichtung und die Namensspalte der Liste. Unverändert: `MainMenu`,
`MainMenu-SinglePlayer`, `MatchLoading`, `ScoreScreen`, `Options`, `QuitConfirm` (Optionen und Beenden-Dialog liegen im Original über dem Hauptmenü) und der HUD-Hash. Die Hashes belegen Reproduzierbarkeit, nicht Richtigkeit.
`tools/CnCNew.UiRenderTool` zeichnet dazu `flow-skirmish-after-map-back`, `flow-main-after-skirmish`, `flow-load-replay-menu`, `flow-load-game`, `flow-challenge-difficulty`, `flow-challenge-menu(-picked)` und
`flow-campaign-difficulty` (1280×720 und 1920×1080).

### 13.6 Nicht belegt (ein Mensch muss hinsehen)

* Dass die Seiten im Fenster wie im Original aussehen: geprüft sind Zeichenlisten, Zustände und einzelne PNGs eines Offscreen-Renderers, kein Originalbild und kein Fenster.
* Die **Schwierigkeitsfrage in Bewegung** (Logoflug, Tippen von „SELECT DIFFICULTY“, Rückweg mit `…FromDiff`) und das Fehlen des Titellogos bei Challenge (Dateneigenheit, nicht gegen ein Original gesehen).
* Dass die **Optionen** im Original wirklich über den **sichtbaren** Hauptmenüknöpfen liegen: das folgt aus dem Code (kein `push`, `SEE_THRU`), nicht aus einem Bild. Zeigt das Original sie nicht, müsste `OptionsScreen.HidesScreensBelow` wahr werden.
* Die **Kartenwahl**: welche Felder der Einrichtung sichtbar bleiben (nur die Namensspalte und die unteren Felder) ist aus `showSkirmishGameOptionsUnderlyingGUIElements` gelesen, nicht gesehen.
* Die **Challenge-Medaillen** (Größe, Hervorhebung, gewählte Medaille) und die Positionen auf der Weltkarte.
* Wie auffällig das schwarze Dahinter ohne die 3D-Shell-Karte des Originals wirkt.
