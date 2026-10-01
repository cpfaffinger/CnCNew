# Eingabe, Auswahl, Befehlsmodi und Hotkeys (Paket C3)

Dieses Dokument beschreibt `src/CnCNew.Presentation/Input/**`: die renderer-neutrale Schicht zwischen den Geräteereignissen des
Stride-Clients und den typisierten Simulationsbefehlen. Sie liest die `SimulationWorld` (nur lesend) und schreibt ausschließlich über
einen `ICommandSink` (`Issue(Func<ulong sequence, long tick, SimCommand>)`); der Live-Adapter `SkirmishSessionCommandSink` ruft
`SkirmishMatchSession.Issue` auf, sodass jede Eingabe als Befehl im Replay landet. Stride-Typen kommen nicht vor; die Kamera wird nur über
`Camera/IViewProjection` (Paket C1) angesprochen. Gleitkomma ist hier erlaubt; Weltpositionen werden erst beim Erzeugen eines Befehls
mit `SimulationQueries.ToFixed` (Rundung auf den nächsten Rohwert, 16 Nachkommabits) in Festkomma umgesetzt.

Referenzquellen (gelesen, nicht kopiert; Pfade relativ zu `references/generals-game-code`): `Core/.../MessageStream/SelectionXlat.cpp`,
`CommandXlat.cpp`, `GUICommandTranslator.cpp`, `PlaceEventTranslator.cpp`, `HotKey.cpp`, `MetaEvent.cpp`, `LookAtXlat.cpp`,
`Core/.../GUI/ControlBar/ControlBar.cpp`, `ControlBarCommand.cpp`, `ControlBarMultiSelect.cpp`, `ControlBarCommandProcessing.cpp`,
`Core/GameEngine/Include/GameClient/MetaEvent.h`, `ControlBar.h`, `GeneralsMD/.../GameClient/InGameUI.cpp`,
`GeneralsMD/.../Common/System/BuildAssistant.cpp`, `GeneralsMD/.../GameLogic/AI/AIGroup.cpp`, `GeneralsMD/.../Common/RTS/Player.cpp`.

## 1. Einstiegspunkte für die Integration

| Typ | Aufgabe |
|---|---|
| `GameInputController` | Pipeline: `Process(InputEvent, world, view)` pro Geräteereignis, `Tick(world)` einmal pro Logiktick **vor** `Step`, `UpdateCamera(view, dt)` pro Frame, `BuildCommandBar(world)`, Ereignis `UiRequest(MetaAction)` für Menü/Pause/Chat/Screenshot usw., `Hint` (Cursor und Aktion unter der Maus). |
| `InputState`, `InputEvent`-Records | Rohereignisse (Maus, Rad, Tasten mit `KeyCode`, Text, Fokusverlust) mit vom Aufrufer gelieferter Millisekundenuhr. |
| `HotkeyMap` | `CommandMap.ini` aus Text (`Parse`) oder Definitionsgraph (`FromGraph`), `WithEngineDefaults`, `Rebind`, `Unbind`, `ToSettings`/`ApplySettings`, `Conflicts`, `Issues`, `Compatibility`. |
| `SelectionModel` | Auswahl, Gruppen, `SelectionChanged`, `Prune`. |
| `CommandModeController` | Kontextbefehle, Modi, Platzierungsgeist (`Ghost`), `Cursor`, `DrainFeedback()`, `Orders` (stehende Befehle). |
| `CommandBarModel`, `CommandButtonCatalog` | Befehlsleiste als Daten, Aktivierung per Slot oder Hotkey, `Hud(world, player)`. |
| `CameraInputController` | `CameraIntent` (Scroll, Drehung, Zoom, Zentrieren, Reset). |
| `ICommandSink`, `SkirmishSessionCommandSink` | Befehlsausgang. |

Die Übersetzungsreihenfolge folgt `GameClient.cpp` (Meta-Tasten 20, Control-Bar-Hotkeys 25, Platzierung 30, GUI-Befehl 40, Auswahl 50,
Kamera 60, Kontextbefehle 70).

## 2. Ereignismodell

`InputState.Apply` liefert Gesten (`PointerGesture`): `Click`, `DoubleClick`, `DragStarted`, `DragMoved`, `DragEnded`, `DragCancelled`.

| Regel | Wert | Quelle |
|---|---|---|
| Ein Druck wird zum Ziehen, sobald eine Achse **mehr als** 25 px abweicht | `DragTolerance = 25` | `Mouse.ini`, `SelectionXlat.cpp` (Ziehbeginn), `MetaEvent.cpp onMouseEvent` (Punktregion) |
| Rechtsklick nur bis 250 ms Haltedauer | `DragToleranceMS = 250` | `Mouse.ini`, `Mouse::isClick` |
| Mittelklick (Kamera-Reset) bis 167 ms, höchstens 5 px Weg | – | `LookAtXlat.cpp` |
| Doppelklick: zweiter Druck höchstens 500 ms nach dem ersten Klick und höchstens 4 px entfernt; ein Doppelklick verbraucht das Paar | Windows-Standard | `WM_LBUTTONDBLCLK` (`Win32Mouse.cpp`) |
| Fokusverlust: alle Tasten/Knöpfe los, laufendes Ziehen → `DragCancelled`, Modifikator-Metas beendet | – | eigene Festlegung |

Modifikatoren (`KeyModifiers`) ergeben sich aus gehaltenen Modifikatortasten; links und rechts sind gleichwertig (`MappableKeyModState`).

## 3. Tastatur: `CommandMap.ini` und `HotkeyMap`

* **Repräsentation im Graphen:** `CommandMap`-Blöcke sind für den Compiler unbekannte Top-Level-Typen und liegen als
  `CompatibilityDefinition` mit Id `commandmap/NAME` und Originaltext der Felder vor. `HotkeyMap.FromGraph` liest diese, `HotkeyMap.Parse`
  liest Rohtext mit einem kleinen eigenen Parser (Blöcke `CommandMap NAME` … `End`, Kommentare mit `;`).
* **Felder** (`TheMetaMapFieldParseTable`): `Key` (`KeyNames`), `Transition` (`DOWN`/`UP`/`DOUBLEDOWN`), `Modifiers` (`NONE`, `CTRL`, `ALT`,
  `SHIFT`, `CTRL_ALT`, `SHIFT_CTRL`, `SHIFT_ALT`, `SHIFT_ALT_CTRL`), `UseableIn` (Bitliste `SHELL GAME OBSERVER`), `Category`
  (`CategoryListName`), `DisplayName`, `Description` (String-Labels, die UI übersetzt). Namensvergleiche ignorieren Groß-/Kleinschreibung wie
  `INI::scanLookupList`. Die letzte Definition einer Aktion gewinnt (`MetaMap::parseMetaMap`).
* **Nie still:** Unbekannte Aktionsnamen (auch Debug-/Cheat-Namen), Tasten, Übergänge, Modifikatoren, Kontexte, Kategorien, Felder, fehlendes
  `Key` und Syntaxfehler landen in `Issues`; der betroffene Eintrag wird verworfen. Die Schreibweise `Feld Wert` ohne `=` ist im Original
  gültig (der Tokenizer behandelt `=` als Trenner, `INI::initFromINIMulti`) und wird akzeptiert, aber in `Compatibility` gezählt.
* **Konflikte:** gleiche Taste, gleicher Modifikatorzustand, gleicher Übergang und überlappende Kontexte. Sie werden in `Conflicts`
  gemeldet und nie aufgelöst: `Resolve` liefert – wie der Originalschleifendurchlauf – alle passenden Aktionen in Aktionsreihenfolge.
* **Dispatch** (`GameInputController.OnKey`, nach `MetaEventTranslator::onKeyPressed`/`onKeyModStateRemoved`): Ein Modifikator allein ist die
  Bindung `KEY_NONE` + Zustand (so beginnen `BEGIN_FORCEATTACK`/`WAYPOINTS`/`PREFER_SELECTION`). Beim Loslassen feuert jede nicht mehr
  gedeckte Kombination ihre `UP`-Bindung, unabhängig von der Loslassreihenfolge. Folge der Originalsemantik: Shift unter gehaltenem Ctrl ist
  der Zustand `SHIFT_CTRL`, für den nichts gebunden ist. Autorepeat löst keine Metas aus. `DOUBLEDOWN` wird geparst, feuert aber nie (im
  Original auskommentiert).
* **Control-Bar-Hotkeys** (`HotKey.cpp`): Das Zeichen nach dem ersten `&` im übersetzten `TextLabel`, kleingeschrieben; ausgelöst beim
  Loslassen ohne Modifikator; eine schon vergebene Taste wird für spätere Slots ignoriert (`addHotKey`); ein sichtbarer, aber gesperrter
  Knopf wird nicht ausgelöst (`HotkeyActivation.Disabled`). Die Übersetzung liefert der Aufrufer als `Func<string, string?>`, damit keine
  Abhängigkeit zur UI entsteht. **Abweichung:** Hat das Drücken derselben Taste schon ein Meta ausgelöst (z. B. `S` = STOP), wird der
  Control-Bar-Hotkey beim Loslassen unterdrückt; das Original würde beide auslösen und doppelte Befehle erzeugen.
* **Engine-Aktionen** (`MetaAction.CameraScrollUp` … `Guard`, Settings-Namen `ENGINE_*`): Das Original liest die Pfeiltasten roh
  (`LookAtXlat.cpp`); `WithEngineDefaults` bindet sie deshalb als `Source = Engine`. Angriffsbewegung und Wachen sind im Original
  Control-Bar-Knöpfe (`&A`, `&G` im Label) und bleiben standardmäßig ungebunden, lassen sich aber für die Optionen frei belegen.
* **Einstellungen:** `ToSettings` liefert für jede Aktion `MODIFIKATOREN TASTE ÜBERGANG` (z. B. `CTRL KEY_1 DOWN`) oder `UNBOUND`, kanonisch
  sortiert; `ApplySettings` meldet unbekannte Aktionen und unlesbare Werte als `Issues` und lässt die Bindung dann unverändert.

## 4. Kamera-Eingabe

`CameraInputController.Update(input, hotkeys, viewport, dt)` ist eine reine Funktion der Eingabe plus einmaliger Anforderungen.

| Eingabe | Wirkung | Wert/Quelle |
|---|---|---|
| Pfeiltasten (Engine-Bindung, Modifikator muss exakt passen: Strg+Pfeil wählt Einheiten) | `Scroll`, pro Sekunde Richtung × Horizontal-/Vertikalfaktor × Tastaturfaktor | `GameData.ini`: 1,6 / 2,0 / 2,0 |
| Cursor im 3-px-Randstreifen (abschaltbar, nicht während Tastaturscroll) | wie Tasten | `edgeScrollSize = 3` (`LookAtXlat.cpp`) |
| Rechts ziehen | Scroll proportional zum Abstand vom Anker, auf halbe Bildschirmgröße begrenzt | `LookAtXlat.cpp`, `RightMouseAlwaysScrolls` |
| Mittel ziehen | Drehung 0,01 rad pro Pixel | `LookAtXlat.cpp` |
| Mittelklick, `CAMERA_RESET` | `Reset` | `LookAtXlat.cpp` |
| Rad | Zoom −10 Höheneinheiten pro Raste | `ZoomHeightPerSecond` |
| `BEGIN/END_CAMERA_ROTATE_*`, `…_ZOOM_*` | 0,1 rad bzw. 10 Einheiten pro Logikframe (30/s) | `KeyboardCameraRotateSpeed`, `InGameUI.cpp` |
| Gruppen-Doppeldruck, `VIEW_TEAMn`, nächste Einheit, `VIEW_COMMAND_CENTER` | `CenterOn` (Weltposition x, y) | `SelectionXlat.cpp`, `CommandXlat.cpp` |

`Scroll` ist kamerarelativ und dimensionslos (1 = Grundgeschwindigkeit der Kamera für eine Sekunde); die Umrechnung in Meter übernimmt der
Kamerabesitzer (C1).

## 5. Auswahl

### Pick-Regeln (`EntityPicker`)

Das Original schneidet einen Strahl mit der gerenderten Geometrie (`W3DView::pickDrawable`). Diese Schicht kennt keine Meshes, daher:

1. **Wählbar** (`Object::isSelectable` + `CanSelectDrawable`): `ALWAYS_SELECTABLE`, oder `SELECTABLE` ohne Status `UNSELECTABLE`/`MASKED`, nicht
   tot, nicht in Entfernung begriffen, nicht in einem Container.
2. **Sichtbar** für den lokalen Spieler (`SimulationWorld.CanPlayerSee`: Verbündete immer, sonst sichtbare Zelle und kein unentdecktes Stealth).
3. **Unter dem Cursor**, wenn der Cursor im projizierten Begrenzungskreis liegt: Radius = größter Bildschirmabstand der projizierten Punkte
   Mittelpunkt + (r,0,0), (0,r,0), (0,0,h/2) mit r = `BoundingCircleRadius`, mindestens 8 px. Projiziert wird der Geometriemittelpunkt
   (`Z + CenterZDelta`).
4. **Vorrang:** kleinste Tiefe entlang des Sichtstrahls (der Strahl trifft das vordere Objekt zuerst), dann kleinerer Bildschirmabstand, dann
   kleinere Id.
5. **Box:** alle wählbaren, sichtbaren Objekte, deren projizierter Mittelpunkt im Rechteck liegt (wie `iterateDrawablesInRegion`), nach Id.

### Auswahlregeln (`SelectionModel.SelectFromCandidates`, `SelectionTranslator::onMouseLeftClick`)

* Eigene Objekte im Klick/Box werden gewählt, eigene Gebäude aber nur, wenn genau ein Gebäude getroffen wurde (bzw. das Gebäude das einzige
  Wählbare ist). Mehrere eigene Gebäude ohne Einheit leeren die Auswahl – eine Eigenheit des Originals, bewusst nachgebildet.
* Ohne eigene Objekte: genau ein Feind, sonst genau ein Neutraler, sonst genau ein Verbündeter wird **zur Information** gewählt
  (`IsCommandable == false`); Feind + Neutral + Verbündet zugleich: nichts.
* Shift (`BEGIN_PREFER_SELECTION`) erweitert, außer die Auswahl enthält Fremdes oder ein eigenes Gebäude; Shift-Klick auf Ausgewähltes
  entfernt es. Eine leere Box ändert nichts.
* Doppelklick auf eine eigene, massenwählbare Einheit (nicht Gebäude): alle gleichen Templates auf dem Bildschirm, mit Alt auf der ganzen
  Karte; Shift behält die bisherige massenwählbare Auswahl; mit Strg kein Doppelklick (Strg = Zwangsangriff). Gleichheit ist hier
  Template-Namensgleichheit (das Original nutzt `isEquivalentTo`, das Reskins/Build-Variationen einschließt).
* `SELECT_MATCHING_UNITS` (E): erst Bildschirm, bei null Neuzugängen die Karte. `SELECT_ALL` (Q) / `SELECT_ALL_AIRCRAFT`: ohne `DOZER`,
  `HARVESTER`, `IGNORES_SELECT_ALL` (bzw. nur `AIRCRAFT`); eine Auswahl mit Ausgeschlossenem wird ersetzt, sonst erweitert; Bildschirm,
  sonst Karte. `MaxSelectionSize` (retail 0 = unbegrenzt) gilt nur hier, wie im Original.
* `SELECT_NEXT/PREV_UNIT`, `…_WORKER`: Ringlauf über eigene mobile, nicht enthaltene Objekte in Id-Reihenfolge ab dem ersten Ausgewählten;
  ersetzt die Auswahl und zentriert. Tab-Durchlauf gibt es nicht (Tab ist `DIPLOMACY`).
* **Gruppen** (`onMetaCreateTeam`/`SelectTeam`/`AddTeam`/`ViewTeam`, `Player::processCreateTeamGameMessage`): Strg+N speichert die eigenen
  Ausgewählten und entfernt sie aus allen anderen Gruppen; N wählt die lebenden eigenen Mitglieder; ein zweiter Druck derselben Gruppe
  innerhalb von 500 ms (strikt kleiner) zentriert auf das letzte Mitglied (höchste Id) und wählt nur neu, wenn nicht alle ausgewählt sind;
  Shift+N ergänzt (ein ausgewähltes oder als erstes Mitglied stehendes Gebäude leert vorher); Alt+N zentriert nur.
* **Pruning** (`Prune`, je Tick): entfernt Gelöschte, Tote, Enthaltene, Unwählbare, Verkaufte (`SOLD`) und fremde Objekte, sobald sie nicht
  mehr allein ausgewählt oder nicht mehr sichtbar sind; Gruppen verlieren Tote und Fremdgewordene. Jede Änderung löst `SelectionChanged`
  aus; eine Auswahländerung bricht einen offenen Ziel- oder Platzierungsmodus ab (`ControlBar.cpp`).

## 6. Befehle

### Mausschemata

| | `LeftClickCommands` (Retail-Standard, `m_useAlternateMouse = FALSE`) | `RightClickCommands` („alternative Maus“) |
|---|---|---|
| Linksklick auf Objekt | Befehl, wenn der Kontext einen gültigen Befehl ergibt und Shift nicht gedrückt ist (`contextCommandForNewSelection`), sonst Auswahl | Auswahl |
| Linksklick auf Boden | Kontextbefehl (Bewegen, Sammelpunkt, Wegpunkt …) | alles abwählen |
| Rechtsklick | offenen Modus abbrechen, sonst alles abwählen | offenen Modus abbrechen, sonst Kontextbefehl |
| Linkes Ziehen / rechtes Ziehen | Box / Kamerascroll | Box / Kamerascroll |

Escape (`OPTIONS`) bricht zuerst einen offenen Modus ab und meldet erst danach `UiRequest(Options)` (Abweichung: das Original kennt keinen
Tastaturabbruch).

### Kontextbefehle (`CommandModeController.Evaluate`, Reihenfolge von `evaluateContextCommand`)

| Bedingung (erste passende gewinnt) | `ContextAction` | Cursor (`Mouse.ini`) | Simulationsbefehl(e) |
|---|---|---|---|
| Auswahl leer/fremd | `None`/`Select` | `Normal`/`Select` | – |
| Eigenes Ziel und Shift | `Select` | `Select` | – |
| Alt (`BEGIN_WAYPOINTS`), Boden, bewegliche Einheiten | `AddWaypoint` | `Waypoint` | erster Punkt sofort `SetMoveTargetCommand`, weitere nachgeladen |
| Strg + Objekt mit Körper, bewaffnet | `ForceAttackObject` | `ForceAttackObj` | stehender Angriff → `FireWeaponCommand`/`SetMoveTargetCommand` |
| Strg + Boden | `ForceAttackGround` | `ForceAttackGround` | **keiner** (fehlt in der Simulation), `FeedbackKind.Unsupported` |
| Eigene Baustelle, Bauer ausgewählt | `ResumeConstruction` | `ResumeConstruction` | `ResumeConstructCommand` (erster Bauer) |
| Alle Ausgewählten Versorgungsträger, eigenes Versorgungszentrum oder beliebiges Lager (`SELECTION_ALL`) | `Dock` | `Dock` | `DockCommand` je Einheit |
| Bauer mit Reparaturrate, eigenes beschädigtes fertiges Gebäude | `Repair` | `DoRepair` | `RepairCommand` je Bauer |
| Beschädigte bewegliche Einheiten, eigenes/verbündetes `RepairDockUpdate` | `GetRepaired` | `GetRepaired` | `DockCommand` je beschädigter Einheit |
| Container, `IsValidContainerFor` (Simulation) für mindestens eine Einheit | `Enter` | `EnterFriendly` | `EnterContainerCommand` je gültiger Einheit |
| Feind mit Körper, eine bewaffnete Einheit in Reichweite | `AttackObject` | `AttackObj` | stehender Angriff |
| Feind, bewaffnet, nur nach Bewegung erreichbar | `AttackObjectAfterMoving` | `OutRange` | stehender Angriff (Verfolgung) |
| Feind, nicht angreifbar | `Invalid` | `GenericInvalid` | – |
| Boden, alle Ausgewählten produzieren (`SELECTION_ALL`) | `SetRallyPoint` | `SetRallyPoint` | `SetRallyPointCommand` je Produzent |
| Sonstiges Objekt | `Select` | `Select` | – |
| Boden, bewegliche Einheiten | `Move` | `Move` | `SetMoveTargetCommand` je Einheit, gleiche Zielposition |
| Sonst (z. B. nur Gebäude + Boden) | `Invalid` | `GenericInvalid` | – |

Befehle gehen stets in aufsteigender Id-Reihenfolge hinaus. Nicht nachgebildet: Entführen, Autobombe, Sabotage, Bergen, Gebäude einnehmen,
Hacken (keine Simulationsbefehle), die Formations-/Gruppenversatzlogik von `groupMoveToPosition` (alle Einheiten erhalten denselben Punkt) und
die Prüfung „schneller Pfad vorhanden“ für den Bewegungscursor.

### Modi und Knöpfe (`Activate`, `processCommandUI`)

* Sofort: `UNIT_BUILD` → `QueueProductionCommand`, `PLAYER_UPGRADE`/`OBJECT_UPGRADE` → `PurchaseUpgradeCommand`, `PURCHASE_SCIENCE` → erste nicht
  besessene Wissenschaft der Liste, `SELL` → `SellCommand` je Gebäude, `STOP`, `EVACUATE` → `ExitContainerCommand` je Insasse,
  `DOZER_CONSTRUCT_CANCEL` → `CancelConstructCommand`, `SPECIAL_POWER` ohne Ziel → `UseSpecialPowerCommand(None)`, `SELECT_ALL_UNITS_OF_TYPE`.
  Warteschlangensymbol → `CancelProduction`, Abbrechen einer Baustelle → `CancelConstruction`.
* Zielmodus (`NEED_TARGET_*`): `ATTACK_MOVE`, `GUARD*`, `SET_RALLY_POINT`, Spezialkräfte mit Objekt- oder Positionsziel, `FIRE_WEAPON` mit
  Objektziel. Ein zu den Optionen passendes Objekt (Feind/Neutral/Verbündet, eigene zählen als verbündet) hat Vorrang vor der Position; der
  Cursor ist `CursorName` bzw. `InvalidCursorName`, sonst `Target`/`GenericInvalid`. Rechtsklick/Escape/Auswahlwechsel brechen ab.
* `SPECIAL_POWER_FROM_SHORTCUT`: Quelle ist das eigene Objekt mit dem bereitesten Modul (bereit zuerst, dann kleinste Id;
  `findMostReadyShortcutSpecialPowerOfType`), unabhängig von der Auswahl.
* Nicht unterstützte Knopfarten (Hack, Entführen, Sabotage, Autobombe, Kampfabsprung, Waffenwechsel, Überladung, Leuchtfeuer, Schienentransport,
  Upgrade-Abbruch, Spezialkraft-Bau, `EXIT_CONTAINER`-Inventar, `FIRE_WEAPON` auf Position/Minenräumung) liefern `ActivationResult.Unsupported`,
  zählen in `UnsupportedCounts` und stehen in der Leiste als `Restricted`/`NotSupportedBySimulation`.

### Stehende Befehle (`StandingOrders`)

Die Simulation kennt nur Einzelschritte (bewegen, ein Schuss, andocken). Mehrtick-Befehle des Originals hält die Eingabeschicht daher selbst und
wiederholt die Einzelschritte in `Tick`, wie die Skirmish-KI; jeder wiederholte Befehl geht durch den Sink und steht im Replay.

* **Angriff** (`groupAttackObject`): in Reichweite → `FireWeaponCommand` sobald nachgeladen (mit denselben Vorbedingungen wie die Simulation,
  also ohne Ablehnungen); außer Reichweite → `SetMoveTargetCommand` zum Ziel, neu erst wenn das Ziel sich mehr als 10 m bewegt hat oder die
  Einheit steht; in Reichweite angekommen → Halt an Ort. Endet, wenn das Ziel stirbt oder unsichtbar wird.
* **Angriffsbewegung**: bewegen und auf den nächsten sichtbaren Feind in Waffenreichweite feuern (gleich weit: kleinere Id); endet bei Ankunft.
* **Wachen** (alle drei Guard-Arten gleich, ohne Verfolgung): zur Position, dort auf Feinde in Reichweite feuern.
* **Wegpunkte**: nächster Punkt bei Ankunft.
* **Stop** (`groupIdle`): stehende Befehle löschen und `SetMoveTargetCommand` auf die eigene Position.
* **Streuen** (`AIGroup::groupScatter`): Schwerpunkt der beweglichen Einheiten, fernste zuerst, der Schwerpunkt wandert pro Einheit 0,01 m
  nach Westen; Ziel = Position + Richtung × 4 × Begrenzungsradius.

### Rückmeldungen

`CommandFeedback` (für kurzlebige Anzeigen): `MoveMarker` (40 Client-Frames, `W3DInGameUI::drawMoveHints`), `WaypointMarker`, `RallyMarker`,
`AttackMarker` (am Ziel), `EnterMarker`, `InvalidOrder`, `Unsupported` (mit Bezeichnung) und `Rejected` (mit `CommandRejection` der Simulation).

## 7. Platzierung

`DOZER_CONSTRUCT` wählt den ersten ausgewählten, nicht beschäftigten Bauer und öffnet den Platzierungsmodus. Der Geist folgt dem Cursor;
Linksdruck verankert (außerhalb des Geländes: Abbruch), Ziehen um mindestens 5 px (`PLACEMENT_DRAG_THRESHOLD_DIST`) dreht auf die
Richtung Anker → Cursor (`InGameUI.cpp`), Loslassen platziert: gültig → `ConstructCommand(builder, template, x, y, angle)`, ungültig →
`InvalidOrder` mit Grund, Anker gelöst, Modus bleibt (`PlaceEventTranslator.cpp`). Cursor `Build`/`InvalidBuild` (Engine-Cursor ohne
`MouseCursor`-Block).

`SimulationQueries.CheckPlacement` ist ein Trockenlauf von `SimulationWorld.CheckConstruct` in gleicher Reihenfolge: Bauer verfügbar →
Template bekannt → instanziierbar → baubar (`STRUCTURE`, `Buildable`, Seite) → Voraussetzungen (`Ignore_Prerequisites`) → Geld →
`NavigationGrid.IsFootprintLegal` (Karte, Klippe, Wasser, fremde Grundfläche; **derselbe Codepfad wie die Simulation**) →
`IsPositionExploredBy` (`LBC_SHROUD`). Die übrigen Prüfungen sind aus öffentlichen Daten nachgebaut, weil `CheckConstruct` privat ist.
Nicht geprüft, weil die Simulation sie auch nicht prüft: Bodenunebenheit (`AllowedHeightVariationForBuilding`), Einheiten im Weg,
Abstand zu Versorgungslagern (`SupplyBuildBorder`), Kartenrandabstand, Linienbau (Mauern).

## 8. Befehlsleiste (`CommandBarModel`)

* **Kontext** (`evaluateContextUI`): nichts gewählt → `Empty`; Fremdes → `Foreign`; alles verkauft/`IGNORED_IN_GUI` → `BeingSold`; eine
  Baustelle → `UnderConstruction` (nur Abbrechen); kein Befehlssatz → `NoCommandSet`; sonst `Command` bzw. `MultiSelect`.
* **Befehlssatz:** eigenes `CommandSet`-Feld, ersetzt durch den Satz des zuletzt ausgeführten `CommandSetUpgrade` (Modulindex-Reihenfolge; den
  Alternativsatz, wenn der Spieler oder das Objekt `TriggerAlt` besitzt – `CommandSetUpgrade::upgradeImplementation`). Der Ausführungszustand
  wird über `SimModule.Describe` gelesen.
* **Raster:** 14 Slots (`ButtonCommand01..14`); Satzeinträge 15–18 zeigt die Retail-Leiste nie (`SlotsBeyondGrid`).
* **Mehrfachauswahl** (`ControlBarMultiSelect.cpp`): nur `OK_FOR_MULTI_SELECT`-Knöpfe des ersten Objekts; ein Slot entfällt, wenn ein weiteres
  Objekt dort einen anderen Knopf hat (außer `ATTACK_MOVE`); aktiv, wenn mindestens ein Objekt ihn ausführen kann; `Sources` nennt diese Objekte.
* **Verfügbarkeit** (`getCommandAvailability`, Reihenfolge wie dort): `SCRIPT_ONLY` → verborgen; `NEED_SPECIAL_POWER_SCIENCE` ohne Wissenschaft
  → verborgen; `POWERED` bei negativer Energiebilanz ohne `IGNORES_UNDERPOWERED` → `Underpowered` (ausgenommen Verkaufen, Räumen, Aussteigen,
  Leuchtfeuer löschen, Sammelpunkt, Stop, Waffenwechsel; `Player.cpp doPowerDisable`); `NEED_UPGRADE` → `MissingUpgrade`; `NOT_QUEUEABLE` bei
  laufender Produktion → `ProducerBusy`; dann je Art: Baubar `No`/`Only_By_AI` → verborgen, kein Bauer / beschäftigt → `NotABuilder` /
  `BuilderBusy`, Warteschlange voll → `QueueFull`, nicht instanziierbar → `NotInstantiable` (eigene, explizite Ergänzung), Voraussetzung →
  `MissingPrerequisite`, Geld → `InsufficientFunds`; Upgrades: besessen → `CantAfford`/`AlreadyOwned`, ohne Produktion (Objekt-Upgrade) →
  `NoProducer`, Wissenschaft → `MissingScience`; Wissenschaftskauf: `AlreadyOwned`, `MissingPrerequisite`, `NotPurchasable`,
  `InsufficientSciencePoints`; Spezialkraft nicht bereit → `NotReady`/`Recharging` mit `ReadyFraction` (aus `availableOnFrame` bzw. dem
  geteilten Bereitschaftsframe); Waffe nachladend → `NotReady`/`Reloading`; leerer Container → `EmptyContainer`.
* **Warteschlange:** bis 9 Einträge (`MAX_BUILD_QUEUE_BUTTONS`) mit Template, Symbol (Bild des passenden `UNIT_BUILD`-Knopfs), Kosten und
  Fortschritt (nur der Kopf läuft) aus `ProductionUpdate.Describe`.
* **HUD** (`Hud`): Geld, Energieerzeugung (Nettobilanz + Verbrauch, also inklusive Kraftwerksboni), Verbrauch (Summe negativer
  `EnergyProduction` fertiger Objekte), Kaufpunkte, Spezialkräfte des Spielers (mit Wissenschaft freigeschaltet, je Kraft das bereiteste Objekt).

## 9. Gemessene Zahlen (Realdaten `.local/phase2-import-a`, 30.09.2026)

* `Data/English/CommandMap.ini`: 96 Einträge, 0 Probleme, 0 Konflikte, 10 Felder ohne `=` (`Description` bei `VIEW_TEAM0..9`). `FromGraph`
  liefert dieselben 96 Bindungen; den 10 Einträgen fehlt dort die Beschreibung, weil der INI-Parser des Datenlayers eine Zeile ohne `=` als
  Anweisung statt als Feld liest. Die UI sollte die Beschreibungen daher aus `Parse` beziehen (oder der Datenlayer übernimmt die Regel).
* Befehlsknöpfe: 820 (7 ohne `Command`, Referenzstandard `NONE`: die `NonCommand_*`-Knöpfe), 472 Befehlssätze, 7 Satzeinträge jenseits von
  Slot 14, 0 Probleme.
* Befehlsleisten (Spieler Amerika, 10.000 Geld, ohne Wissenschaften): Kommandozentrale Slot 1 `Command_ConstructAmericaDozer` verfügbar,
  Slot 13/14 Sammelpunkt/Verkaufen, Gunship (Slot 2) verborgen; Dozer Slot 1 Kraftwerk verfügbar, mehrere Gebäude mit
  `MissingPrerequisite` bzw. `NotInstantiable`; Crusader Slot 11/13/14 Angriffsbewegung/Wachen/Stop verfügbar; Kaserne Slot 1 Ranger
  (derzeit `NotInstantiable`), Slot 13/14 Sammelpunkt/Verkaufen.
* Testprogramm `tests/CnCNew.Presentation.Tests`: 14 synthetische Tests (`InputTests`) plus 1 Realdatentest; zwei Läufe liefern
  byteidentische Ausgaben. Der Abnahmetest treibt über die Ereignispipeline Box-Auswahl → Strg+1 → Rechtsklick-Bewegung → Dozer-Klick →
  Hotkey `P` → Platzierung → `1` → Rechtsklick und prüft die exakte Befehlsfolge; die aufgezeichneten Befehle ergeben im `ReplayRunner` denselben
  Zustandshash.

## 10. Grenzen und fehlende Simulations-API

Die Schicht liest die lebende `SimulationWorld`; einen unveränderlichen Lese-Snapshot gibt es noch nicht. Aus der Simulation fehlen (bzw.
wären als rein lesende Zugriffe hilfreich):

1. ~~`SimulationWorld.CheckConstruct(ConstructCommand)` als öffentlicher Trockenlauf~~ **erledigt (Phase 10):** `SimulationWorld.CheckConstruct(PlayerId, ConstructCommand)`
   ist jetzt `public` (`SimulationWorld.Construction.cs`) und die exakte Logik, die `SimulationQueries.CheckPlacement` bisher nachbauen musste; die beiden
   können nicht mehr auseinanderlaufen.
2. ~~Öffentliche Lesezugriffe auf die Produktionswarteschlange, `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed`~~ **erledigt (Phase 10):**
   `ProductionUpdate.Queue` (`ImmutableArray<ProductionQueueItem>`, Vorlage/Kosten/Restticks, Kopf zuerst), `SpecialPowerModuleBase.AvailableOnFrame(IWorldServices)`
   (liest bei geteilten Kräften denselben gemeinsamen Bereitschaftsframe wie `IsReady`) und `UpgradeMuxModule.Executed` sind jetzt öffentlich.
3. ~~Ein öffentliches Dock-Kennzeichen~~ **erledigt (Phase 10):** `SimulationWorld.IsDock(EntityId)` ersetzt den Modulnamen-Abgleich von außen; `IDockModule`
   bleibt intern, wird aber nicht mehr gebraucht.
4. Befehle für: Feuern auf eine Position (`FORCE_ATTACK_GROUND`, `FIRE_WEAPON` mit `NEED_TARGET_POS`), dauerhaftes Angreifen/Angriffsbewegung/
   Wachen/Wegpunkte als KI-Zustände (heute clientseitig wiederholt), Formationsbewegung, Entführen, Sabotage, Hacken, Kampfabsprung,
   Waffenwechsel, Leuchtfeuer, Upgrade-Abbruch, Spezialkraft-Bau, Linienbau. **Seit Phase 10 vorhanden:** `OverchargeCommand` (`TOGGLE_OVERCHARGE`,
   schaltet `OverchargeBehavior.Toggle`) und, als bewusst vereinfachtes „Einnehmen“, `CaptureCommand` (direkte Übernahme samt Energiepool-Effekt durch
   ein eigenes, in Reichweite stehendes beliebiges bewegliches Objekt — ohne den Kanalisierungstimer/Wissenschaftsvorbehalt/Tarnungs-Check der echten,
   an `SpecialAbilityUpdate` hängenden Fähigkeit; siehe `docs/SIMULATION-CORE.md` §23 und `CommandTypes.Capture`s Kommentar für die Abgrenzung).
5. Energie: die Simulation führt nur die Nettobilanz; Erzeugung/Verbrauch des HUD sind aus Template-Werten rekonstruiert, und der
   „underpowered“-Zustand von `POWERED`-Objekten wird nur in der Leiste abgebildet (die Simulation deaktiviert nichts).
6. `EnterContainerCommand` wirkt sofort ohne Anlaufweg (Simulationsstand Phase 5).

Weitere bewusste Vereinfachungen: `CommandSetUpgrade` wird beim Anzeigen ausgewertet statt beim Auslösen gespeichert; Gleichheit bei
Doppelklick/`E` per Template-Name; `DOUBLEDOWN` nie ausgelöst; `GUARD_FLYING_UNITS_ONLY` wie normales Wachen; `SELECT_HERO`,
`SELECT_NEXT_IDLE_WORKER`, Kamera-Lesezeichen (`SAVE/VIEW_VIEW`) und `VIEW_LAST_RADAR_EVENT` werden als `UiRequest` weitergereicht.

Keine neue NuGet-Abhängigkeit.
