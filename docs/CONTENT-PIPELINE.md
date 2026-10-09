# Runtime content contract and format matrix

As of: 27 September 2026

## Layer boundaries

`CnCNew.IO` and `CnCNew.Archives` form the import side exclusively. `CnCNew.Formats`,
`CnCNew.Images`, `CnCNew.Data` and `CnCNew.W3D` validate file semantics without engine types.
`CnCNew.Import` connects these readers, builds the dependency graph and publishes the
materialised content. `CnCNew.Content` knows neither BIG nor the import VFS and at
runtime opens only ordinary files from a published schema 2 manifest.

## Schema 2

`content-manifest.json` is canonical UTF-8 JSON. It contains the schema version, the name and
the version of the importer as well as deterministically sorted options and assets. Each asset carries:

- a logical ID `asset://` plus the ordinally lower-cased, normalised virtual path;
- the case-preserving virtual path and a typed format class;
- SHA-256 and byte count of the source;
- the selected overlay source, priority and all displaced candidates;
- sorted direct dependencies with status `resolved` or `fallback`;
- the safe relative path, hash and size of every generated runtime artefact;
- typed diagnostics, in particular for preserved files outside the product scope.

Paths use `/`, are relative and must not contain empty, `.` or `..` segments.
Comparisons are ordinal and case-insensitive; pure case collisions are a hard
import error. Physical Windows segments are percent-encoded, for example `Data/*` as `Data/%2A`.
Schema 1 remains readable and receives a visible `legacy-manifest` diagnostic. New imports
write schema 2 only; unknown schema versions are rejected.

## Import, cache and publication

`runtime-import` hashes and validates every selected overlay source, extracts references and builds
the graph. An unchanged SHA-256 entry is taken over from the published artefact.
Dependencies are recomputed deterministically in every run. Machine paths and timestamps
do not enter the result.

The run is created in a unique sibling staging directory. Only after successful
format, reference and manifest validation is the old target renamed and staging atomically put in
its place. On errors the old target is restored. Tests check cache hits,
invalidation, identical fresh imports and the unchanged content after an import error.

## Format matrix of the shipped content

| Files | Format class | Strategy and validated semantics |
| ---: | --- | --- |
| 8,904 | W3D | runtime decoder; chunk tree, bounds, meshes, materials and texture references; animation, hierarchy and HLOD are preserved as a validated chunk domain for Phase 7 |
| 8,475 | WAV | external free runtime decoder; RIFF/WAVE length, chunk bounds, `fmt ` and `data` |
| 6,602 | DDS | runtime decoder; header, dimensions, mips, BC1/2/3 and RGB24/32 including payload size |
| 826 | TGA | runtime decoder; 24/32 bit, raw/RLE, dimensions, orientation and pixel stream |
| 256 | INI | runtime decoder; SAGE syntax and file references; typed game data follow in Phase 3 |
| 150 | MAP | runtime decoder; 145 EAR/RefPack, four raw CkMp and one ZL5/zlib variant; decompression size, symbol table, chunk IDs, versions, sizes and end of file |
| 104 | ANI | runtime decoder; RIFF/ACON; the size-plus-8 write error that is identical in all files is explicitly permitted |
| 80 | WND | runtime decoder; `FILE_VERSION = 2`, structure brackets and references |
| 70 | BIK | external free decoder path (FFmpeg/libav); version, file size, frames, dimensions and frame rate |
| 56 | MP3 | external free decoder path; ID3 version/size and first MPEG frame sync |
| 24 | WAK | runtime decoder; 20-byte record layout, length and finite coordinates |
| 12 | STR | runtime decoder; comments, keys, quoted values, `END` and duplicates |
| 4 | SCB | runtime decoder; CkMp signature and plausible object count |

Particles and FX are present as INI definitions with W3D, texture and audio references. The
remaining 119 files are classified: 31 installation/Miles binaries, 46 documents, seven
installer images, 18 opaque installation metadata files, one unused CSF catalogue and 16
legacy shaders. They are preserved for fidelity of origin but are not part of the product scope.
No extension remains unclassified.

## Graph and fallbacks

Of 41,527 direct references, 41,308 are resolved. The search checks the canonical path,
owner folder, unique file name and, for W3D, `Art/Textures` including DDS/TGA exchange. Real
cycles are errors; WND self-mentions are not interpreted as a dependency.

219 uses in 128 assets are deliberately typed: 164 `visible-magenta-texture`, 51
`silent-audio` and four `disabled-missing-map` (maps not shipped, including the
EA demo campaign). Video has the documented `skip-video` fallback in case the free external
decoder is missing; in the shipped graph it is not needed. There is no silent fallback.

## Reproducible acceptance

> Historical section (acceptance of Phase 2, 27 September 2026). The directories named here,
> `.local/game-original`, `.local/content` (schema 1 at the time) and `.local/phase2-import-a`/`-b`, no longer exist.
> Today `scripts/dev-content.ps1` generates the developer content from the Steam installation into `game/content`
> (schema 2, ignored directory `game/` in the root of the repository); the path for users is described in
> `docs/INSTALLATION.md`.

```powershell
dotnet run --project tools/CnCNew.AssetTool -- audit-formats .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-dependencies .local/content
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-a
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-b
dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
```

Both fresh imports produced 25,682 assets and the same manifest SHA-256
`ACDF0B6CF98538469AA05F62E23D2A83BD3E17A911433B4EA73373FFFB1245FF`. The following run hit
25,682/25,682 cache entries. `verify` checked all artefacts without BIG access. Local imports and
the manifest of 27,174,097 bytes remain ignored and are not committed.
