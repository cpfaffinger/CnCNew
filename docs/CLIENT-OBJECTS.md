# Client-Objektlayer (Stride)

Zeichnet jedes `DrawableInstance` eines laufenden Matches (Einheiten, Gebäude, Props, Szenerie) als
animiertes W3D-Modell. Quelle: `src/CnCNew.Client.Stride/Layers/ObjectLayer.cs` (dünner
`IWorldLayer`, den `WorldLayers.CreateAll()` unter diesem Namen instanziiert) und
`src/CnCNew.Client.Stride/Layers/Objects/**` (eigentliche Implementierung, `ObjectLayerCore`).
`ObjectLayer` reicht `Attach`/`Update`/`Dispose`/`Stats` unverändert an `ObjectLayerCore` weiter, weil
`WorldLayers.cs` einen parameterlosen `ObjectLayer` im Namespace `CnCNew.Client.Stride.Layers`
erwartet; die restlichen Quellen leben zusammen unter `Layers/Objects` im Namespace
`CnCNew.Client.Stride.Layers.Objects`.

## Koordinaten und Platzierung

Die Präsentation liefert Weltkoordinaten (X Ost, Y Nord, Z hoch, rechtshändig) identisch zum
Simulationsraum; W3D-Meshes sind ebenfalls Z-hoch, daher werden Vertexpositionen und Normalen aus
`W3dMesh` unverändert übernommen (kein Y-Swap, keine Zentrierung, keine Skalierung –
`ObjectRenderVertexBuilder`, bewusst eine eigene, schlankere Kopie von `W3dStrideScene.AddVertex`
ohne dessen Konsolen-Rahmenlogik). Die Instanzmatrix ist
`CreateScale(scale) * CreateRotationZ(yaw) * CreateTranslation(position)` (Zeilenvektor-Konvention,
`world = local * parent`); W3D-„vorne" ist +X, Sim-Gieren 0 ist ebenfalls +X.

## Instanzierungsstrategie: ein Skelett-Knoten pro starrem Teil

Jedes W3D-Modell wird einmal über `W3dAssetLibrary` (aus `CnCNew.W3D`, gebunden an
`RenderServices.Content`) geladen und zu einem `ModelAsset` kompiliert
(`ModelAssetCache`/`ModelAsset.Build`). Die höchste LOD-Stufe wird in starre und geskinnte
Renderobjekte aufgeteilt:

- **Starre Teile** teilen sich **ein** Stride-`Model` und **ein** Skelett: Knoten 0 ist eine
  ungenutzte Wurzel, jedes starre Renderobjekt bekommt Knoten `Index+1` mit den Flags
  `EnableRender | OverrideWorldMatrix`. Laut dekompiliertem `Stride.Rendering.SkeletonUpdater`
  (`UpdateNode`) lässt dieses Flag-Paar die automatische Welt-Matrix-Berechnung aus der Hierarchie
  komplett aus – der Layer schreibt jeden sichtbaren Frame `boneWorld * instanceMatrix` direkt in
  `ModelComponent.Skeleton.NodeTransformations[node].WorldMatrix` (Bone-Weltmatrix aus einer
  `W3dPose`, ausgewertet mit `root = Identity`, da die Instanzmatrix die Wurzel schon enthält). Damit
  zeichnet **ein** gepooltes `PooledEntity` (Entity + `ModelComponent`) ein komplettes,
  mehrteiliges starres Modell; nichts wird pro Instanz geklont, außer bei HOUSECOLOR-Vertexfarbe
  (siehe unten).
- Sub-Objekt-Sichtbarkeit (Standard aus `W3dSubObject.IsVisibleByDefault`, überschrieben durch
  `ModelStateSpec.SubObjects` in Deklarationsreihenfolge, letzter Treffer gewinnt, Abgleich über
  Vollname oder Suffix nach dem Punkt, `OrdinalIgnoreCase` – `RenderObjClass::Get_Sub_Object_By_Name`)
  wird **jeden sichtbaren Frame neu gesetzt**, indem das `EnableRender`-Bit des Knotens umgeschaltet
  wird; ein Modellwechsel setzt es ohnehin über `ModelComponent.CheckSkeleton`/`ModelUpdated` auf den
  Modell-Standard zurück.
- **Geskinnte Teile** (169 im Vollbestand) bekommen ein eigenes, einknotiges Modell pro Instanz
  (`SkinPart.CreateRuntimeModel`, Skelett und Materialien geteilt, aber eigene, dynamische
  Vertexpuffer pro Batch – ein Puffer kann nicht von mehreren Instanzen mit unterschiedlicher Pose
  geteilt werden). Jeden sichtbaren Frame: `W3dModel.SkinVertices(mesh, pose.BoneWorldTransforms, …)`
  liefert Positionen/Normalen in Modell-Raum (root = Identity, wie bei starren Teilen); ein
  `SourceIndices`-Array bildet jeden (pro Dreieck dupliziert vorliegenden) Ausgabe-Vertex auf seinen
  Quell-Vertex ab, Farbe/UV bleiben aus der Vorlage. Der Knoten (Index 1) bekommt dieselbe
  Instanzmatrix wie starre Teile.
- Nicht aufgelöst: Kamera-ausgerichtete Billboards (`W3dSubObject.IsCameraFacing`), HLOD-Aggregate
  und -Proxies. Türme folgen seit Phase 15 den Schüssen (`DrawableInstance.TurretYaw/TurretPitch`, siehe
  „Phase 15: Korrekturen der Weltdarstellung“); ohne Schuss bleibt die Ruhepose.

Nicht indizierte Dreieckslisten-Batches kommen aus `W3dRenderPlan.Build`; ein einziger,
bedarfsweise wachsender Identitäts-Indexpuffer (`SharedIndexBuffer`) bedient jede Batchgröße, da nur
das Präfix bis zur jeweiligen Vertexzahl gelesen wird. Batches mit animiertem Mapper
(`W3dLinearOffsetTextureMapper`/`W3dGridTextureMapper`) bekommen einen dynamischen Vertexpuffer, der
höchstens einmal pro Frame und Asset neu berechnet wird (`ModelAsset.RefreshAnimatedBatches`,
geteilt über alle Instanzen desselben Assets – die UV-Verzerrung hängt nur von der Zeit ab, nicht
von der Instanz).

## Teamfarbe

Zwei unabhängige, im Original getrennte Mechanismen (`W3DAssetManager.cpp Recolor_Mesh`):

1. **ZHC-Texturen** (`remapAlphaTexture32Bit`, `DO_HUE_SHIFT`): Texturnamen mit Präfix „ZHC" werden
   als Alpha-Masken-TGA erwartet (vierter Buchstabe „A"); der Alphakanal ist eine Maske
   (`maske = 255 - alpha`), maskierte Pixel übernehmen den Farbton (Hue) der Teamfarbe und skalieren
   ihre Sättigung mit der Teamsättigung, Ausgabealpha ist immer 255. Implementiert in
   `Layers/Objects/HouseColor.Recolor` (HSV-Konvertierung nach Beschreibung neu geschrieben, nicht
   aus dem Original kopiert) und über `W3dTextureCache.HouseColor` an `W3dMaterialFactory.Create`
   angeschlossen (additive Erweiterung, siehe Diff in `W3dTextureCache.cs`). Pro (Textur, Teamfarbe)
   gecacht; nicht unterstützte ZHC-Varianten (keine TGA, kein Alpha-Masken-Byte) werden einmal
   gewarnt und ungefärbt gezeichnet. Neutrale Teamfarbe (`TeamColors.Neutral`, RGB 200/200/200) läuft
   durch denselben Pfad wie jede andere Farbe.
2. **HOUSECOLOR-Vertexfarbe** (`Recolor_Vertex_Material`): Meshes, deren Name (nach dem Container-
   Punkt) mit „HOUSECOLOR" beginnt (`_strnicmp(…, 10)`), tönen im Original Ambient/Diffuse des
   Vertexmaterials. Hier: `HouseColor.TintVertexColors` ersetzt das RGB von Farbkanal 0 (und Kanal 2,
   falls der Batch zwei Material-Pässe hat) durch die Teamfarbe, Alpha bleibt erhalten. Da die Farbe
   in den Vertexpuffer gebacken ist, braucht das eine **Modellvariante pro Team**: `ModelAsset`
   erkennt beim Laden, ob überhaupt HOUSECOLOR-Meshes vorkommen (`HasHouseColorMeshes`); nur dann
   baut `GetRigidModel(team)` verzögert eine Variante (geteiltes Skelett, geteilte Materialliste,
   nur die betroffenen `Mesh`-Objekte werden mit neuem Vertexpuffer ersetzt), gecacht pro
   Team-Hexcode. Bei geskinnten HOUSECOLOR-Batches wird stattdessen einmalig beim (Neu-)Aufbau des
   Skin-Runtime-Modells getönt (bekannte Einschränkung: ein Besitzerwechsel nach dem ersten Aufbau
   färbt den Skin nicht nach, da danach nur noch Position/Normale neu berechnet werden).

## Zustände, Animation, Konstruktionshöhe

Die Präsentation hat den passenden `ModelStateSpec` pro Draw-Modul bereits ausgewählt
(`DrawableInstance.Selections`, `BestStateSelector`); der Layer wählt daraus nur noch, welche
Animation aus `ModelStateSpec.Animations` gespielt wird: deterministisch aus einem Hash der
Entity-/Szenerie-ID und dem Zustandsindex, bevorzugt eine Nicht-Idle-Animation, sonst Idle. Bei
`AnimationFlags.RandomStart` wird ein ebenso deterministischer Zeitversatz in die Startzeit addiert,
damit gleiche Einheiten nicht synchron animieren. Wiedergabegeschwindigkeit kommt aus
`AnimationSpeedMin/Max`, ebenso deterministisch interpoliert. `W3dAnimationClock.GetFrame` liefert
den Frame aus `AnimationMode`/Bildrate/verstrichener Zeit; `W3dPose.Evaluate` wird nur aufgerufen,
wenn sich der Frame um mehr als ~0,001 geändert hat. Fehlt die Animation
(`W3dAssetLibrary.GetAnimation` liefert `null`), bleibt die Ruhepose stehen, der Name wird einmalig
gezählt und gewarnt (`ObjectLayerStats.MissingAnimations`). `AnimationMode.Manual` steht seit Phase 15 auf
dem Startframe des Zustands (Türen, Pakete); die Flags `StartFrameFirst/Last` und
`MaintainFrameAcrossStates*` werden weiter nicht ausgewertet. Türme, Reifen und Mündungsfeuer: siehe
„Phase 15: Korrekturen der Weltdarstellung“.

`AnimationFlags.AdjustHeightByConstructionPercent` senkt die Instanz um
`Höhe * (1 - Baufortschritt)` (`W3DModelDraw::adjustTransformMtx`, `references/generals-game-code/
Core/GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DModelDraw.cpp`); die Höhe ist die
maximale Z-Ausdehnung der Ruhepose über dem Pivot (`ModelAsset.Height`), eine Näherung der
Original-Kollisionsgeometrie (`GeometryInfo::getMaxHeightAbovePosition`) über die sichtbaren Bounds.

## Culling und Statistik

`match.Camera.Frustum.Intersects(Aabb)` mit einer Kugel-abgeleiteten AABB um die Instanzposition
(Radius aus der Ruhepose aller Teile, ca. 25 % + 2 Einheiten Spielraum, `ModelAsset.Radius`) – eine
bewusste Vereinfachung gegenüber einer rotierten Bounding-Box; für gedrehte, längliche Objekte kann
das Culling etwas konservativer ausfallen als nötig. `ObjectLayerStats` zählt pro Frame Drawables,
Gezeichnete, Ausgeblendete, gepoolte Entities, geskinnte Zeichnungen, Dreiecke sowie kumulativ
geladene/fehlende Modelle, fehlende Texturen, fehlende Animationen und Platzhalter; dazu
Frame-Millisekunden und pro Thread allokierte Bytes (`Stopwatch`/
`GC.GetAllocatedBytesForCurrentThread`). Im `--smoke-test`-Modus wird die Zeile einmal beim
Erreichen von `--frames` (oder beim Beenden) auf die Konsole geschrieben.

Nicht mehr gesehene Drawables geben ihre gepoolten Entities zurück (`EntityPool`, ein einziger
Freispeicher für starre, geskinnte und Platzhalter-Entities, da nur `ModelComponent.Model`
ausgetauscht wird); ausgeblendete, aber weiterhin vorhandene Instanzen werden nur aus der Szene
entfernt, nicht freigegeben, und bei Rückkehr in den Sichtbereich wiederverwendet.

## Fehlende Modelle

Schlägt `W3dAssetLibrary.GetModel` fehl (Datei oder Hierarchie fehlt), wird der Name einmalig
gewarnt und gezählt (`ObjectLayerStats.ModelsMissing`); die Instanz bekommt stattdessen eine sichtbar
pinke Platzhalter-Box (`PlaceholderBox`, Einheitswürfel, unlackiert, über denselben
Skelett-Knoten-Trick platziert wie starre Teile) und zählt in `ObjectLayerStats.Placeholders`. In
allen Testläufen (Alpine Assault, drei Fraktionen, mehrere hundert Frames) traten keine fehlenden
Modelle, Texturen oder Animationen auf.

## Bekannte Einschränkungen

Stand Phase 15 (neu gemessen mit `PresentationTool model-audit`, siehe unten); frühere Einträge zu schwarzen
Rückseiten und fehlenden Türmen sind durch die Phase-15-Korrekturen überholt.

- **Nicht gesetzte Modellzustände:** 54 der 106 in den Zuständen benannten `ModelCondition`-Namen setzt die
  Präsentation nie (`model-audit` listet sie mit Zahl der betroffenen Objekte). Die sichtbarsten:
  `DOOR_1_OPENING/CLOSING/WAITING_OPEN` (Fabrik-/Flugfeldtüren, Kommandozentrale), `PREATTACK_A/B/C`
  (Anschlag der Infanterie), `PACKING/UNPACKING`, `FREEFALL`, `EXPLODED_BOUNCING/FLAILING`,
  `SPECIAL_CHEERING`, `RAISING_FLAG`, `PANICKING`, `JETAFTERBURNER/JETEXHAUST`, `JAMMED`,
  `TURRET_ROTATE`, `POST_COLLAPSE`. Das heißt: Türen öffnen sich nicht, Infanterie zeigt keine
  Anschlagshaltung, Jets keinen Nachbrenner. Die Modelle haben diese Zustände, sie werden aber nicht
  ausgelöst; die Simulation liefert für Türen/Vorangriff keinen Zustand.
- **Federung und Lenkung fehlen:** `W3DTruckDraw`/`W3DTankDraw` bewegen außer den Reifen auch Lenkachsen
  und Federungsbones (Schaukeln bei Beschleunigung). Nur die Raddrehung ist gebaut.
- **Straßen werden nicht gezeichnet**, der `GlobalLighting`-Chunk der Karte wird übersprungen: die
  Objektbeleuchtung kommt aus `GameData` (`TerrainObjectsLighting*`), nicht aus der Karte.
- **Verhüllung nur für Szenerie:** Kartenobjekte ohne Simulationsentität werden bis zur Erkundung ihres
  Bodens ausgeblendet; im Nebel (erkundet, nicht sichtbar) werden **Objekte nicht abgedunkelt** – die
  Abdunklung kommt allein vom Nebel-Layer des Geländes.
- Kamera-ausgerichtete Billboards, HLOD-Aggregate/-Proxies werden nicht gezeichnet (gezählt in
  `W3dModel.UnresolvedSubObjects`/`Aggregates`/`Proxies`; `model-audit`: 10 Modelle mit nicht
  auflösbaren Einträgen; 47 Modelle laden nicht, weil die Datei in den Assets fehlt – Kartenszenerie `cb…`).
- `StartFrameFirst/Last`, `MaintainFrameAcrossStates*` (siehe oben).
- Environment-Mapping-Texturstufen nutzen eine feste Normalen-/Blickrichtung
  (`ObjectRenderVertexBuilder.FixedViewDirection`, ca. (0, −0,6, 0,8) normiert) statt einer
  echten Pro-Pixel-Reflexion, weil die Geometrie pro Asset einmal gebaut und von allen Instanzen
  geteilt wird.
- Kein Blob-/Kontaktschatten (optionaler Punkt des Auftrags): nicht umgesetzt.
- HOUSECOLOR-Vertexfärbung eines geskinnten Teils aktualisiert sich nicht bei Besitzerwechsel nach
  dem ersten Aufbau (siehe oben).

## Phase 15: Korrekturen der Weltdarstellung

Auslöser war die erste Sichtprüfung durch einen Menschen (`docs/DEMO-FINDINGS.md`): „die Hälfte der Wände
fehlt“, Reifen nach oben, keine Türme, Gebäude im Nebel ohne Gelände. Die Ursachen waren nicht, wie
vermutet, fehlende Mesh-Teile (der Mesh-Audit zählt alle Dreiecke als gezeichnet), sondern vier
unabhängige Fehler der Darstellung:

1. **Index-Puffer freigegeben.** `SharedIndexBuffer` ersetzte beim Wachsen den GPU-Puffer und gab den
   alten sofort frei, obwohl bereits gebaute Modelle ihn noch referenzierten; deren Teile verschwanden
   oder zeigten Fremddreiecke. Jetzt bleiben ersetzte Puffer bis `Dispose` am Leben (`_superseded`);
   `ModelAsset.CountGpuResourceProblems` zählt tote/fehlende GPU-Ressourcen (`ObjectLayerStats`,
   `gpuProblems`), und der `--smoke-test` bricht bei einem Problem ab.
2. **Windungssinn.** W3D-Dreiecke sind gegen den Uhrzeigersinn orientiert, Strides Vorderseite ist im
   Uhrzeigersinn; der Geländelayer dreht die Indizes bereits um, der Objektlayer (und `W3dStrideScene`)
   nicht. Folge: Rückseitenauslese entfernte die Außenseiten der Wände und zeigte die Innenseiten.
   Beide Bauer emittieren jetzt die Ecken 0,2,1 (`ObjectRenderVertexBuilder`, `W3dStrideScene`).
3. **Keine Beleuchtung im Spiel.** Die Live-Szene hatte keine Richtungslichter; Objekte waren schwarz
   oder nur ambient. `Rendering/SceneLighting` baut Sonne und Ambient aus `TerrainLighting`, dessen
   Objektwerte `ObjectsFromDefinitions` aus `GameData` (`TerrainObjectsLighting*`) liest. Stride-Lichter
   zeigen entlang −Z der Entität; `RotationFromForward` dreht entsprechend.
4. **Bone-Sichtbarkeit ignoriert.** Teile, die der HLOD/die Animation per Sichtbarkeitskanal ausblendet
   (`Set_Animation_Hidden`), wurden gezeichnet; Mündungsfeuer-Unterobjekte standen dauerhaft sichtbar.
   Beides folgt jetzt dem Bone-Sichtbarkeitskanal; Mündungsfeuer erscheint nur nach einem Schuss.

Dazu die Zustände, die vorher nicht existierten:

- **Türme** (`TurretMotion`, `TurretTracker`): die Simulation hat keinen Turmzustand. Die Präsentation
  leitet den Zielwinkel aus den `WeaponFired`-Ereignissen ab; Drehrate, Ruhestellung und Höhenwinkel
  stammen aus den `Turret`/`AltTurret`-Blöcken des `AIUpdateInterface` und steuern die Turm- und
  Höhen-Bones (`Capture_Bone/Control_Bone`-Technik). Der zweite Turm (`AltTurret`) ist getrennt. Das ist
  eine Näherung: der Turm folgt dem Schuss, nicht dem Zielen vor dem Schuss.
- **Reifen** (`W3DTruckDraw`): Drehwinkel = Multiplikator · zurückgelegte Strecke (`WheelTravel` aus
  den Positionsänderungen); Lenkung und Federung fehlen (siehe Einschränkungen).
- **Mündungsfeuer** (`MuzzleFlashTicks`, `FiredSlots`): Unterobjekte `MUZZLEFX*`/`BARRELFX*` nur kurz
  nach einem Schuss des zugehörigen Slots.
- **Unerkundete Szenerie** (`FrameProjector`): Kartenobjekte ohne Entität erscheinen erst, wenn ihr
  Boden erkundet ist, und bleiben dann (erinnert) stehen – vorher standen Gebäude im Schwarz.
- **Heilsymbole** (`Drawable::drawHealing`): Schraubenschlüssel (`SCPWrench000–009`, Ping-Pong 60 ms) für
  Gebäude/Fahrzeuge, Kreuz (`SCPCross000–015`, 30 ms) sonst; Bedingung: Leben unter Maximum, Heilung
  innerhalb von 3 s (`ShowsHealing`). Gezeichnet vom neuen **HUD-Overlay** (`Game/Hud/HudOverlayRenderer`):
  Auswahlrahmen, Ringe (Einheiten) bzw. Eckklammern (Gebäude), Lebensbalken, Befehlslinien, Sammelpunkt-
  Flaggen und Bauvorschau. Das Overlay wurde bis Phase 15 berechnet, aber nie gezeichnet.
- **Dock-Zustand:** das Original setzt `DOCKING` zwischen Einfahrt und Ausfahrt (`DockUpdate::onEnterReached`
  bis `onExitReached`), nicht schon beim Anflug; der Versorgungs-Chinook senkt sein Netz jetzt am Depot,
  nicht im Flug dorthin (`AIUpdateInterface.IsInsideDock`).

### Kartenwinkel (Punkt 5 der Demo-Befunde)

`PresentationTool angle-audit` prüft an `ObjectsList`-Reihen länglicher Vorlagen (Zäune, Mauern), welche
Konvention die Reihen in ihrer Längsrichtung ausrichtet. Auf 150 Karten (161 längliche von 1.520
platzierten Vorlagen, 11.313 Reihenproben) richtet der **gespeicherte Winkel unverändert** 64,6 % der Proben
auf 5° aus (7.313); Vorzeichenumkehr 22,1 %, Vierteldrehung 3,2 %, beides 23,5 % (die Anteile überlappen, eine
Probe kann mehrere Konventionen erfüllen). Bei durchgehenden Zaunreihen ist die gespeicherte Konvention
klar die beste (`ChainLinkFence` 90,7 % gegen 23,1 %). Die Umrechnung (Bogenmaß, Vorzeichen, +X-Vorderachse)
ist damit **gemessen plausibel**; der gemeldete Befund „falsch gedreht“ ist **nicht reproduziert**.
Wahrscheinliche Ursache des Eindrucks waren Windung und Beleuchtung (Punkte 2 und 3 oben).
**Nicht belegt:** dass ein Mensch die Kartenobjekte jetzt als richtig gedreht empfindet.

### Sichtprüfung der Weltdarstellung (Checkliste für den Menschen)

`scripts/capture-world.ps1` nimmt einen festen Satz von Szenen auf (`--smoke-test --capture`, feste
Karte, Seed, Kamera; die Bilder liegen unter `%TEMP%\cncnew-captures`, nie im Repo; Szenen laufen
nacheinander, nicht parallel). `manifest.txt` enthält **vorläufige** SHA-256: sie belegen
Reproduzierbarkeit, nicht Richtigkeit; Golden Hashes entstehen erst nach der Abnahme. Jede Zeile unten
ist von einem Menschen gegen die Aufnahme abzuhaken; ohne diese Abnahme gilt Phase 15 nicht als bestätigt.

| Szene | Zu sehen | Abgenommen |
| --- | --- | --- |
| `viewer-commandcenter` | geschlossene Wände, Sternwappen vorn, kein schwarzes Gitterskelett | ☐ |
| `viewer-barracks` | alle Wände, Zaun, Fahnenmast | ☐ |
| `viewer-crusader` | Wanne, Turm, Rohr in Ruhepose, Ketten unten | ☐ |
| `viewer-dozer` | Planierraupe im Modellbetrachter: Reifen unten, Schaufel vorn (die Betrachterkamera ist fest, die Ansicht ungewohnt) | ☐ |
| `match-start` | Basis mit Wanddetails, Rest der Karte schwarz, keine Gebäude im Schwarz | ☐ |
| `match-lowangle` | flache Kamera: Wände beleuchtet, nicht schwarz | ☐ |
| `match-selected` | Eckklammern + Balken an der Kommandozentrale, Ring + Balken an der Planierraupe | ☐ |
| `match-fog-unexplored` | unerkundetes Gelände mit Ölquelle: alles schwarz | ☐ |
| `match-base-late` | ausgebaute Basis, Teamfarbe, Geschütztürme, Zäune an den Kanten | ☐ |
| `match-combat` | Rohr zeigt aufs Ziel, Mündungsfeuer, Reifen/Türme nicht in Ruhepose | ☐ |
| `mission-start` | Challenge-Basis mit Gebäuden, Beleuchtung wie im Gefecht | ☐ |

Zusätzlich im laufenden Spiel zu prüfen (nicht aufnehmbar): Reifen drehen beim Fahren; Planierraupe
repariert ein Gebäude → Schraubenschlüssel erscheint; Chinook senkt das Netz erst über dem Depot;
Infanterie bleibt nicht im Fußabdruck des Depots stehen; Einheiten umgehen eine arbeitende Planierraupe.

### Messung des Mesh-Audits (`PresentationTool model-audit <dir> --structures`, neu gemessen)

Die Audit-Befehle (`model-audit`, `angle-audit`) entstanden in dieser Phase; eine Tabelle „vorher“ gibt
es mit dem Werkzeug daher nicht – das „vorher“ ist die Beobachtung des Menschen und die
Ursachenanalyse oben (Puffer, Windung, Licht), keine Zahl.

| Größe | Wert |
| --- | --- |
| nach Objektzuständen gewählte Modelle | 6.041 (4.933 von Gebäuden benutzt); geprüft 4.886; nicht ladbar 47 |
| Unterobjekte (feinste LOD) | 56.746; gezeichnet 56.615; standardmäßig versteckt 4; nur Kollision 127 |
| Dreiecke | 1.585.417 gesamt, **1.585.401 gezeichnet**, 16 standardmäßig versteckt, 0 im Renderplan verloren |
| zweiseitig gezeichnete Teile | 246 |
| nie gezeigte versteckte Unterobjekte | 4 (`NBGATTLING.MUZZLEFX01/02`, `UBUNDTUNN(_S).BARRELFX01`) |
| Modellzustände nie gesetzt | 54 von 106 Namen (Liste in den Einschränkungen) |

## Gemessene Zahlen (Alpine Assault, `.local/phase2-import-a`, neu messen bei Bedarf)

- FactionAmerica vs. FactionChina, Startaufnahme: 590 Drawables gesamt, 10 im Sichtkegel gezeichnet,
  78 Modelle geladen, 2.080 Dreiecke, 0 fehlende Modelle/Texturen/Animationen,
  **0,19–0,27 ms Layerzeit pro Frame, 0 zusätzlich allokierte Bytes** im eingeschwungenen Zustand
  (zwei unabhängige Läufe bei 300 bzw. 1.500 Frames: identischer SHA-256 der PNG-Aufnahme).
- FactionGLA vs. FactionAmerica, `--autoplay --frames 6000`: 612 Drawables gesamt, 16 gezeichnet,
  9 geskinnte Infanterieinstanzen, 124 Modelle, 6.133 Dreiecke, 0,38–0,60 ms Layerzeit,
  168 Byte/Frame (Stride-interne Pufferaktualisierung der Skin-Meshes); zwei unabhängige Läufe
  ergaben denselben SHA-256.
- Der Auftrag nennt „~600 Drawables unter 4 ms" als Budget: Alle Testkarten liefern tatsächlich
  ~590–612 Drawables **pro Frame insgesamt** (nicht nur im Sichtfeld), und genau diese Menge
  durchläuft jeden Frame die Zustands-/Culling-Auswahl – gemessen 0,19–0,60 ms, weit innerhalb des
  Budgets. Nur ein kleinerer Teil (10–26 in diesen Aufnahmen) besteht zusätzlich den Frustum-Test
  und durchläuft die teurere Pose-/Skin-/Knotenaktualisierung; die RTS-Kamera begrenzt den
  Sichtbereich so, dass in keinem Testlauf alle ~600 gleichzeitig sichtbar waren – die berichtete
  Zeit ist daher die tatsächlich gemessene Volllast-Zahl, keine Hochrechnung.
- Sichtprüfung (PNG-Aufnahmen, `--capture`): Gebäude, Fahrzeuge, geskinnte Infanterie, alle drei
  getesteten Fraktionen mit korrekter Teamfarbe (blaue/rote/grüne Fahnen und Bannerflächen),
  Animation über die Zeit (unterschiedlicher SHA-256 bei Frame 100 vs. 130 bei sonst gleichem Setup).

## Quellenverweise

- `references/generals-game-code/Core/GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/
  W3DModelDraw.cpp`: `adjustTransformMtx` (Konstruktionshöhe), `handleClientTurretPositioning`
  (Näherung aus Schussereignissen, siehe Phase 15), `adjustAnimation`/`doDrawModule` (Animationswahl bei
  Zustandswechsel, hier durch einen deterministischen Hash ersetzt statt Zufall).
- `W3DAssetManager.cpp`: `Recolor_Mesh`, `Recolor_Vertex_Material`, `remapAlphaTexture32Bit`
  (Teamfarbe, siehe oben und `HouseColor.cs`).
- `colorspace.h`: `RGB_To_HSV`/`HSV_To_RGB` (aus der Beschreibung neu geschrieben, nicht kopiert).
- Stride-Interna verifiziert per Dekompilierung (`ilspycmd`, `Stride.Rendering.dll` 4.3.0.2507):
  `SkeletonUpdater.UpdateNode` (Verhalten von `OverrideWorldMatrix`), `ModelComponent.Update`
  (Wurzelknoten-Sonderfall), `IndexingDictionary<T>` (`Clear`, `null` entfernt einen Eintrag).
