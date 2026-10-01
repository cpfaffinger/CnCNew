# In-Match-HUD und Steueroberfläche des menschlichen Spielers (Paket G2)

Dieses Dokument beschreibt `src/CnCNew.Game/Hud/**`: die Stride-freie, headless testbare Schicht, die für den menschlichen Spieler
das gesamte Steuer- und Anzeigeflächenmodell eines laufenden `LiveMatch` bereitstellt. `MatchController` besitzt den
`GameInputController` (Paket C3, `CnCNew.Presentation.Input`) und die aus den Original-Layouts gebundene Control-Bar-HUD, wandelt
`InputEvent`s ausschließlich über `LiveMatch.Commands` in Simulationsbefehle um (damit `SkirmishMatchSession.ToReplay()` alles
reproduziert, was der Spieler getan hat) und liefert pro Frame eine `UiDrawList` (Paket B, `CnCNew.Ui.Runtime`) sowie
bildschirmraumbezogene Overlay-Daten. Kein Stride-Typ kommt vor; die gesamte Klasse läuft headless in Tests.

## 1. Aufrufreihenfolge

Pro gerendertem Frame:

1. `controller.Process(inputEvent)` für jedes anstehende Geräteereignis (Maus, Tastatur, Fokusverlust).
2. `liveMatch.Advance(deltaSeconds)` (Paket-C-Vertrag, unverändert außer den unten beschriebenen additiven Erweiterungen).
3. `controller.Update(deltaSeconds)` – wendet die Kameraabsicht an, baut `CommandBar`/`Hud` neu, aktualisiert den Ticker und das
   `Overlay`.
4. `controller.BuildDrawList(width, height)` – aktualisiert die Sichtbarkeit der dynamischen Fenster und liefert die fertige
   `UiDrawList` inklusive der dynamischen Cameo-/Kostüm-/Abklingzeit-Overlays.

`MatchController.WindowRect(decoratedName)` gibt den aktuellen Bildschirmbereich eines benannten Fensters zurück (z. B.
`"ControlBar.wnd:ButtonCommand01"`); ein Host kann damit ohne eigene Skalierungsrechnung Hit-Tests durchführen, und die Tests
(`HudTests.cs`) klicken echte Schaltflächen darüber an.

## 2. Additive Erweiterungen an `LiveMatch.cs`

`src/CnCNew.Game/Live/LiveMatch.cs` gehört Paket C4; G2 hat nur vier zusätzliche, rückwärtskompatible Mitglieder ergänzt:

* `ICommandSink Commands` – lazygebauter `SkirmishSessionCommandSink(Session, Human)`; der einzige Weg, wie diese Schicht die Welt
  verändert.
* `Action<SimulationWorld>? BeforeTick` – wird in `Advance`s Tick-Schleife unmittelbar **vor** `Session.Step()` aufgerufen.
  `MatchController` hängt hier `Input.Tick(world)` ein (Selektion bereinigen, stehende Befehle nachführen), wie es der Vertrag von
  `GameInputController.Tick` verlangt.
* `event Action<TickResult>? TickStepped` – nach jedem Tick mit dem Ergebnis (Zustandshash, Ereignisse). `MatchController` speist
  hierüber `MatchStatsTracker` und den Ticker.

Keines der drei Felder wird von `LiveMatch` selbst aufgerufen, wenn niemand zuhört; das Verhalten ohne `MatchController` ist
unverändert (siehe `LiveMatchTests.cs`, weiterhin grün).

## 3. Dateien

| Datei | Inhalt |
|---|---|
| `MatchController.cs` | Öffentliche API: `Process`, `Update`, `BuildDrawList`, `WindowRect`, Ereignisse `UiRequest`/`MenuActionRequested`/`MatchEnded`, öffentliche Zustände `CommandBar`/`Hud`/`Overlay`/`IsMenuOpen`. |
| `HudAssets.cs` | Lädt einmalig `Window/ControlBar.wnd`, `Window/Menus/QuitMenu.wnd`, `Data/<Sprache>/HeaderTemplate.ini`, `Data/INI/ControlBarScheme.ini` und die `LocalizedText`/`MappedImageTable`-Adapter. |
| `ControlBarScheme.cs` | Eigenständiger, kleiner Parser für `ControlBarScheme.ini` (keine `=`/`;`-Grammatik, siehe unten). |
| `HudTextAdapters.cs` | `MonospaceTextMeasure` (deterministisches Ersatzmaß für Headless-Tests/Renderer ohne echte Glyphenmetrik), `MappedImageLookup`, `LocalizedTextSource`. |
| `Overlays.cs` | Bildschirmraum-Records: `SelectionBoxOverlay`, `UnitMarker`, `RallyPointMarker`, `OrderLine`, `PlacementGhostOverlay`, `HudOverlay`. |
| `MatchResult.cs` | `PlayerMatchStats`, `MatchResult`, `MatchStatsTracker` (siehe Abschnitt 7). |

## 4. HUD-Bindung: `ControlBar.wnd` und `QuitMenu.wnd`

`MatchController` baut einen `UiWindowTree` mit `UiScaleMode.Anchor` (aus `GameSettings.Display.UiScale` abgeleitet: `Stretch`→
`Stretch`, `Uniform`→`Anchor`, `IntegerMultiple`→`Uniform`) und lädt beide Original-Layouts hinein. `ControlBarParent` deckt nur den
unteren Streifen ab (`0,740`–`1920,1080` in 1920×1080-Erstellungsraum) und wird von `UiScaler` deshalb als eigene Ankergruppe
klassifiziert: X wird gestreckt, Y am unteren Rand verankert, eine gemeinsame gleichförmige Skala für beide Achsen. Dieselbe
Transformation gilt für jedes Kindfenster der Datei (gleiche Erstellungsgröße, gleiche Gruppe), weshalb `MatchController`
`ControlBarParent.Transform` auch für die aus `ControlBarScheme.ini` gelesenen Geld-/Energie-Rechtecke wiederverwendet
(`Transform.Map(new WndScreenRect(...))`), statt eine eigene Skalierung zu rechnen.

`QuitMenu.wnd` wird mitgeladen und sofort versteckt (`QuitMenuParent.SetHidden(true)`); `MetaAction.Options` (Esc, siehe
`GameInputController.DispatchMeta`) wird von `MatchController` selbst abgefangen (nicht über `UiRequest` durchgereicht) und
öffnet/schließt das Menü, wobei `LiveMatch.Paused` gesetzt/zurückgesetzt wird. Ein Klick auf `ButtonReturn` schließt es ebenfalls
(`MenuActionRequested(MenuAction.Resume)`); die übrigen vier Knöpfe (`ButtonSaveLoad`, `ButtonOptions`, `ButtonRestart`,
`ButtonExit`) lösen `MenuActionRequested` mit dem passenden Wert aus – ihre Umsetzung (Speichern/Laden, Optionsmenü, Neustart,
Beenden) liegt beim Shell/Host (Paket G1), nicht bei G2.

Die beiden `ObserverPlayerInfoWindow`/`ObserverPlayerListWindow`-Fenster bleiben in der Originaldatei sichtbar; da dieses Paket
nur den menschlichen Spieler bedient, werden sie beim Aufbau fest versteckt.

### 4.1 Eingaberouting: HUD gegen Spielwelt

Tastatur geht immer direkt an `GameInputController.Process` (die `UiWindowTree`-Tastaturpfade werden nicht benutzt; Hotkeys,
Steuergruppen, Kameratasten usw. laufen vollständig über Paket C3). Einzige Ausnahme: Ist das Menü offen, wird nur `Escape`
ausgewertet (schließt das Menü), alle anderen Tasten werden verschluckt, damit das pausierte Menü modal wirkt.

Für Mausereignisse entscheidet `MatchController` pro Tastendruck, ob die HUD oder das Spiel das Ereignis bekommt:

* Liegt der Punkt innerhalb von `LeftHUD` (dem Minimap-Rahmen), behandelt `MatchController` ihn selbst (Abschnitt 6), unabhängig
  vom Mausschema.
* Ist das Menü offen, gehört jeder Klick der HUD (das Spiel darunter ist eingefroren).
* Sonst entscheidet `UiWindowTree.InputTarget(x, y)`: Trifft der Klick ein sichtbares, aktiviertes Fenster (z. B. eine
  Control-Bar-Schaltfläche), bekommt der Baum `MouseDown`/`MouseUp` und `Dispatch()`; sonst bekommt `GameInputController.Process`
  das Ereignis. `MouseMove` geht immer zusätzlich an den Baum (für Hover/Tooltip-Zeitmessung), unabhängig vom Ergebnis.

Wichtig für Aufrufer (und für `HudTests.cs`): Welche Control-Bar-Fenster überhaupt sichtbar/aktiviert sind, wird ausschließlich in
`BuildDrawList` aktualisiert (`RefreshDynamicWindows`), weil die Fensterzustände (`ButtonCommandNN` etc.) direkt aus
`CommandBarView` gesetzt werden. Ein Klick auf eine Control-Bar-Schaltfläche funktioniert daher erst, nachdem `BuildDrawList`
mindestens einmal nach der letzten Auswahländerung gelaufen ist.

### 4.2 `ControlBarSystem`, `QuitMenuSystem`

`ButtonCommandNN`, `ButtonQueueNN` und die übrigen Control-Bar-Knöpfe haben selbst `SYSTEMCALLBACK = "[None]"`; das Ereignis
wandert (`PassSelectedButtonsToParentSystem`) bis zu `ControlBarParent.SYSTEMCALLBACK = "ControlBarSystem"` hoch, das
`MatchController` als einzigen Handler registriert:

| Fenstername | Wirkung |
|---|---|
| `ButtonCommand01`…`14` | `CommandBarModel.Activate(world, view, slot, commands)` – Bau, Einheit, Verkauf, Spezialkraft usw. je nach `CommandButtonInfo.Kind`. |
| `ButtonQueue01`…`09` | `CommandModeController.CancelProduction(producer, index)`. |
| `ButtonCancelConstruction` | `CommandModeController.CancelConstruction(producer)`. |
| `ButtonIdleWorker` | `GameInputController.DispatchMeta(MetaAction.SelectNextIdleWorker, …)`. |
| `ButtonOptions` | öffnet das Menü (wie Esc). |
| `ButtonGeneral`, `ButtonPlaceBeacon`, `PopupCommunicator` | nicht umgesetzt (Generalspunkte-Popup, Leuchtfeuer, Chat – außerhalb des Auftragsumfangs); erzeugen eine Ticker-Meldung „… is not implemented.“, nie einen stillen Fehlschlag. |

`QuitMenuParent.SYSTEMCALLBACK = "QuitMenuSystem"` wird analog registriert (Abschnitt 4).

## 5. Dynamischer HUD-Inhalt

Die vom `.wnd`-Baum selbst gezeichneten Elemente (Fensterhintergründe, Text laut `TEXT`/`TOOLTIPTEXT`) reichen nicht: Cameo-Bilder,
Hotkey-Buchstaben, Kosten, Abklingzeit-/Baufortschrittsüberlagerungen sowie Geld/Energie existieren im Original nur durch
`DRAWCALLBACK`s wie `W3DCommandBarBackgroundDraw`, `W3DLeftHUDDraw`, `W3DPowerDraw`, die dieses Projekt (noch) nicht implementiert.
`UiWindowTree.BuildDrawList()` wandelt jeden unbekannten `DRAWCALLBACK` bereits in ein `UiCustomDraw(callback, window, rect)` um,
die einem Renderer das komplette Zeichnen dieser Elemente überlässt.

`MatchController.BuildDrawList` nutzt diesen Mechanismus für die 3D-Ansicht und die Minimap unverändert:

* `W3DLeftHUDDraw` (aus der Originaldatei, für das `LeftHUD`-Fenster) ist der Minimap-Rahmen; ein Renderer zeichnet
  `LiveMatch.Minimap` (per `MinimapImage.Compose`) achsenerhaltend hineinskaliert (siehe `TryMinimapWorld`, Abschnitt 6, für die
  exakt gleiche Rechenregel, mit der `MatchController` Klicks zurückrechnet).
* `Viewport3D` ist **kein** Originalname, sondern eine eigene Ergänzung dieses Pakets: ein `UiCustomDraw("Viewport3D", "Viewport",
  rect)`, dessen Rechteck den gesamten Bereich oberhalb der Control-Bar abdeckt (`0,0` bis `Breite, ControlBarParent.Rect.Y`), damit
  ein Host weiß, wohin die 3D-Szene gerendert werden soll, ohne die Control-Bar-Geometrie selbst nachzurechnen.

Für alles andere ergänzt `MatchController` die vom Baum gelieferte `UiDrawList` um zusätzliche Befehle (`BuildDynamicCommands`),
bevor sie zurückgegeben wird:

* **Cameo:** `UiImageDraw(button.ButtonImage, slotRect, …)`, sofern das benannte Bild in `GameData.Images` existiert (fehlt es,
  wird nichts gezeichnet – kein Platzhalter, kein Absturz; das Original hätte hier ebenfalls kein Bild).
* **Hotkeybuchstabe** oben links, **Kosten** unten rechts (nur wenn `CommandBarButton.Cost != 0`).
* **Abklingzeit/Nachladen:** ein halbtransparentes Rechteck, das von oben in den Knopf hineinwischt (`ButtonAvailability.NotReady`,
  `ReadyFraction`); dieselbe Wischlogik für den ersten Warteschlangeneintrag (`QueueEntry.Progress`).
* **Warteschlangen-Cameo** je sichtbarem `ButtonQueueNN`.
* **Geld/Energie:** Text bzw. gefüllter Balken an den aus `ControlBarScheme.ini` gelesenen Rechtecken (`MoneyUL/LR`,
  `PowerBarUL/LR`), projiziert über `ControlBarParent.Transform` (Abschnitt 4). Der Energiebalken ist proportional zu
  `Produktion / Verbrauch` (auf `[0,1]` begrenzt) statt eines festen Balkens, was dem Original nahekommt, ohne dessen exakte
  Balkengrafik zu benötigen.

**Nicht umgesetzt** (bewusst außerhalb des Umfangs, siehe Auftrag): Beacon- und Diplomatie-Fenster, das Generalspunkte-Popup, die
fünf `UnitUpgradeN`-Symbole (die Simulation liefert nur die Namen der abgeschlossenen Upgrades über
`CommandBarView.Upgrades`, aber keinen Symbolkatalog dafür), ein Objektporträt in `CameoWindow`/`WinUnitSelected` (nur
Sichtbarkeit je nach `CommandBarContext.Command` wird geschaltet, kein Bildinhalt). Diese Lücken sind funktional harmlos (die
Schaltflächen bleiben nutzbar), aber optisch unvollständig; sie sind für eine spätere Phase vorgemerkt.

## 6. Minimap: Klick, Ziehen, Rechtsklick-Befehl

Das Original bindet die Minimap-Klicklogik nicht über das normale Gadget-System (das `LeftHUD`-Fenster ist ein reines
`USER`-Fenster ohne interaktiven Gadget-Typ), sondern direkt im Eingabepfad. `MatchController.TryMinimapWorld` bildet einen
Bildschirmpunkt exakt achsenerhaltend zentriert (Letterbox) auf eine Weltkoordinate von `LiveMatch.Minimap` ab (dieselbe Regel, mit
der ein Renderer das Minimapbild in `LeftHUD.Rect` zeichnen sollte). Linksklick zentriert die Kamera dorthin und hält beim Ziehen
mit; Rechtsklick löst über `CommandModeController.IssueContext` einen Bewegungsbefehl der aktuellen Auswahl an diese Stelle aus –
unabhängig vom gewählten `MouseScheme` (Minimap-Konventionen sind im Original schemaunabhängig).

## 7. Auswahlmodus und Kontextbefehle

`GameInputController.Settings.Scheme` bleibt der C3-Standard `MouseScheme.LeftClickCommands`: ein Linksklick auf freiem, begehbarem
Boden löst (ohne Ziel unter dem Cursor) einen Bewegungsbefehl aus, ein Rechtsklick storniert einen Modus oder leert die Auswahl.
`HudTests.cs` benutzt konsequent Linksklicks für Bewegung/Angriffsbewegung (ein häufiger Stolperstein beim Schreiben eigener
Skripte gegen diese Schicht).

## 8. Bildschirmraum-Overlay (`HudOverlay`)

`MatchController.Update` berechnet pro Frame:

| Feld | Herkunft |
|---|---|
| `SelectionBox` | `InputState.IsDragging(Left)`/`PressPosition(Left)` – reines Ablesen des laufenden Ziehvorgangs, keine eigene Zustandsverfolgung. |
| `Selected` (`UnitMarker`) | `SelectionModel.Selected` projiziert über `EntityPicker.TryProject`; Lebensanteil aus `IBodyModule.Health/MaxHealth`. |
| `RallyPoints` | Für jede ausgewählte Einheit mit `ProductionUpdate`: `SimulationQueries.Describe(production)` liest `hasRally`/`rallyX`/`rallyY` (als `Fixed` im `StateNode`) – der einzige öffentliche Weg an diesen Zustand. |
| `OrderLines` | `CommandModeController.Orders.All` (stehende Befehle: Angriffsbewegung, Wache, Wegpunkte); die Punktliste ist Startposition → `Destination` → verbleibende `Waypoints`, projiziert. Ein einfacher Bewegungsbefehl (`ContextAction.Move`) erzeugt **keinen** `StandingOrder` (er ist ein Einzelbefehl) und taucht deshalb hier nicht auf. |
| `Placement` | `CommandModeController.Ghost` plus eine selbst berechnete Grundfläche (`Footprint`): bei `Geometry == BOX` die um `Angle` gedrehten Eckpunkte aus `GeometryMajorRadius`/`MinorRadius`, sonst ein Quadrat aus `BoundingCircleRadius` (die Simulation liefert für Nicht-Box-Geometrien keine Kantenlängen). |
| `HintText` | grobe Textform von `GameInputController.Hint.Action` (kein String-Tabellen-Label – nur ein technischer Platzhaltertext für einen Host, der noch keine eigene Übersetzung hat). |
| `Ticker` | Siehe unten. |

### Ticker (EVA-Ersatz)

Es gibt keine eigene EVA-Sprachausgabe in diesem Paket; der Ticker ist eine reine Textliste (höchstens sechs Einträge, acht
Sekunden Lebensdauer, eigene Uhr aus den `deltaSeconds`-Summen von `Update`), gespeist aus genau den Quellen, die die Simulation
hergibt:

* `CommandModeController.DrainFeedback()`: `Rejected`, `Unsupported`, `InvalidOrder`.
* `SimEventKind.ConstructionCompleted`, `ProductionCompleted`, `PlayerDefeated`, `SpecialPowerUsed`, `UpgradeCompleted`,
  `CommandRejected` aus `LiveMatch.TickStepped`.

Jede andere `SimEventKind` wird ignoriert; es gibt keinen generischen Sammeltext.

## 9. Kameraeinstellungen

`GameSettings.Controls` (Schieberegler `0..1`) wird auf `CnCNew.Presentation.Input.CameraSettings` abgebildet, indem die
Werkseinstellungen mit `0.5 + Reglerwert` skaliert werden (`MapCameraSettings`): bei `0.5` (Vorgabe) bleiben die
Engine-Vorgabewerte unverändert, an den Reglerenden werden sie halbiert bzw. verdoppelt. Es gibt keine Originaltabelle für diese
Zuordnung, da das Original einen eigenen, nichtlinearen internen Geschwindigkeitswert aus dem Regler ableitet, den diese Schicht
nicht nachbildet; die Zuordnung ist eine bewusste, dokumentierte Näherung.

## 10. Spielergebnis und Statistik (`MatchResult.cs`)

`MatchStatsTracker.Observe(TickResult, world)` wird von `MatchController` an `LiveMatch.TickStepped` gehängt und wertet
ausschließlich Felder aus, die die Simulation bereits über `SimEvent` offenlegt (keine Zusatzabfrage von Spielerzustand):

* **Gebaute Einheiten:** ein `EntitySpawned` ohne `STRUCTURE`-KindOf, mit echtem Spieler (die anfängliche Kartenbevölkerung
  entsteht vor dem ersten `Advance`-Aufruf und erreicht `TickStepped` nie).
* **Gebaute Gebäude:** `ConstructionCompleted` (trägt den Besitzer direkt).
* **Verlorene Einheiten:** Besitzer der gestorbenen Einheit (`EntityDied.Entity`), vor einer eventuellen Entfernung nachgeschlagen.
* **Kills:** Besitzer des Angreifers (`EntityDied.Other`), wenn er sich vom Opfer unterscheidet und keiner der beiden das
  Systemkonto ist (entspricht `Object::scoreTheKill`: kein Eigenbeschuss, kein Selbstmord).
* **Verdientes Geld:** `SupplyDeposited` gutgeschrieben an den Besitzer des Lagerhauses (`Entity`, nicht `Other` – das Ereignis
  trägt kein eigenes Spielerfeld).

`MatchController.MatchEnded` feuert genau einmal, sobald `SkirmishMatchSession.IsOver` erstmals beobachtet wird.

## 11. Bekannte Grenzen

* **Objektporträt, Upgrade-Symbole, Generalspunkte, Leuchtfeuer, Chat** – siehe Abschnitt 5, bewusst außerhalb des Umfangs.
* **`ControlBarScheme.ini`** wird nur für Geld-/Energie-Rechtecke und Hintergrundbildteile gelesen; Umschalt-/Chat-/Leuchtfeuer-
  Knopfbilder, `AnimatingPart`-Blöcke und die vier Rahmenfarben (`ButtonBorder*Color`) werden geparst-übersprungen bzw. gar nicht
  gelesen, weil dieses Paket sie nirgends braucht.
* **Minimap-Seitenverhältnis:** `TryMinimapWorld` nimmt an, dass ein Renderer das Minimapbild seitenverhältniserhaltend zentriert in
  `LeftHUD.Rect` zeichnet (Letterbox); zeichnet ein Host es anders (z. B. gestreckt), muss er entweder dieselbe Regel verwenden oder
  eigene Klick-Umrechnung ergänzen.
* **Reproduzierbarkeit von Replays bei laufendem `LiveMatch` (wichtiger Befund, nicht in diesem Paket entstanden):** Ein
  `LiveMatch`, das über `Advance` mehrere Ticks lief, liefert einen `World.StateHash`, der **nicht** mit einem frischen
  `ReplayRunner.Run(session.ToReplay(), …)` derselben Aufzeichnung übereinstimmt, sobald der Computergegner den ersten eigenen
  Befehl erteilt. Das reproduziert sich mit einer minimalen Gegenprobe, die nur `LiveMatch.Create`/`LiveMatch.Advance` benutzt –
  ganz ohne `MatchController`, ohne Eingabe, ohne HUD-Code – gegen echte Inhalte (`.local/phase2-import-a`, Karte
  „Alpine Assault“). Die bestehenden Replay-Tests in `CnCNew.Simulation.Tests` (`Tests.Skirmish.cs`) vergleichen bisher nur zwei
  frische `ReplayRunner.Run`-Aufrufe gegeneinander, nie gegen den Hash einer live fortgeschrittenen `SkirmishMatchSession` – die
  Lücke war also bisher ungetestet. `tests/CnCNew.Game.Tests/HudTests.cs` prüft deshalb nur die intern konsistente Reproduzierbarkeit
  zweier frischer Replay-Läufe derselben Aufzeichnung (wie es die bestehenden Simulationstests auch tun), nicht die Übereinstimmung
  mit dem Live-Hash. Ein Hintergrundauftrag zur Ursachensuche wurde separat angestoßen (siehe Abschlussbericht von Paket G2).
* **Kameraeinstellungs-Zuordnung** (Abschnitt 9) ist eine dokumentierte Näherung, keine originalgetreue Übernahme.

## 12. Tests

`tests/CnCNew.Game.Tests/HudTests.cs` (registriert in `Program.cs`, `--content-dir` erforderlich, sonst `SKIP`):

* Start-Dozer per Klick auswählen, ein Bauwerk über die Control-Bar und Platzierung errichten (bevorzugt ein Kraftwerk, sonst das
  erste verfügbare `DozerConstruct`, damit der Test auch für GLA ohne Energiewirtschaft läuft), eine Einheit von einem Erzeuger in
  die Warteschlange stellen (sucht selbst nach dem ersten eigenen Erzeuger mit einem `UnitBuild`-Knopf, nicht hartkodiert auf
  Infanterie, da das von Fraktion/Karte abhängt), Bewegungs- und – falls die produzierte Einheit einen Angriffsbewegungs-Knopf
  besitzt – Angriffsbewegungsbefehl per Klick.
* Wiedergabe: Der aufgezeichnete Befehlsstrom enthält den Bewegungsbefehl des Menschen; zwei unabhängige frische
  `ReplayRunner.Run`-Läufe derselben Aufzeichnung stimmen überein (siehe Abschnitt 11 für die Live-Hash-Einschränkung).
* HUD-Zeichenlisten bei 800×600, 1280×720, 1920×1080 und 2560×1080: nicht leer, mindestens der Minimap-`UiCustomDraw` vorhanden,
  alle 14 Befehlsknöpfe liegen vollständig im Bildschirmbereich, der `Viewport3D`-Bereich deckt die volle Breite ab.
* Minimapklick verschiebt die Kamera.
* Ziehauswahl selektiert den Start-Dozer.
* Strg+1 speichert, `1` ruft die Gruppe ab.
* Goldener Hash von `BuildDrawList(1280, 720).Dump()` gegen `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`
  (wird beim ersten Lauf ohne vorhandene Datei geschrieben; danach ein Review-Gegenstand wie jede andere Baseline).

## 13. Anfragen an Simulation/Präsentation

* **`ReplayRunner`/`SkirmishMatchSession`-Divergenz** (Abschnitt 11) – bitte durch das Team untersuchen, das `LiveMatch.cs`
  bzw. die Simulation/Presentation-Kopplung verantwortet; ein Hintergrundauftrag wurde dafür angelegt.
* Die von Paket C3 offenen Wünsche (`CheckConstruct`-Trockenlauf, Produktionswarteschlangen-Zugriff, `AvailableOnFrame`
  für Spezialkräfte, `Executed` für Upgrades) sind seit Phase 10 erledigt (`SimulationWorld.CheckConstruct`, `ProductionUpdate.Queue`,
  `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed` — siehe `docs/PRESENTATION-INPUT.md` §10, `docs/SIMULATION-CORE.md` §23);
  G2 selbst hatte keine weiteren Simulationsänderungen benötigt.
* Für ein originalgetreues Objektporträt und Upgrade-Symbole bräuchte `CommandBarView`/`ObjectTemplate` einen Verweis auf ein
  Porträtbild bzw. einen Upgrade→Bild-Katalog; aktuell liefert die Simulation nur Namen.
