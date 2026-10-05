# TEXT-AUDIT – Warum die Texte „komisch“ aussehen (Phase 16, Punkt 4)

Stand 5. Oktober 2026 (alle Zahlen in diesem Dokument sind in dieser Sitzung neu gemessen). Auftrag: `docs/HANDOFF-PHASE16.md` Punkt 4, Befund
`docs/DEMO-FINDINGS.md` Nr. 10. Ein Mensch fand die Texte der Demos im Vergleich zum Original „komisch“; eine Ursache (Vorlagenschriften gegen 800 Pixel Breite,
`docs/GAME-SHELL.md` §11) war schon behoben, der systematische Vergleich fehlte.

**Methode und ihre Grenze.** Es gibt keine Originalaufnahmen. Die Beweise sind (1) die Daten des Spiels (WND-Layouts, `Language.ini`, `HeaderTemplate.ini`,
`Mouse.ini`, die englische CSF), (2) der Referenzcode (`references/ea-generals-zero-hour/GeneralsMD/Code/…`, nur gelesen und neu geschrieben, Quellverweise unten) und
(3) Messungen am eigenen Renderer. Ein Pixelvergleich mit dem Original ist nicht möglich; was hier „behoben“ heißt, heißt „stimmt mit dem Referenzcode überein“, nicht „sieht
aus wie im Original“. Die letzte Instanz bleibt ein Mensch (Abschnitt 7).

## 1. Zusammenfassung

Drei Gruppen von Ursachen, in der Reihenfolge ihrer sichtbaren Wirkung:

1. **Die Schrift selbst.** Der Client rasterte mit FreeType und einer geschätzten Stauchung für die Schrift „Generals“, und die Ränder der Buchstaben wurden im Linearraum statt im Gammaraum
   überblendet (heller, fetter). Das Original rastert mit GDI und blendet in Bytes. Auf den 80 Originallayouts
   sind 326 von 1.190 Fenstertexten (27 %) mit FreeType anders breit, zusammen 2.258 px; **294 davon sind „Generals“-Texte** (Titel, Hauptmenüknöpfe, Überschriften; 2.224 px,
   bis zu 85 px je Text). 29 von 36 verwendeten Schriftgrößen haben eine andere Zeilenhöhe (meist 1 px). Behoben: `GdiFontProvider`.
2. **Das Layout des Textblocks.** Umbruchregel, Einzug nach Umbruch, Blockbreite, Zentrierung, Zuschnitt und Versätze der Gadgets wichen von `Render2DSentenceClass` und den
   `W3D…Draw`-Funktionen ab. Behoben: `UiSentenceLayout`, Zuschnitt je Gadgettyp, Versätze von Listen, Eingabefeldern, Auswahlfeldern.
3. **Texte, die es so nicht geben dürfte:** der Platzhalter „$$$“ oben links, aufgedruckte Hotkeys und Kosten auf den Befehlssymbolen, das Wort „Entry“ in Eingabefeldern, interne
   Namen (`FactionAmerica`, `Color 1`) in der Skirmish-Einrichtung, feste Schriftgrößen in den Credits, **keine Tooltips**. Behoben (Tooltips vereinfacht).

Danach behoben: durchscheinende Texte darunterliegender Menüs (Abschnitt 7) und die Missionstexte (Abschnitt 4.6: Schriften aus `Language.ini`, Positionen und Farben aus `InGameUI.ini`, Schreibmaschinentext der Missionsbeschriftung, Zeitgeber, Superwaffenliste, Meldungsfenster).

## 2. Wie das Original Text zeichnet (Quellen)

| Stufe | Verhalten | Quelle |
|---|---|---|
| Schriftwahl | `FontDesc` aus `Language.ini` (Name, Punkt, fett); Vorlagenschriften `HeaderTemplate.ini` mit `adjustFontSize` = `floor(Punkt · clamp(1 + (Breite/800 − 1) · 0,7, 1, 2))`; `FONT` eines Fensters bleibt unverändert, Fenster ohne `FONT` bekommen `DefaultWindowFont` (Times New Roman 14) | `GlobalLanguage.cpp` (`adjustFontSize`, `FontDesc::FontDesc`), `HeaderTemplate.cpp` (`populateGameFonts`), `GameWindowManager.cpp` (`WinCreate`), `GameWindowManagerScript.cpp` (`parseFont`, Ende von `parseGameWindow`) |
| Rastern | `FontCharsClass::Create_GDI_Font`: Höhe `−MulDiv(Punkt, 96, 72)`; Name „Generals“ = Arial mit `lfWidth = 0,40·Höhe`, `CharOverhang = 0`; `PixelOverlap = Höhe/8` auf [0, 4]; `ANTIALIASED_QUALITY`. `Store_GDI_Char`: `ExtTextOutW` (`W` bei x = 1) in eine 24-Bit-DIB, Breite = `GetTextExtentPoint32W` + Überlappung, Alpha = `Pixel >> 4` in einer A4R4G4B4-Textur | `render2dsentence.cpp` |
| Vorschub | `Get_Char_Spacing` = Breite − Überlappung − `tmOverhang` (kein Kerning); `Get_Char_Data`: Zeichen ≥ 256 aus `UnicodeFontName` („Arial Unicode MS“) | `render2dsentence.cpp`, `W3DGameFont.cpp` (`loadFontData`) |
| Umbruch/Block | `Build_Sentence_Not_Centered` (Umbruch bei `Cursor.X + Wortbreite ≥ Umbruchbreite`, Leerzeichen am Anfang der Folgezeile, `extent.X = maxX + Überlappung`), `Build_Sentence_Centered` (zählt das Leerzeichen doppelt, zentriert jede Zeile auf die Blockbreite des anderen Aufbaus) | `render2dsentence.cpp` |
| Zeichnen | Schatten bei (+1, +1) in der Randfarbe, danach der Text; Zuschnitt nur, wenn ihn die Gadgetfunktion setzt | `W3DDisplayString.cpp` (`draw`), `W3DStaticText.cpp` (Zuschnitt, Rand 7, Umbruch Breite − 10), `W3DPushButton.cpp` (`drawButtonText`: Umbruch = Breite, kein Zuschnitt, Kurzbefehlsknöpfe oben links + 2), `W3DCheckBox.cpp` (x = Höhe, kein Umbruch), `W3DRadioButton.cpp` (kein Umbruch), `W3DListBox.cpp` (Versatz `TEXT_X_OFFSET` = 5, Schatten fest schwarz), `W3DTextEntry.cpp` (Eingabefeld: x + 5 + 2, y + 5, Zuschnitt, Halbbreitenschritte; Auswahlfeld ohne Eingabe: x + 10) |
| Geld | `GadgetStaticTextSetText(MoneyDisplay, format("GUI:ControlBarMoneyDisplay", money))`, Position/Größe aus `MoneyUL/LR` des Schemas | `InGameUI.cpp` (`update`), `ControlBarScheme.cpp` |
| Befehlsknöpfe | `GadgetButtonSetText(button, "")` (kein Text auf dem Symbol), Tooltipfunktion `commandButtonTooltip` / Popup `ControlBarPopupDescription.wnd` (Name, Kosten, Beschreibung) | `ControlBar.cpp` (`setControlCommand`), `ControlBarPopupDescription.cpp` (`populateBuildTooltipLayout`) |
| Tooltip | `Mouse::drawTooltip`: Kasten bei (x + 20, y), Umbruch bei `TooltipWidth` % der Bildbreite, Einblendung über `TooltipFillTime`, Schrift `TooltipFontName` aus `Language.ini` | `Mouse.cpp` (`setCursorTooltip`, `drawTooltip`, `createStreamMessages`, `mouseNotifyResolutionChange`) |
| Credits | `CreditsTitleFont`/`CreditsMinorTitleFont`/`CreditsNormalFont` mit `adjustFontSize` | `Credits.cpp` |

## 3. Der Capture-Satz

`scripts/capture-text.ps1 [-ContentDir …] [-OutDir …] [-NoBuild]` (die Bilder liegen nicht im Repo, nur lokal): 96 PNGs und eine Liste der Pixel-Prüfsummen, zwei Läufe
bit-gleich (alle 96 Prüfsummen identisch).

* `ui/` (Werkzeug `tools/CnCNew.UiRenderTool`, 92 Bilder): die rohen Layouts (Hauptmenü, Optionen, Skirmish-Einrichtung, Befehlsleiste, Beenden-Menü) in 800x600, 1280x720,
  1920x1080, 2560x1080; die Hülle über `GameShell` mit echten Eingaben in 1280x720 und 1920x1080 (`shell-*`: Hauptmenü, Zustände der Knöpfe, Optionen und Regler, Beenden-Dialog;
  `flow-*`: Einzelspielermenü, Zeigertooltip halb eingeblendet und fertig, Skirmish-Einrichtung, Kartenwahl leer und mit Auswahl, Einrichtung mit Karte, Ladeschirm,
  **Abschlussbildschirm nach einem echten 600-Tick-Gefecht**, Credits, Kampagnenbildschirm USA, Schwierigkeitsdialog, Briefing zu Beginn und später);
  dazu `font-audit.txt`.
* `client/` (Stride-Client `--smoke-test --capture`, 4 Bilder): `hud-money` (Gefecht mit Auswahl, Geldfenster) und `hud-tooltip` (Eingabeskript: Planierraupe wählen, auf den ersten
  Baubefehl zeigen) in beiden Größen.
* Missionstexte: `ui/missiontext-WxH.png` (Werkzeugszene `MissionTextScenes`, 800x600, 1280x720, 1920x1080: Meldungszeilen, Missionsbeschriftung halb getippt, Zeitgeber und Zähler, Superwaffenliste, Filmtext, Meldungsfenster); die Bilder des Werkzeugs haben einen Alphakanal, in dem durchscheinende Flächen halbdurchsichtig bleiben (ein Betrachter über hellem Grund zeigt sie zu hell; vor dem Ansehen den Alphakanal verwerfen). Im Client: `--smoke-test --mission … --filmstrip` (`docs/GAME-HUD.md` §17.3).
* Nicht im Satz: Übergangsanimationen.

## 4. Tabelle: Element × Abweichung × Ursache

Status: **behoben** (mit Test oder Messung), **vereinfacht**, **offen**, **bewusst** (anders als das Original, mit Grund).

### 4.1 Schrift und Glyphen

| Element | Abweichung | Ursache | Status |
|---|---|---|---|
| Schrift „Generals“ (Vorlagen `Title` 20, `MainButton` 15 …; 294 Texte in 4 Größen) | 7 % zu breit (Großbuchstaben bei 24 px: 390 statt 364 px), je Text bis 85 px, Σ 2.224 px | FreeType-Näherung „91 % Stauchung“; das Original lässt GDI mit `lfWidth = 0,40·Höhe` die Breite wählen (real ca. 87 %) | **behoben** (`GdiFontProvider`), Selbstprüfung des Werkzeugs, `--font-audit` |
| Zeilenhöhe/Ascent | 29 von 36 Schriftgrößen 1 px abweichend (z. B. Arial 16 px: 18 statt 17) | FreeType-Zellmaße (hhea/OS/2, gerundet) statt `tmHeight`/`tmAscent` | **behoben** |
| Zeichenvorschub | je Schrift 1–19 Zeichen (ASCII + Latin-1) mit anderer Breite, 326 Texte betroffen | FreeType: gerundeter, ungehinteter Vorschub; GDI: gehinteter Ganzzahlvorschub aus `GetTextExtentPoint32` | **behoben** |
| Buchstabe `W`, `PixelOverlap` | – | Sonderfälle von `Store_GDI_Char`/`Get_Char_Spacing` (`W` ein Pixel nach rechts, Überlappung zur Blockbreite) | **behoben** (`UiFontFaceMetrics.Overlap`) |
| Kantendeckung | 256 statt 16 Stufen | Original: 4-Bit-Alpha der A4R4G4B4-Textur | **behoben** (Rasterergebnis nur 0, 17 … 255) |
| Überlappende Glyphspalten | Alphamischung statt ODER der Alphabits | `Blit_Char` verknüpft die `PixelOverlap` ersten Spalten mit ODER | **offen**, nur bei Zeichen mit Überstand sichtbar |
| `Courier`, `FixedSys` (Language.ini: Copyright, Militärtext, Debug) | als Arial ersetzt | FreeType kann nur TrueType; die Familien sind Rasterschriften | **behoben** durch GDI (`Courier/12` und `/18` kommen in Layouts vor) |
| `Arial Unicode MS` (UnicodeFontName) | auf dem Messrechner nicht installiert (Office) | GDI ersetzt still durch Arial; jetzt per `GetTextFace` erkannt, gezählt, auf stderr gemeldet | **bewusst/gezählt**; alle Codepunkte der englischen CSF liegen in Arial außer drei C1-Zeichen |
| U+0085 (267 Texte, vor allem Untertitel „…“), U+0093/0094 | unsichtbar, Breite 0 („all#we need support“) | Übersetzer legten cp1252-Codes in UTF-16 ab; GDI hat dafür weder Glyph noch Breite – im Original ebenso (`Get_Char_Spacing` = 0) | **bewusst** (kein Unterschied zum Original, aber sichtbar fehlende Auslassungspunkte) |
| Windows-Version/Arial-Fassung | Pixel der Kanten und einzelne Vorschübe können vom Stand von 2003 abweichen | GDI dieser Installation | **offen**, nicht messbar ohne Original |
| Kantenhelligkeit (Überblendung) | helle Schrift auf dunklem Grund war fetter und verwaschener: ein Randpixel mit 6/15 Deckung wurde 170 statt 102, 10/15 wurde 213 statt 170 (die Probe an `shell-options` vor der Änderung: 222, 213, 170, 231, 247, 203) | Das Original blendet im Bildspeicher in Bytes (Gammaraum); Stride blendet mit sRGB-Zielansicht im Linearraum (`docs/CLIENT-UI-RENDERING.md` §3). Gleiche Deckung ergibt im Linearraum einen helleren Rand | **behoben** für helle Schrift (`UiAlphaCurves.LightOnDark`: gespeichertes Alpha = `decode(Deckung)`, über Schwarz exakt der Gammaraum-Wert; nach der Änderung sind die Randpixel 102, 153, 170, 187, 204, 238 – die 17er-Stufen). Näherung über nicht schwarzem Grund; dunkle Schrift unverändert. Im Clientfenster (Swap-Chain) nicht nachgemessen |
| `FONT` in Uniform-Modus | Fenster ohne Vorlage skalieren mit dem 4:3-Faktor, das Original lässt `FONT` unskaliert (nur Vorlagen wachsen mit `adjustFontSize`) | Layoutmodus der Hülle (`UiScaleMode.Uniform`) | **bewusst** (alles wächst gleichmäßig; bei 1280x720 sind Vorlagen 1,2- statt 1,42-fach) |
| `DefaultWindowFont` (Times New Roman 14) | `UiWindowTree.FallbackFont` ist Arial Unicode MS 12 | gemessen: **0 von 792 Fenstern mit Text** haben weder `FONT` noch Vorlage, also ohne Wirkung | **offen**, folgenlos |
| `LocalFontFile`, `UseHardWordWrap` | nicht angewandt | Englisch: kein `LocalFontFile`, Hartumbruch aus | nicht prüfbar (nur Englisch vorhanden) |

### 4.2 Layout des Textblocks

| Element | Abweichung | Ursache | Status |
|---|---|---|---|
| Zeilenumbruch (Statik, Knöpfe) | Wort wurde bei Breite = Umbruchbreite noch auf die Zeile gesetzt | greedy `>`; Original `≥` (`Cursor.X + Leerzeichen + Wort ≥ Breite`) | **behoben** (`UiSentenceLayout`, `SentenceLayoutTests`) |
| Umgebrochene linksbündige Zeile | ohne Einzug | `Build_Sentence_Not_Centered` lässt die Folgezeile mit dem Leerzeichen beginnen (ein Leerzeichen breiter Einzug) | **behoben** |
| Blockbreite/Zentrierung | Mitte um die Überlappung daneben | `extent.X = maxX + Get_Extra_Overlap` | **behoben** |
| `WRAP_CENTERED` | Zeilen um ein Leerzeichen anders gebrochen und zentriert | `Build_Sentence_Centered` zählt das Leerzeichen doppelt und zentriert auf die Blockbreite des anderen Aufbaus | **behoben** (Höhe des Blocks bleibt die des anderen Aufbaus, wie im Original) |
| Radioknöpfe | wurden umgebrochen | `drawRadioButtonText` setzt weder Umbruch noch Zentrierung | **behoben** |
| Knöpfe, Kontrollkästchen, Radioknöpfe | Text am Fensterrand abgeschnitten („Alternate Mouse Setu“) | Zuschnitt auf das Fenster, den im Original nur statischer Text setzt | **behoben** |
| Listen-/Auswahltext | Versatz 4, Randfarbe des Fensters | `TEXT_X_OFFSET` = 5, Schatten fest `(0, 0, 0, 255)` | **behoben** |
| Eingabefelder | Text mittig, Versatz 4, kein Scrollen | Original: x + 5 + 2, y + 5, Zuschnitt, Scrollen in Halbbreitenschritten; schreibgeschütztes Auswahlfeld: x + 10 | **behoben** (Blinken des Cursors nicht nachgebildet) |
| Kurzbefehlsknöpfe (`SHORTCUT_BUTTON`) | – | Status entsteht im Original zur Laufzeit im Code, nicht in den Layouts; die Kurzbefehlsleiste fehlt im Client | **offen** |
| `&`-Hotkeymarken (`HOTKEY_TEXT`) | – | nur in `ControlBarPopupDescription.wnd`; 385 englische Texte enthalten `&` (z. B. „C&C“), dort wird es nie entfernt – richtig, weil `ParseHotKey` nur dort gilt | **offen** (mit dem Popup) |

### 4.3 Farbe, Umriss, Schatten

| Element | Abweichung | Ursache | Status |
|---|---|---|---|
| Schatten | – | (+1, +1) in der Randfarbe vor dem Text, durchsichtige Randfarbe = kein Schatten – stimmt mit `W3DDisplayString::draw` überein | geprüft, unverändert |
| Textfarben je Zustand | – | `TEXTCOLOR` Enabled/Disabled/Hilite wie in den `W3D…Draw`-Funktionen | geprüft, unverändert |

### 4.4 Zeichenketten (CSF/STR)

| Element | Abweichung | Ursache | Status |
|---|---|---|---|
| Platzhalter | – | 148 englische Zeichenketten mit printf-Umwandlungen; `GameTextFormatter` prüft Typ und Anzahl und wirft statt zu raten; Geld, Kosten, Gebäudezähler laufen darüber | geprüft (`TextAndSettingsTests`) |
| Fehlende Labels | – | `LocalizedText.Get` liefert `??Label??` und zählt (`MissingLabels`); im Shell-Durchlauf 0, im Werkzeug nur die absichtlichen und `Static Text`, `Version` | geprüft |
| Ungelöste Layout-Texte | 27 Texte in 80 Layouts (`GUI:Monkey`, `GUI:MockupScreen`, `$$$` …) | Platzhalter von GUIEdit; sie stehen in Layouts, die kein Bildschirm anzeigt, oder werden vom Programmcode ersetzt | gezählt (`font-audit.txt`) |
| `Data/Patch.str` (7 Zeilen) | wird nicht gelesen | das Original liest `Generals.csf`/`Generals.str` und `map.str`, nie `Patch.str` | geprüft |
| Nur Englisch | andere Sprachen nicht prüfbar | nur `Data/English/generals.csf` ist ausgeliefert | offen |

### 4.5 Texte, die es so nicht geben dürfte

| Element | Abweichung | Ursache | Status |
|---|---|---|---|
| Geldanzeige | „$$$“ oben links in der Ecke plus eine Zahl in Arial 10 am Schemarechteck | `MoneyDisplay` blieb mit Platzhalter und 100x100-Rechteck stehen; die Zahl wurde zusätzlich von Hand gezeichnet | **behoben** (`$ 10000` im Fenster am Schemarechteck, `HudTests.MoneyDisplay`) |
| Befehlssymbole | Hotkeybuchstabe oben links, Kostenzahl unten rechts | Zusatz des Clients, das Original druckt nichts auf die Symbole | **behoben** (entfernt; Beschreibung als Tooltip) |
| Tooltips | fehlten vollständig (`UiWindowTree.Tooltip` wurde von keinem Code gezeichnet) | nie umgesetzt | **behoben/vereinfacht**: `UiTooltipLayout`, `TooltipPainter`; die Befehlsknopfbeschreibung folgt `populateBuildTooltipLayout` (Name, Kosten, Beschreibung, „Insufficient funds“, „Requires: …“, gekauftes Upgrade; `docs/GAME-HUD.md` §5a), aber als Zeigertooltip statt `ControlBarPopupDescription.wnd`; Geld- und Energiefenster beschreiben sich; Objekttooltips und Wissenschaftstexte fehlen |
| Tooltip-Zeitverhalten | blieb sichtbar, solange der Zeiger im Fenster war | Original: der Zeiger muss `Delay` still stehen (`createStreamMessages`) | **behoben** (`WndTests.TooltipStateMachine`) |
| Optionen: Eingabefelder | zeigten „Entry“ | TEXT der Layoutdatei, `OptionsMenuInit` setzt Werte | **behoben** (leer) |
| Skirmish: Armeen, Farben, Teams | `FactionAmerica`, `Color 1`, `None` | feste Namen | **behoben** (USA/China/GLA/Random, Gold/Red/Blue/Green/Orange/Cyan/Purple/Pink, Team:0..4; `ShellTests`) |
| Skirmish: „Computer“, „(no map selected)“ | feste englische Wörter | kein CSF-Label | **behoben**: die Slots bieten wie `SkirmishGameOptionsMenuInit` `GUI:Open`, `GUI:Closed`, `GUI:EasyAI`, `GUI:MediumAI`, `GUI:HardAI`; Namensfeld `GUI:Player`; die Karte ist wie im Original vorbelegt (`getPreferredMap`/`getDefaultMap`), einen Zustand „keine Karte“ gibt es nur ohne jede Mehrspielerkarte (dann leer). **Offen:** die drei Armeen starten dieselbe KI (die Simulation kennt keine Schwierigkeit; gezählt in `GameShell.SkirmishDifficultyIgnored`) |
| Skirmish: Farbeinträge ohne Farbfläche | Text statt farbiger Fläche | Auswahlfelder kennen keine Eintragsfarben | **offen** |
| Credits | Schriften Arial 24 fett/16 fett/12 | feste Werte | **behoben** (Language.ini + `adjustFontSize`, `ShellTests`); **offen:** `COLUMN`-Paare, Scrollrate (Original 2 px je Frame, hier 40 px/s) |
| Abschlussbildschirm | Spielernamen sind die Armeenamen („FactionAmerica“) | Namen kommen aus der Sitzung | **offen** (außerhalb dieses Auftrags: Spielername aus der Einrichtung) |
| Briefing/Ladeschirm | Standbild statt Film und Fortschrittstexten | Filme/Bink fehlen | **offen** (Phase 16, Punkt 5) |
| Missionstexte | feste Arial-Größen, zentrierte Zeilen, erfundene Platzierung in `MissionPresentation.cs` | nicht über `Language.ini`/`InGameUI.ini` | **behoben**, Abschnitt 4.6 |
| Cursortext (`drawCursorText`), Beschriftungen über Objekten | gibt es nicht | nie umgesetzt | **offen** |
| Superwaffen-/Zeitgeberanzeige | feste Werte im Code (Position, Arial 10, Blinkphase) | `InGameUI.ini` war nicht typisiert | **behoben**, Abschnitt 4.6 |
| Skirmish: Kartenliste | Namen nach zwei Buchstaben abgeschnitten („Al“, „Ar“) | Namen in der ersten von zwei Spalten (5 %/95 %) | **behoben** (zweite Spalte) |
| Skirmish: Kartenvorschau | Diagonale Kreise über „No map selected“ | `ButtonMapStartPosition0..7` im Layout nicht versteckt, das Original versteckt sie bis `positionStartSpots` | **behoben/vereinfacht** (versteckt; Lage der Startplätze fehlt im Katalog) |
| Skirmish: Kartenwahl | Spaltenüberschriften, Felder und Start der Einrichtung scheinen durch die Liste | `showSkirmishGameOptionsUnderlyingGUIElements` nicht umgesetzt | **behoben** (`GAME-SHELL.md` §13.1) |
| Challenge-Menü | `GUI:BioNameEntry` … als Biografie, Knöpfe ohne Bild | Platzhalter des Layouts statt Persona-Texten und Medaillen | **behoben** (§13.4); **offen:** Porträt, Buchstabe-für-Buchstabe-Eingabe |

### 4.6 Missionstexte (`InGameUI`)

Das Original zeichnet die Texte der Missionsskripte aus den Daten von `InGameUI.ini` (Positionen, Farben, Zeiten) und `Language.ini` (Schriften; `InGameUI::init` ersetzt jede Schrift des Blocks durch den gleichnamigen
Language-Platz). Der Client benutzte feste Arial-Größen, zentrierte Zeilen und eigene Texte; jetzt liest `InGameUiSettings` (`CnCNew.Ui/Text`, streng typisiert, Retail: 29 Radiusdekal-Unterblöcke gezählt, Schlüssel
ohne Wert laufen auf die Konstruktorstandardwerte von `InGameUI.cpp` und stehen in `DefaultedKeys`) die Datei und `InGameUiStyle` (`CnCNew.Game/Data`) löst Schriften wie alle anderen Texte (`LanguageFonts.Resolve`,
`adjustFontSize`) auf. Einzelheiten und Quellen: `docs/SCRIPT-ENGINE.md` §7a „Bildschirmtexte nach `InGameUI`“.

| Element | Abweichung (vorher) | Ursache | Status |
|---|---|---|---|
| `DISPLAY_TEXT` | eine zentrierte Titelzeile, 5 s | kein Weg zur Meldungsliste | **behoben**: Zeilenliste oben links (`MessagePosition` 10, 22 in Pixeln, `MessageFont` Arial 10 fett angepasst, `MessageColor1/2` abwechselnd, bis zu 6 Zeilen), Verblassen nach der ganzzahligen Rechnung des Originals (`MessageDelayMS / 30 / 1000` = 0; etwa 9,2 s) |
| `SHOW_MILITARY_CAPTION` | Titel und Text zu einer Zeile zusammengeklebt („KazakhstanEn route“), zentriert, Arial 18 | Zeilenumbruch des Labels ohne Wirkung, feste Schrift | **behoben**: Schreibmaschine (22 Bilder Startverzögerung `MilitaryCaptionDelayMS`, danach ein Zeichen je zweitem Bild, Zeilenwechsel mit erneuter Verzögerung, höchstens 4 Zeilen), erste Zeile Titelschrift (Courier New fett), weitere Zeilen Textschrift, Blockcursor im Takt von 10 Bildern, Position (10, 340) von 800x600 auf den Bildschirm skaliert, nach der Dauer Ausblenden (`floor(Bilder · 0,1)` je Schritt) |
| `DISPLAY_CINEMATIC_TEXT` | feste Arial 16 fett im unteren Balken | – | **behoben**: Schrift des Skripts (`Name - Size:N [Bold]`) angepasst, zentriert, Umbruch Breite − 20, Oberkante bei 0,9 der Höhe, weiß ohne Schatten, `30 · Sekunden` Bilder; von keiner Kampagnenkarte benutzt, nur synthetisch geprüft |
| `DISPLAY_COUNTER` / `DISPLAY_COUNTDOWN_TIMER` | Arial 14 fett, Liste von oben unter dem Balken | feste Werte | **behoben**: ab `NamedTimerCountdownPosition` (0,90 / 0,666 der Bildfläche) nach oben gestapelt, rechtsbündig in der rechten Hälfte, Schrift/Farbe `NamedTimerCountdown*`, bei 0:00 Bereitschaftsschrift und Blinken (500 ms = 15 Bilder) in der Blinkfarbe |
| Superwaffenliste | Konstanten im Code (`PositionX` …), Arial 10 aus dem Code, Blinkphase gegenläufig, „...“ rechtsbündig | `InGameUI.ini` nicht typisiert | **behoben**: Werte und Schriften aus `InGameUiStyle`, Blinkfarbe in ungeraden Perioden (`m_superweaponUsedFlashColor` beginnt wahr), „...“ linksbündig ab x |
| `INGAME_POPUP_MESSAGE` | wie eine Titelzeile, nach 5 s verschwunden | nie als Fenster umgesetzt | **behoben/vereinfacht**: `InGamePopupMessage.wnd` mit OK-Knopf (`MissionPopupWindow`), Enter/Escape beim Loslassen, pausiert und ist modal, wenn das Skript es verlangt; Textfarbe die des Layouts (254) statt `PopupMessageColor` (255) |
| Sprach-/Filmplatzhalter | Arial 14; kein Untertitel | feste Schrift; `doSpeechPlay` blieb unbeachtet | **behoben/bewusst**: Untertitel `DIALOGEVENT:<Name>Subtitle` als Missionsbeschriftung (8 s) wenn vorhanden, nicht leer und ohne führendes `*` (englisch 1 von 2.455); der Platzhalter („(speech: Name)“, „(movie placeholder: Name)“) bleibt, gekennzeichnet, in der `MessageFont` der Daten |
| Hinweis „(cinematic: the mission script has disabled input)“ | erfundener Text | Debughilfe | **entfernt** (das Original zeigt nichts) |
| Meldungen des Spiels (Befehl abgelehnt, Bau fertig, Angriff …) | ein „Ticker“ mit erfundenen englischen Texten, von keinem Code gezeichnet | nie an die Meldungsliste angeschlossen | **behoben**: `HudMessageFeed` schreibt die Labels des Originals in dieselbe Liste wie `DISPLAY_TEXT` (`docs/GAME-HUD.md` §8 „Meldungsliste“); was im Original keine Zeile hat, wird gezählt |
| Letterbox-Balken | ein Achtel der Bildhöhe, unter den Texten | erfunden | **behoben**: `(Höhe − 9/16 · Breite) / 2` oben und unten (800x600: 75, 1024x768: 96, 16:9 und breiter: keine), über den Meldungen und unter dem Filmtext, in einer Sekunde eingeblendet, beim Ende blendet nur der obere aus (`W3DDisplay::renderLetterBox`, `W3DDisplay::draw`) |
| Tippklick der Missionsbeschriftung | fehlte | nicht verdrahtet | **behoben**: je getipptem Zeichen `MilitarySubtitlesTyping` an den Ton des Clients (`InGameUI::update`); gezählt im Missionsbericht |
| Ergebnisbanner („MISSION ACCOMPLISHED“) | feste Arial 28 fett | kein Original-Gegenstück (Ergebnisbildschirm/Film) | **bewusst** als erfundener Platzhalter, feste Schrift, in `MissionOutputCatalog`/Doku so benannt |
## 5. Messwerte (`UiRenderTool --font-audit`, 80 Layouts, 1280x720 und 1920x1080, Uniform)

* 36 verschiedene aufgelöste Schriften (Familie, Pixelhöhe, fett, Stauchung); 1.190 Fenstertextzeilen aus 792 Fenstern mit Text.
* GDI gegen FreeType: 326 Zeilen mit anderer Breite (Σ 2.258 px); davon „Generals“: 294 Zeilen, 2.224 px, größte Abweichung 85 px (Vorlage mit 24 Punkt, 32 px).
* Zeilenhöhe: 29 von 36 Schriften verschieden.
* Vorschub (Großbuchstaben A–Z, „Generals“ 24 px): GDI 364, FreeType-91-% 390, FreeType 100 % 417.
* Glyphdeckung der englischen CSF (6.422 Zeichenketten): 97 verschiedene Codepunkte (einer davon der Zeilenumbruch); alle ab U+0020 liegen in Arial bis auf drei C1-Zeichen (U+0085 ×267, U+0093 ×5, U+0094 ×6), die in der ganzen Kette keinen Glyph haben.
* Zeichenketten mit `&`: 385; mit printf-Umwandlung: 148; mit Zeilenumbruch: 772.
* Familien: `FixedSys`, `Courier`, `Courier New`, `Times New Roman`, `Arial` vorhanden; `Arial Unicode MS` ersetzt (gezählt).
* Werkzeuglauf (92 Bilder + Selbstprüfung): `commands=20997 sprites=44459 drawCalls=3735 batches=1368 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0
  substitutedGlyphs=12 customDraws=56 unbalancedClips=4` (die zwölf fehlenden Labels/Bilder und die zwölf ersetzten Glyphen sind die absichtlichen der synthetischen Szenen plus
  `Static Text`/`Version`/`HardCodedWithControlBarSchemeIni` der rohen Layouts). Neu in den Briefing-Szenen: `WarSchoolBar.tga` ist 512x512, die Tabelle sagt 1.024x1.024 (gezählt,
  Quellrechtecke werden skaliert).

## 6. Goldene Werte

Die Sollwerte belegen Reproduzierbarkeit, nicht Richtigkeit.

* `tests/CnCNew.Game.Tests/baselines/screens.txt`: **48 von 54** Werten geändert (alle Bildschirme außer `MatchLoading`): `MainMenu`, `MainMenu-SinglePlayer`, `QuitConfirm`, `Credits`,
  `ScoreScreen`, `SkirmishMapSelect`, `SkirmishSetup`, `SkirmishSetup-MapChosen`, je sechs Auflösungen. Ursachen: Zuschnittrechtecke der Knopf-/Kästchentexte (jetzt ganzer Bildschirm), Umbruch und
  Einzug, Eingabefeld-/Auswahlfeldtexte, lokalisierte Skirmish-Wörter, Credits-Schriften. (Die Hash-Bildung nutzt das synthetische `ShellTextMeasure`, die Werte hängen also nicht von GDI ab.)
* Danach (Phase 16, Menüs): vier der 54 Werte (`Credits`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`, je sechs Auflösungen, also 24) neu aufgenommen, weil das Hauptmenü unter diesen Bildschirmen nicht mehr gezeichnet wird
  (`GAME-SHELL.md` §13.5).
* `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`: geändert (`B2C97FCE…` → `CE98E458…`): das Geldfenster trägt den Text „$ 10000“ am Schemarechteck statt „$$$“ im 100x100-Feld, die
  von Hand gezeichnete Zahl entfällt.
* `UiRenderTool`: die Bilder ändern sich insgesamt (Schrift); es gibt keine Sollwerte, nur die Zweilauf-Gleichheit.
* Nach dem Zusammenführen mit dem Übergangsauftrag `screens.txt` neu schreiben (`CNCNEW_UPDATE_BASELINES=1`) und die Zeilenenden (CRLF) prüfen.

## 7. Was nicht belegt ist (ein Mensch muss hinsehen)

1. **Ob die Texte jetzt wie im Original aussehen.** Alles Obige ist Übereinstimmung mit dem Referenzcode; die Rastergenauigkeit hängt an der Arial-Fassung und am Hinting dieser Installation.
2. **Das Clientfenster.** Alle Bilder stammen aus dem Offscreen-Werkzeug und dem `--smoke-test --capture` des Clients, nie aus einem echten Fenster mit Swap-Chain (Gammaverhalten und damit die
   Wirkung von `UiAlphaCurves`, DPI-Skalierung des Fensters, `UiRenderer`-Zähler im Betrieb).
3. **Tooltips:** Aussehen, Platzierung, Geschwindigkeit der Einblendung und die vereinfachte Befehlsknopfbeschreibung gegen das Originalpopup.
4. **Uniform-Modus gegen Originalmaßstab:** bei 1280x720 und größer sind Texte im Verhältnis zu den Fenstern anders als im Original, weil die Hülle gleichmäßig skaliert (Abschnitt 4.1).
5. **Durchscheinende Menüs** – **behoben** (`docs/GAME-SHELL.md` §13): die Hülle zeichnet nur noch den obersten ersetzenden Bildschirm (`Shell::push` versteckt den darunter) und was darüber liegt; Optionen, Kartenwahl
   und Dialoge sind wie im Original Überlagerungen. Eine Hintergrundebene war nicht nötig (Rahmen und Lineal bringen die ersetzenden Layouts selbst mit). Vorher schienen die Knopftexte des Hauptmenüs („CHALLENGE“,
   „BACK“, „SKIRMISH“ …) durch die neue Seite, am stärksten in `flow-skirmish-setup-map-*.png`.
6. **`SinglePlayerMenu.wnd`** (Neu/Laden/Zurück nach „USA“) – **behoben**: ist ein Erbstück des ersten Generals, das Zero Hour nie öffnet; das Original fragt die Schwierigkeit im Hauptmenü (`MapBorder4`), die Hülle
   ebenso (`GAME-SHELL.md` §13.2). Die Seite mit Times New Roman gibt es nicht mehr.
7. **Tooltip der Befehlsknöpfe:** Voraussetzungen, Geldmangel und gekaufte Upgrades stehen jetzt darin (`docs/GAME-HUD.md` §5a); anzusehen bleibt, ob die Zeilen im Zeigertooltip lesbar sind – das Original hat dafür ein eigenes Fenster.
8. **Missionstexte (Abschnitt 4.6):** Alle Zeiten, Reihenfolgen und Zahlen stimmen mit `InGameUI.cpp`/`W3DDisplay.cpp` überein und sind in Tests festgehalten, aber nie mit einem Original verglichen. Ein Mensch muss ansehen: wie die
   Schreibmaschine im Bewegtbild wirkt (Takt, Cursor, Ausblenden), ob Courier New in dieser Größe wie im Original aussieht, die Lage von Meldungen, Zeitgebern und Superwaffenliste auf den Bildschirmen, das Meldungsfenster
   (Knopfbilder, Textumbruch) und den Wegfall des Hinweises bei gesperrter Eingabe.
9. **Aktualisierung je Logikschritt statt je Bild:** Das Original verblasst Meldungen und Missionsbeschriftung in `InGameUI::update` einmal je gezeichnetem Bild (also schneller ab 60 Bildern je Sekunde); hier einmal je
   Logikschritt (30 je Sekunde). Bei der Schreibmaschine macht das keinen Unterschied (sie vergleicht mit dem Logikbild), beim Verblassen schon.
10. **Letterbox** – **behoben** nach `W3DDisplay::renderLetterBox`: Balken `(Höhe − 9/16 · Breite) / 2`, bei 16:9 und breiter keine; sie liegen über den Meldungen (y = 22 verschwindet bei 4:3
    also unter dem oberen Balken, wie im Original) und unter dem Filmtext. Abweichung: die Blende zählt Logikschritte (30 = 1000 ms) statt Wanduhr, und der Mauszeiger des Clients liegt
    über den Balken (im Original darunter). Anzusehen: ein 4:3-Fenster während eines Intros.
11. **Tippklick** – **behoben**: je getipptem Zeichen `MilitarySubtitlesTyping` (`SoundEffects.ini`: `utext1a`, Lautstärke 15, `Type = ui player`); auf `MD_USA01` 52 Klicks angefragt und
    52 gestartet. **Gehört hat es niemand**, auch nicht, ob 15 Klicks je Sekunde über der Musik angenehm sind.
12. **Befehlsrückmeldungen** – **behoben**: sie stehen in derselben Meldungsliste wie `DISPLAY_TEXT` (`docs/GAME-HUD.md` §8). Anzusehen: ob die Zeilen oben links auffallen, wenn ein
    Bauplatz abgelehnt wird.

## 8. Tests und Werkzeuge

| Prüfung | Ort |
|---|---|
| Umbruch/Blockbreite/Zentrierung (9 Fälle) und Tooltip-Layout (Position, Kanten, Einblendung) | `tests/CnCNew.Ui.Tests/SentenceLayoutTests.cs` |
| Tooltipstil aus `Mouse.ini`, strenge Fehler | `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (`MouseTooltipSettingsAreTypedFromTheGraph`) |
| Zuschnitt und Position der Knopf-/Kästchentexte, Tooltipzeit und -alter | `tests/CnCNew.Ui.Tests/WndTests.cs` (`DrawListGolden`, `TooltipStateMachine`) |
| Geldfenster, Befehlsknopf-Tooltip, keine Texte auf den Symbolen | `tests/CnCNew.Game.Tests/HudTests.cs` |
| Skirmish-Wörter, leere Eingabefelder, Zeigertooltip der Hülle, Credits-Schriften | `tests/CnCNew.Game.Tests/ShellTests.cs` |
| Bildschirmstapel (nichts vom Hauptmenü unter ersetzenden Screens), Kartenwahl, Challenge-Menü, Escape in der Ladeliste | `tests/CnCNew.Game.Tests/ShellTests.cs` (`ScreenStackDrawing`, `ChallengeMenuBio`, `LoadGameEscapeWithoutFocus`) |
| `KeyboardHome`, Bild-/Größenersatz eines Fensters | `tests/CnCNew.Ui.Tests/WndTests.cs` |
| `InGameUI.ini` typisiert (synthetisch und Retaildatei, Language-Platz gegen INI in Größe/Fett) | `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (`InGameUiSettingsAreTypedFromTheGraph`, `RealInGameUi`) |
| Meldungsliste, Schreibmaschine Bild für Bild, Ausblenden, Zeitgeber, Filmtext, Meldungsfenster, Skalierung | `tests/CnCNew.Game.Tests/MissionTextTests.cs` (synthetisch; mit `--content-dir`: `MD_USA01`, `MD_CHI02`, Meldungsfenster in `GC_ChemGeneral`) |
| Meldungen des Spiels: Labels, Lage, Farben, Verblassen, Zählung; abgelehnter Bauplatz über den echten Klickweg | `tests/CnCNew.Game.Tests/HudTests.cs` (`MessageList`, `MessageListFromARefusedPlacement`), `MissionTextTests.cs` (`RealSharedMessageList`) |
| Letterbox (Maße, Blende, Reihenfolge), Tippklick (synthetisch und `MD_USA01` mit echtem Audio-Director) | `tests/CnCNew.Game.Tests/MissionTextTests.cs` (`LetterboxFollowsTheOriginal`, `TypingClick`, `RealTypingClick`) |
| Knopfbeschreibung mit Voraussetzungen, Geldmangel, gekauftem Upgrade; Geld- und Energiefenster | `tests/CnCNew.Game.Tests/HudTests.cs` (`CommandButtonRequirements`, `MoneyAndPowerTooltips`) |
| Tooltip eines deaktivierten Fensters | `tests/CnCNew.Ui.Tests/WndTests.cs` (`TooltipStateMachine`) |
| Bild der Befehlsleiste mit Meldungszeilen und „Requires“-Tooltip, Letterbox über den Meldungen (drei Größen) | `tools/CnCNew.UiRenderTool` (`HudFeedbackScenes`: `hudfeedback-*.png`, `hudletterbox-*.png`) |
| Missionstextbild (alle Texte eines Bildschirms, drei Größen) | `tools/CnCNew.UiRenderTool` (`MissionTextScenes`) |
| GDI-Schrift (Familie wirklich Arial, 17er-Deckungsstufen, Zellmaße, Generals schmaler) | `tools/CnCNew.UiRenderTool` (`FontAudit.SelfCheck` bei jedem Lauf) |
| Schriftvergleich, Glyphdeckung, Platzhalter-/Hotkey-Zählung | `tools/CnCNew.UiRenderTool -- --font-audit` |
| Alphakurve für helle Schrift, Glyphcache mit Kurve | `tests/CnCNew.Ui.Tests/UiRenderingTests.cs` (`LightTextGetsTheGammaSpaceAlphaCurve`) |
