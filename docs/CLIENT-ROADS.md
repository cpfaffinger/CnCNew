# Straßen im Stride-Client

Zuständig: `src/CnCNew.Presentation/Terrain/Road*.cs` (renderer-neutrales Modell),
`src/CnCNew.Client.Stride/Layers/Terrain/RoadMeshBuilder.cs`, `TerrainMaterials.CreateRoad` und
`TerrainLayer.CreateRoads` (Zeichnen). Die Straßen einer Karte sind Kartenobjekte mit gesetzten
`FLAG_ROAD_POINT1`/`FLAG_ROAD_POINT2`-Bits (`MapObject.h`); ihr Objektname ist der Name eines `Road`-Blocks
aus `Roads.ini`. Das Referenzspiel baut daraus in `W3DRoadBuffer` texturierte Streifen, die auf dem
Höhenfeld liegen. Dieses Dokument beschreibt, was davon nachgebaut wurde, wie es gemessen wurde und was
nur angenähert ist. Fremder Code wurde gelesen und neu geschrieben, nicht kopiert; die Quellverweise
stehen jeweils bei der Funktion.

Bis Phase 15 wurden Straßenpunkte von `StaticScenery` nur gezählt (`SceneryReport.RoadOrBridgePoints`)
und verworfen. Das ist geblieben: Straßenpunkte sind keine Szenerie-Objekte, sie laufen über
`RoadModel`.

## Datenquelle

- **Karte**: `MapObject.Flags` (`CnCNew.Data.World`). Neu benannt wurden die bisher nur numerisch
  vorhandenen Bits `FlagRoadCornerAngled` (0x08, `FLAG_ROAD_CORNER_ANGLED`), `FlagRoadCornerTight` (0x40,
  `FLAG_ROAD_CORNER_TIGHT`) und `FlagRoadJoin` (0x80, `FLAG_ROAD_JOIN`); `FlagRoadPoint1/2` (0x02/0x04)
  gab es schon. Ein Straßensegment ist ein Objektpaar *unmittelbar hintereinander* in der `ObjectsList`
  (erst `POINT1`, dann `POINT2`); die Reihenfolge der Datei bleibt erhalten (`WorldHeightMap.cpp`
  hängt Objekte an).
- **`Roads.ini`**: `Road <Name>` mit `Texture`, `RoadWidth` und `RoadWidthInTexture`
  (`TerrainRoads.cpp`, `m_terrainRoadFieldParseTable`). Die Datenschicht hält `Road` und `Bridge` weiterhin
  als *Kompatibilitätsblöcke* (`baselines/compatibility.json`: `Road` 63, `Bridge` 27, Ziel „phase-specific
  typed definition compiler“) – wie `Terrain`/`WaterSet`. Es gab deshalb **keine Änderung an
  `CnCNew.Data`-Schema oder Baseline**: `RoadCatalog.FromDefinitions` liest die drei Schlüssel streng über
  `WorldDefinitionReader` (fehlender Schlüssel oder falscher Werttyp wirft, nichts fällt auf Standardwerte
  zurück) und `GameData.MaxRoadSegments`. Die Reihenfolge der Vorlagen ist die der Deklaration
  (`IniSourceLocation`), nicht die kanonische Namensreihenfolge des Graphen: sie bestimmt die Ids
  (`TerrainRoadCollection::m_idCounter`, ab 1) und damit, welcher Straßentyp bei gleicher Stapelung
  zuerst gezeichnet wird.
- **Brücken** (`FLAG_BRIDGE_POINT1/2`, `Bridge`-Blöcke) sind Modelle mit Türmen und Schadenszuständen
  und gehören nicht dazu.

Namen werden wie in `TerrainRoadCollection::findRoad` verglichen (`AsciiString::operator==`: groß/klein
unterscheidend). Ein Straßenpunkt mit unbekanntem Vorlagennamen wird **nicht** gezeichnet, sondern gezählt
(`RoadStats.UnknownTemplates`); das Referenzspiel zeichnet ihn stillschweigend mit dem ersten Straßentyp bei
Breite 8 (`addMapObjects`, `found == false`). Im Realbestand betrifft das drei Segmente mit *leerem* Namen
in `Maps/USA02/USA02.map` – bekannter Datenfehler, in `road-audit` und im Realdatentest ausdrücklich
erlaubt (jeder andere unbekannte Name lässt beide fehlschlagen).

## Aufbau des Modells (`CnCNew.Presentation`)

`RoadModel.Build(objects, heights, catalog, lighting)` ist rein und deterministisch (nur
`float`-Arithmetik auf `Vector2`/`Vector3`, keine Zeit/Zufall/Reihenfolge von Hashtabellen).

1. **`RoadLayout`** (Topologie, Port von `W3DRoadBuffer::loadRoads`):
   - `addMapObjects`/`addMapObject`: Punktpaare werden Segmente; fallen beide Punkte zusammen, wird `x`
     um 0,25 verschoben; identische Segmente (beide Richtungen) werden verworfen; Segmente, die
     Ende an Ende liegen (`count == 1`), werden eingekettet und umsortiert (`checkLinkBefore/After`,
     `moveRoadSegTo`); danach zweiter Durchlauf mit den endgültigen Zählern. Rand/Ober-/Unterkante
     (`top`/`bottom`) liegen `Breite × RoadWidthInTexture / 2` neben der Mittellinie.
   - `updateCountsAndFlags`: für jeden Endpunkt die Zahl *anderer* Segmente desselben Typs, die dort
     enden (1 = Kette, 2 = T, 3 = Kreuzung).
   - `insertTeeIntersections`: 2 → `insertTee` (erst `insertY`; „fast gerade“ ab −cos 30°, sonst Y;
     ein Arm mit |cos| > 0,5 gegen die Durchgangsrichtung ergibt das schräge H-Stück, sonst das
     gerade T), 3 → `insert4Way`. Die beteiligten Segmentenden werden vom Knoten weg verschoben
     (`offset3Way`, `offsetH`, `offsetY`, `offset4Way`) und das Knotenstück als zusätzliches Segment angehängt.
   - `insertCurveSegments`/`insertCurveSegmentAt`: Knicke in einer Kette werden entweder als Kurve aus
     30°-Teilstücken auf Kreisradius `1,5 × Breite` (Flag `…CORNER_TIGHT`: 0,5) gelegt, oder – bei Winkel
     < 27° (`count < 0,9`), `FLAG_ROAD_CORNER_ANGLED` oder wenn die Kurve „aus der Existenz geclippt“
     würde (`DOT_LIMIT`) – per `miter` gehrt (Schnitt der Kantenlinien).
   - `insertCrossTypeJoins`: Enden mit `FLAG_ROAD_JOIN` und ohne Anschluss bekommen eine Alpha-Blende
     (`ALPHA_JOIN`); liegt das Ende auf einem geraden Segment eines *anderen* Typs, richtet sie sich daran
     aus und `adjustStacking` zeichnet den joinenden Typ darüber.
   - `MaxRoadSegments` (4000) begrenzt wie im Original die Segmentzahl (Kartenobjekte *und* eingefügte
     Stücke); was darüber hinausgeht, wird gezählt (`DroppedBySegmentBudget`) statt wortlos wegzufallen.
2. **`RoadGeometry`** (Dreiecke, Port von `preloadRoadSegment`, `loadCurve`, `loadTee`, `loadY`, `loadH`,
   `loadAlphaJoin`, `loadFloatSection`, `loadFloat4PtSection`): jedes Stück ist ein Viereck, bilinear
   in Spalten (eine je Zelle, `uCount = Länge/10 + 1`, mindestens 2) geteilt. Jede Spalte wird auf die
   **größte Eckhöhe** der Zellen unter ihr (`getMaxCellHeight`, `TerrainHeightSampler.MaxCellHeight`)
   *flach quer zur Straße* gesetzt und liegt `HeightScale / 8` (0,078) darüber (`FLOAT_AMOUNT`, gegen
   Z-Fighting). Spalten, die auf der Geraden zwischen ihren Nachbarn liegen (Abweichung < 1,1 Höhenstufen),
   entfallen (`MAX_ERROR`); dabei wird die vertauschte Gewichtung der Referenz (Gewicht der Nachbarn nach
   Abstand zur *aktuellen* Spalte statt zur anderen) absichtlich übernommen, weil sie bestimmt, welche
   Spalten entfallen. Eine Spalte hat zwei Vertices (untere/obere Kante); die UV laufen aus der
   Position relativ zum Bezugspunkt: `u = uOffset + U/(4·Breite)`, `v = vOffset − V/(4·Breite)`
   (eine Texturbreite deckt vier Straßenbreiten ab, die Textur wiederholt sich).
3. **Texturlayout**: jede Straßentextur ist ein 512×512-Streifenatlas mit festen Rechtecken für
   die Stücke (Offsets in 512stel, aus `W3DRoadBuffer`; Streifen, Kurve, Y und Vierwege an der selbst
   dekodierten `TRTwoLane.dds` nachgesehen):

   | Stück | `uOffset` | `vOffset` | Bemerkung |
   |---|---|---|---|
   | Segment | 0 | 85 | Streifen oben, Breite `RoadWidthInTexture` |
   | Kurve | 4 | 255 (eng: 425) | Keilform, Viereck an den Keil angepasst |
   | T / Vierwege | 425 | 255 / 425 | Breite `1,03 ×` (`TEE_WIDTH_ADJUSTMENT`) |
   | Y | 255 | 226 | Fünfeck, Viereck deckt auch transparente Ecken |
   | H (und gespiegelt) | 202 | 364 | schräger Abzweig |
   | Alpha-Join | 106 | 425 | 48 Pixel + 8 Pixel Überlappung |

4. **Beleuchtung**: wie `RoadSegment::updateSegLighting` die statische Terrainbeleuchtung des
   *nächsten Höhenvertex* (`x/10 + 0,5` abgeschnitten, plus Rand) – hier aus
   `TerrainLighting.Shade(TerrainHeightSampler.VertexNormal(…))`, dieselbe Formel wie `TerrainRenderModel`
   (ein Test vergleicht beide). Alpha der Vertexfarbe ist 255; die Textur bestimmt die Transparenz.
5. **Ausgabe**: `RoadMesh` je Vorlage (`Template`, `Texture`, `Stacking`, Positionen, UV, Farben, Indices,
   Bounds); Dreiecke sind von oben gesehen gegen den Uhrzeigersinn (die Referenz überlässt das der
   Segmentrichtung), Reihenfolge der Stücke wie `loadRoadsInVertexAndIndexBuffers`: Segment, Kurve, T,
   Vierwege, Y, H, H gespiegelt, Alpha-Join.

## Zeichnen (`CnCNew.Client.Stride`)

- `RoadMeshBuilder.Build` macht aus den Meshes `WaterVertex`-Puffer (Position, Farbe, UV, Nebel-UV) und
  dekodiert die Beleuchtung im linearen Farbraum wie beim Wasser (`PackedColor.SrgbDecodeTable`).
- `TerrainMaterials.CreateRoad` entspricht `RoadShader2Stage` (`ST_ROAD_BASE`): Textur × Vertexfarbe ×
  Nebel, `SRCALPHA`/`INVSRCALPHA`, Tiefentest `LessEqual`, **kein Tiefenschreiben**, Textur wiederholt
  (`Wrap`), doppelseitig. Die Fog-Textur ist dieselbe wie bei Terrain und Wasser; der Nebel multipliziert nur
  RGB, die Alpha bleibt (verschattete Straßen sind nicht transparenter). Wichtig: das Emissive-Feature
  verwirft die Alpha der Textur, wenn `UseAlpha` fehlt – ohne sie erschienen die transparenten Ecken des
  Y- und H-Stücks als graue Rechtecke (so zuerst gesehen und behoben).
- `TerrainLayer.CreateRoads` baut ein Mesh je Straßenvorlage und legt die Entitäten in Stapelreihenfolge
  an (`Stacking`, dann Deklarationsreihenfolge, wie `drawRoads`). Stride sortiert transparente Meshes nach
  dem Abstand ihrer Bounding-Box; damit die Reihenfolge nicht von der Kamera abhängt, bekommen alle
  Straßen-Meshes **dieselbe** Bounding-Box (die Vereinigung aller): gleiche Schlüssel behalten die
  Anlegereihenfolge. Eine Textur, die sich nicht auflösen lässt, lässt `Attach` fehlschlagen (kein
  Magenta-Ersatz für Straßen).
- Die Straße verdeckt kein Objekt (Tiefentest) und wird von Objekten verdeckt; Fog/Shroud dunkeln sie wie das
  Gelände ab (gesehen am Shroud-Rand von `Desert Fury`, siehe unten).
- Debug: `CNCNEW_ROADS=0` schaltet die Straßen ab (Vergleich mit/ohne); `CNCNEW_TERRAIN_REVEAL=1` zeigt
  die ganze Karte (Straßen liegen meist außerhalb der Startsicht).

## Messwerte (`PresentationTool road-audit .local/content`, Stand 4. Oktober 2026; neu messen)

- 150 Karten gebaut, 0 Fehler, **130** Karten mit Straßen.
- **40.960** Punktpaare → **40.801** Segmente (156 Doppelte verworfen, 7 unpaarige `POINT1`-Objekte, 3 mit
  leerem Namen verworfen), insgesamt **69.047** Stücke und **206.956** Dreiecke.
- Stücke: Segment 40.801, Kurvenstück 20.446 (aus 8.229 gekurvten Ecken; 18.231 gehrte Ecken, davon 390
  abgebrochene Kurvenversuche), T 3.518, Vierwege 506, Y 546, schräges H 276 + 233 gespiegelt, Alpha-Join 2.721
  (1.420 gegen einen anderen Straßentyp).
- 59 von 63 `Road`-Vorlagen werden verwendet; `StreetHoleCover` (`TRStrtHoleCvr.tga`) hat keine Textur im
  Inhalt, wird aber von keiner Karte benutzt – alle benutzten Texturen lösen über `W3dTexturePathResolver`
  auf (`.tga` → `Art/Textures/*.dds`).
- Größte Karte: `MD_GLA05_END` mit 1.811 Paaren, 2.879 Stücken, 8.628 Dreiecken – weit unter
  `MaxRoadSegments` 4000 (keine Karte stößt daran).
- Im Client (`Attach`, Release, ein Lauf, nicht deterministisch): `Alpine Assault` 313 Segmente, 1.718 Dreiecke,
  6 Meshes in 73 ms (Modellbau, Texturladen, Puffer); `Death Valley` 363 Segmente, 2.976 Dreiecke, 12 Meshes in 73 ms.
  Die Zeit steht in der `terrain:`-Zeile des Clients.
- Baseline unverändert: `IniTool compat-check` meldet 751 Einträge (die `Road`-Blöcke bleiben Kompatibilitätswerte).
- Prüfung: kein Vertex nicht-endlich oder mehr als 300 Einheiten außerhalb der Punkte-Box seiner Karte;
  zwei Läufe liefern identische Meshes.

## Sichtprüfung

Aufnahmen im Scratchpad der Session (nicht im Repo; `scripts/capture-world.ps1` enthält jetzt die
Szenen `roads-junctions`, `roads-crossing`, `roads-overview`, alle mit `CNCNEW_TERRAIN_REVEAL=1`). Was ein
Mensch gegen die Bilder abhaken soll:

- Die Straße ist ein durchgehendes Band, folgt dem Gelände, hat die erwartete Breite und die Mittellinie
  läuft in den Kurven und Knoten sauber weiter (gesehen: Kiesstraße mit Kurven und T-Stück, Asphalt mit
  Vierwege-Kreuzung um 45° gedreht, Y-Kreuzung mit Zebrastreifen, schräge Einmündungen, Bahngleise, Parkplatzlinien,
  Gehwege neben Asphalt und Pflaster, Rollfeld mit Markierungen auf `Red Rock`).
- Knoten sind geschlossen, an den Rändern keine Lücken. Die Ränder des Y/H-Stücks sind weich ausgeblendet
  (kein graues Rechteck – das war der gefundene Alpha-Fehler).
- Straßen verschwinden im Shroud mit dem Gelände (`Desert Fury`, Startansicht).
- **Offen für den Blick eines Menschen**: kleine dunkle Quadrate an manchen Gehweg-Außenecken (`Bear Town
  Beatdown`, Gehweg + Pflaster; wohl Überlappung der gehrten Ecke, im Original vermutlich ähnlich), und
  die Überlappung vieler Markierungstypen auf `GC_LaserGeneral` (hier nur über `Red Rock`/`Bear Town` gesehen).
  Rauchtests und Hashes beweisen Reproduzierbarkeit, nicht Richtigkeit.

## Abweichungen und Grenzen

- **Unbekannte Vorlagen** werden gezählt, nicht (wie im Original) mit dem ersten Straßentyp gezeichnet.
- **Nicht initialisierte Felder** des Originals (`isJoin`, `count` an erzeugten Stücken) sind als
  `false`/0 angenommen; für Stücke, die der Algorithmus selbst einfügt, ist das die einzige sinnvolle Lesart.
- **Kein Sichtbereichs-Neuaufbau**: das Original lädt nur die in Sicht liegenden Segmente in dynamische Puffer
  (`MaxRoadVertex` 3000 / `MaxRoadIndex` 5000 je Typ) und lässt bei Überlauf Stücke weg. Hier liegt alles in
  statischen Puffern, es gibt nichts zu überlaufen (208 k Dreiecke für alle Karten zusammen).
- **Keine Wolken-/Rauschpässe** (`ST_ROAD_BASE_NOISE*`) – dasselbe Fehlen wie beim Terrain.
- **Keine Neuberechnung bei Geländeänderung**: Straßen werden einmal beim Anlegen aufgebaut. Das Original ruft
  bei geändertem Gelände (Bau von Strukturen, `updateCenter`) `updateLighting` und baut die Puffer neu; der Client
  verformt das Gelände nicht.
- **Dynamische Lichter** (`loadLit4PtSection`) sind im Original deaktiviert (`#if 0`) und fehlen hier ebenso.
- **Reihenfolge überlappender Straßentypen** hängt am Stride-Sortierschlüssel (gleiche Bounding-Box); sie ist
  wiederholbar, aber nicht über eine API garantiert. Fällt sie bei einer Stride-Änderung anders aus, wäre die
  Folge eine falsche Überdeckung von Markierungen, nicht ein Fehler.
- **Gleitkomma**: Sinus/Kosinus/Wurzel kommen aus der .NET-Laufzeit; kleinste Abweichungen zu den
  `WWMath`-Näherungen des Originals sind möglich und für die Darstellung bedeutungslos.
- **Gehrte Ecken** (`miter`) verwenden unendliche Geradenschnitte wie `LineSegClass::Find_Intersection`;
  fast parallele Kanten können lange Spitzen ergeben. In den 150 Karten tritt das nicht auf (Audit-Grenze 300).
- **Brücken** sind nicht Teil dieses Stücks, und die Simulation sieht Straßen nicht (keine Fahrspuren, keine Geländeänderung).

## Tests und Werkzeuge

- `tests/CnCNew.Presentation.Tests/RoadTests.cs` (synthetisch, ohne Originaldaten): Katalog, gerades Segment
  inklusive UV/Beleuchtung/Höhe, Spalten am Hügel, 90°-Kurve, Gehrung, T/H/Y/Kreuzung, Alpha-Join und
  Stapelung, Zählung (unbekannt, doppelt, unpaarig, Budget), Determinismus und Windung, Sampler
  (`MaxCellHeight`, Vertexnormalen gegen das Terrainmodell).
- `RoadRealDataTests.cs` (mit `--content-dir`): 63 Vorlagen, alle 150 Karten bauen, jede Textur löst auf, goldene
  Zahlen (40.960 / 40.801 / 206.956), Determinismus an `Alpine Assault`.
- `dotnet run -c Release --project tools/CnCNew.PresentationTool -- road-audit .local/content [--map "Maps/…"]`
  (mit `--map` zusätzlich Knotenpositionen und Mesh-Bounds für Aufnahmen).
