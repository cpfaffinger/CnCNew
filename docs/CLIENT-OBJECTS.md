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
  und -Proxies. Türme folgen der Ruhepose, weil `DrawableInstance.TurretYaw/TurretPitch` von der
  Präsentation aktuell immer `null` liefert (`W3DModelDraw::handleClientTurretPositioning` ist damit
  nicht nachgebildet – dokumentierte Lücke, kein stiller Fallback).

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
gezählt und gewarnt (`ObjectLayerStats.MissingAnimations`). Bekannte Lücken: `AnimationMode.Manual`
rendert immer Frame 0 (die Präsentation liefert keinen manuellen Frame); die Flags
`StartFrameFirst/Last` und `MaintainFrameAcrossStates*` werden nicht ausgewertet.

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

- **Zweiseitige Meshes können lokal schwarz erscheinen.** Einige dünne, zweiseitige Elemente
  (`mesh.IsTwoSided` → `CullMode.None`) zeigen von der Rückseite eine unbeleuchtete (schwarze)
  Fläche, weil `MaterialDiffuseLambertModelFeature` die Normalen für Rückseiten nicht spiegelt – ein
  Verhalten der geteilten `W3dMaterialFactory`/Stride-Materialpipeline, nicht des Objektlayers
  selbst (dieselbe Geometrie zeigt denselben Effekt in `W3dStrideScene`). Betrifft nur vereinzelte
  Anbauteile (Gitter, Streben), keine Hauptgeometrie; eine echte Behebung erfordert einen
  Shader-Mixin für Rückseiten-Normalen und ist außerhalb des additiven Änderungsrahmens dieser
  Phase für `W3dMaterialFactory.cs`.
- Türme, `AnimationMode.Manual`, `StartFrameFirst/Last`, `MaintainFrameAcrossStates*` (siehe oben).
- Kamera-ausgerichtete Billboards, HLOD-Aggregate/-Proxies werden nicht gezeichnet (gezählt in
  `W3dModel.UnresolvedSubObjects`/`Aggregates`/`Proxies`, hier nicht weiter ausgewertet).
- Environment-Mapping-Texturstufen nutzen eine feste Normalen-/Blickrichtung
  (`ObjectRenderVertexBuilder.FixedViewDirection`, ca. (0, −0,6, 0,8) normiert) statt einer
  echten Pro-Pixel-Reflexion, weil die Geometrie pro Asset einmal gebaut und von allen Instanzen
  geteilt wird.
- Kein Blob-/Kontaktschatten (optionaler Punkt des Auftrags): aus Zeitgründen in dieser Phase nicht
  umgesetzt.
- HOUSECOLOR-Vertexfärbung eines geskinnten Teils aktualisiert sich nicht bei Besitzerwechsel nach
  dem ersten Aufbau (siehe oben).

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
  (nicht implementiert, siehe oben), `adjustAnimation`/`doDrawModule` (Animationswahl bei
  Zustandswechsel, hier durch einen deterministischen Hash ersetzt statt Zufall).
- `W3DAssetManager.cpp`: `Recolor_Mesh`, `Recolor_Vertex_Material`, `remapAlphaTexture32Bit`
  (Teamfarbe, siehe oben und `HouseColor.cs`).
- `colorspace.h`: `RGB_To_HSV`/`HSV_To_RGB` (aus der Beschreibung neu geschrieben, nicht kopiert).
- Stride-Interna verifiziert per Dekompilierung (`ilspycmd`, `Stride.Rendering.dll` 4.3.0.2507):
  `SkeletonUpdater.UpdateNode` (Verhalten von `OverrideWorldMatrix`), `ModelComponent.Update`
  (Wurzelknoten-Sonderfall), `IndexingDictionary<T>` (`Clear`, `null` entfernt einen Eintrag).
