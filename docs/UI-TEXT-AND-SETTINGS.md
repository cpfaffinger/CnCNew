# UI texts, localisation, mapped images and settings

As of: 30 September 2026 (package B1 of Phase 8)

`CnCNew.Ui` references only `CnCNew.Data`. Parsers take `ReadOnlySpan<byte>`/`Stream`, file existence comes
as a `Func<string,bool>` from the caller; there is no access to archives, `CnCNew.Content` or the file system –
with the exception of `SettingsStore`, which writes into a directory chosen by the client (`%APPDATA%\CnCNew`).
No new NuGet dependency (JSON via `System.Text.Json` from the BCL).

## Entry points

| Area | Types |
|---|---|
| String tables | `CsfReader.Read(span/stream) → CsfFile`, `StringTable.FromCsf(file, source)`, `StrReader.Read(span/stream, source) → StringTable` |
| Text facade | `LocalizedText(table, mapTable?)`: `TryGet`, `Get`, `Format`, `MissingLabels`, `MissCount`, `WithMapStrings` |
| Formatting | `GameTextFormatter.Format(format, args)` |
| Language | `LanguageCatalog.Resolve/Discover(…, exists)`, `GlobalLanguageSettings.FromGraph(graph)` |
| Mapped images | `MappedImageTable.FromGraph(graph)`, `MappedImageTextures.Resolve(table, language, exists)` |
| Settings | `GameSettings` (+ sub-records), `SettingsStore(directory).Load()/Save()`, `SettingsValidator.Normalize`, `SettingsJson` |
| Resolutions | `Resolutions.Standard`, `Resolutions.Classify`, `Resolutions.BestFit(w, h, desktopW, desktopH)` |

## CSF (`Data/<Sprache>/Generals.csf`)

Verified against `GameText.cpp` (`GameTextManager::getCSFInfo`, `parseCSF`) and against the retail content:

- All integers 32 bit little-endian. Identifiers are the integer from four ASCII characters (`('C'<<24)|…`),
  so on disk there are the bytes `" FSC"`, `" LBL"`, `" RTS"`, `"WRTS"`.
- Header (24 bytes): identifier, version, label count, `num_strings`, `skip`, language ID. The language ID
  (`LanguageID` from `Common/Language.h`) is valid only from version 2 on; before that the original assumes US.
- Label: `LBL `, number of values, name length, name (bytes). Value: `STR ` or `STRW`, length in UTF-16 units,
  the units **bitwise inverted** (`~c`); with `STRW` an ASCII extra value follows (speech file).
- The original uses only the **first** value of a label (text and speech name, the latter only if not empty) and
  normalises it with `stripSpaces` (leading blanks, runs of blanks, blanks before/after `\n`/`\t`, trailing blank;
  only U+0020 counts). `CsfFile` keeps all values raw, `CsfLabel.ToEntry()` delivers the entry of the game.
- A decoded U+0000 ends the text as in the original (null-terminated copy).
- Deviations from the original are explicit `CsfFormatException`s instead of silent short reads: wrong identifiers,
  version outside 1..3, unknown language ID, negative lengths, truncated data, and a label count that
  does not match the header (the original allocates according to the header and reads to the end of the file).
- The original does not check duplicate labels for CSF; `qsort`/`bsearch` then find an arbitrary one. `StringTable` takes
  the first and lists every collision in `Duplicates` (retail: 0).

The test writer `CsfTestWriter` (only in the test project) produces synthetic fixtures as the inverse of the reader.

## STR (`Data/Generals.str`, `Maps/<Karte>/map.str`)

Verified against `parseStringFile`, `readToEndOfQuote`, `translateCopy`, `parseMapStringFile`:

- Lines are trimmed (C whitespace), empty lines and lines beginning with `//` are skipped. One line is the
  label, after it a text in quotation marks and `END` (case does not matter).
- The text may run over lines; line breaks and whitespace become blanks. A `"` ends it, unless it is
  escaped by `\`. Escapes: `\\ \' \" \? \t \n`, every other escaped character stands for itself.
- Behind the closing quotation mark, a speech name may stand in the same line (blanks and `=` before it
  are skipped, then `[A-Za-z0-9_]`). If it ends in a digit, the original appends `e` (English
  speech file names); that is reproduced.
- Bytes are Latin-1 (`*inbuf & 0x00FF`). After that `stripSpaces` as with CSF.
- Explicit errors (`StrFormatException`) instead of undefined behaviour: duplicate label and second text in one
  entry (`DEBUG_CRASH` in the original), missing `END` (the original discards the file), unclosed
  quotation mark (the original then reads NUL up to the buffer limit) and every other line within an entry
  (the original skips it silently).
- Precedence: release builds set `m_useStringFile`, so `GameTextManager::init` first tries `Data/Generals.str`
  and only then the CSF. `LanguageCatalog.Resolve` reproduces this order.

## Lookup and missing labels

`GameTextManager::fetch`: labels are compared **without regard to case** (`compareLUT` with
`stricmp`); first the main table, then the map table (`initMapStringFile`). For a missing
label the original delivers `MISSING: 'label'` and notes it in `m_noStringList`. `LocalizedText.Get` delivers the visible marker
`??label??` (never an empty string) and counts the miss (`MissingLabels` sorted and unique, `MissCount`
with repetitions). `TryGet` does not count. `Format` corresponds to `fetchFormat`: a missing label is returned unformatted as
a marker. `getStringsWithLabelPrefix` corresponds to `StringTable.LabelsWithPrefix` (prefix case-sensitive like
`strstr`).

`GameTextFormatter` implements the printf subset of the texts: `%[-+0 #][Breite][.Präzision][h|l|ll|I64]` with
`d i u x X c s S f F %`. `%s`, `%ls`, `%hs` and `%S` take .NET strings. Type errors, missing or surplus
arguments, unknown conversions, `*` widths and an open `%` are `FormatException`s. Formatting with
the invariant culture.

## Language.ini and language choice

`LanguageCatalog` maps language name → files without a file system (`Data/Generals.str`,
`Data/<Sprache>/Generals.csf`, `Data/<Sprache>/Language.ini`; paths from `WinMain.cpp` `g_strFile`/`g_csfFile` and
`GlobalLanguage::init`). Language names must consist of ASCII letters.

The `Language` block comes as the compatibility definition `language/language` from the `CompiledDefinitionGraph`
(the load plan loads the file of the chosen language, `DefinitionCompilerOptions.Language`). `GlobalLanguageSettings`
types it according to `TheGlobalLanguageDataFieldParseTable` of the retail code (EA source `GlobalLanguage.cpp`):
`UnicodeFontName`, `LocalFontFile`, `MilitaryCaptionSpeed`, `UseHardWordWrap`, `ResolutionFontAdjustment`,
`MilitaryCaptionDelayMS` and 17 font slots (`parseFontDesc`: name, optionally in quotation marks, point size,
`Yes`/`No` bold). Unknown keys and faulty values are errors; keys not set keep the
constructor values (`FontDesc`: "Arial Unicode MS", 12, not bold; delay 750 ms; adjustment 0.7) and are listed
in `DefaultedKeys`. `AdjustFontSize` follows `GlobalLanguage::adjustFontSize` (factor `1 + (Breite/800 − 1) ·
Anpassung`, limited to 1..2, rounded down). The TheSuperHackers addition `ResolutionFontSizeMethod` does not belong to the
retail format and is rejected.

## Mapped images

The load plan loads `Data/INI/MappedImages/TextureSize_512` and `HandCreated` (`ImageCollection::load`, Image.cpp) in
this order; every `MappedImage` block becomes the compatibility definition `mappedimage/<name>`. A later
block of the same name updates the image (`INI::parseMappedImageDefinition` looks it up and parses over it); the
name lookup is case-insensitive (`nameToLowercaseKey`).

`MappedImageTable` types strictly according to `Image::m_imageFieldParseTable`: `Texture` (one file name), `TextureWidth`,
`TextureHeight` (positive integers), `Coords = Left:n Top:n Right:n Bottom:n` (`parseImageCoords`), `Status`
(`NONE`, `ROTATED_90_CLOCKWISE`, `RAW_TEXTURE`, `parseBitString32`). UV = pixel / texture size; width/height =
`Right−Left`/`Bottom−Top`, swapped for `ROTATED_90_CLOCKWISE` (`parseImageStatus`). Deviating from the original,
missing keys, texture size 0 (the original then leaves pixel coordinates standing as UV), empty, reversed or
rectangles reaching beyond the page, unknown status names and foreign keys are `MappedImageFormatException`s.

`MappedImageTextures` checks texture pages like `GameFileClass::Set_Name` (W3DFileSystem.cpp): first
`Data/<Sprache>/Art/Textures/` (texts in images), then `Art/Textures/`, each time literal name and `.dds` stem
(`DDSFileClass`, ddsfile.cpp). Which of the two forms the renderer prefers is not decided by this report.

## Settings

`GameSettings` (schema 2 since Phase 38; schema 1 migrates) with sub-records and value ranges:

| JSON path | Type, range | Default |
|---|---|---|
| `display.mode` | `windowed`, `borderless`, `fullscreen` | `windowed` |
| `display.width`/`height` | 800..16384 / 600..16384 (clamped) | 1280 × 720 |
| `display.vsync` | bool | `true` |
| `display.uiScale` | `uniform`, `stretch`, `integerMultiple` | `uniform` |
| `display.aspect` | `keepHorizontal` (the original's horizontal field of view at every aspect, what the client always drew), `keepVertical` (the vertical field of view of 4:3: more to the sides of a wide window) | `keepHorizontal` |
| `display.skin` | `original`, `modern` (docs/UI-MODERN.md) | `original` (D-21) |
| `audio.master/music/effects/voice/ambient` | 0..1 (clamped, NaN replaced) | 1 / 0.55 / 0.8 / 0.7 / 0.8 (`AudioSettings.ini`) |
| `audio.device` | the name of a sound output device as waveOut lists it, at most 255 characters, no control characters; empty is the system's default (a name that is not there any more plays on the default, said once) | empty |
| `controls.scrollSpeed`/`cameraRotateSpeed` | 0..1 | 0.5 / 0.5 |
| `controls.edgeScrolling` | bool | `true` |
| `accessibility.highContrastUi` | bool | `false` |
| `accessibility.uiTextScalePercent` | 100, 125, 150 (snapped to the nearest value) | 100 |
| `accessibility.colorBlindSafeTeamColors` | bool | `false` |
| `general.language` | 1..32 ASCII letters | `English` |
| `general.showFps`, `general.pauseOnFocusLoss` | bool | `false`, `true` |
| `general.defaultAiDifficulty` | `easy`, `medium`, `hard` | `medium` |
| `skirmish.lastMap` | relative virtual path or `null` | `null` |
| `skirmish.lastFactions` | up to 8 names `[A-Za-z0-9_]+` | empty |
| `skirmish.lastStartMoney` | 0..1,000,000 | 10,000 (`DefaultStartingCash`) |

`SettingsStore`:

- File `settings.json` in the directory passed in; serialisation with a fixed order (`version`, `display`,
  `audio`, `controls`, `accessibility`, `general`, `skirmish`), invariant number formatting (shortest
  round-trip representation), enums in camelCase, `\n` line endings: two saves are byte-identical.
- Atomic: write `settings.json.tmp`, bring it to disk with `WriteThrough`, then rename it over the target file via
  `File.Move(…, overwrite)`. A leftover `.tmp` is reported as `stale-temp-file` on loading.
- If the file is missing: default values (`Defaults`). Not parseable, root not an object or `version` not an integer:
  the file is moved to `settings.json.broken` (an older `.broken` is replaced), default values,
  message `corrupt` (`RecoveredFromCorruptFile`). A UTF-8 BOM is accepted.
- Newer schema version: `SettingsVersionException`, the file stays unchanged. Older version: the chain
  `SettingsMigration` (JSON object → JSON object, one step each) runs, every step is reported as `migrated`,
  a gap is a `SettingsVersionException`. `SettingsStore.Migrations` holds the one step from schema 1 to 2 (Phase 38): `display.skin` = `original`,
  `audio.device` = empty, and `display.aspect` = `keepHorizontal` - schema 1's three aspect values were never applied by any client; tests inject further steps
  through the constructor.
- Binding without guessing: missing sections/properties (`missing`), wrong types (`invalid-type`), unknown
  properties (`unknown-property`), after that `SettingsValidator.Normalize` with `clamped`, `snapped`, `invalid-value`.
  `Save` stores the normalised values and returns the messages.

`Resolutions`: aspect ratios 4:3, 16:10, 16:9, 21:9 (64:27 and 43:18) and 32:9 with 3 % tolerance,
25 standard resolutions from 800 × 600 to 5120 × 2160. `BestFit` for windowed mode: if the request fits into the
usable desktop area, it stays; otherwise the largest standard resolution of the same aspect ratio, otherwise the largest
of any; below 800 × 600 it is raised, a smaller desktop gives 800 × 600 with a reason.

## Measured numbers (`game/content`)

`dotnet run --project tests/CnCNew.Ui.Tests -- --content-dir game/content` (two runs byte-identical):

- `Data/English/generals.csf`: version 3, language US, 6,422 labels (all decoded), 6,421 values (= header
  `num_strings`), 0 duplicate labels, 0 labels with several values, 1 label without a value, 2 `STRW` values with a speech name.
  Most frequent prefixes: `DIALOGEVENT` 2,455, `GUI` 916, `CONTROLBAR` 756, `MAP` 473, `OBJECT` 443, `TOOLTIP` 277.
- printf conversions in the texts: `%ls` 62, `%d` 54, `%s` 27, `%hs` 11, `%02d` 5, `%%` 3, `%c` 2, `%.0f`,
  `%2.2d`, `%S`, `%u` 1 each – all supported by the formatter; 36 `% x` places are literal percent signs.
- `.str` files: 11 of 12 read (97 labels). `Maps/MD_GLA05/map.str` is commented out line by line with `;`,
  which the string format does not know; the original takes the first line as a label, skips the
  rest and discards the file at the end of the file (no `END`) – the same effect, here as an explicit error.
- WND coverage (`TEXT`/`TOOLTIPTEXT`, without the `TEXT` of input fields, which `setWindowText` displays literally) in
  `MainMenu`, `OptionsMenu`, `SkirmishGameOptionsMenu`, `SkirmishMapSelectMenu`, `ControlBar`, `ScoreScreen` and the
  seven `*Load*` menus: **204 of 229** unique labels per file resolve. `MainMenu`, `SkirmishMapSelectMenu`,
  `PopupSaveLoad`, `SaveLoad` completely. The 25 open ones are fixed as a baseline in the test:
  17 placeholder texts without a label prefix that the menu code replaces at run time (`Static Text` ×6, `[None]`, `Version`,
  `##`, `$$$`, `PlayerName`, `Buidings Built`, `Buildings Killed`, `Buildings Lost`, `Units Built`, `Units Destroyed`,
  `Units Lost`), and 8 labels that are missing in the retail CSF: `Tooltip:LoadProgress` (4 loading screens),
  `GUI:BioNameEntry`, `GUI:BioBirthplaceEntry`, `GUI:BioStrategyEntry` (Challenge loading screen),
  `GUI:LanguageFilter` (options).
- Mapped images (retail graph without map overlays): 1,435 images (2 rotated) from 1,486 blocks, 51 images defined in more than
  one file; 124 texture pages, of which 66 in the localised `Data/English/Art/Textures`, 12 are missing with one
  image each (`China2_BackgroundSM`, `China2_Frame`, `China2_FrameSM`, `COB2_BackgroundSM`, `COB2_FrameSM`,
  `GeneralsChallengeMapInterface`, `GLA2_BackgroundSM`, `GLA2_Frame`, `GLA2_FrameSM`, `MainMenuBlackBorder`,
  `Skirmish_Load`, `USAMissionDemoLoadScreen`). None of these pages lies in the extracted archive content
  (`.local/assets/by-archive`, name search); the list is fixed as a baseline in the test.
- `Language.ini` (English): "Arial Unicode MS", all 17 font slots set, `LocalFontFile` and
  `UseHardWordWrap` at the default. Language folders: 9, only English has a string table, all have `Language.ini`.

## Limits

- The definition compiler keeps the last value for a repeated scalar key; `LocalFontFile`
  accumulates in the original. `GlobalLanguageSettings.FromGraph` therefore explicitly rejects a repeated `LocalFontFile`
  (retail: at most one per language).
- For a mapped image defined in several files, `Name` carries the spelling of the last block (graph
  semantics); the original keeps that of the first. The lookup is case-insensitive anyway.
- The UV values are computed from the final values; when parsing `Coords`, the original divides by the texture size read
  up to that point. In all 1,486 retail blocks `TextureWidth` stands before `Coords`, the result is the same.
- The WND label check in the test is a line scan (`WINDOWTYPE` before `TEXT`), not a WND parser (package B2).
- The colour-blind and contrast switches are only stored settings; their presentation belongs to the
  presentation. Languages other than English are without a string table in the retail content and therefore tested only synthetically.
