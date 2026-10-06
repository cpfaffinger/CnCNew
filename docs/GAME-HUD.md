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
3. `controller.Update(deltaSeconds)` – wendet die Kameraabsicht an, baut `CommandBar`/`Hud` neu, aktualisiert die Meldungsliste und das
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
  hierüber `MatchStatsTracker` und die Meldungsliste.

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
| `ButtonGeneral`, `ButtonPlaceBeacon`, `PopupCommunicator` | nicht umgesetzt (Generalspunkte-Popup, Leuchtfeuer, Chat – außerhalb des Auftragsumfangs); erzeugen eine eingeklammerte Meldungszeile „(not supported in this build: …)“ (`HudMessageFeed.ClientNote`), nie einen stillen Fehlschlag. |

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
* **Kein Text auf den Symbolen.** Das Original druckt weder Hotkeybuchstaben noch Kosten auf die Befehlssymbole (`ControlBar::setControlCommand` leert den
  Knopftext, Name, Kosten und Hotkey stehen in der Beschreibung); der Client hat sie früher aufgedruckt und ist davon abgekommen (Phase 16, `docs/TEXT-AUDIT.md`).
  Stattdessen gibt es **Tooltips** (Abschnitt 5a).
* **Abklingzeit/Nachladen:** ein halbtransparentes Rechteck, das von oben in den Knopf hineinwischt (`ButtonAvailability.NotReady`,
  `ReadyFraction`); dieselbe Wischlogik für den ersten Warteschlangeneintrag (`QueueEntry.Progress`).
* **Warteschlangen-Cameo** je sichtbarem `ButtonQueueNN`.
* **Geld/Energie:** das Fenster `ControlBar.wnd:MoneyDisplay` trägt den Text `GUI:ControlBarMoneyDisplay` (`$ %d`) mit dem Geld des lokalen Spielers
  (`InGameUI::update`) und liegt am Geldrechteck des Schemas (`MoneyUL/LR`, `ControlBarScheme`; `UiWindow.SetScreenRect`); die Layoutdatei hat dort nur den
  Platzhalter „$$$“ in einem 100x100-Rechteck oben links, den der Client früher so zeigte. Der Energiebalken ist ein gefüllter Balken am Rechteck
  `PowerBarUL/LR`, projiziert über `ControlBarParent.Transform` (Abschnitt 4). Der Energiebalken ist proportional zu
  `Produktion / Verbrauch` (auf `[0,1]` begrenzt) statt eines festen Balkens, was dem Original nahekommt, ohne dessen exakte
  Balkengrafik zu benötigen.

### 5a. Tooltips (Phase 16)

`MatchController` und `GameShell` hängen den Zeigertooltip des Fensters unter dem Zeiger an die Zeichenliste (`TooltipPainter`, `UiTooltipLayout`;
`Mouse::drawTooltip` in Mouse.cpp): Kasten 20 Pixel rechts vom Zeiger (links bzw. über dem Zeiger am Bildrand), Umbruch bei `TooltipWidth` Prozent der
Bildbreite, Text von links über `TooltipFillTime` eingeblendet (Hervorhebungsfarbe auf den vorderen 15 Pixeln), Stil aus dem `Mouse`-Block von `Mouse.ini`,
Schrift `TooltipFontName` aus `Language.ini` (Arial 8) mit Auflösungsanpassung. Er erscheint, wenn der Zeiger `TOOLTIPDELAY` (sonst 800 ms) still
über dem Fenster stand, und verschwindet bei jeder Bewegung.

**Beschreibung der Befehlsknöpfe** (`CommandButtonDescription`, nach `ControlBar::populateBuildTooltipLayout` in ControlBarPopupDescription.cpp und
`ProductionPrerequisite::getRequiresList`): die drei Texte des Originalpopups – Name (`TextLabel`, ohne `&`), Kosten (`TOOLTIP:Cost`, nur wenn > 0), Beschreibung (`DescriptLabel`) – als
Zeilen des Zeigertooltips (`TOOLTIPDELAY = 1`). An die Beschreibung hängt das Original, und jetzt auch der Client:

| Fall | Text | Regel |
|---|---|---|
| Einheit/Gebäude, zu wenig Geld | Leerzeile, `TOOLTIP:TooltipNotEnoughMoneyToBuild` („Insufficient funds“) | `canMakeUnit` = `CANMAKE_NO_MONEY`; nur mit Beschreibungslabel und ausgewähltem Objekt |
| Einheit/Gebäude oder Upgrade, Warteschlange voll | Leerzeile, `TOOLTIP:TooltipCannotPurchaseBecauseQueueFull` | `CANMAKE_QUEUE_FULL` bzw. `getProductionCount() == MAX_BUILD_QUEUE_BUTTONS` |
| Voraussetzung fehlt | neue Zeile `CONTROLBAR:Requirements` („Requires: %s“) mit je einem Eintrag je unerfüllter `Prerequisites`-Zeile, durch „, “ getrennt: Anzeigename des Objekts; bei Alternativen die **letzten zwei** mit `CONTROLBAR:OrRequirement` („A or B“ – `getRequiresList` nennt nur diese beiden); bei einer Wissenschaftszeile `CONTROLBAR:GeneralsPromotion` | `getRequiresList` je Voraussetzung; `Ignore_Prerequisites` wird dort nicht beachtet |
| Upgrade schon vorhanden (Spieler oder ausgewähltes Objekt) | `PurchasedLabel` des Knopfs, sonst `TOOLTIP:AlreadyUpgradedDefault`, **statt** der Beschreibung, ohne Kosten | `hasUpgradeComplete`/`hasUpgrade` |
| Upgrade, Wissenschaft des Knopfs fehlt | neue Zeile „Requires: General's Promotion“ | `getScienceVec`, `hasScience` |
| Overcharge-Knopf | neue Zeile `TOOLTIP:TooltipNukeReactorOverChargeIsOn`/`…IsOff` | `OverchargeBehaviorInterface::isOverchargeActive` |

Dazu die zwei Fenster, die das Original im selben Popup beschreibt: `ControlBar.wnd:MoneyDisplay` (`CONTROLBAR:Money`, `CONTROLBAR:MoneyDescription`) und `ControlBar.wnd:PowerWindow`
(`CONTROLBAR:Power`, `CONTROLBAR:PowerDescription` mit Erzeugung und Verbrauch des Spielers); das Energiefenster liegt jetzt wie das Geldfenster am Rechteck des Schemas
(`ControlBarScheme::init`), dort wo der Balken gezeichnet wird. Die fehlenden Zeilen einer Vorlage liefert `SimulationQueries.MissingPrerequisites` (derselbe Test wie
`PrerequisitesSatisfied`, je Zeile).

**Ausgegraute Knöpfe erklären sich:** Die Tooltip-Suche des Originals ignoriert den Enabled-Zustand (`GameWindowManager::winProcessMouseEvent`:
`winPointInAnyChild( x, y, TRUE, TRUE )`), die Eingabe nicht. `UiWindowTree.Tooltip` fragt deshalb zuerst das tiefste sichtbare Fenster unter dem Zeiger, wenn es (oder ein Fenster
darüber) deaktiviert ist; vorher bekam ein deaktivierter Knopf nie einen Tooltip – genau die Knöpfe, bei denen „Requires: …“ steht. Eingabe und Hervorhebung bleiben unverändert beim
aktivierten Fenster (`WndTests.TooltipStateMachine`).

**Nebenbefund Energiefenster:** `ControlBar.wnd:PowerWindow` steht in der Layoutdatei als 100x100-Rechteck oben links (bei 1920x1080) mit `INPUTCALLBACK = GameWinBlockInput`; solange es
dort blieb, verschluckte die HUD jeden Klick in die linke obere Bildecke (etwa 67x67 Pixel bei 1280x720). Am Schemarechteck liegt es jetzt in der Befehlsleiste. Dadurch ändert sich der
goldene Wert `hud-drawlist-1280x720.sha256`: das Rechteck des `W3DPowerDraw`-Eintrags der Zeichenliste ist ein anderes (der Client zählt diesen Eintrag nur; den Balken zeichnet weiterhin
`BuildDynamicCommands`).

**Vereinfacht / nicht gebaut:** Das Original zeigt das alles in `ControlBarPopupDescription.wnd` (eigenes Fenster über der Befehlsleiste, eigene Zeichenfunktion
`W3DCommandBarHelpPopupDraw`, unterstrichener Hotkey); hier bleibt es der Zeigertooltip. Es fehlen: die Texte der Wissenschaftsknöpfe (`ScienceStore::getNameAndDescription`,
`TOOLTIP:ScienceCost` – der Client hat kein Generalspunkte-Fenster), der Text bei widersprechendem Upgrade (`ConflictingLabel`; die Simulation kennt kein `affectedByUpgrade`), die Gründe
„Höchstzahl“ und „Flugfeld voll“ (`TooltipCannotBuild…BecauseMaximumNumber`, `…ParkingFull`), das `GeneralsExp`-Fenster, Objekttooltips (Einheit unter dem Zeiger).
**Nicht belegt:** ob ein Mensch die Zeilen lesbar findet und ob Lage und Tempo des Tooltips taugen – nichts davon wurde gesehen.

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
| `OrderLines` | Stehende Befehle **der ausgewählten eigenen Einheiten** (Angriff, Angriffsbewegung, Wache; seit Phase 16 nicht mehr die aller Einheiten): eine Linie Einheit → Ziel (beim Angriff die aktuelle Position des Opfers). Ein einfacher Bewegungsbefehl (`ContextAction.Move`) erzeugt **keinen** `StandingOrder` und hat deshalb keine Linie, sondern eine Befehlsmarke. Wegpunkte stehen in `WaypointPaths`. |
| `WaypointPaths` (`WaypointPathOverlay`) | `OrderMarkers.WaypointPaths` (§16): je ausgewählter Einheit mit Wegpunktbefehl die Punktliste Einheit → Stationen, nur solange Alt gehalten wird. |
| `OrderHints` (`OrderHintMarker`) | `OrderHintBook` (§16): die Marken frisch erteilter Befehle mit ihrem Alter 0..1. |
| `Placement` | `CommandModeController.Ghost` plus eine selbst berechnete Grundfläche (`Footprint`): bei `Geometry == BOX` die um `Angle` gedrehten Eckpunkte aus `GeometryMajorRadius`/`MinorRadius`, sonst ein Quadrat aus `BoundingCircleRadius` (die Simulation liefert für Nicht-Box-Geometrien keine Kantenlängen). |
| `HintText` | grobe Textform von `GameInputController.Hint.Action` (kein String-Tabellen-Label – nur ein technischer Platzhaltertext für einen Host, der noch keine eigene Übersetzung hat). |
| `Ticker` | Die Texte der Meldungsliste dieses Bildes (siehe unten); gezeichnet wird die Liste selbst, nicht dieses Feld. |

### Meldungsliste (`InGameUI::message`, Phase 16)

**Vorher:** `MatchController` führte einen „Ticker“ mit erfundenen englischen Texten („Rejected: …“, „Cannot do that.“, „… ready“, „Command rejected“), acht Sekunden Lebensdauer – und
**kein Zeichencode las ihn** (`HudOverlay.Ticker` hatte außerhalb von `MatchController` keinen Verbraucher). Der Spieler erfuhr also nie, warum ein Befehl nichts tat.

**Jetzt** gibt es wie im Original **eine** Meldungsliste (`InGameUI::m_uiMessages`, hier `UiMessageLog` aus der Missionstext-Arbeit: sechs Zeilen, `MessagePosition`/`MessageFont`/
`MessageColor1/2` aus `InGameUI.ini`, Verblassen nach der Ganzzahlrechnung des Originals, weg nach 277 Logikbildern). `HudMessageFeed` (`CnCNew.Game/Hud`) schreibt die Zeilen des
Spiels hinein, `DISPLAY_TEXT` der Missionsskripte die seinen – in dieselbe Liste: in einer Mission gehört sie dem Director (`MissionPresentationDirector.UseMessages`, er verblasst und
zeichnet sie, auch während die Befehlsleiste versteckt ist), im Skirmish dem HUD (`MatchController.OwnsMessageList`, ein Update je Logikschritt, gezeichnet nach den Fenstern wie
`InGameUI::postDraw`). Jede Zeile ist ein Label der Stringtabelle, gewählt wo das Original es wählt; ein fehlendes Label erscheint als `??Label??` und wird von `LocalizedText`
gezählt.

| Anlass | Label (englischer Text) | Quelle im Original |
|---|---|---|
| Produktion/Bau abgelehnt: kein Geld | `GUI:NotEnoughMoneyToBuild` („You don't have enough money to build that“) | `ControlBar::processCommandUI`, `PlaceEventTranslator::translateGameMessage` |
| Upgrade abgelehnt: kein Geld | `GUI:NotEnoughMoneyToUpgrade` | `UpgradeCenter::canAffordUpgrade` |
| Warteschlange voll | `GUI:ProductionQueueFull` | `ControlBar::processCommandUI` |
| Höchstzahl erreicht (`MaxSimultaneousOfType`) | `GUI:UnitMaxedOut` | ebenda |
| Bauplatz unerkundet | `GUI:CantBuildShroud` | `InGameUI::displayCantBuildMessage` (`LBC_SHROUD`) |
| Bauplatz sonst abgelehnt | `GUI:CantBuildThere` | ebenda (`LBC_GENERIC_FAILURE`) |
| Gebäude des Spielers fertig | `DOZER:ConstructionComplete` mit dem Anzeigenamen (`Object.DisplayName`, sonst `INI:MissingDisplayName`) | `DozerAIUpdate` (Abschluss der Bauaufgabe) |
| vom Spieler bestelltes Upgrade fertig | `UPGRADE:UpgradeComplete` mit `Upgrade.DisplayName` | `ProductionUpdate::update` |
| eigenes Objekt von einem anderen Spieler beschädigt | `RADAR:UnitUnderAttack`, `RADAR:HarvesterUnderAttack`, `RADAR:StructureUnderAttack` (Gebäude mit `MP_COUNT_FOR_VICTORY`), sonst `RADAR:UnderAttack`; höchstens eine Zeile je 250 Welteinheiten und 10 Sekunden | `Radar::tryUnderAttackEvent`, `Radar::tryEvent` |
| Spieler besiegt (nur Skirmish) | `GUI:PlayerHasBeenDefeated` mit dem Armeenamen | `VictoryConditions::update` (läuft nur, wenn `RecorderClass::isMultiplayer`) |

**Ohne Zeile, gezählt** (`HudMessageFeed.WithoutLine`, Schlüssel = Art, Wert = Anzahl): jede andere Ablehnung der Simulation (`rejected <Grund> (<Befehl>)`), ein ungültiger Befehl
ohne Bauplatz (`invalid order` – das Original zeigt dort den Verbots-Zeiger, keinen Text), ein Upgrade ohne `DisplayName`, und Upgrades, die niemand bestellt hat
(`upgrade granted, not ordered`: das Radar des Kommandozentrums, `GrantUpgradeCreate`, Skripte – das Ereignis der Simulation unterscheidet sie nicht von einem gekauften, deshalb merkt sich
die Liste die angenommenen `PurchaseUpgradeCommand`s des Spielers; ohne das stand „Upgrade Complete: Radar“ am Anfang jeder Partie). **Eigene Zeile des Clients** (eingeklammert, wie
die Sprach-/Filmplatzhalter): „(not supported in this build: …)“ für Knöpfe, deren Befehl die Simulation nicht kennt.

**Vereinfacht/nicht umgesetzt:** Die Simulation meldet einen „blockierten Grundriss“, wo das Original Gelände, Neigung, Hindernisse, Versorgungsnähe und Weg unterscheidet
(`GUI:CantBuildRestrictedTerrain` …) – alle laufen auf `GUI:CantBuildThere`. `GUI:ParkingPlacesFull`, `DOZER:RepairComplete`, `RADAR:Infiltration`, `GUI:OverchargeExhausted`,
die Kampfplan- und Tarnungs-Meldungen, `GUI:NothingSelected`/`SelectedAcross…` und die Meldungen der Tastenkürzel (`GUI:MessagesOn` …) haben keine Quelle in diesem Stand. Der Name des
besiegten Spielers ist der Armeename (`PlayerTemplate.DisplayName`), weil die Sitzung keine Spielernamen trägt. In den meisten Fällen verhindert schon der ausgegraute Knopf den Befehl
(wie im Original: `getCommandAvailability` liefert `COMMAND_RESTRICTED` bei zu wenig Geld); die Ablehnungszeilen erscheinen dann nur bei Tastenkürzeln und Wettläufen.
**Bildproben (nur selbst angesehen, keine Abnahme):** `tools/CnCNew.UiRenderTool` schreibt `hudfeedback-WxH.png` (echte Befehlsleiste, drei Meldungszeilen, Zeiger auf einem ausgegrauten
Bauknopf mit „Requires: …“) und `hudletterbox-WxH.png` (dieselben Zeilen unter den Letterbox-Balken); der Client zeigt die Zeile im Rauchtest mit
`--match "Maps/Alpine Assault/Alpine Assault.map" --frames 110 --input-script scripts/input/placement-refused-click.txt --capture <png>` (Klick auf das eigene Kommandozentrum:
„You can't build here“ oben links, Vorschau rot).
**Nicht belegt:** Aussehen, Lesbarkeit und Takt der Liste im Fenster – kein Mensch hat sie gesehen.

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
* Mauszeiger und Marken (Phase 16, §16): Cursor je Kontext, Alt-Wegpunkte als Pfad mit drei Stationsmarken, verblassende Befehlsmarke.

`dotnet run --project tests/CnCNew.Game.Tests -- --content-dir <dir> --only HudTests` führt nur diese Suite aus (das ganze Programm braucht auf
Realdaten viele Minuten; `--only` nimmt eine kommagetrennte Liste von Suitennamen, ein unbekannter Name ist ein Fehler mit Exitcode 2).

## 13. Anfragen an Simulation/Präsentation

* **`ReplayRunner`/`SkirmishMatchSession`-Divergenz** (Abschnitt 11) – bitte durch das Team untersuchen, das `LiveMatch.cs`
  bzw. die Simulation/Presentation-Kopplung verantwortet; ein Hintergrundauftrag wurde dafür angelegt.
* Die von Paket C3 offenen Wünsche (`CheckConstruct`-Trockenlauf, Produktionswarteschlangen-Zugriff, `AvailableOnFrame`
  für Spezialkräfte, `Executed` für Upgrades) sind seit Phase 10 erledigt (`SimulationWorld.CheckConstruct`, `ProductionUpdate.Queue`,
  `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed` — siehe `docs/PRESENTATION-INPUT.md` §10, `docs/SIMULATION-CORE.md` §23);
  G2 selbst hatte keine weiteren Simulationsänderungen benötigt.
* Für ein originalgetreues Objektporträt und Upgrade-Symbole bräuchte `CommandBarView`/`ObjectTemplate` einen Verweis auf ein
  Porträtbild bzw. einen Upgrade→Bild-Katalog; aktuell liefert die Simulation nur Namen.

## 14. Phase 11: dieselbe Befehlsleiste für Kampagnenmissionen (`LiveGame`, `LiveMission`, `MissionController`)

`MatchController` (und die Stride-Schichten, `AudioDirector`) sahen bis Phase 10 nur ein `LiveMatch`, also eine Skirmish-Sitzung. Seit Phase 11 sehen sie ein `LiveGame`
(`src/CnCNew.Game/Live/LiveGame.cs`): die gemeinsame Präsentationsschicht (projizierte Frames, Fog, Kamera, Minimap, Effekte, Teamfarben, 30-Hz-Schleife `Advance`) hinter der
abstrakten Sicht „welche Simulation läuft“ (`IsOver`, `Winner`, `Players`, `HumanFaction`, `Commands`, `StepSimulation`). Aus `LiveMatch.cs` wurde nichts kopiert – die Konstruktion der
Schichten wanderte unverändert in die Basisklasse, `LiveMatch` blieb für seine Aufrufer gleich (`Create`, `Setup`, `Session`, `Commands`).

- **`LiveMission`** (`Live/LiveMission.cs`) setzt eine `MissionRun` (`MissionSession`) dahinter. Der Betrachter ist die lokale Seite der Mission (`MissionSession.LocalPlayerOf`,
  einschließlich der von `ChallengeSides` gewählten Seite); die Kamera startet über dem Schwerpunkt seiner Objekte. Ein Tick ist `MissionPresentationDirector.BeforeStep` → `Session.Step()`
  (Skripte, automatisches Gefecht, Welt) → `AfterStep`; die vom Skript bestellte Kamera fährt je Frame über die Dauer des Skripts (`OnAdvanced` → `ApplyCameraTo`, seit Phase 16; vorher ein Sprung je neuem Ziel – `docs/SCRIPT-ENGINE.md` §7a). Befehle des Menschen laufen
  über `MissionCommandSink` (`Presentation/Input/CommandSink.cs`) in `MissionSession.Issue`, also durch denselben Sequenzzähler je Spieler wie die der Skripte.
  `Winner` ist der Betrachter bei einem Missionssieg, sonst `null` (die Gegner sind geskriptet, kein konkurrierender Mensch).
- **`MissionController`** (`Hud/MissionController.cs`) setzt ein `MatchController` über die `LiveMission`, hängt `SelectionModel.SelectionChanged` an `MissionUiBridge.OnSelectionChanged`
  (die Auswahl erreicht `NAMED_SELECTED`), liefert `BuildDrawList` mit dem Bildschirmtext des Directors (Meldungszeilen, getippte Missionsbeschriftung, Zeitgeber und Zähler, Filmtext, Sprach-/Filmplatzhalter, seit Phase 16 auch Letterbox, Blenden, Ergebnisbanner; während der Letterbox ohne Befehlsleiste) über dem HUD, danach das Meldungsfenster (`MissionPopupWindow`, `INGAME_POPUP_MESSAGE`: eigener Fensterbaum aus `InGamePopupMessage.wnd`; ein Fenster mit Pausenmarke pausiert die Mission und schluckt alle Ereignisse, OK/Enter/Escape schließen es und heben die Pause auf), sperrt die Welteingabe, solange das Skript sie gesperrt hat (`MatchController.WorldInputEnabled`), und meldet das Missionsende
  einmal (`MissionEnded`); danach ruft der Host `GameShell.MissionFinished(run, controller)`.
- **Was Skripte an Befehlsleiste und HUD ändern** (Phase 16, zweite Sitzung; `MissionController` setzt `IMissionHudControl`, Wortlaut je Ausgabetyp in `docs/SCRIPT-ENGINE.md` §7a):
  `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE`/`COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT` verändern den **Befehlssatz** des Objekttyps, nicht ein einzelnes Objekt: `ControlBarScriptState`
  (`Presentation/Input/ControlBarScriptState.cs`) hält Überschreibungen je (Satzname, Platz) – ein entfernter Knopf ist eine Überschreibung ohne Knopf (`GameLogic::setControlBarOverride`,
  `CommandSet::getCommandButton` fragt sie zuerst) –, und `CommandBarModel.Build` (Einzel- und Mehrfachauswahl) sowie die Bauschlangen-Bilder lesen sie über `ButtonOf`. Der Spieler sieht und
  klickt den Knopf also bei **jedem** Objekt dieses Satzes nicht mehr; ein Knopf, der schon fehlt, bleibt weg, ein unbekannter Knopf oder Objekttyp wird abgewiesen und gemeldet. In `MD_USA01`
  setzt das Skript in Tick 0 25 solcher Entfernungen (Rekrutierungs-/Aufrüstknöpfe der Fraktionen). `PLAYER_SCIENCE_AVAILABILITY` (nur für die lokale Seite) wirkt auf `PurchaseScience`-Knöpfe
  („Hidden“ = Knopf verborgen, „Disabled“ = gesperrt, `ButtonReason.NotPurchasable`); sichtbar wird das erst mit einem Generals-Powers-Fenster, das der Client nicht hat. `OPTIONS_SET_DRAWICON_UI_MODE`
  schaltet `MatchController.IconUiEnabled`: aus = keine Heilsymbole (Auswahlmarken bleiben). `UNIT_AFFECT_OBJECT_PANEL_FLAGS` „Selectable“ = `EntityPicker.SetScriptSelectable`: der Pick und die
  Auswahl lassen das Objekt weg, eine bestehende Auswahl verliert es (`SelectionModel.Replace/Change`). Zustände gehen beim Speichern/Laden einer Mission nicht mit. Tests:
  `MissionEffectsTests` (Attrappen; echte `MD_USA01`: Knopf verschwindet aus der Leiste eines gewählten Objekts und kommt zurück, Wissenschaft, Auswahl).
- **Superwaffen-Liste** (`Hud/SuperweaponTimers.cs`, `MatchController.Superweapons`, gilt für Gefechte und Missionen): oben rechts, bei x = 0,90, y = 0,01 der Bildfläche (`SuperweaponCountdownPosition` der
  Retail-`InGameUI.ini`, jetzt aus `InGameUiSettings`), je Macht mit `PublicTimer` und je Objekt eine Zeile „Name: m:ss“ – der Name rechtsbündig bis x, die Zeit linksbündig ab x (`SuperweaponInfo::drawName/drawTime`), in der Hausfarbe
  des Besitzers, **auch die gegnerischen** (ein feindlicher Atomschlag läuft auf dem eigenen Bildschirm herunter). Regeln nach `InGameUI::draw`: Spieler nach Index, Mächte nach Name, Objekte in
  Anlegereihenfolge; eine Baustelle zeigt nichts; eine Macht mit gemeinsamem Zeitgeber (`SharedSyncedTimer`) steht je Spieler einmal; fehlt dem Besitzer die geforderte Wissenschaft, fehlt die Zeile;
  die Zeit ist `(Bereitschaftsbild − Tick) / 30` ganzzahlig (`CommandBarModel.ReadyFrameOf`), eine bereite Macht hat die Bereitschaftsschrift (`SuperweaponCountdownReadyFont`, Retail: fett) und wechselt alle 15 Logikbilder (`SuperweaponCountdownFlashDuration` 500 ms) zwischen Hausfarbe und `SuperweaponCountdownFlashColor`, die Blinkfarbe in den ungeraden Perioden (der Zustand `m_superweaponUsedFlashColor` beginnt wahr; vorher lag die Phase umgekehrt); die Liste
  hört über der Befehlsleiste (0,82 der Sichthöhe) mit „...“ auf (linksbündig ab x in der Normalschrift, wie `drawTime` im Original; vorher rechtsbündig). Der Name ist der Eintrag `GUI:<Machtname>` der Zeichenkettentabelle; fehlt er, steht der Schlüssel selbst da und
  `SuperweaponTimers.MissingLabels` zählt ihn. Missionsskripte blenden die ganze Liste (`DisplayEnabled`) oder einzelne Objekte aus (`HiddenByScript`, über `IMissionHudControl`). Die Werte (Position,
  Blinkdauer und -farbe aus `InGameUI.ini`, die Schriften aus `Language.ini` über `InGameUiStyle`, `docs/TEXT-AUDIT.md` §4.6) sind keine Konstanten mehr; `InGameUiSettings` ist streng typisiert und von `TextAndSettingsTests` an der Retaildatei geprüft. **Nicht übernommen:**
  die EVA-Ansage „Superwaffe bereit“, die `drawSuperweaponTimers` im Original mit auslöst (Eva meldet das hier über die Simulationsereignisse), der Wechsel der Schrift beim Bereitwerden mit eigenem
  Textobjekt, die Wiederaufnahme des Zustands aus einem Spielstand. Die benannten Missionszeitgeber (`DISPLAY_COUNTDOWN_TIMER`) stehen seit der Missionstext-Sitzung bei
  `NamedTimerCountdownPosition` (X:0,90 Y:0,666) der Retail-Datei (`MissionTextOverlay.NamedTimers`, `docs/SCRIPT-ENGINE.md` §7a).
- **Stride:** `LiveMatchHost` und alle Weltschichten nehmen ein `LiveGame`; `CnCNew.Client.Stride --mission <Karte> [--human-side <Name>]` startet eine Kampagnenkarte (Rauchtest:
  `--smoke-test --mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --content-dir .local/phase2-import-a`). Seit Phase 12 zeichnet die Stride-Hülle auch die HUD-Zeichenliste
  (`MissionController.BuildDrawList`) und verarbeitet die Shell-Ausgaben (`StartMissionOutput`), §15.
- **Grenzen:** das Schnellspeichern im Spiel bleibt `MissionPauseMenu` (`MatchController` hat seinen eigenen Quit-Dialog); Kampagnenzustand und Fortschritt hält weiter der
  `CampaignController`. Die Skirmish-Statistik (`MatchResult`) wird für Missionen mit denselben Ereignissen gefüllt (`MatchStatsTracker.Snapshot(Players, Winner, Tick)`), ihre Zählung
  „gebaut/verloren/getötet“ gilt also auch dort, ein Missionsergebnis mit Zielen/Punkten gibt es nicht. Test: `tests/CnCNew.Game.Tests/MissionHudTests.cs` (echte Karte
  `GC_ChemGeneral`: Frames, Zeichenliste, Befehl über die Senke, Auswahl erreicht die VM, Ende genau einmal).

## 15. Phase 12: HUD, Eingabe und Shell im Stride-Client (`ClientRuntime`)

Die Stride-Hülle zeichnete bis Phase 11 nur die Welt. Seit Phase 12 verdrahtet `src/CnCNew.Client.Stride/ClientRuntime.cs` alles, was die Präsentationsschicht liefert – ohne Simulationszustand zu berühren, sie liest
nur, was die Controller zeigen, und gibt typisierte Eingaben weiter:

- **Eingabe:** `Ui/StrideInputTranslator.cs` übersetzt je Frame `InputManager.Events` und die absolute Mausposition in die gerätneutralen `PresentationInput.InputEvent`s (Fensterpixel, Ursprung oben links =
  Raum des Backbuffers, in dem das UI gelegt ist; Move-Ereignis zuerst, wenn sich der Zeiger bewegte; Tasten ohne Namen in `KeyNames` werden verworfen, Text geht als Zeichen durch). Der Empfänger ist
  der Bildschirmbesitzer: die Shell (Menüs) oder das HUD des laufenden Spiels (`MatchController`/`MissionController`).
- **Zeichnen:** `GameShell.BuildDrawList`, `MatchController.BuildDrawList` und `MissionController.BuildDrawList` werden mit **einem** `UiRenderer` über der 3D-Welt (`LiveMatchHost`) gezeichnet. Die Fenster, die die
  Befehlsleiste dem Host überlässt (§5): `W3DLeftHUDDraw` ist der Minimaprahmen, seitenverhältniserhaltend zentriert wie in `MatchController.TryMinimapWorld` (§11) – ein Punkt je Objekt, das der Betrachter sieht
  (Teamfarbe; die Projektion lässt aus, was Nebel/Schatten verbergen); `Viewport3D` braucht nichts (die Welt liegt schon dahinter); jeder andere Rückruf wird vom Renderer gezählt und bleibt ungezeichnet
  (`ClientRuntime.LastFrame`: gezeichnete Befehle und offen gebliebene Eigenzeichnungen, für Rauchtests).
- **Shell-Ausgaben:** `StartMatchOutput`, `StartMissionOutput`, `ExitRequestedOutput` und die Anzeige-/Einstellungsänderungen werden ausgeführt; ein beendetes Spiel meldet sich genau einmal an die Shell
  zurück (`GameShell.MatchFinished`/`MissionFinished`: Ergebnis-/Abspannbildschirme), danach fällt die Welt weg und die Shell erscheint wieder. Befehlszeile: `--shell` (Standard bei `--content-dir` ohne
  `--match`/`--mission`/`--model-path`), `--match`, `--mission`, `--smoke-test [--frames N --capture <png>]`.
- **Schema-Kunst der Befehlsleiste (Fund beim Verdrahten):** die Befehlsleiste hatte bisher nur die Fensterbaumgrafik. `ControlBarScheme.ini` liefert den eigentlichen Hintergrund: `BuildSchemeBackground`
  (`ControlBarScheme::drawBackground`) legt die `ImagePart`s der Seite des Menschen in Ebenenreihenfolge (Ebene 0 zuletzt, oben) unter den Fensterbaum, jeweils über dieselbe Abbildung wie die Geld-/Energie-
  Rechtecke; ein Bild, das die Tabelle nicht kennt, wird übersprungen. Das Porträtfenster rechts nennt das Bild `HardCodedWithControlBarSchemeIni`, das das Original durch das `RightHUDImage` des Schemas (das
  Seitenlogo) ersetzt (`ResolveHardCodedImages`, neues Feld `ControlBarSchemeInfo.RightHudImage`). Das hat die HUD-Zeichenliste geändert: der Golden-Hash `hud-drawlist-1280x720.sha256` wurde bewusst neu
  erzeugt (Test `HudTests`).
- **Grenzen:** Schriftmetrik und echte Glyphen kommen weiter aus `ShellTextMeasure` (synthetisch, `docs/GAME-SHELL.md` §10); Video und die Tonausgabe der Shell/Missionen sind nicht Teil dieses Pakets (die Ausgabe `Audio/AudioOutput` ist unverändert);
  die Verdrahtung ist headless per Rauchtest geprüft, nicht von einem Menschen gespielt.
- **Bildprobe (2. Oktober 2026):** `--smoke-test --mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 60 --capture <png>` zeigt über der Welt die Minimap mit Teampunkten im Rahmen, die Befehlsleiste mit Geldanzeige, Fortschrittsbalken und
  leerem Befehlsraster sowie den Sprach-Untertitel der Mission (`(speech: Taunts_Toxin102)`); das Porträtfenster rechts bleibt leer. Die Shell (`--shell`) zeichnet 172 Befehle ohne fehlende Bilder/Texturen/Beschriftungen/Glyphen.

## 16. Phase 16: Mauszeiger, Wegpunkte und Befehlsmarken

Befund aus der ersten Tech-Demo (`docs/DEMO-FINDINGS.md` Punkt 8): der Client zeigte den Pfeil des Betriebssystems, Alt änderte nichts Sichtbares, gesetzte Wegpunkte waren unsichtbar. Die Regeln (welcher Cursor, welche
Marken) stehen in `docs/PRESENTATION-INPUT.md` §11/§12; dieser Abschnitt beschreibt, was das HUD und der Client daraus machen. Nichts davon schreibt in die Simulation: Cursor und Marken lesen Auswahl, `StandingOrders`
und die Welt; Befehle gehen weiter nur als typisierte Kommandos durch die Senke.

### Cursor im HUD (`MatchController`)

| Mitglied | Bedeutung |
|---|---|
| `Cursor` (`CursorState`) | Der Cursor dieses Frames: im geöffneten Spielmenü der Pfeil, sonst `GameInputController.Cursor(PointerOverWindow)`. |
| `PointerOverWindow` | Zeiger über Befehlsleiste, Minimap oder Menü, oder ein dort begonnener Druck wird noch gehalten (`UiWindowTree.InputTarget`, `TryMinimapWorld`). Dann gilt der Pfeil (`InGameUI`: `underWindow`). |
| `PointerPosition` | Zeigerposition in Fensterpixeln, `null` vor dem ersten Mausereignis. |

`Update` liest den Kontext unter dem ruhenden Zeiger in jedem Frame neu (`GameInputController.RefreshHint`), solange der Zeiger über der Welt steht: die Welt bewegt sich unter ihm, und die Auswahl kann sich ohne
Eingabeereignis ändern. Für Missionen gilt dasselbe über `MissionController.Hud`.

### Software-Cursor im Client (`CnCNew.Client.Stride/Ui/SoftwareCursor.cs`)

Das Original lässt Windows die ANI-Dateien zeichnen (`Win32Mouse::setCursor`, `LoadCursorFromFile`). Der Client zeichnet dieselben Dateien selbst als letztes Sprite über der Zeichenliste (`ClientRuntime.Draw`), damit der
Cursor in Captures erscheint und nicht vom Fenstersystem abhängt: `GameData.LoadCursors()` dekodiert die Cursor über `CnCNew.Content`, je Frame entsteht bei Bedarf eine Textur (`R8G8B8A8_UNorm` wie alle UI-Texturen),
gezeichnet 1:1 mit dem Hotspot der Datei auf der Zeigerposition. Die Animationsuhr (`CursorAnimator`) beginnt bei jedem Cursorwechsel neu.

* Der Betriebssystem-Cursor wird versteckt (`Game.IsMouseVisible = false`), solange der Software-Cursor verfügbar ist. Lässt sich der Pfeil aus `Mouse.ini` nicht laden, bleibt der Betriebssystem-Cursor sichtbar
  und der Client schreibt **eine** Warnung auf die Konsole. Außerhalb des Fensters zeichnet der Client nichts.
* Shell und Spielmenü zeigen den Pfeil; ein laufendes Spiel den Cursor des HUD.
* Ein Cursor ohne Bild wird nicht ersetzt: `Build`/`InvalidBuild` haben in den Retaildaten keinen `MouseCursor`-Block, beim Platzieren ist also wie im Original kein Cursor zu sehen (die Bauvorschau steht unter dem
  Zeiger). Ein definierter Cursor, dessen Datei fehlt oder nicht dekodierbar ist, wird je Art gezählt (`unresolved` in der Berichtszeile).
* `--smoke-test` läuft weiter ohne Geräteeingabe und druckt eine Zeile `cursor: software; 37 definitions, issues [], 34 files loaded, 235 frames, 0 inverting pixels, missing files [], undecodable [], engine cursors
  without a definition [Build, InvalidBuild]; N frames drawn, …; current <Name> (<Datei> frame i/n, hot spot x,y)` (gemessen 4. Oktober 2026; ohne Zeiger: `current none (no pointer position yet)`).
* Capture-Hilfen eines Festschritt-Laufs: `--cursor-at x,y` setzt den Zeiger über ein synthetisches Mausereignis je Frame (derselbe Ereignisweg wie eine echte Maus); `--waypoints "dx,dy;dx,dy;…"` (mit
  `--select-own`) hält Alt und ordnet je Versatz (Meter ab der ersten beweglichen ausgewählten Einheit) einen Wegpunkt über die Eingabeschicht an.

### Wegpunktpfad und Befehlsmarken (`HudOverlayRenderer`)

| Marke | Zeichnung | Original |
|---|---|---|
| Wegpunktpfad | Linie Einheit → Station 1 → … in (64, 128, 255), 2 px; je Station eine Raute mit Mittelring. Drei Wegpunkte = drei Marken, das Segment ab der Einheit plus zwei zwischen den Stationen. | `W3DWaypointBuffer::drawWaypoints`: Linie in (0,25; 0,5; 1,0), additiv, 1,5 breit, mit `EXLaser.tga`; je Station das Modell `SCMNode`. Der Client hat noch keine 3D-Markenmodelle, die Station ist deshalb eine 2D-Raute im Overlay. |
| Befehlsmarke Bewegen / Wegpunkt / Sammelpunkt / Betreten | Ring, der sich von 20 px auf 6 px Radius zusammenzieht und dabei verblasst, mit Mittelkreuz; Farbe wie die jeweilige Linie. | `W3DInGameUI::drawMoveHints`: das Modell `MoveHintName` spielt seine Animation einmal und verschwindet nach 40 Frames. |
| Befehlsmarke Angriff | Vier rote Eckwinkel, die sich um das Ziel schließen; die Marke folgt dem Ziel, solange es existiert. | `drawAttackHints` ist im Original leer; das Ziel blinkt dort (`SelectionFlash`, schon vorhanden). Die Marke ist eine Ergänzung. |
| Sammelpunkt | unverändert die gelbe Fahne am Sammelpunkt des ausgewählten Produktionsgebäudes. | `W3DWaypointBuffer` zeichnet außerhalb des Wegpunktmodus die Sammelpunktlinie der Auswahl. |

Bodenpunkte (Stationen, Sammelpunkt, Marken, Befehlslinien) werden jetzt auf Geländehöhe projiziert (`LiveGame.Heights`), vorher auf z = 0 – auf einer hügeligen Karte lag die Fahne sonst neben dem Punkt. Ein Punkt
hinter der Kamera beendet den Pfad an dieser Stelle.

**Sichtbarkeit wie im Original:** der Wegpunktpfad erscheint nur für ausgewählte Einheiten und nur, solange Alt gehalten wird; lässt der Spieler Alt los, läuft die Einheit den Pfad weiter ab, er wird aber nicht mehr
gezeichnet. Eine erreichte Station verschwindet aus dem Pfad, ein anderer Befehl (oder der Tod der Einheit) entfernt ihn ganz.

### Tests und Bildproben

* `tests/CnCNew.Game.Tests/HudTests.cs` „Cursor and markers“ (Realdaten, Alpine Assault): Pfeil ohne Zeiger; `Normal` ohne Auswahl; `Move` mit ausgewählter Planierraupe; Alt schaltet ohne Mausbewegung auf
  `Waypoint`; drei Alt-Klicks ergeben einen Pfad mit vier Punkten, dessen Stationen höchstens 2,5 px neben den Klickpunkten liegen, 33 blaue Linien (3 Segmente + 3 × 10 Markenlinien) und drei Marken, die nach 40
  Frames weg sind; Alt los: Pfad verborgen; ein einfacher Bewegungsbefehl ersetzt den Pfad und hinterlässt eine Marke, die mit der Zeit verblasst; über der Befehlsleiste der Pfeil; am rechten Bildrand der
  Scrollcursor Richtung 0, für den `GameData.LoadCursors()` das Bild `SCCScroll0.ani` hat.
* `scripts/capture-world.ps1`: Szenen `match-cursor-move` und `match-waypoints`. Beide Bilder wurden headless erzeugt und angesehen (4. Oktober 2026): der Bewegungscursor (vier grüne Pfeile nach innen) steht
  über dem Boden, der Auswahlcursor über der Kommandozentrale, die Scrollpfeile zeigen am rechten und unteren Rand nach rechts bzw. unten; die blaue Wegpunktlinie führt von der Planierraupe über drei Rauten,
  um jede Station liegt der verblassende Ring. Zwei Läufe der Wegpunktszene lieferten dieselbe Prüfsumme.

### Nicht belegt, nicht getan

* **Kein Mensch hat Cursor und Marken im laufenden Client gesehen** (Wirkung, Größe, Lesbarkeit, Verzögerung des Software-Cursors gegenüber der echten Maus, Verhalten beim Verlassen des Fensters und im Vollbild).
* Der Radius-Cursor der Spezialkräfte (`RadiusCursorType`, die Wirkkreis-Decals) und der Cursor-Text (`CursorText`) werden nicht gezeichnet.
* 3D-Marken (`SCMNode`, das Bewegungsmarken-Modell) fehlen; die Marken sind 2D-Ersatz im Overlay.
* Die Einnahme-, Hack-, Entführungs- und Scharfschützen-Cursor kommen nur über Befehlsknöpfe, die die Eingabeschicht noch als „nicht unterstützt“ führt (`docs/PRESENTATION-INPUT.md` §6); ihre Bilder sind geladen,
  gezeigt werden sie heute nur von Knöpfen mit Zielmodus.

## 17. Phase 16: Eingabe-Wiedergabe im Client (`--input-script`) und der Befund „Dozer baut nicht“

### 17.1 Die Testeinrichtung

`src/CnCNew.Client.Stride/Testing/InputScript.cs` spielt eine Folge von Zeiger- und Tastenereignissen **an der Stelle ein, an der sonst `StrideInputTranslator.Poll` in `ClientRuntime.Update`
mündet**. Ein Skript läuft damit durch den echten Clientweg: das HUD ist für den echten Backbuffer gelegt, die Textmaße kommen vom Renderer, pro Frame gibt es ein Update und eine Zeichenliste –
nichts davon sieht ein Headless-Test des `MatchController`. Die Einrichtung schreibt nie in die Simulation: alles ist ein `InputEvent`; die einzige Ausnahme `look` bewegt nur die Kamera.

Aufruf (nur mit festen Zeitschritten): `--smoke-test --frames N --input-script <Datei>` zusammen mit `--match`, `--mission` oder `--scenario`.

Format: eine Zeile je Schritt, `#` beginnt einen Kommentar, `FRAME VERB ARGUMENTE` (Frames zählen die Updates der Laufzeit ab 1).

| Verb | Bedeutung |
| --- | --- |
| `move ZIEL` | Zeiger auf das Ziel |
| `down [left\|right\|middle] [ZIEL]`, `up […]` | Taste drücken/loslassen (ohne Ziel an der aktuellen Zeigerposition) |
| `click [left\|right\|middle] ZIEL` | Bewegen und Drücken in diesem Frame, Loslassen zwei Frames später auf demselben Pixel |
| `key down\|up KEYCODE` | Taste (`KeyCode`-Name) |
| `look ZIEL` | Kamera auf das Ziel zentrieren (Testhilfe, keine Eingabe) |

Ziele werden im Frame ihrer Verwendung aufgelöst: `screen:X,Y`; `own:KINDOF[#N]` und `template:NAME[#N]` (das N-te eigene lebende Objekt nach Id, an seinem Auswahl-Mittelpunkt);
`button:FENSTER` (Mitte eines HUD-Fensters, z. B. `ControlBar.wnd:ButtonOptions`); `command:ART[:OBJEKT]` (der erste freigeschaltete Befehlsleisten-Platz dieser `GuiCommandKind`);
`ground:X,Y`; `ground:+DX,+DY from OBJEKT`; `placement from OBJEKT` (die erste Stelle um das Objekt, an der die anstehende Platzierung angenommen würde). Wie beim echten Gerät gibt es **eine**
Zeigerposition je Frame: zuerst ein Bewegungsereignis, wenn sie sich änderte, dann die Tastenereignisse des Frames mit dieser Position.

Bericht am Ende (Konsole, Präfix `input-script:`): jeder Schritt mit aufgelöstem Pixel und Ziel, jede Auswahländerung, jeder Wechsel des Befehlsmodus und der Bauvorschau (gültig/ungültig mit
Grund, gezeichnet oder nicht), jeder abgesetzte Befehl mit Antwort der Simulation (`LiveGame.CommandIssued`: angenommen oder Ablehnungsgrund), ungültige Klicks, entstandene und fertige
Baustellen, der Zustand am Ende und eine Summenzeile (`input-script summary: steps=… events=… unresolved=… selectionChanges=… commandsAccepted=… commandsRejected=… invalidOrders=… sitesCreated=…
sitesCompleted=…`). Ein Ziel, das sich nicht auflösen lässt, wird gemeldet und gezählt, nie still übergangen; der Prozess endet dann mit Exitcode 3.

Regressionsprüfung: `scripts/check-dozer-build.ps1 [-Quick] [-NoBuild]` (Skripte unter `scripts/input/`) fährt den Release-Client fünfmal, jeweils zeitbegrenzt, und prüft die Summenzeile:
Mission `GC_ChemGeneral` und Gefecht `Alpine Assault` je in 1280x720 und 1920x1080 (Gebäude platziert und fertig gebaut) sowie Klicks während der Eröffnungssequenz der Mission (müssen
verworfen werden, die Sperre muss enden). Gemessen 4. Oktober 2026: 5 von 5 bestanden.

### 17.2 Befund: „im Mission-Demo kann ich keine Gebäude mit dem Dozer bauen“

Mit der Wiedergabe „Dozer wählen → Bauknopf → Zeiger über den Boden → Klick“ im echten Client gemessen:

- **Nach der Eröffnungssequenz funktioniert der Bau** in `--mission GC_ChemGeneral` und in `--match Alpine Assault`, jeweils in 1280x720 und 1920x1080: Auswahl, Knopf
  (`ButtonCommand01` bei 445,617 59x47 in 1280x720), Platzierungsmodus, gültige Vorschau, `ConstructCommand` angenommen, Baustelle entsteht, Dozer baut fertig (407 Ticks). UI-Skalierung,
  Renderer-Textmaß und die Update-/Zeichenreihenfolge des Clients sind also nicht die Ursache.
- **In den ersten 25 Sekunden der Mission scheiterte er**: dieselbe Folge ab Frame 40 ergab im alten Client eine gültige Vorschau bei Frame 60 und zehn Frames später am selben Pixel
  `BlockedFootprint` – der Klick wurde als „Cannot do that“ abgewiesen und der Platzierungsmodus blieb stehen. Ursache: `GC_ChemGeneral` beginnt mit einer Kamerafahrt über die gegnerische Basis
  (Tick 0–750: `CAMERA_LETTERBOX_BEGIN`, `DISABLE_INPUT`, `SETUP_CAMERA`, `MOVE_CAMERA_TO` bei Tick 60 und 330, `RESET_CAMERA` bei 681, `ENABLE_INPUT` bei 750). Der Client hat davon nur die
  Kameraziele übernommen – als harten Sprung –, aber weder die Eingabesperre noch die Letterbox: die Befehlsleiste blieb bedienbar, während das Skript die Kamera unter dem Zeiger wegzog. Wer
  in dieser Zeit baute, klickte auf eine andere Weltstelle als die, die er sah.
- **Behoben** (§7a in `docs/SCRIPT-ENGINE.md`): die Skriptkamera fährt jetzt über die Dauer des Skripts, `DISABLE_INPUT`/`ENABLE_INPUT` sperren die Welteingabe (`MatchController.WorldInputEnabled`;
  wie `ScriptActions::doDisableInput` werden Auswahl, Befehlsmodi und gehaltene Tasten gelöscht, das Spielmenü bleibt mit Esc erreichbar), die Letterbox blendet die Befehlsleiste aus und zeigt
  Balken mit dem Hinweis, dass das Missionsskript die Eingabe gesperrt hat.
- **Rückmeldung beim Bauen** (Bildprobe, PNGs außerhalb des Repos angesehen): die Bauvorschau war ein dünner Rahmen mit Diagonalen; sie ist jetzt zusätzlich schraffiert
  (`HudOverlayRenderer`, grün = erlaubt, rot = nicht erlaubt). Ein Geistermodell des Gebäudes gibt es weiterhin nicht. Die Baustelle ist während des Baus sichtbar (Zaun und Gerüstteile des
  Modells), der Dozer fährt sichtbar hin.
- **Zusätzlich gehärtet:** `StrideInputTranslator` skaliert die normalisierte Zeigerposition jetzt mit dem Backbuffer statt mit der Fenstergröße; weicht die Fensterfläche vom Backbuffer ab
  (ein 1920x1080-Fenster, das der Desktop nicht fasst; Anzeigeskalierung), trifft ein Klick trotzdem den Knopf unter dem Zeiger. Der Client schreibt beide Größen beim Start
  (`display: back buffer …, window client area …`). Das ließ sich headless nicht nachstellen und ist **nicht** als Ursache belegt.
- **Nicht belegt:** ob der Tester genau in diesen 25 Sekunden gebaut hat. Die Eröffnungssequenz ist der einzige Weg, auf dem die Wiedergabe im echten Client scheiterte; ein Mensch muss im
  laufenden Client bestätigen, dass der Bau jetzt geht (Fenster, echte Maus, echte Frametimes sind headless nicht prüfbar).

### 17.3 Bildprobe der Missionstexte

Die Missionstexte (`docs/TEXT-AUDIT.md` §4.6, `docs/SCRIPT-ENGINE.md` §7a „Bildschirmtexte nach `InGameUI`“) wurden so angesehen: (1) im Client mit
`--smoke-test --mission "Maps/MD_USA01/MD_USA01.map" --content-dir … --size 1280x720 --mute --frames 1500 --filmstrip <Ordner> --every 20` (die Schreibmaschine Bild für Bild: Frame 60 zeigt 19 Zeichen der Titelzeile, Frame 100 die fertige Titelzeile
mit Cursor auf der leeren zweiten Zeile, Frame 160 beide Zeilen) und `MD_CHI02` mit `--frames 2700 --every 90` (der Zeitgeber „Reinforcements Arrive In“ rechts bei 0,90/0,666 neben einer sich tippenden Beschriftung);
(2) in `tools/CnCNew.UiRenderTool` als Szene `missiontext-WxH.png` (800x600, 1280x720, 1920x1080), die alle Texte eines Bildschirms einschließlich Superwaffenliste und Meldungsfenster auf einem Untergrund zeigt – die
Superwaffenliste und die Meldungszeilen kommen in den Kampagnenkarten nicht (oder erst spät) vor und sind sonst nur über Tests zu sehen. **Stolperstein:** die PNGs des Werkzeugs behalten den Alphakanal der Zielfläche, durchscheinende Flächen (Meldungsfenster,
Schatten) erscheinen in manchen Betrachtern zu hell; der Alphakanal ist vor dem Ansehen zu verwerfen. Bewegtbild (Takt der Schreibmaschine, Ausblenden) und das echte Clientfenster sind damit **nicht** belegt.
## 18. Phase 16: Vorschaumodell beim Platzieren

Bisher zeigte die Platzierung eines Gebäudes nur die schraffierte Grundfläche (`PlacementGhostOverlay`, `HudOverlayRenderer`). Das Original zeigt außerdem das Gebäude selbst durchscheinend am Zeiger
(`InGameUI::placeBuildAvailable` legt ein `Drawable` der Vorlage an, `handleBuildPlacements` setzt Ort und Winkel jeden Frame, `setDrawableOpacity(0.45)`, bei verweigertem Ort `colorTint(rot)`).

**Datenweg** (jede Stufe getestet):
1. `CommandModeController.Ghost` (Präsentation) – Ort, Winkel, gültig oder `PlacementIssue`; der Winkel beginnt beim `PlacementViewAngle` der Vorlage (`docs/PRESENTATION-INPUT.md` §7).
2. `MatchController.Update` (nach `LiveGame.Advance`, vor den Welt-Layern) setzt `LiveGame.PlacementGhost` = Geist im Platzierungsmodus (nicht bei offenem Spielmenü), sonst `null`.
3. `LiveGame.ObjectFrame` = `Frame` plus `PlacementPreview.Create(…)` des Geistes (`DrawableInstance` mit `Opacity` 0,45, `FlashTint` rot bei verweigertem Ort, `SceneryIndex = PlacementPreviewIndex`). `Frame` selbst bleibt
   frei davon: Minimap-Punkte, Heilsymbole und Auswahl lesen es und sehen die Vorschau nie.
4. `LiveMatchHost` gibt dem Objekt-Layer `ObjectFrame`; der Layer zeichnet `Opacity < 1` mit durchscheinenden Materialvarianten (`docs/CLIENT-OBJECTS.md`, „Durchscheinende Objekte“).

**Tests:** `PlacementPreviewTests` (Präsentation, 5 Tests: Winkel im Katalog, gültiger/verweigerter Geist, nichts zu zeigen, Zustandsauswahl), `InputTests` „a placement starts at the template's PlacementViewAngle …“,
`HudTests` „Placement preview …“ (echte Daten, Dozer, Bauknopf, Hover: `ObjectFrame` hat genau ein Objekt mehr als `Frame`, `Opacity` 0,45, rot auf der eigenen Basisstruktur, nach dem Klick weg).

**Sichtprobe** (Client, `--smoke-test --match "Maps/Alpine Assault/Alpine Assault.map" --size 1280x720 --frames 100 --capture …`, Skripte `scripts/input/placement-preview.txt` und `placement-refused.txt`, PNGs
außerhalb des Repos angesehen): Dozer auswählen, Kraftwerk-Knopf, Zeiger neben den Dozer: in der grünen Grundfläche steht das Kraftwerk als blasses, durchscheinendes Modell; auf der eigenen Basisstruktur
(`BlockedFootprint`) ist es rötlich getönt, die Grundfläche rot. Objekt-Layer-Zeile: `translucent=1` (ein durchscheinender Teil), `tinted=1` bei verweigertem Ort.

**Nicht umgesetzt:** Linienbau (Mauern: `m_placeIcon[1..]`, `buildTiledLocations` – es gibt ein Modell, keine Kette), das Fraktions-Bib-Decal unter einem verweigerten Ort (`addFactionBibDrawable`), die Spielerfarbe
der Anzeige bei Nacht (`setIndicatorColor` mit Nachtfarbe), ein Schatten des Vorschaumodells. Die schraffierte Grundfläche bleibt als Hilfe (das Original zeichnet keine).

**Nicht belegt:** wie das Vorschaumodell im laufenden Fenster aussieht (Lesbarkeit bei 0,45 gegen helles Gelände, Rottönung bei dunklen Texturen, Schatten- und Sortierreihenfolge mit den anderen
durchscheinenden Objekten) – gesehen sind zwei Standbilder des Headless-Laufs; die Rottönung ist ein Faktor auf die Oberflächenfarbe (`FlashTint.Gain`), nicht die Licht-Addition des Originals.

## 19. Phase 21: Zeit und Tickrate

Das HUD nimmt nirgends mehr an, dass ein Tick 1/30 s dauert. Die Werte aus `InGameUI.ini` sind in **Originalbildern** (1/30 s; `InGameUiSettings` rechnet mit `CnCNew.Data.OriginalFrame.PerSecond`, die eigene Konstante `LogicFramesPerSecond` gibt es nicht mehr); verglichen werden sie mit der Spieluhr in Originalbildern (`LiveGame.Clock.OriginalFrames`), nie mit Simulationsticks. Bei 30 Hz sind alle 54 Screen-Hashes, der HUD-Drawlist-Hash und die 164 Dateien des `UiRenderTool` unverändert (`docs/TICKRATE-MIGRATION.md` §14.2).

| Was | Vorher | Jetzt |
|---|---|---|
| Spielschleife (`LiveGame.Advance`) | fester Schritt `1.0 / 30`, höchstens 8 Ticks je Bild | Schritt `1.0 / world.Time.TicksPerSecond`; Aufholgrenze als Zeitbudget von 8 Originalbildern (0,27 s Spielzeit, bei 60 Hz 16 Ticks) |
| Spieluhr | – | `LiveGame.Clock` (`GameClock`: Tick, Rate, Interpolationsanteil; ganzzahlig geführt, steht bei Pause, läuft mit der Spielgeschwindigkeit) |
| Meldungsliste (`HudMessageFeed.Clock`, `UiMessageLog.Update`) | Stempel und ein Update je Tick | Stempel in Originalbildern, ein Update je Originalbild (`MatchController.OnTickStepped` nur auf Bildgrenzen) |
| Unter-Beschuss-Zeile | `30 * 10` Ticks | `OriginalFrame.PerSecond * 10` Originalbilder, in Ticks der Welt verglichen |
| Superwaffenliste | Restzeit `(readyFrame - Tick) / 30`, Blinken `tick / flashFrames` | Restzeit `world.Time.ToWholeSeconds`, Blinken auf der Originalbild-Uhr |
| Benannte Zähler der Mission | Timerwert der Skript-VM (Ticks) durch 30 | der Director rechnet den Timerwert in Originalbilder um (`CounterForDisplay`), `MissionTextOverlay.NamedTimers` teilt durch `OriginalFrame.PerSecond` |
| Nebeldarstellung | alle `SimulationWorld.VisionIntervalTicks` Ticks | alle `world.VisionInterval` Ticks (die Konstante ist entfernt) |
| Daueraufträge (`StandingOrders.Update`) | je Tick geprüft, je Tick ein Feuerbefehl | je Originalbild (auf den Ticks, die eines beginnen): die Befehle, die ein Dauerauftrag erzeugt – und damit der Replay-Inhalt – folgen der Spielzeit; bei 30 Hz dieselben Befehle wie zuvor |
| Befehlsmarken, Tooltip, Mauszeiger, Kamera | Wandzeit mit einer eigenen 30 | Wandzeit, die 30 ist `OriginalFrame.PerSecond` (`OrderHintBook`, `CameraSettings.FramesPerSecond`, `RtsCamera`) |

Rauchtest und Eingabeskripte (§17): ein gezeichnetes Bild ist ein Originalbild Spielzeit bei jeder Rate; die Bildnummern eines Skripts bleiben gültig, wenn der Client mit `--tick-rate 60` läuft.

**Offen:** die Spielstandslisten (`MissionPauseMenu`, `LoadGameScreen`) zeigen weiter den rohen Tick („tick N“), und `SaveGame` (Version 1) schreibt keine Rate; ein Spielstand einer anderen Rate wird über den Definitions-Fingerabdruck (der die Rate enthält) und den eingebetteten Missionszustand abgelehnt, nicht über ein eigenes Feld. **Nicht belegt:** Aussehen und Bedienung bei 60 Hz – kein Mensch hat es gesehen.
