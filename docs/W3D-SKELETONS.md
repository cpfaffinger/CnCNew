# W3D-Hierarchien, Animationen und Modelle

Stand: 30. September 2026

`CnCNew.W3D` liefert neben Meshes und Materialien (`W3D-MATERIALS.md`) jetzt ganze Modelle: Skelett
(Hierarchie), HLOD mit Sub-Objekten pro Knochen, unkomprimierte und komprimierte Animationen,
Sichtbarkeitskanäle, Skinning-Einflüsse sowie eine rendererunabhängige Posenauswertung. Die Schicht
kennt weder Stride noch `CnCNew.Content`; Dateien kommen über einen Resolver
`Func<string, Stream?>` (virtueller Pfad → Stream, `null` = fehlt).

## Einstiegspunkte für Integratoren

| Typ | Zweck |
| --- | --- |
| `W3dAssetLibrary(resolver, "Art/W3D")` | Lädt und cacht thread-sicher `GetModel(name)`, `GetSkeleton(hierarchyName)`, `GetAnimation("HIER.ANIM")`. |
| `W3dAssetFile.Read(stream)` | Dekodiert alle Top-Level-Objekte einer Datei (Meshes, Hierarchien, HLODs, Animationen, Boxen, Null-Objekte). |
| `W3dModel` | Geteiltes, unveränderliches Modell: `Skeleton`, `SubObjects` (LOD, Knochen, Mesh/Box, Flags), `FindBone`, `FindBones`, `FindSubObject`, `CreatePose()`, statisch `SkinVertices`. |
| `W3dPose` | Instanzzustand: `SetRestPose(root)`, `Evaluate(binding, frame, root, rawSampling)`, `BoneWorldTransforms`, `IsBoneVisible`, `SetTurretYaw/SetTurretPitch/SetRecoil/SetBoneOverride`. |
| `W3dAnimationBinding` | Animation gebunden an eine Pivotanzahl; `Sample(pivot, frame, …)`. |
| `W3dAnimationClock.GetFrame(mode, frameCount, frameRate, elapsedSeconds, speed)` | Zeit → Frame für `Loop`, `Once`, `OnceBackwards`, `LoopBackwards`, `LoopPingPong`, `Manual`. |
| `W3dSkeletonAudit.Run(names, library)` | Vollbestandsprüfung, genutzt von `W3dTool audit-skeletons` und dem Realdatentest. |

Matrizen folgen der `System.Numerics`-Zeilenvektorkonvention: `world = local * parentWorld`,
Punkte werden mit `Vector3.Transform(p, M)` transformiert. Knochen 0 ist die Wurzel und erhält die
übergebene Objektmatrix. Starre Sub-Objekte werden mit der Weltmatrix ihres Knochens gezeichnet;
Skins (`IsSkinned`) werden per `SkinVertices` in den Raum der Wurzelmatrix deformiert und ignorieren
ihren HLOD-Knochen. Modell, Skelett und Animationsbindung sind unveränderlich und werden von allen
Instanzen geteilt; teuer ist nur `W3dPose` pro Instanz (eine Matrix, ein Bool und ein optionaler
Override je Knochen).

## Verifizierte Chunk-Layouts

Alle Größen wurden gegen den Realbestand (`.local/phase2-import-a/Art/W3D`, 8.897 Dateien) per
Hex-/Skriptanalyse geprüft; Quelle der Strukturen ist `w3d_file.h` (GeneralsMD/WW3D2).
Little Endian, Namen sind nullterminierte Latin-1-Felder fester Länge.

| Chunk | Id | Layout (Bytes) | Befund im Bestand |
| --- | --- | --- | --- |
| HIERARCHY_HEADER | 0x101 | Version u32, Name[16], NumPivots u32, Center vec3 = 36 | immer 36, Version 4.1 |
| PIVOTS | 0x102 | je Pivot: Name[16], Parent u32 (0xFFFFFFFF = Wurzel), Translation vec3, Euler vec3, Rotation quat (x,y,z,w) = 60 | Eltern stets vor Kindern, Pivot 0 Wurzel |
| PIVOT_FIXUPS | 0x103 | je Pivot 4×3 float (MAX-Matrix, Zeile 3 = Translation) = 48 | in allen 6.481 Hierarchien, nur informativ |
| ANIMATION_HEADER | 0x201 | Version u32, Name[16], HierarchyName[16], NumFrames u32, FrameRate u32 = 44 | Version 4.1, 30 fps |
| ANIMATION_CHANNEL | 0x202 | FirstFrame u16, LastFrame u16, VectorLen u16, Flags(Typ) u16, Pivot u16, Pad u16, dann (Last−First+1)·VectorLen float | 21.490 Kanäle mit Überhang (s. u.) |
| BIT_CHANNEL | 0x203 | FirstFrame u16, LastFrame u16, Flags u16, Pivot u16, Default u8, dann ⌈n/8⌉ Bytes LSB-first | Größe exakt 9 + ⌈n/8⌉ |
| COMPRESSED_ANIMATION_HEADER | 0x281 | wie 0x201, aber FrameRate u16 + Flavor u16 | Version 0.1, Flavor 0 |
| COMPRESSED_ANIMATION_CHANNEL (time-coded) | 0x282 | NumTimeCodes u32, Pivot u16, VectorLen u8, Flags u8, dann je Key TimeCode u32 + VectorLen float | Größe exakt, erster Key Frame 0, streng steigend |
| COMPRESSED_ANIMATION_CHANNEL (adaptive delta) | 0x282 | NumFrames u32, Pivot u16, VectorLen u8, Flags u8, Scale f32, dann Frame-0-Vektor float und Pakete | nicht im Bestand, synthetisch getestet |
| COMPRESSED_BIT_CHANNEL | 0x283 | NumTimeCodes u32, Pivot u16, Flags u8, Default u8, dann Keys u32 (MSB = Zustand) | nicht im Bestand, synthetisch getestet |
| HLOD_HEADER | 0x701 | Version u32, LodCount u32, Name[16], HierarchyName[16] = 40 | Version 1.0, immer 1 LOD |
| HLOD_LOD_ARRAY / AGGREGATE_ARRAY / PROXY_ARRAY | 0x702 / 0x705 / 0x706 | Container: SUB_OBJECT_ARRAY_HEADER (0x703: ModelCount u32, MaxScreenSize f32 = 8), dann SUB_OBJECT (0x704: BoneIndex u32, Name[32] = 36) | nur LOD-Arrays; 0 Aggregate, 0 Proxies |
| BOX | 0x740 | Version u32, Attributes u32, Name[32], Color rgb+pad, Center vec3, Extent vec3 = 68 | 679 Boxen, Attribut 1 (oriented) bzw. 2 (aligned) |
| NULL_OBJECT | 0x750 | Version u32, Attributes u32, Pad[2] u32, Name[32] = 48 | nicht im Bestand |
| VERTEX_INFLUENCES | 0x0E (im Mesh) | je Vertex BoneIdx u16 + 6 Byte Padding = 8 | 169 Skins, Padding immer 0 |

Kanaltypen (`Flags`): 0/1/2 = Translation X/Y/Z (VectorLen 1), 3/4/5 = Euler XR/YR/ZR (VectorLen 1),
6 = Quaternion (VectorLen 4). Die Enumeratoren `TIMECODED_*`/`ADAPTIVEDELTA_*` (7–14) werden von den
Ladern (`hrawanim.cpp`/`hcanim.cpp` `add_channel`) nie als Kanaltyp benutzt; auch komprimierte Kanäle
tragen 0/1/2/6. Bitkanaltyp 0 = Sichtbarkeit; andere Werte verwirft das Original stillschweigend –
hier sind sie ein Decodefehler.

## Strenge und bewusste Kompatibilitätsfälle

Decodefehler (`InvalidDataException`) sind: unbekannte Top-Level-Chunks, unbekannte oder doppelte
Unterchunks, falsche Strukturgrößen, Eltern hinter ihren Kindern, Hierarchie-/Animationsversion vor
3.0 (die Wurzelverschiebung des Originals ist nicht implementiert, der Bestand hat keine solchen
Dateien), unbekannte Kanaltypen und Vektorlängen, Kanäle außerhalb der Frameanzahl, Time-Codes, die
nicht bei Frame 0 beginnen oder nicht streng steigen, unbekannter Flavor, doppelte Kanäle gleichen
Typs für einen Pivot, Skin-Padding ungleich 0 (spätere SAGE-Spiele legen dort zweite Knochen und
Gewichte ab), Skin-Knochen außerhalb des Skeletts und HLOD-Knochen außerhalb des Skeletts.

Explizit gezählte, nicht fatale Fälle:

- **Überhang unkomprimierter Kanäle**: Ein Exporterfehler schrieb mehr Floats als der Framebereich
  angibt; `MotionChannelClass::Load_W3D` (motchan.cpp) überspringt sie. CnCNew ebenso, gezählt in
  `W3dAnimation.SurplusChannelBytes` (Bestand: 8.333.276 Byte).
- **Kanäle jenseits der Hierarchie**: `HRawAnimClass::Load_W3D` verwirft Kanäle mit Pivot ≥
  Pivotanzahl. `W3dAnimationBinding.DiscardedChannels` listet sie (Bestand: 233).
- **Euler-Kanäle** werden geladen, vom Original aber nie ausgewertet; `IgnoredEulerChannels` zählt
  sie (Bestand: 0).
- **Nicht auflösbare HLOD-Einträge** (Exportreste wie Splines, `RECTANGLE01`, `... TOE0`): der
  Assetmanager des Originals liefert `null`, `HLodClass` überspringt sie. CnCNew listet sie in
  `W3dModel.UnresolvedSubObjects` (Bestand: 45).
- **Ignorierte Render-Objekte**: Emitter, Dazzle, Light, Morph-Animation, HModel, LOD-Model,
  Collection, Points, Aggregate, Sphere, Ring, Lightscape, Sound, ShaderMesh werden als
  `W3dIgnoredChunk` mit Art und Offset geführt, nicht interpretiert (Bestand: 3 Emitter, sonst 0).
- **CRLF-Textmodusschäden**: fünf `UISabotr_*`-Animationen werden über die bestehende Rekonstruktion
  in `W3dFileReader` repariert und im Audit namentlich ausgewiesen.

## Auswertungsregeln

**Basispose** (`htree.cpp` `HTreeClass::read_pivots`, `Base_Update`): lokale Matrix je Pivot
`R(Rotation) * T(Translation)`; Welt = lokal · Elternwelt; Pivot 0 erhält die Wurzelmatrix, seine
Datei-Transformation wird ignoriert. Die Euler-Winkel der Pivots sind redundant und werden nur
gespeichert.

**Animation** (`HTreeClass::Anim_Update`): je Pivot 1..n−1
`lokal = Override · R(qAnim) · T(tAnim) · Basis`. Animationswerte sind Deltas zur Basispose.
Pivots ohne Kanal erhalten Translation 0 und Einheitsquaternion, also die Basispose. Pivots ab der
Pivotanzahl der Animationshierarchie bleiben in der Basispose und sichtbar. Wurzelkanäle werden
(wie im Original, Schleife ab Pivot 1) ignoriert.

**Unkomprimiert** (`hrawanim.cpp` `Get_Translation`/`Get_Orientation`): `frame0 = round_half_even(frame − 0.499999)`
(x87-`fistp` von `WWMath::Float_To_Long`), `frame1 = frame0 + 1`, bei `frame1 ≥ NumFrames` auf 0
gewrappt; Translation linear, Rotation per Slerp. Frames außerhalb `First..Last` eines Kanals liefern
Identität (0 bzw. Einheitsquaternion), nicht die Basispose. Retail-EA verwendet für Raw-Animationen
die nicht interpolierende Überladung `HTreeClass::Anim_Update(root, HRawAnimClass*, frame)`
(`round_half_even(frame)`, Wrap auf 0); GeneralsGameCode aktiviert standardmäßig die Interpolation
(`WW3D_ENABLE_RAW_ANIM_INTERPOLATION`). `W3dRawSampling.Interpolated` (Standard) und
`W3dRawSampling.Nearest` bilden beide Varianten ab.

**Slerp** (`quat.cpp` `Fast_Slerp`, C-Fallback): kürzester Bogen (negatives Skalarprodukt dreht das
Zielquaternion), unterhalb `1 − cos θ < 1e−8` linear, keine Renormalisierung. Die tabellenbasierten
`Fast_Acos`/`Fast_Sin` sind durch exakte `MathF`-Funktionen ersetzt.

**Time-coded** (`motchan.cpp` `TimeCodedMotionChannelClass::Get_Vector`, `Get_QuatVector`,
`binary_search_index`): gesucht wird der letzte Key mit Zeit ≤ `(uint)frame`. Ist er der letzte Key
oder trägt der Folgekey das MSB („binary movement“), gilt der Wert des gefundenen Keys unverändert
(Stufe), sonst linear bzw. Slerp mit `ratio = (frame − t1)/(t2 − t1)`.

**Adaptive Delta** (`motchan.cpp` `AdaptiveDeltaMotionChannelClass::decompress`, `getframe`,
Filtertabelle `filtertable`): Frame 0 steht als Float-Vektor am Datenanfang. Frame f ≥ 1 liegt in
Gruppe `(f−1) >> 4` an Nibble `(f−1) & 15`; je Gruppe und Komponente folgt ein 9-Byte-Paket
(Filterindex + 16 Nibbles, gerade Nibbles im Low-, ungerade im High-Halbbyte, vorzeichenbehaftet
−8..7). Wert(f) = Wert(f−1) + nibble · filter[index] · Scale. Filtertabelle: Einträge 0–15 sind
1e−8 … 1e7, Einträge 16–255 sind `1 − sin(90° · i / 240)` (Winkel in double gerechnet, `sinf`).
CnCNew dekodiert einmal in eine dichte Tabelle; die Additionsreihenfolge entspricht der
inkrementellen Originaldekodierung. Abfrage: `frame1 = (uint)frame`, Folgeframe auf den letzten
Frame geklemmt (kein Wrap), Translation linear, Rotation per Slerp.

**Sichtbarkeit**: Raw-Bitkanal (`BitChannelClass::Get_Bit`) liefert außerhalb `First..Last` den
Default, sonst das Bit `frame − First`; Frameindex `(int)frame`. Time-coded (`TimeCodedBitChannelClass::Get_Bit`):
letzter Key mit Zeit ≤ Frame, vor dem ersten Key gilt Key 0; der gespeicherte Default wird vom
Original nicht verwendet. Knochen ohne Kanal sind sichtbar. Sub-Objekte sind anfänglich sichtbar,
wenn sie renderbar sind und das Mesh-Flag `HIDDEN` fehlt (`MeshClass` ruft `Set_Hidden`).

**Überschreibungen** (`pivot.cpp` `PivotClass::Capture_Update`, `W3DModelDraw.cpp`
`handleClientTurretPositioning`, `handleClientRecoil`): ein gefangener Knochen erhält nach Basis und
Animation eine Nachmultiplikation im eigenen Raum und wird sichtbar erzwungen. Turm: `Rotate_Z(yaw)`,
Rohrneigung: `Rotate_Y(−pitch)`, Rückstoß: `Translate_X(−shift)`. Die Wurzel kann nicht gefangen
werden (W3DModelDraw behandelt Knochen 0 als „nicht gefunden“). Die Variante mit
Welt-Translation (`WorldSpaceTranslation`) ist nicht implementiert, weil W3DModelDraw sie nicht nutzt.

**Zeit → Frame** (`animobj.cpp` `Compute_Current_Frame`): spielbarer Bereich 0..NumFrames−1, eine
Schleife umfasst NumFrames−1 Frames. `Loop`: `t mod (n−1)`; `Once`: auf n−1 geklemmt;
`OnceBackwards`: `(n−1) − t`, bei 0 geklemmt; `LoopBackwards`: `(n−1) − (t mod (n−1))`;
`LoopPingPong`: Dreieckswelle mit Periode 2(n−1). Das Original schreitet inkrementell fort; die
geschlossene Form ist für monotone Zeit gleichwertig, weicht aber an den „noch zu weit“-Resets des
Originals (Überschreiten einer ganzen Periode in einem Schritt) und an der Grenze
`LoopBackwards` bei exakt einem Periodenvielfachen (n−1 statt 0) ab.

**Knochensuche** (`W3DModelDraw.cpp` `doSingleBoneName`, `findSingleBone`,
`rendobj.cpp` `Get_Sub_Object_By_Name`): `FindBone` ist exakt und case-insensitiv (erster Treffer).
`FindBones("MUZZLE")` liefert den unverzierten Namen und `MUZZLE01..MUZZLE99` bis zur ersten Lücke
als Knochen (Knochen 0 zählt als nicht gefunden); nur wenn nichts als Knochen existiert, werden
dieselben Namen als Sub-Objekte des detailliertesten LOD gesucht (voller Name, dann Suffix nach dem
Punkt) und deren Knochen verwendet. So werden INI-Namen aus `Turret`, `TurretPitch`,
`WeaponFireFXBone`, `WeaponLaunchBone`, `WeaponRecoilBone` und `WeaponMuzzleFlash` aufgelöst.

**Skinning** (`meshgeometry.cpp` `MeshGeometryClass::get_deformed_vertices`): jeder Vertex hängt an
genau einem Knochen, Position = Vertex · Knochenwelt, Normale = Rotationsanteil ohne
Renormalisierung.

**Namensauflösung**: Modell `X` und Hierarchie `X` liegen in `X.W3D`, Animation `HIER.ANIM` in
`ANIM.W3D` (`assetmgr.cpp` `Get_HAnim`, `Get_HTree`). Das Original registriert Hierarchien
„first loaded wins“ (`htreemgr.cpp` `Load_Tree`) und hängt damit von der Ladereihenfolge ab. CnCNew
löst deterministisch auf: ein Modell verwendet die gleichnamige Hierarchie der eigenen Datei, sonst
`<HierarchyName>.W3D`; eine Animation ebenso die Hierarchie ihrer eigenen Datei, sonst
`<HierarchyName>.W3D`. Zwölf Hierarchienamen sind im Bestand mehrfach definiert (teils mit
verschiedener Pivotanzahl, etwa `CVBAIKTRANSNR_D` mit 14/10/10); das Audit listet sie.

## Messwerte (`W3dTool audit-skeletons`, Release)

Gemessen am 30. September 2026 auf `.local/phase2-import-a`, zwei Läufe byteidentisch
(Stdout-SHA-256 `FEE71ECF45DF6CBABF5FB371546A4E0C52AA1C8BB773B144CAB3895D22C21EE7`):

| Größe | Wert |
| --- | --- |
| W3D-Dateien in `Art/W3D` / fehlerfrei dekodiert | 8.897 / 8.897 |
| Hierarchien / Knochen gesamt / maximal | 6.481 / 134.535 / 345 |
| HLOD-Modelle / Modelle aus nackten Meshes | 6.505 / 1.172 |
| Hierarchiequelle eigene Datei / extern / Default | 6.409 / 96 / 1.172 |
| Animationen unkomprimiert / time-coded / adaptive delta | 4.074 / 21 / 0 |
| Kanäle unkomprimiert X / Y / Z / Q | 14.272 / 12.360 / 15.718 / 36.350 |
| Kanäle time-coded X / Y / Z / Q | 581 / 569 / 562 / 722 |
| Sichtbarkeitskanäle (raw / time-coded) | 1.042 / 0 |
| Quaternion-Samples / nicht normiert oder NaN | 4.650.500 / 0 |
| Meshes / Skins / Skin-Vertices | 70.585 / 169 / 22.710 |
| Sub-Objekte / renderbar / Boxen / anfangs verborgen | 71.263 / 70.585 / 678 / 48 |
| Nicht auflösbare HLOD-Einträge | 45 |
| Nicht auflösbare Hierarchiereferenzen | 6 (Animationen `GENERALSKEL.GENERAL_*`, Hierarchie fehlt im Bestand) |
| Verworfene Kanäle jenseits der Hierarchie | 233 |
| Überhang unkomprimierter Kanäle | 8.333.276 Byte |
| Ignorierte Chunks | 3 Emitter |
| Textmodus-reparierte Dateien | 5 |
| Lesen + Dekodieren aller Dateien | 3,9 s (Release, inkl. Datei-I/O) |
| Gesamtaudit inkl. Modellbau, Bindung, Posenstichproben | 4,8 s |

Der Realdatentest (`dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir …`) verlangt 0
Fehler und prüft diese Zählungen sowie `AVCrusader` (Turm dreht das Rohr um den Turmpivot,
`TurretFX01` per Präfix), `ABArFrcCmd` mit eingebetteter Animation, `AIRngr_SKN` mit
`AIRngr_SKL.AIRngr_STA` (Skin bewegt sich) und die time-coded Animation `AIOFCR_SKL.AICIAOFF_PLNT`.

## Bekannte Grenzen

- Aggregate- und Proxy-Arrays werden dekodiert und am Modell bereitgestellt, aber nicht zu Objekten
  aufgelöst; der Bestand enthält keine.
- LOD-Umschaltung nach `MaxScreenSize` ist nicht implementiert; alle 6.505 HLODs haben genau einen
  LOD. `W3dModel` liefert alle LODs mit Index.
- Zwei-Animationen-Blend (`Blend_Update`) und Animationskombinationen (`Combo_Update`) sind nicht
  implementiert; die Posenauswertung kennt eine Animation plus Überschreibungen.
- Welt-Translation für gefangene Knochen, Pre-3.0-Dateien und Morph-Animationen werden nicht
  unterstützt (nicht im Bestand bzw. nicht von Generals genutzt).
- Adaptive-Delta-Kanäle und time-coded Bitkanäle sind nur synthetisch getestet, weil der Bestand
  keine enthält.
- `Fast_Slerp` rechnet im Original mit Tabellen-Approximationen; CnCNew rechnet exakt, die Posen
  können daher im Bereich der Tabellenfehler abweichen.
- Meshes eines Modells werden beim Laden vollständig dekodiert (nicht lazy); der Speicher wird aber
  pro Modell einmal belegt und über `W3dAssetLibrary` von allen Instanzen geteilt.
- Emitter und alle anderen ignorierten Render-Objektarten werden nur gezählt, nicht interpretiert.
