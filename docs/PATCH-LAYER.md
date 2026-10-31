# The patch: one game, GeneralsGamePatch over the original

The product is one variant (decisions D-3, D-32, D-33 of `docs/plan/PROGRAMME.md` §7, 31 October 2026): the game the player gets, and the yardstick of every test,
is the English Steam installation with the community patch **GeneralsGamePatch** (TheSuperHackers, `references/generals-game-patch`, the directory `Patch104pZH`) above
it, every adopted fix of `docs/GGC-FIXES.md` and the movement profile of `docs/PERF-MOVEMENT.md` §10. The original stays beside it only as a switch that tells a defect
of the patch from one of the port: `game/original/content` and `--rules original`.

The patch changes the game's data: INI files, window layouts, map scripts and strings, models, textures, sounds. Neither this repository nor a package holds a file of it.

## 1. How it reaches a machine

- **`baselines/patch-master.json`** names the patch's commit and every source file with size and SHA-256 - facts about files, never their content - and what the import
  of the game with this layer gives (`expectedExport`). It is compiled into `CnCNew.Import` (`PatchMaster.LoadBuiltIn`) like `baselines/steam-master.json`.
- **The installation** (`GameInstaller.InstallAsync(..., patch:)`, `AssetTool install`): after the check of the Steam installation `PatchFetcher` puts the sources into
  `<target>/patch-files/source` - from the blob of the recorded commit (`raw.githubusercontent.com`, eight at a time, 110 MB, three attempts per file), or, with
  `--patch <GameFilesEdited directory>`, from a checkout, whose text files may hold CRLF where the blob has LF. A file is taken only with its recorded size and hash. The
  directory is gone when the installation is verified. An intact installation is verified without the network. A record of schema 1 (`install.json` from before the
  patch) is ours, and the next installation brings it up to the patched content.
- **The export** (`RuntimeContentImporter.ImportAsync(..., layer:)`): `GameFileSystem.OpenInstallation(root, overlay)` puts the layer above the installation; each of its
  files becomes instance zero of its path. The content manifest records for every replaced file both candidates and selects `overlay:<layer>:<path>`, and names the layer
  in the importer options (`layer`, `layerSource`, `layerFiles`, `layerSha256`).
- **The developer's machine** (`scripts/dev-content.ps1`): `game/content` as a player has it, `game/original/content` beside it (`install --original`, the recorded
  export of `baselines/steam-master.json`). The publication audit compares the repository with both.
- **A new commit of the patch**: `AssetTool describe-patch auto references/generals-game-patch/Patch104pZH baselines/patch-master.json --commit <commit>` asks for every
  source at that commit and writes the description, then `scripts/get-references.ps1`' commit and the manifests move with it.

## 2. What the patch's tool chain does, rewritten

`CnCNew.Import.GeneralsGamePatchLayer.Classify` takes what the core bundles take in English (`ModBundleCoreItems.json`, `ModBundleCoreAudioItems.json`, the English part of
`ModBundleCoreLanguageItems.json`), and counts every other file by reason. `CnCNew.Import.PatchBuild` does what the patch's builder does with them; its result depends only on
the bytes of the sources.

| Source (`GameFilesEdited/...`) | Result |
| --- | --- |
| `Data/INI/**/*.ini` | the same path, without the blocks between `;patch104p-optional-begin` and `;patch104p-optional-end` (the bundle's `excludeMarkersList`; those blocks hold the alternative of the optional bundle, and active lines) |
| `Data/generals.str` | `Data/English/generals.csf`: the multi-language string file (UTF-8, `US: "..."`, ten more languages, `//patch104p-optional-...` blocks) as the English CSF |
| `Art/Textures/GenerateMip/*.psd`, `*.tga` | `Art/Textures/*.dds`, uncompressed BGRA with the mip chain down to one pixel (each level the rounded average of four pixels); `exhorde` and `exhorde_up` also as `exhordeb` and `exhordeb_up` |
| `Art/Textures/NoMip/*.psd` | `Art/Textures/*.dds` without a chain |
| `Art/Textures/GenerateTga/*.psd` | `Art/Textures/*.tga`, 32 bits, top row first |
| `Data/English/*.ini`, `Data/Audio/Sounds/*.wav`, `Data/Audio/Sounds/English/*.wav`, `Maps/**/*.ini`, `Maps/**/*.str`, `Window/**/*.wnd`, `Art/W3D/*.W3D`, `Art/Textures/*.tga`, `*.dds`, `Data/Cursors/*.ani` | as they are |

A Photoshop file is read by its merged image: 8-bit RGB, raw or PackBits, the fourth channel being alpha (every one of the patch's 205 is of this kind). The patch's builder
makes block-compressed DDS; the uncompressed ones here are the same picture at more bytes. What it does beyond this - comments and whitespace out of INI and WND files, CRLF -
changes nothing the game reads. Its `Art/Models` directories hold no scene at this commit.

Two kinds of the patch's models are read as the original's loader reads them, not as this port's reader wanted them: a top-level hierarchy without the container flag
(`ABSupplyCT_A7`, `_A8`; `ChunkLoadClass::Open_Chunk` never asks, counted as `W3dFile.UnmarkedHierarchies`) and a visibility channel with a default of 255
(`BitChannelClass::Get_Bit` returns it as it stands). `ABLGCLink_L` ends with a byte after its last chunk (`W3dFile.TrailingBytes`). Four colour lines of `ParticleSystem.ini` carry a
token the original ignores (`compatibility-ignored-tokens`). `StatusBitsUpgrade` and `SabotageSupplyDropzoneCrateCollide` are engine modules only the patch uses
(`docs/SIMULATION-CORE.md` §50).

## 3. Measured (31 October 2026, patch commit `6c7f40d6af6a4babb77a33a4502ec7656446c13e`)

- 600 source files, 110,778,867 bytes; not taken: the sounds of other languages, 59 files of other language bundles and `Data/Autorun.str`, the repository's notes.
- The export: 25,663 runtime files (the original's 25,608), **547 replaced and 55 added**, content manifest 27,447,222 bytes. An installation over a known directory takes
  100 s; `describe-patch` 170 s.
- Compatibility baseline 674 entries (`baselines/compatibility.json`, the one baseline since the patch is the content).

## 4. What is not done

- The patch's balance is taken as a whole: its core INI files carry fixes and balance changes together. Balance is not assessed (D-9).
- The patch's optional bundles (`GameFilesOptional`, the `ModBundleOptional*` items) and every language but English.
- No human has seen or played the patched game: the acceptance suite (`docs/ACCEPTANCE.md`) is what judges it.
- A textured view of the converted pictures against the original's: the pixels are read by a test on synthetic files, the game's pictures by the importer's validation.
- Installing where GitHub cannot be reached: the installation says which file it could not fetch and stops; there is no bundle to hand over.
