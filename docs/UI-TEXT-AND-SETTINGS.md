# UI-Texte, Lokalisierung, Mapped Images und Einstellungen

Stand: 30. September 2026 (Paket B1 der Phase 8)

`CnCNew.Ui` referenziert nur `CnCNew.Data`. Parser nehmen `ReadOnlySpan<byte>`/`Stream`, Dateiexistenz kommt
als `Func<string,bool>` vom Aufrufer; es gibt keinen Zugriff auf Archive, `CnCNew.Content` oder das Dateisystem –
mit Ausnahme von `SettingsStore`, der in ein vom Client gewähltes Verzeichnis (`%APPDATA%\CnCNew`) schreibt.
Keine neue NuGet-Abhängigkeit (JSON über `System.Text.Json` aus der BCL).

## Einstiegspunkte

| Bereich | Typen |
|---|---|
| Stringtabellen | `CsfReader.Read(span/stream) → CsfFile`, `StringTable.FromCsf(file, source)`, `StrReader.Read(span/stream, source) → StringTable` |
| Textfassade | `LocalizedText(table, mapTable?)`: `TryGet`, `Get`, `Format`, `MissingLabels`, `MissCount`, `WithMapStrings` |
| Formatierung | `GameTextFormatter.Format(format, args)` |
| Sprache | `LanguageCatalog.Resolve/Discover(…, exists)`, `GlobalLanguageSettings.FromGraph(graph)` |
| Mapped Images | `MappedImageTable.FromGraph(graph)`, `MappedImageTextures.Resolve(table, language, exists)` |
| Einstellungen | `GameSettings` (+ Teilrecords), `SettingsStore(directory).Load()/Save()`, `SettingsValidator.Normalize`, `SettingsJson` |
| Auflösungen | `Resolutions.Standard`, `Resolutions.Classify`, `Resolutions.BestFit(w, h, desktopW, desktopH)` |

## CSF (`Data/<Sprache>/Generals.csf`)

Verifiziert an `GameText.cpp` (`GameTextManager::getCSFInfo`, `parseCSF`) und am Retailbestand:

- Alle Ganzzahlen 32 Bit little-endian. Kennungen sind der Integer aus vier ASCII-Zeichen (`('C'<<24)|…`),
  auf der Platte stehen daher die Bytes `" FSC"`, `" LBL"`, `" RTS"`, `"WRTS"`.
- Kopf (24 Byte): Kennung, Version, Labelanzahl, `num_strings`, `skip`, Sprach-ID. Die Sprach-ID
  (`LanguageID` aus `Common/Language.h`) gilt erst ab Version 2; davor nimmt das Original US an.
- Label: `LBL `, Anzahl Werte, Namenslänge, Name (Bytes). Wert: `STR ` oder `STRW`, Länge in UTF-16-Einheiten,
  die Einheiten **bitweise invertiert** (`~c`); bei `STRW` folgt ein ASCII-Zusatzwert (Sprachdatei).
- Das Original verwendet nur den **ersten** Wert eines Labels (Text und Sprachname, dieser nur, wenn nicht leer) und
  normalisiert ihn mit `stripSpaces` (führende Blanks, Blankfolgen, Blanks vor/nach `\n`/`\t`, abschließender Blank;
  nur U+0020 zählt). `CsfFile` behält alle Werte roh, `CsfLabel.ToEntry()` liefert den Eintrag des Spiels.
- Ein dekodiertes U+0000 beendet den Text wie im Original (nullterminierte Kopie).
- Abweichungen vom Original sind explizite `CsfFormatException`s statt stiller Kurzlesungen: falsche Kennungen,
  Version außerhalb 1..3, unbekannte Sprach-ID, negative Längen, abgeschnittene Daten, und eine Labelanzahl, die
  nicht zum Kopf passt (das Original alloziert nach dem Kopf und liest bis Dateiende).
- Doppelte Labels prüft das Original bei CSF nicht; `qsort`/`bsearch` finden dann ein beliebiges. `StringTable` nimmt
  das erste und listet jede Kollision in `Duplicates` (Retail: 0).

Der Testschreiber `CsfTestWriter` (nur im Testprojekt) erzeugt synthetische Fixtures als Umkehrung des Lesers.

## STR (`Data/Generals.str`, `Maps/<Karte>/map.str`)

Verifiziert an `parseStringFile`, `readToEndOfQuote`, `translateCopy`, `parseMapStringFile`:

- Zeilen werden getrimmt (C-Whitespace), leere und mit `//` beginnende Zeilen übersprungen. Eine Zeile ist das
  Label, danach ein Text in Anführungszeichen und `END` (Groß-/Kleinschreibung egal).
- Der Text darf über Zeilen laufen; Zeilenumbrüche und Whitespace werden zu Blanks. Ein `"` beendet ihn, außer es ist
  durch `\` maskiert. Escapes: `\\ \' \" \? \t \n`, jedes andere maskierte Zeichen steht für sich selbst.
- Hinter dem schließenden Anführungszeichen darf in derselben Zeile ein Sprachname stehen (Blanks und `=` davor
  werden übersprungen, dann `[A-Za-z0-9_]`). Endet er auf eine Ziffer, hängt das Original `e` an (englische
  Sprachdateinamen); das ist nachgebildet.
- Bytes sind Latin-1 (`*inbuf & 0x00FF`). Danach `stripSpaces` wie bei CSF.
- Explizite Fehler (`StrFormatException`) statt undefinierten Verhaltens: doppeltes Label und zweiter Text in einem
  Eintrag (im Original `DEBUG_CRASH`), fehlendes `END` (das Original verwirft die Datei), nicht geschlossenes
  Anführungszeichen (das Original liest dann NUL bis zur Puffergrenze) und jede andere Zeile innerhalb eines Eintrags
  (das Original überspringt sie still).
- Vorrang: Release-Builds setzen `m_useStringFile`, `GameTextManager::init` versucht daher zuerst `Data/Generals.str`
  und erst dann die CSF. `LanguageCatalog.Resolve` bildet diese Reihenfolge ab.

## Nachschlagen und fehlende Labels

`GameTextManager::fetch`: Labels werden **ohne Beachtung der Groß-/Kleinschreibung** verglichen (`compareLUT` mit
`stricmp`); erst die Haupttabelle, dann die Kartentabelle (`initMapStringFile`). Das Original liefert für ein fehlendes
Label `MISSING: 'label'` und merkt es in `m_noStringList`. `LocalizedText.Get` liefert die sichtbare Marke
`??label??` (nie einen leeren String) und zählt den Fehlgriff (`MissingLabels` sortiert und eindeutig, `MissCount`
mit Wiederholungen). `TryGet` zählt nicht. `Format` entspricht `fetchFormat`: ein fehlendes Label wird unformatiert als
Marke zurückgegeben. `getStringsWithLabelPrefix` entspricht `StringTable.LabelsWithPrefix` (Präfix case-sensitiv wie
`strstr`).

`GameTextFormatter` implementiert die printf-Teilmenge der Texte: `%[-+0 #][Breite][.Präzision][h|l|ll|I64]` mit
`d i u x X c s S f F %`. `%s`, `%ls`, `%hs` und `%S` nehmen .NET-Strings. Typfehler, fehlende oder überzählige
Argumente, unbekannte Konvertierungen, `*`-Breiten und ein offenes `%` sind `FormatException`s. Formatierung mit
invarianter Kultur.

## Language.ini und Sprachwahl

`LanguageCatalog` bildet Sprachname → Dateien ohne Dateisystem ab (`Data/Generals.str`,
`Data/<Sprache>/Generals.csf`, `Data/<Sprache>/Language.ini`; Pfade aus `WinMain.cpp` `g_strFile`/`g_csfFile` und
`GlobalLanguage::init`). Sprachnamen müssen aus ASCII-Buchstaben bestehen.

Der `Language`-Block kommt als Kompatibilitätsdefinition `language/language` aus dem `CompiledDefinitionGraph`
(der Ladeplan lädt die Datei der gewählten Sprache, `DefinitionCompilerOptions.Language`). `GlobalLanguageSettings`
typisiert ihn nach `TheGlobalLanguageDataFieldParseTable` des Retailcodes (EA-Quelle `GlobalLanguage.cpp`):
`UnicodeFontName`, `LocalFontFile`, `MilitaryCaptionSpeed`, `UseHardWordWrap`, `ResolutionFontAdjustment`,
`MilitaryCaptionDelayMS` und 17 Schriftslots (`parseFontDesc`: Name, optional in Anführungszeichen, Punktgröße,
`Yes`/`No` fett). Unbekannte Schlüssel und fehlerhafte Werte sind Fehler; nicht gesetzte Schlüssel behalten die
Konstruktorwerte (`FontDesc`: „Arial Unicode MS“, 12, nicht fett; Verzögerung 750 ms; Anpassung 0,7) und stehen
in `DefaultedKeys`. `AdjustFontSize` folgt `GlobalLanguage::adjustFontSize` (Faktor `1 + (Breite/800 − 1) ·
Anpassung`, begrenzt auf 1..2, abgerundet). Der TheSuperHackers-Zusatz `ResolutionFontSizeMethod` gehört nicht zum
Retailformat und wird abgewiesen.

## Mapped Images

`Data/INI/MappedImages/TextureSize_512` und `HandCreated` lädt der Ladeplan (`ImageCollection::load`, Image.cpp) in
dieser Reihenfolge; jeder `MappedImage`-Block wird zur Kompatibilitätsdefinition `mappedimage/<name>`. Ein späterer
Block gleichen Namens aktualisiert das Bild (`INI::parseMappedImageDefinition` sucht es und parst darüber); die
Namenssuche ist case-insensitiv (`nameToLowercaseKey`).

`MappedImageTable` typisiert strikt nach `Image::m_imageFieldParseTable`: `Texture` (ein Dateiname), `TextureWidth`,
`TextureHeight` (positive Ganzzahlen), `Coords = Left:n Top:n Right:n Bottom:n` (`parseImageCoords`), `Status`
(`NONE`, `ROTATED_90_CLOCKWISE`, `RAW_TEXTURE`, `parseBitString32`). UV = Pixel / Texturgröße; Breite/Höhe =
`Right−Left`/`Bottom−Top`, bei `ROTATED_90_CLOCKWISE` vertauscht (`parseImageStatus`). Abweichend vom Original sind
fehlende Schlüssel, Texturgröße 0 (das Original lässt dann Pixelkoordinaten als UV stehen), leere, umgedrehte oder
über die Seite hinausragende Rechtecke, unbekannte Statusnamen und fremde Schlüssel `MappedImageFormatException`s.

`MappedImageTextures` prüft Texturseiten wie `GameFileClass::Set_Name` (W3DFileSystem.cpp): zuerst
`Data/<Sprache>/Art/Textures/` (Texte in Bildern), dann `Art/Textures/`, jeweils Literalname und `.dds`-Stamm
(`DDSFileClass`, ddsfile.cpp). Welche der beiden Formen der Renderer bevorzugt, entscheidet dieser Bericht nicht.

## Einstellungen

`GameSettings` (Schema 1) mit Teilrecords und Wertebereichen:

| JSON-Pfad | Typ, Bereich | Standard |
|---|---|---|
| `display.mode` | `windowed`, `borderless`, `fullscreen` | `windowed` |
| `display.width`/`height` | 800..16384 / 600..16384 (geklemmt) | 1280 × 720 |
| `display.vsync` | bool | `true` |
| `display.uiScale` | `uniform`, `stretch`, `integerMultiple` | `uniform` |
| `display.aspect` | `expandHorizontal`, `pillarbox`, `stretch` | `expandHorizontal` |
| `audio.master/music/effects/voice/ambient` | 0..1 (geklemmt, NaN ersetzt) | 1 / 0,55 / 0,8 / 0,7 / 0,8 (`AudioSettings.ini`) |
| `controls.scrollSpeed`/`cameraRotateSpeed` | 0..1 | 0,5 / 0,5 |
| `controls.edgeScrolling` | bool | `true` |
| `accessibility.highContrastUi` | bool | `false` |
| `accessibility.uiTextScalePercent` | 100, 125, 150 (auf den nächsten Wert gerastet) | 100 |
| `accessibility.colorBlindSafeTeamColors` | bool | `false` |
| `general.language` | 1..32 ASCII-Buchstaben | `English` |
| `general.showFps`, `general.pauseOnFocusLoss` | bool | `false`, `true` |
| `general.defaultAiDifficulty` | `easy`, `medium`, `hard` | `medium` |
| `skirmish.lastMap` | relativer virtueller Pfad oder `null` | `null` |
| `skirmish.lastFactions` | bis zu 8 Namen `[A-Za-z0-9_]+` | leer |
| `skirmish.lastStartMoney` | 0..1.000.000 | 10.000 (`DefaultStartingCash`) |

`SettingsStore`:

- Datei `settings.json` im übergebenen Verzeichnis; Serialisierung mit fester Reihenfolge (`version`, `display`,
  `audio`, `controls`, `accessibility`, `general`, `skirmish`), invarianter Zahlformatierung (kürzeste
  Round-Trip-Darstellung), Enums in camelCase, `\n`-Zeilenenden: zwei Speicherungen sind byteidentisch.
- Atomar: `settings.json.tmp` schreiben, mit `WriteThrough` auf die Platte bringen, dann per `File.Move(…, overwrite)`
  über die Zieldatei benennen. Ein liegengebliebenes `.tmp` wird beim Laden als `stale-temp-file` gemeldet.
- Fehlt die Datei: Standardwerte (`Defaults`). Nicht parsebar, Wurzel kein Objekt oder `version` keine Ganzzahl:
  Datei wird nach `settings.json.broken` verschoben (ein älteres `.broken` wird ersetzt), Standardwerte,
  Meldung `corrupt` (`RecoveredFromCorruptFile`). Ein UTF-8-BOM wird akzeptiert.
- Neuere Schemaversion: `SettingsVersionException`, die Datei bleibt unverändert. Ältere Version: die Kette
  `SettingsMigration` (JSON-Objekt → JSON-Objekt, je ein Schritt) läuft, jeder Schritt wird als `migrated` gemeldet,
  eine Lücke ist eine `SettingsVersionException`. Schema 1 ist das erste, `SettingsStore.Migrations` ist daher leer;
  Tests injizieren eine Migration über den Konstruktor.
- Bindung ohne Raten: fehlende Abschnitte/Eigenschaften (`missing`), falsche Typen (`invalid-type`), unbekannte
  Eigenschaften (`unknown-property`), danach `SettingsValidator.Normalize` mit `clamped`, `snapped`, `invalid-value`.
  `Save` speichert die normalisierten Werte und gibt die Meldungen zurück.

`Resolutions`: Seitenverhältnisse 4:3, 16:10, 16:9, 21:9 (64:27 und 43:18) und 32:9 mit 3 % Toleranz,
25 Standardauflösungen von 800 × 600 bis 5120 × 2160. `BestFit` für den Fenstermodus: passt die Anforderung in die
nutzbare Desktopfläche, bleibt sie; sonst die größte Standardauflösung gleichen Seitenverhältnisses, sonst die größte
beliebige; unter 800 × 600 wird angehoben, ein kleinerer Desktop ergibt 800 × 600 mit Begründung.

## Gemessene Zahlen (`game/content`)

`dotnet run --project tests/CnCNew.Ui.Tests -- --content-dir game/content` (zwei Läufe byteidentisch):

- `Data/English/generals.csf`: Version 3, Sprache US, 6.422 Labels (alle dekodiert), 6.421 Werte (= Kopf
  `num_strings`), 0 doppelte Labels, 0 Labels mit mehreren Werten, 1 Label ohne Wert, 2 `STRW`-Werte mit Sprachname.
  Häufigste Präfixe: `DIALOGEVENT` 2.455, `GUI` 916, `CONTROLBAR` 756, `MAP` 473, `OBJECT` 443, `TOOLTIP` 277.
- printf-Konvertierungen in den Texten: `%ls` 62, `%d` 54, `%s` 27, `%hs` 11, `%02d` 5, `%%` 3, `%c` 2, `%.0f`,
  `%2.2d`, `%S`, `%u` je 1 – alle vom Formatierer unterstützt; 36 `% x`-Stellen sind wörtliche Prozentzeichen.
- `.str`-Dateien: 11 von 12 gelesen (97 Labels). `Maps/MD_GLA05/map.str` ist zeilenweise mit `;`
  auskommentiert, was das Stringformat nicht kennt; das Original nimmt die erste Zeile als Label, überspringt den
  Rest und verwirft die Datei am Dateiende (kein `END`) – dieselbe Wirkung, hier als expliziter Fehler.
- WND-Abdeckung (`TEXT`/`TOOLTIPTEXT`, ohne `TEXT` von Eingabefeldern, das `setWindowText` wörtlich anzeigt) in
  `MainMenu`, `OptionsMenu`, `SkirmishGameOptionsMenu`, `SkirmishMapSelectMenu`, `ControlBar`, `ScoreScreen` und den
  sieben `*Load*`-Menüs: **204 von 229** eindeutigen Labels je Datei lösen auf. `MainMenu`, `SkirmishMapSelectMenu`,
  `PopupSaveLoad`, `SaveLoad` vollständig. Die 25 offenen sind als Basislinie im Test fixiert:
  17 Platzhaltertexte ohne Labelpräfix, die der Menücode zur Laufzeit ersetzt (`Static Text` ×6, `[None]`, `Version`,
  `##`, `$$$`, `PlayerName`, `Buidings Built`, `Buildings Killed`, `Buildings Lost`, `Units Built`, `Units Destroyed`,
  `Units Lost`), und 8 Labels, die in der Retail-CSF fehlen: `Tooltip:LoadProgress` (4 Ladebildschirme),
  `GUI:BioNameEntry`, `GUI:BioBirthplaceEntry`, `GUI:BioStrategyEntry` (Challenge-Ladebildschirm),
  `GUI:LanguageFilter` (Optionen).
- Mapped Images (Retail-Graph ohne Kartenoverlays): 1.435 Bilder (2 gedreht) aus 1.486 Blöcken, 51 Bilder in mehr als
  einer Datei definiert; 124 Texturseiten, davon 66 im lokalisierten `Data/English/Art/Textures`, 12 fehlen mit je
  einem Bild (`China2_BackgroundSM`, `China2_Frame`, `China2_FrameSM`, `COB2_BackgroundSM`, `COB2_FrameSM`,
  `GeneralsChallengeMapInterface`, `GLA2_BackgroundSM`, `GLA2_Frame`, `GLA2_FrameSM`, `MainMenuBlackBorder`,
  `Skirmish_Load`, `USAMissionDemoLoadScreen`). Keine dieser Seiten liegt im extrahierten Archivbestand
  (`.local/assets/by-archive`, Namenssuche); die Liste ist im Test als Basislinie fixiert.
- `Language.ini` (Englisch): „Arial Unicode MS“, alle 17 Schriftslots gesetzt, `LocalFontFile` und
  `UseHardWordWrap` auf Standard. Sprachordner: 9, nur Englisch hat eine Stringtabelle, alle haben `Language.ini`.

## Grenzen

- Der Definitionscompiler behält bei einem wiederholten skalaren Schlüssel den letzten Wert; `LocalFontFile`
  sammelt im Original. `GlobalLanguageSettings.FromGraph` weist einen wiederholten `LocalFontFile` deshalb explizit ab
  (Retail: höchstens einer je Sprache).
- Bei einem in mehreren Dateien definierten Mapped Image trägt `Name` die Schreibweise des letzten Blocks (Graph-
  semantik); das Original behält die des ersten. Die Suche ist ohnehin case-insensitiv.
- Die UV-Werte werden aus den Endwerten berechnet; das Original teilt beim Parsen von `Coords` durch die bis dahin
  gelesene Texturgröße. In allen 1.486 Retailblöcken steht `TextureWidth` vor `Coords`, das Ergebnis ist gleich.
- Die WND-Labelprüfung im Test ist ein Zeilenscan (`WINDOWTYPE` vor `TEXT`), kein WND-Parser (Paket B2).
- Die Farbenblind- und Kontrastschalter sind nur gespeicherte Einstellungen; ihre Darstellung gehört zur
  Präsentation. Andere Sprachen als Englisch sind im Retailbestand ohne Stringtabelle und daher nur synthetisch getestet.
