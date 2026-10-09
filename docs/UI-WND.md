# UI-WND – Fensterlayouts, Skalierung, Laufzeit und Zeichenliste

Paket B2 der Präsentationsschicht (`src/CnCNew.Ui/Wnd`, `Layout`, `Runtime`; Tests in `tests/CnCNew.Ui.Tests/WndTests.cs`).
`CnCNew.Ui` hängt nur von `CnCNew.Data` ab (derzeit ohne genutzte Typen daraus): kein Stride, keine Simulation, kein
`CnCNew.Content`, keine Archive. Parser nehmen `string`, `ReadOnlySpan<byte>` oder `Stream`; der Aufrufer liefert die Dateien.
Die Bibliothek liest keine Wandzeit, erzeugt keine Zufallszahlen und rendert nichts selbst.

## 1. Einstiegspunkte für Integratoren

| Aufgabe | API |
| --- | --- |
| `.wnd` parsen | `WndParser.Parse(stream \| bytes \| text, fileName)` → `WndLayout` (Fenster, `WndDiagnostic`s, Schlüsselzählung) |
| Header-Templates | `WndHeaderTemplateTable.Parse(text, "Data/English/HeaderTemplate.ini")` |
| Fensterbaum | `new UiWindowTree(viewport, UiScaleOptions, new UiTreeServices(textMeasure) { Images, Text, HeaderTemplates, DefaultTooltipDelayMs })`, `AddLayout(layout)`, `RemoveLayout(fileName)`, `SetViewport(viewport, options)` |
| Suche | `FindByName("MainMenu.wnd:ButtonSinglePlayer")` (Groß-/Kleinschreibung zählt, erstes erzeugtes Fenster gewinnt) |
| Eingabe | `MouseMove/MouseDown/MouseUp/MouseWheel`, `KeyDown(UiKey, UiModifiers)`, `Character(char)`, `AdvanceTo(ms)`; Rückgabe `true` = UI hat das Ereignis verbraucht |
| Nachrichten | `Register(callbackName, Action<UiWindowTree, UiEvent>)`, `RegisterTooltip(name, Func<…, string?>)`, `Dispatch()` → `UiDelivery`s, `UnregisteredCallbacks`, `Post(kind, source)` |
| Gadget-Zustand | `UiWindow.SetHidden/SetEnabled/SetText/SetTextLabel/SetChecked/SetProgress/AddListRow/SetListSelection/AddComboEntry/SetComboSelection/SetSliderValue/SetEntryText/SelectTab` (still, wie die `Gadget*Set*`-Helfer des Originals) |
| Zeichnen | `BuildDrawList()` → `UiDrawList` (Befehle + `UiDrawStatistics`, `Dump()` für Goldens) |
| Renderer-Schnittstellen | `IUiTextMeasure` (Breite, Zeilenhöhe), `IUiImageLookup` (Größe eines Mapped Image, z. B. über die `MappedImageTable` von Paket B1), `IUiTextSource` (Labels wie `GUI:Options`) |

## 2. Format (gegen die Realdaten geprüft)

Grammatik nach `GameWindowManagerScript.cpp` (`winCreateFromScript`, `parseLayoutBlock`, `parseWindow`, `parseChildWindows`,
Tabelle `gameWindowFieldList`), neu geschrieben:

- Erste Anweisung `FILE_VERSION = 2;`, ab Version 2 ein Block `STARTLAYOUTBLOCK … ENDLAYOUTBLOCK` mit `LAYOUTINIT`,
  `LAYOUTUPDATE`, `LAYOUTSHUTDOWN` (Namen ohne Anführungszeichen; `[None]` → `null`).
- Schlüssel sind durch Leerraum getrennte Tokens; ein Wert reicht vom Token nach `=` bis zum nächsten `;`. Zeilenenden
  (`\r\n`/`\n`) und Tabs sind bedeutungslos (Test mit CRLF/Tab-Variante byteidentisch serialisiert). `;` ist Anweisungsende,
  keine Kommentarmarke – die WND-Dateien enthalten keine Kommentare.
- `WINDOW` beginnt ein Fenster: zuerst `WINDOWTYPE`, dann `SCREENRECT = UPPERLEFT: x y, BOTTOMRIGHT: x y, CREATIONRESOLUTION: w h`,
  danach beliebige Felder, `CHILD` öffnet die Kinderliste (vor jedem Kind-`WINDOW` steht erneut `CHILD`), `ENDALLCHILDREN`
  schließt sie, `END` schließt das Fenster. Dateireihenfolge = Z-Reihenfolge (später = oben), wie `addWindowToParent` +
  `drawWindow` (Kinder rückwärts gezeichnet).
- Felder: `NAME` (`Datei.wnd:Name`), `STATUS`/`STYLE` (`+`-Listen über `WindowStatusNames`/`WindowStyleNames`, `NULL` = leer),
  `SYSTEMCALLBACK`, `INPUTCALLBACK`, `TOOLTIPCALLBACK`, `DRAWCALLBACK`, `HEADERTEMPLATE` (Anführungszeichen, `[None]`/`[NONE]` → `null`),
  `FONT = NAME: "…", SIZE: n, BOLD: n`, `TOOLTIPDELAY`, `TOOLTIPTEXT`, `TEXT`, `TEXTCOLOR` (sechs RGBA-Werte),
  `IMAGEOFFSET`, die neun Einträge `IMAGE: name, COLOR: r g b a, BORDERCOLOR: r g b a` jeder `*DRAWDATA`-Familie
  (`ENABLED/DISABLED/HILITE` plus List-Box-, Slider-Thumb- und Combo-Box-Teile), Gadgetdaten `LISTBOXDATA`
  (optional `SCROLLIFATEND`, `COLUMNSWIDTH` nur bei mehr als einer Spalte), `COMBOBOXDATA`, `SLIDERDATA`, `RADIOBUTTONDATA`,
  `STATICTEXTDATA`, `TEXTENTRYDATA`, `TABCONTROLDATA`. Veraltete Schlüssel `DATA` und `TOOLTIP` werden erkannt und als
  Kompatibilitätsfall gemeldet.
- Fenstertypen: `USER, PUSHBUTTON, RADIOBUTTON, CHECKBOX, VERTSLIDER, HORZSLIDER, SCROLLLISTBOX, ENTRYFIELD, STATICTEXT,
  PROGRESSBAR, TABCONTROL, TABPANE, COMBOBOX`; unbekannte Typen bleiben als `WndWindowType.Unknown` mit Originalnamen erhalten.
- `255 255 255 0` ist `GAME_COLOR_UNDEFINED` (`Color.h`); die Zeichenliste lässt Farben mit Alpha 0 weg.
- `TEXT` eines `ENTRYFIELD` ist Literaltext (`setWindowText` übersetzt ihn nicht), sonst ein Label.

**Diagnosen.** Jede Abweichung ist ein `WndDiagnostic(Code, Severity, File, Line, Message)`. `Compatibility` = in den
ausgelieferten Dateien vorkommende, erklärte Eigenheit, die exakt wie im Original behandelt wird; `Unknown` und `Error` sind
unerklärt und dürfen in den Realdaten nicht vorkommen. Erklärte Fälle:

| Code | Vorkommen | Verhalten (wie das Original) |
| --- | --- | --- |
| `status-disabled-flag` | 2 (`ControlBar.wnd`) | `DISABLED` ist kein Statusname; `parseBitString` ignoriert ihn, das Fenster ist deaktiviert, weil `ENABLED` fehlt |
| `statement-swallows-next-key` | 4 (`ControlBar.wnd`) | `ENABLEDDRAWDATA` ohne `;` verschluckt `DISABLEDDRAWDATA`; `parseDrawData` liest neun Einträge, der Rest verfällt, die Disabled-Daten bleiben undefiniert |
| `label-mismatch` | 6 (`ControlBar`, `Diplomacy`, `GeneralsExpPoints`, `InGameChat`) | `TEXTCOLOR` mit `ENABLEDBORDER` an der Stelle von `HILITEBORDER`; das Original verwirft Label-Wörter ungeprüft und liest positionsweise |
| `obsolete-data-key`, `obsolete-tooltip-key`, `field-after-children`, `children-closed-by-end`, `child-default-color` | 0 | nur synthetisch getestet |

## 3. Skalierung (`UiScaler`, `UiScaleOptions`)

Fenster sind in einer Erstellungsauflösung (`CREATIONRESOLUTION`, Realdaten: 800x600 und 1920x1080) in absoluten
Bildschirmkoordinaten verfasst. Abgebildet werden immer beide Kanten; die Größe ist ihre Differenz, gemeinsame Kanten bleiben
gemeinsam. Abbildung pro Achse `Offset + floor(v * Zähler / Nenner)` in exakter Ganzzahlarithmetik (`long`, Abrundung gegen
minus unendlich). Das Original rechnet `(Int)(x * (Real)W / (Real)cw)` in `float` und liegt für manche Werte ein Pixel tiefer
(100 · 2,4f = 239,99998 → 239 statt 240); diese Abweichung ist bewusst.

| Modus | Regel |
| --- | --- |
| `Stretch` (Original, `parseScreenRect`) | x und y unabhängig: `W/cw`, `H/ch` |
| `Uniform` | ein Faktor `s = min(W/cw, H/ch)`; der Erstellungsbereich wird zentriert (Letterbox/Pillarbox) |
| `Anchor` | Gruppenregel: Ein Fenster, das den ganzen Erstellungsbereich bedeckt (Hintergründe), wird gestreckt; seine Kinder bilden eigene Gruppen. Jede andere Gruppenwurzel wählt pro Achse einen Anker, den alle Nachkommen teilen: volle Ausdehnung → Strecken, sonst nach der Lage der Mitte (erstes Drittel → Anfang, letztes Drittel → Ende, sonst Mitte) mit Faktor `s`. Offsets sind `0`, `(W − floor(cw·s))/2` oder `W − floor(cw·s)`. |

Schriften: `Stretch` folgt dem Original – Header-Template-Schriften wachsen mit `GlobalLanguage::adjustFontSize`
(klassisch `1 + (W/800 − 1) · ResolutionFontAdjustment`, begrenzt auf [1, 2], abgerundet; `Language.ini`: 0,7), `FONT`
des Fensters bleibt unverändert. `Uniform`/`Anchor` skalieren die `FONT` des Fensters mit dem gleichförmigen Faktor seiner
Erstellungsauflösung, eine Header-Template-Schrift dagegen mit dem gleichförmigen Faktor gegen 800x600 – Template-Punktgrößen
beziehen sich wie in `adjustFontSize` immer auf 800 Pixel Breite, auch in einem bei 1920x1080 verfassten Layout (sonst
schrumpfen dessen Texte auf 800/1920). Danach multipliziert der Barrierefreiheitsfaktor `TextScale`; Mindestgröße 1 (`decimal`, abgerundet).
`MinimumHitTarget` (Pixel, 0 = aus) vergrößert die Trefferfläche interaktiver Gadgets symmetrisch, ohne sie zu verschieben;
Kinder bleiben auf den Elternbereich beschnitten.

Geprüfte Invarianten (Tabelle 640x480, 800x600, 1024x768, 1280x720, 1280x800, 1600x900, 1920x1080, 2560x1080, 3440x1440,
3840x1080, 1080x1920, alle drei Modi):

- Jedes sichtbare, im Erstellungsbereich verfasste Fenster bleibt im Viewport (Realdaten: 0 Verstöße; 25 Fenster pro Durchlauf
  sind außerhalb verfasst und ausgenommen).
- Überlappungsbeziehungen (Paare gleicher Erstellungsauflösung): `Stretch`/`Uniform` identisch, solange keine Achse schrumpft;
  beim Verkleinern können nur Überlappungen verloren gehen (monotone Abbildung), nie neue entstehen. `Anchor` kann Überlappungen
  zwischen verschiedenen Gruppen auflösen (Realdaten 1080x1920: 125 Paare, übrige Auflösungen je 1), erzeugt aber keine neuen.
- Treffertest der vier Eckpixel und der Mitte jedes sichtbaren, aktiven Gadgets (verfasste Punkte durch die Transformation des
  Gadgets abgebildet) liefert dasselbe Fenster wie in der Erstellungsauflösung: exakt für `Stretch`/`Uniform`, wenn keine Achse
  schrumpft (streng monotone Abbildung). Realdaten: 62.315 Proben je Modus, 0 Abweichungen ohne Schrumpfen; mit Schrumpfen
  (z. B. 640x480: 699) oder in Dateien mit gemischter Erstellungsauflösung (`Diplomacy`, `GeneralsExpPoints`,
  `DisconnectScreen`, `MessageBox`, `QuitMessageBox`) werden die Abweichungen gezählt und ausgegeben. `Anchor` ohne Schrumpfen:
  30 Abweichungen bei 1080x1920, sonst höchstens 1 je Auflösung (gruppenübergreifende Überdeckungen).
- Neu-Layout ist reproduzierbar; zwei Testläufe liefern bis auf Zeitmessungen identische Ausgaben.

## 4. Laufzeit (`UiWindowTree`, `UiWindow`)

- **Zustand:** versteckt, aktiviert (eigene Bits wie im Original; wirksam über die Eltern), hilited, selected, Fokus,
  Text (Label oder Klartext), Gadgetzustände (`UiListBoxState`, `UiSliderState`, `UiComboBoxState`, `UiTextEditState`,
  `UiTabControlState`, Fortschritt in Prozent).
- **Z-Reihenfolge:** Wurzeln in Einfügereihenfolge, Ebenen `BELOW` < normal < `ABOVE` (`winRepaint`); Kinder in Dateireihenfolge.
- **Treffertest** (`GameWindow::winPointInChild`): tiefstes sichtbares, aktives Fenster; spätere Geschwister liegen oben;
  Kinder nur innerhalb ihres Elternbereichs; deaktivierte und versteckte Fenster werden übersprungen; Enthaltensein halboffen
  `[x, x+w)` (das Original prüft `<=`). Eingabeziel: zuerst die offene Combo-Liste („lone window"), `NOINPUT`-Fenster
  verschlucken das Ereignis außer als Teil einer Combo-Box (`winProcessMouseEvent`).
- **Maus:** Grab-Fenster beim Linksdruck; das Loslassen zählt nur innerhalb des Grab-Fensters (Klick = Druck und Loslassen im
  Button). Enter/Leave (`GBM_MOUSE_ENTERING/LEAVING`) und die Hervorhebung nur für Gadgets mit `MOUSETRACK`, deren
  Eingabefunktion `GWM_MOUSE_ENTERING` behandelt (Knopf, Check-Box, Radio, Regler, List-Box, Combo-Box, Texteingabe) –
  nicht für Static Text, Fortschrittsbalken und Tab-Control, obwohl die Dateien ihnen `MOUSETRACK` und `HILITE*`-Daten
  mitgeben. Das Verlassen löscht die Hervorhebung unbedingt (auch am fokussierten Gadget), Verstecken oder Deaktivieren
  löscht Hilite und Pressed, `PointerLeft()` meldet dem Baum, dass ein anderer Screen die Maus übernommen hat. Während
  eines Grabs ruht die Hover-Verfolgung wie im Original. Den Tastaturfokus nimmt ein Klick nur bei Combo-Box, List-Box
  und Texteingabe (`winSetFocus` in deren Eingabefunktionen), nicht bei Knöpfen, Check-Boxen, Radios und Reglern.
- **Gadgets:** Push-Button (Hover, gedrückt, Klick; `ON_MOUSE_DOWN`, `CHECK_LIKE`, `RIGHT_CLICK`), Check-Box (Umschalten beim
  Loslassen im Fenster), Radio-Gruppen (Gruppe + Datei wie `unselectOtherRadioOfGroup`), Slider (Ziehen, Klick springt,
  Pfeiltasten; Thumb 13 bzw. 16 Einheiten der Erstellungsauflösung, skaliert), List-Box (Auswahl, Mehrfachauswahl,
  Doppelklick über `DoubleClickMs` der Tree-Uhr, Rechtsklick, Rad, Scrollbar-Pfeile/-Spur, Spaltenbreiten aus
  `COLUMNSWIDTH`, `LENGTH` mit `AUTOPURGE`), Combo-Box (Aufklappen, Auswahl, Escape/Außenklick schließt, `MAXDISPLAY`,
  editierbar), Texteingabe (Caret, Einfügen/Löschen, Umschalt-Auswahl, Filter `NUMERICALONLY`/`ALPHANUMERICALONLY`/`ASCIIONLY`,
  `MAXLEN`, `SECRETTEXT`), Fortschrittsbalken, Tab-Control (Reiterköpfe entlang der Oberkante, genau eine Pane sichtbar).
- **Tastatur/Barrierefreiheit:** Tab/Umschalt+Tab durchlaufen alle fokussierbaren Gadgets (sichtbar, wirksam aktiv, weder
  `NOFOCUS` noch `NOINPUT`) in Lesereihenfolge (Oberkante, dann linke Kante, dann Erzeugungsreihenfolge; keine
  Zeilenbündelung). Enter/Leertaste lösen Buttons, Check-Boxen, Radios aus; Pfeiltasten bewegen den Fokus zwischen Buttons
  (`winNextTab`/`winPrevTab`), ändern Slider/List-/Combo-Auswahl und bewegen den Caret. Fokussierte Buttons sind hilited
  (`GWM_INPUT_FOCUS`), das ist die sichtbare Fokusmarke.

### Nachrichten- und Callback-Modell

Gadgets senden an ihren Owner = Elternfenster (Wurzel-Gadgets an sich selbst). `Dispatch()` liefert die wartenden Ereignisse
in Reihenfolge (auch von Handlern erzeugte) an den `SYSTEMCALLBACK` des Empfängers:

- `[None]`, `GameWinDefaultSystem`, `Gadget*System` → `NoCallback` (die Standard- bzw. Gadget-Systemfunktion ignoriert sie).
- `PassMessagesToParentSystem` → weiter zum Elternfenster; `PassSelectedButtonsToParentSystem` → nur `GBM_SELECTED`,
  `GBM_SELECTED_RIGHT`, `GBM_MOUSE_ENTERING/LEAVING`, `GEM_EDIT_DONE` weiter, sonst `Filtered` (`GameWindowManager.cpp`).
- registrierter Name → `Handled`; unbekannter Name → `Unregistered`, gezählt in `UnregisteredCallbacks` (nie still).
- Unverbrauchte Tasten gehen als `UiEventKind.Key` vom Fokus (ohne Fokus von der obersten sichtbaren Wurzel) aufwärts zum
  nächsten `INPUTCALLBACK` (`winProcessKey`): `GameWinBlockInput` → `Blocked`, `GameWinDefaultInput`/`[None]` → weiter,
  unbekannte Namen werden gezählt und weitergereicht (im Original: keine Funktion).

Realdaten: `GBM_SELECTED` aller 440 Push-Buttons (83 Dateien, je Datei ein eigener Baum) landet bei 49 verschiedenen Menü-Callbacks (z. B. `MainMenuSystem` 30,
`SkirmishGameOptionsMenuSystem` 13, `ControlBarSystem` 68), 16 enden ohne Callback. Diese Namen muss die Shell registrieren.

Ausgesendete Nachrichten (`UiEventKind`): `GBM_MOUSE_ENTERING`, `GBM_MOUSE_LEAVING`, `GBM_SELECTED` (Button, Check-Box, Radio;
Wert 1/0 bei Umschaltern), `GBM_SELECTED_RIGHT`, `GSM_SLIDER_TRACK`, `GSM_SLIDER_DONE`, `GLM_SELECTED`, `GLM_DOUBLE_CLICKED`,
`GLM_RIGHT_CLICKED`, `GCM_SELECTED`, `GCM_UPDATE_TEXT`, `GEM_UPDATE_TEXT`, `GEM_EDIT_DONE`, `GGM_FOCUS_CHANGE`, Taste (`GWM_CHAR`).

Nicht ausgesendet (bewusst): `GGM_LEFT_DRAG` (Ziehen verschiebbarer `DRAGGABLE`-Fenster ist nicht umgesetzt), `GGM_SET_LABEL`,
`GGM_GET_LABEL`, `GGM_RESIZED`, `GGM_CLOSE`, `GBM_SET_SELECTION`, `GSM_SET_SLIDER`, `GSM_SET_MIN_MAX`, `GLM_ADD_ENTRY`,
`GLM_DEL_ENTRY`, `GLM_DEL_ALL`, `GLM_SET_SELECTION`, `GLM_GET_SELECTION`, `GLM_TOGGLE_MULTI_SELECTION`, `GLM_GET_TEXT`,
`GLM_SET_UP_BUTTON`, `GLM_SET_DOWN_BUTTON`, `GLM_SET_SLIDER`, `GLM_SCROLL_BUFFER`, `GLM_UPDATE_DISPLAY`, `GLM_GET/SET_ITEM_DATA`,
`GCM_ADD_ENTRY`, `GCM_DEL_ENTRY`, `GCM_DEL_ALL`, `GCM_GET_TEXT`, `GCM_SET_TEXT`, `GCM_EDIT_DONE`, `GCM_GET/SET_ITEM_DATA`,
`GCM_GET/SET_SELECTION`, `GEM_GET_TEXT`, `GEM_SET_TEXT`, `GPM_SET_PROGRESS` – die Setter/Getter sind Methoden an `UiWindow`;
rohe `GWM_*`-Eingabenachrichten (`GWM_CREATE`, `GWM_DESTROY`, `GWM_SCRIPT_CREATE`, `GWM_INPUT_FOCUS`, `GWM_IME_*`) sind durch
die Eingabemethoden ersetzt.

Bewusste Abweichungen: `GSM_SLIDER_DONE` auch für horizontale Slider (das Original sendet ihn nur beim vertikalen Thumb) und
nach Pfeiltasten; die Pfeiltasten des horizontalen Sliders sind nicht vertauscht wie in `GadgetHorizontalSliderInput`;
vertikale Slider haben das Maximum oben; Enter/Leertaste lösen bei Buttons schon beim Drücken aus.

### Tooltips

Reine Zustandsmaschine über `AdvanceTo(ms)` (die Uhr darf nicht rückwärts laufen). Ein Fenster zeigt seinen Tooltip, wenn es
seit `TOOLTIPDELAY` ms (≥ 0) bzw. `DefaultTooltipDelayMs` (`Mouse.ini TooltipDelayTime`, Einzelhandel 800) gehovert ist;
Fensterwechsel startet neu, ein gehaltener Button unterdrückt ihn. Vorrang wie `winProcessMouseEvent`: ein
`TOOLTIPCALLBACK` vor `TOOLTIPTEXT`; `GameWinDefaultTooltip` zeigt nichts (9 Fenster in den 83 Dateien), unregistrierte Namen werden einmal pro
Hover gezählt; Labels werden über `IUiTextSource` aufgelöst, sonst mit Label als Text geliefert.

## 5. Zeichenliste (`UiDrawList`)

Befehle: `UiFillRect`, `UiBorderRect` (1 px innen), `UiImageDraw` (Mapped-Image-Name, Zielrechteck, Tönung, Spiegelung),
`UiTextDraw` (eine gesetzte Zeile: Text, Label-Schlüssel, Schrift, Farbe, Schattenfarbe = `TEXTCOLOR`-Border, Position,
Ausrichtung, Clip), `UiPushClip`/`UiPopClip`, `UiCustomDraw` (nicht umgesetzter `DRAWCALLBACK`, vom Host zu zeichnen).
Reihenfolge wie `winRepaint`/`drawWindow`; `SEE_THRU` überspringt nur das Fenster selbst; `W3DNoDraw`/`GameWinDefaultDraw`
zeichnen nichts; `W3DGameWinDefaultDraw` zeichnet den Standardhintergrund. Zustand → Datensatz: deaktiviert (eigenes
`ENABLED`-Bit fehlt) → `DISABLED*`, hilited → `HILITE*`, sonst `ENABLED*`. Gadgetregeln (Quellen `W3DGameWindow.cpp`,
`W3DPushButton.cpp`, `W3DCheckBox.cpp`, `W3DRadioButton.cpp`, `W3DStaticText.cpp`, Header `Gadget*.h`):

- Standard: mit `IMAGE` Bild 0 (plus `IMAGEOFFSET`), sonst Rahmen und Füllung von Eintrag 0.
- Push-Button: mit Mittelbild (Enabled-Eintrag 5) dreiteilig links 0/1, Mitte 5/3, rechts 6/4 (ausgewählt), sonst ein Bild
  (Enabled+ausgewählt nimmt Hilite 1 wie das Original); Farbvariante Eintrag 0/1; Text zentriert, Umbruch auf Breite,
  `WRAP_CENTERED` zentriert jede Zeile.
- Check-Box: Kästchen links (Höhe − 2·3), Einträge 1/2; in der Farbvariante zusätzlich eine gefüllte Häkchenmarke; Text ab x + Höhe.
- Radio: dreiteilig 0/1/2, ausgewählt Hilite 3/4/5.
- Static Text: Umbruch bei Breite − 10, links 7 oder zentriert, vertikal zentriert, Clip auf das Fenster.
- Texteingabe: dreiteilig 0/2/1, Auswahl in Hilite-Textfarbe, Caret (1 px) im Fokus; List-Box: Zeilen (Zeilenhöhe =
  `LineHeight` der Schrift), Auswahl dreiteilig 1/3/2 oder Farbe 1, Zellentext je Spalte, Scrollbar aus den
  `LISTBOX*`-/`SLIDERTHUMB*`-Daten; Combo-Box: Editfeld, Pfeilknopf, offene Liste zuletzt (oben); horizontaler Slider mit
  `IMAGE` (`W3DGadgetHorizontalSliderImageDraw`): zentrierte Reihe quadratischer Kästchen (Kante = Breite des gefüllten
  Bildes im Anzeigemaßstab, Abstand 2), bis zum Wert Disabled 0, danach Disabled 1, bei Hervorhebung dahinter eine Reihe
  Hilite 0; ohne `IMAGE` Rahmen und Füllung von Eintrag 0; vertikaler Slider mit `IMAGE` dreiteilig 0/2/1. Thumb des
  horizontalen Sliders 13x16, 10 unter der Oberkante (`gogoGadgetSlider`), aus `SLIDERTHUMB*` nach Zustand, beim Ziehen
  Eintrag 1; Fortschritt: Hintergrund 0/2/1, Balken 4/6/5 im Clip der Füllbreite (Farbvariante Eintrag 0 und 4).
- Mittelteile werden unter einem Clip gekachelt (höchstens 256 Kacheln, sonst gestreckt). Unbekannte Bildgrößen werden in
  `UnresolvedImageSizes` gezählt (Enden dann halbe Höhe, Mitte gestreckt); unaufgelöste Labels in `UnresolvedLabels`.

Golden-Tests vergleichen `Dump()` mit literalen Erwartungen (Hintergrund, gedrückter Button, umbrochener Static Text,
Check-Box, Custom-Draw; dreiteiliger Button mit Kacheln).

## 6. Realdatenzahlen (`dotnet run --project tests/CnCNew.Ui.Tests -- --content-dir game/content`)

- `Window/`: 83 Dateien = 80 `*.wnd` + 3 Varianten (`OptionsMenu.wnd_hideip`, `NetworkDirectConnect.wnd_hideip`,
  `GameSpyLoginProfile.wnd_hidemail`), alle geparst; die Varianten lädt das Spiel nicht über ihren Namen.
- 1.814 Fenster (davon 1.675 in den 80 `*.wnd`): STATICTEXT 582, USER 450, PUSHBUTTON 440, COMBOBOX 123, CHECKBOX 66,
  SCROLLLISTBOX 40, ENTRYFIELD 39, PROGRESSBAR 34, RADIOBUTTON 24, HORZSLIDER 15, VERTSLIDER 1; keine TABCONTROL/TABPANE.
  Erstellungsauflösung 800x600: 1.467, 1920x1080: 347.
- Schlüssel (Auszug): WINDOW 1.814, CHILD 1.731, ENDALLCHILDREN 261, TEXT 880, TOOLTIPTEXT 159, STATICTEXTDATA 582,
  COMBOBOXDATA 123, LISTBOXDATA 40, TEXTENTRYDATA 39, RADIOBUTTONDATA 24, SLIDERDATA 16, HEADERTEMPLATE 1.782
  (vollständige Liste gibt der Test aus).
- Diagnosen: 12, alle erklärt (Tabelle in Abschnitt 2), 0 unerklärt. Alle Fenster tragen einen dekorierten Namen.
- `HeaderTemplate.ini`: 19 Templates, 0 Diagnosen, keine unbekannten Template-Namen; kein Fenster ohne Schrift. In einem Baum
  mit allen Layouts: 211 doppelte Namen (Varianten und Mehrfachdateien; `FindByName` liefert das zuerst erzeugte Fenster).
- Laufzeiten (Debug, einmalige Messung, JIT inklusive): Parsen aller 83 Dateien ≈ 0,4 s; 2.739 Bäume (83 × 11 Auflösungen
  × 3 Modi) ≈ 0,08 s; Zeichenlisten aller Dateien je Auflösung und Modus 15–30 ms.
- Zeichenliste 1920x1080 `Stretch`: 8.357 Befehle (Bilder 6.443, Text 703, Rahmen 326, Füllungen 254, Clip 305/305,
  Custom 21); 1280x720 `Stretch`: 6.412; `Uniform`/`Anchor` 7.008 bzw. 5.492 (kleinere Kachelzahl). Mit Bildgrößen-Fake
  16x16 und ohne Textquelle (671 unaufgelöste Labels).
- Vom Host zu zeichnende `DRAWCALLBACK`s: `W3DDrawMapPreview` 8, `W3DMainMenuDraw` 3, je 1 `IMECandidateMainDraw`,
  `IMECandidateTextAreaDraw`, `W3DCommandBarBackgroundDraw`, `W3DCommandBarForegroundDraw`, `W3DCommandBarGenExpDraw`,
  `W3DCommandBarHelpPopupDraw`, `W3DCreditsMenuDraw`, `W3DLeftHUDDraw`, `W3DPowerDraw`, `W3DRightHUDDraw`.

## 7. Grenzen

- `ShellMenuScheme` (`Data/INI/ShellMenuScheme.ini`, im `CompiledDefinitionGraph` als Kompatibilitätsdefinition) wird hier
  nicht gelesen; Menüübergänge (`WindowTransitions.ini`, `LAYOUTINIT`-Animationen) sind Aufgabe der Shell (`docs/GAME-SHELL.md` §12; der Handler liegt in `CnCNew.Ui/Transitions`).
- Modale Fenster (`winSetModal`), Mauscapture durch Callbacks, Ziehen von `DRAGGABLE`-Fenstern, IME, Videopuffer,
  `USE_OVERLAY_STATES`/`FLASHING`/`NOT_READY`-Overlays, Hotkey-Text und Uhr-Overlays der Control Bar sind nicht umgesetzt.
- Der alte Rahmen `WIN_STATUS_BORDER` (`winDrawBorder`) wird nicht gezeichnet, nur gezählt (`BorderStatusWindows`; Realdaten: 37 gezeichnete Fenster je Auflösung, versteckte nicht mitgezählt).
- Tab-Control nur minimal (Reiterköpfe oben, `TABORIENTATION`/`TABEDGE`/`PANEBORDER` ungenutzt); in den Realdaten kommt keins vor.
- Die Farbvarianten von Check-Box, Radio, Slider und Scrollbar sind vereinfacht; Scrollbar-Spur und Thumb einteilig.
- Lesereihenfolge ohne Zeilenbündelung; Zeilenhöhe = `LineHeight` ohne Zusatzabstand; Textumbruch nur an Leerzeichen.
- Bildgrößen, Schriftmetriken und Übersetzungen liefert der Host; `UniformImages` im Realdatentest ist ein Fake.
- `CnCNew.Ui` nutzt keine neue NuGet-Abhängigkeit.
