# The patch layer: GeneralsGamePatch over the original

Phase 30, item 4 (S3-M7 (b), (c)). The community patch **GeneralsGamePatch** (TheSuperHackers, `references/generals-game-patch`, the directory `Patch104pZH`) changes
the game's data: INI files, window layouts, map scripts and strings, models, textures, sounds. This port takes it as **a layer of its own above the original
installation**, made by the import, never committed, with its own definition fingerprint - so a room on a server names original or patch by that fingerprint,
and only clients with the same one join.

## 1. How it is built

- `CnCNew.IO.GameFileSystem.OpenInstallation(root, overlay)`: a `GameFileOverlay` (name, virtual path → file) above the installation. Each of its files becomes
  instance zero of its path; the installation's own loose files and archive entries stay behind it as further candidates.
- `CnCNew.Import.RuntimeContentImporter.ImportAsync(..., layer:)`: the same import as for the original, through that file system. The content manifest records
  for every replaced file both candidates and selects `overlay:<layer>:<path>`, and names the layer in the importer options (`layer`, `layerSource`, `layerFiles`,
  `layerSha256` - SHA-256 over the layer's paths and files). Without a layer the manifest is byte for byte the original export (the master check depends on it).
- `CnCNew.Import.GeneralsGamePatchLayer`: which of the patch's files form the layer. The patch builds its release with its own tool chain from
  `ModBundleCoreItems.json`; the layer takes the files of those core bundles that the patch ships as they are, and counts every other file by reason:

| Taken (`GameFilesEdited/...`) | Not taken, by reason |
| --- | --- |
| `Data/INI/**/*.ini`, `Maps/**/*.ini`, `Maps/**/*.str`, `Window/**/*.wnd`, `Art/W3D/*.W3D`, `Art/Textures/*.tga` and `*.dds`, `Data/Cursors/*.ani` | texture sources the patch converts into DDS/TGA (Photoshop, TIFF, TGA to be mip-mapped); Blender scenes it builds into W3D; string sources it compiles into a CSF table (`generals.str`); notes of its repository; everything outside its core bundles (languages, audio, optional items) |

- `AssetTool import-layer <game> <content> --patch <Patch104pZH> [--patch-source TEXT]` checks the game directory against the master (as an installation does) and
  imports into a content directory of its own, e.g. `game/content-patch`. The client and the tools take it with `--content-dir`; its definition fingerprint is
  the patch's.

## 2. Measured (14 October 2026, patch commit `6c7f40d6af6a4babb77a33a4502ec7656446c13e`)

- The layer: **366 files** taken; 371 not taken (216 texture sources, 6 Blender scenes, 2 string sources, 41 notes, 106 outside the core).
- The import: 25,636 runtime files (the original export has 25,608), **338 replaced and 28 added** by the layer, 92 s.
- One model of the patch (`ABLGCLink_L.W3D`) ends with one byte after its last chunk, which the original's loader ignores and the reader refused; the reader now
  counts such a remainder (`W3dFile.TrailingBytes`, `80ec76c`).
- **The layer's definitions do not compile yet** in this port's strict compiler. Found, in this order:
  1. `Data/INI/ParticleSystem.ini`: four colour key lines with an extra token (`Color2 = R:255 G:150 B:255 0 5`). The original reads the colour and the frame
     (`ParticleSystemTemplate::parseRGBColorKeyframe`) and never looks at the rest of the line; this port refuses unread text. It needs an explicit, counted
     compatibility case of its own.
  2. Two modules the shipped data never uses, so this port never implemented them: `StatusBitsUpgrade` (`Data/INI/Object/AirforceGeneral.ini`) and
     `SabotageSupplyDropzoneCrateCollide`. Both are engine modules of the original; they need catalogue schema, binding, implementation and tests like every
     module.
  What follows after these is not known: the compiler stops at the first error.

## 2a. Phase 32: the layer compiles and plays (15 October 2026)

- **The colour lines** (`477c15e`): a colour keyframe with tokens after its frame keeps colour and the first frame, as the original's parser reads it, and the
  compiler writes `compatibility-ignored-tokens` for it - counted, never silent.
- **The two modules** (`a3834a8`, `docs/SIMULATION-CORE.md` section 50): `StatusBitsUpgrade` and `SabotageSupplyDropzoneCrateCollide`, written from the original's
  source with tests.
- **Nothing else was found**: after these the layer's definitions compile to the end. Its compatibility inventory is `baselines/compatibility-patch.json`
  (706 entries; the original's has 713), checked by `IniTool compat-check game/content-patch baselines/compatibility-patch.json`.
- **Played** (60 Hz, Release, the definition fingerprint of the layer `AC16ECF6B2454A24` against `4E6F6F5623FE86DC`): `SimTool reference-manifest results` over
  the 50 runs of the reference manifest, twice on the layer with byte-identical results. Against the original: all **27 mission runs end the same way**; the
  **16 AI pairings** are decided 14 times in both, **11 of 16 with the same outcome** - ChinaTankGeneral against AirForce goes to AirForce on Alpine Assault,
  Bitter Winter and Death Valley, Alpine Assault America against China becomes an America win, Armored Fury GLA against America a stalemate; the four
  eight-player runs stay stalemates within their budget; the **retail reference match** finds no winner in 432,000 ticks (two runs and a continuation from the
  snapshot identical) where the original ends with FactionGLA at tick 32,520. **Balance is not assessed** - the patch changes balance on purpose.
- **On a server** (`scripts/check-patch-layer.ps1`, step `patch-layer`): two clients of the layer play 30 s with every tick agreed; a client of the original
  content asking for a seat in their room is refused with `Definitions`. An AI match on the layer ends with the same final hash twice.

## 3. What is still not done (after Phase 32)

- "Balance changes separately switchable": the patch itself does not separate them - its core INI files carry bug fixes and balance changes together. A
  finer switch would need the patch's changes split per change; not done. The layer is switchable as a whole (original or patch).
- The converted parts (textures from Photoshop sources, models from Blender scenes, the string table) and the language and audio bundles: a patched object
  that names a texture of those parts draws without it (the renderer counts a missing texture).
- No human has seen or played a match on the layer; the layer is not offered by the installer (decision D-33). A room shows "original" or "patch" only by its
  definition fingerprint.
- The 50 runs were measured at 60 Hz only; the layer at 30 and 120 Hz is not measured.
