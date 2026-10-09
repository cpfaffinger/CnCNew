# UI gegen die unveränderten Layouts des Steam-Masters

Stand 9. Oktober 2026. Bis zu diesem Tag liefen alle Realdatenprüfungen gegen einen Runtimebestand, der aus einer
**modifizierten** Installation importiert war (ControlBarPro samt 1080p-Fix, HD-Befehlsleiste, HD-Decals, erweitertes
LAN-Menü, eigene Karten). Der einzige unterstützte Master ist jetzt die frische englische Steam-Installation
(Runtimebestand `master-import-a`, 25.608 Dateien). Dieses Dokument hält fest, was am UI-Code auf die Mod zugeschnitten
war, was das Original tut, was geändert wurde und was offen ist.

**Was diese Bewertung ist und was nicht:** Verglichen wurden Standbilder (PNG) mit dem, was der Quelltext der
Originalengine beschreibt (`references/ea-generals-zero-hour/GeneralsMD/Code/...`). Kein Bild wurde neben ein Bild des
laufenden Originals gelegt, Bewegung wurde nicht gesehen, und nichts davon ist eine Abnahme durch den Projektinhaber.

## 1. Was der Unterschied der Daten ist

| | Mod-Bestand | Steam-Master |
|---|---|---|
| `Window/ControlBar.wnd` | 105 Fenster, alle `CREATIONRESOLUTION: 1920 1080`; `ControlBarParent` 0,740–1920,1080; die schemagesteuerten Fenster (Optionen, Arbeiter, Funk, Generalsknopf, Umschalter, Geld, Energie) als Platzhalter 0,0–100,100; eigene Bilder `ControlBarPro*` | 98 Fenster, alle `800 600`; `ControlBarParent` 0,416–**799,599**; die schemagesteuerten Fenster ohne Bild an vorläufigen Stellen |
| `Data/INI/ControlBarScheme.ini` | 14 Schemata, `ScreenCreationRes X:1920 Y:1080` | 14 Schemata, `ScreenCreationRes X:800 Y:600` |
| `Window/Menus/QuitMessageBox.wnd`, `MessageBox.wnd` | 1920×1080; Ja schon auf dem Platz von Ok, Nein auf dem von Abbrechen; Vorlagen `LabelSmall`/`Button`; Logo versteckt | 800×600; drei Knopfplätze (Ok, Nein, Abbrechen), Ja eine Zeile höher geparkt; Vorlagen `LabelRegular`/`MainButton`; Logo sichtbar |
| `Window/Menus/QuitMenu.wnd` | 1920×1080, Hintergrundbild `ControlBarProQuitMenuWindow` | 800×600, kein Hintergrundbild, Logo oben |
| `Window/Menus/MainMenu.wnd` | aus `WindowZH.big` (208.561 Byte): die Mod-Installation hatte `PatchWindow.big` ersetzt | aus `PatchWindow.big` (212.069 Byte): zusätzlicher Knopf `ButtonCustomMission` zwischen Skirmish und Zurück |
| `Data/English/HeaderTemplate.ini` | 19 Vorlagen (vier `ControlBarProScrollListBox*`) | 15 Vorlagen |
| `Data/INI/InGameUI.ini` | `MessagePosition = X:10 Y:22` | `MessagePosition = X:10 Y:10` (einzige abweichende Zeile) |
| Vollbildfenster | – | 14 Layouts schreiben das Vollbild als 0,0–**799,599**, 19 als 0,0–800,600 |

Die übrigen 36 abweichenden Layoutdateien unterscheiden sich so, dass die gezeichneten Szenen pixelgleich bleiben
(siehe Abschnitt 5: 112 von 162 Bildern der Werkzeugszenen waren zwischen Mod und Master schon vor jeder Änderung gleich).

## 2. Was das Original tut (mit Quelle)

- **Layout → Bildschirm:** `parseScreenRect` (`GameWindowManagerScript.cpp`) liest `UPPERLEFT`, `BOTTOMRIGHT` und
  `CREATIONRESOLUTION` jedes Fensters und skaliert beide Achsen unabhängig mit `Display / CreationResolution`
  (`xScale`, `yScale`). Die Erstellungsauflösung steht je Fenster in der Datei; nichts ist angenommen. Bei 16:9 ist die
  Befehlsleiste des Originals damit so breit wie der Bildschirm und waagerecht 1,33-fach gedehnt.
- **Schema → Bildschirm:** `ControlBarSchemeManager::setControlBarSchemeByPlayer` setzt
  `m_multiplyer = Display / m_ScreenCreationRes`; `ControlBarScheme::drawBackground`/`drawForeground` zeichnen jedes
  `ImagePart` bei `Position * multi`; `ControlBarScheme::init` rechnet `resMultiplier` gleich und setzt damit Position und
  Größe von `PopupCommunicator`, `ButtonIdleWorker`, `ButtonOptions`, `ButtonPlaceBeacon`, `MoneyDisplay`, `PowerWindow`,
  `ButtonGeneral`, `ButtonLarge` und `WinUAttack` (`winSetPosition`/`winSetSize`) sowie deren Zustandsbilder
  (`GadgetButtonSetEnabledImage`, `…HiliteImage`, `…HiliteSelectedImage`, `…DisabledImage`), das Bild von
  `ExpBarForeground`, das Logo (`ControlBar::updateRightHUDImage`) und das Bild leerer Warteschlangenplätze
  (`ControlBar::updateBuildQueueDisabledImages`). Ebenen 5–3 liegen unter den Fenstern (`W3DCommandBarBackgroundDraw`),
  2–0 darüber (`W3DCommandBarForegroundDraw`; `CONTROL_BAR_SCHEME_FOREGROUND_IMAGE_LAYERS` = 3, `ControlBarScheme.h`).
- **Rechtes Fenster:** `ControlBar::setPortraitByObject` zeigt bei einem gewählten Objekt dessen `SelectPortrait` in
  `CameoWindow` und nimmt `RightHUD` den Status `IMAGE`; ohne Objekt zeigt `RightHUD` das Logo (`W3DRightHUDDraw`
  zeichnet nur mit `IMAGE`). `W3DGadgetPushButtonImageDrawOne` (`W3DPushButton.cpp`) zeichnet nur ein Bild, nie einen Kasten.
- **Meldungsfenster:** `GameWindowManager::gogoMessageBox` (`GameWindowManager.cpp`) liest die Plätze von `ButtonOk`,
  `ButtonNo`, `ButtonCancel`, setzt Ja auf den ersten, Nein auf den letzten (mit Abbrechen: auf den mittleren).
- **Schrift:** `GlobalLanguage::adjustFontSize` bezieht Vorlagengrößen immer auf 800 Pixel Breite; das war schon allgemein
  umgesetzt (`UiScaler.ScaleFontSize`) und brauchte keine Änderung.

## 3. Was auf die Mod zugeschnitten war

Alle Fundstellen mit Zahlen- oder Namensliteralen (Suche nach `1920`, `1080`, `800`, `600`, `*Pro`, Fensternamen):

| Stelle (Stand vor den Änderungen) | Was | Urteil |
|---|---|---|
| `src/CnCNew.Game/Hud/MatchController.cs:677, 692, 696, 765` | Schema-Rechtecke als `WndScreenRect(…, 1920, 1080)` | **Annahme.** Wirkte nur zufällig richtig: `UiTransform.Map` ignoriert die Erstellungsgröße des übergebenen Rechtecks. Behoben. |
| `src/CnCNew.Game/Hud/ControlBarScheme.cs` | las `ScreenCreationRes` nicht; Kommentar „1920x1080 creation space“; unbekannte Schlüssel still übergangen; übersprang die Zeile nach einem `AnimatingPart` | **Annahme / stiller Rückfall.** Behoben. |
| `src/CnCNew.Ui/Layout/UiScaler.cs:103` (`ClassifyAxis`), `:133` | „volle Ausdehnung“ nur bei `high >= extent` | **Annahme** (passt zu 0–1920, nicht zu 0–799). Folge: Leiste bei 16:9 mittig in 4:3-Breite. Behoben. |
| `MatchController.ResolveHardCodedImages` | Logo nur über den Bildnamen `HardCodedWithControlBarSchemeIni`, den nur das Mod-Layout trägt | **Annahme.** Mit dem Master nie gezeichnet. Behoben. |
| `MatchController` (fehlend) | Schemagesteuerte Knöpfe weder platziert noch bebildert | **Lücke, von der Mod verdeckt** (dort Platzhalter 0,0–100,100 und Kunst im Hintergrund). Behoben. |
| `src/CnCNew.Ui/Runtime/UiDrawBuilder.cs:49` | Fenster mit `W3DGadgetPushButtonImageDraw` ohne Bild als schwarzer Kasten | unabhängig von der Mod falsch (auch dort sichtbar). Behoben. |
| `src/CnCNew.Game/Shell/Screens/MessageBoxScreen.cs` | Knöpfe nur ein-/ausgeblendet, nicht auf die Plätze gesetzt | **Lücke, von der Mod verdeckt.** Behoben. |
| `src/CnCNew.Game/Data/GameData.cs` (`LoadText`) | `Data/Patch.str` nicht gelesen | Lücke; mit dem Master sichtbar (fehlender Text). Behoben, siehe Abschnitt 6. |
| `tests/CnCNew.Game.Tests` | Quit-Dialog „ist ein 1920x1080-Layout“, `LabelSmall`, Größen 36/14/21; Meldungsposition 10,22 | Erwartungen der Mod. Neu gemessen. |
| `scripts/input/clicks-during-intro.txt:5` | `screen:474,640` als „erster Befehlsknopf“ | Position des Mod-Layouts (im Master der zweite Knopf). Jetzt `button:ControlBar.wnd:ButtonCommand01`. |
| `src/CnCNew.Ui/Wnd/WndParser.cs:391, 397` | 800×600 als Ersatz bei kaputtem `SCREENRECT` | **allgemein**: nur mit Fehlerdiagnose, die Erstellungsauflösung wird sonst aus der Datei gelesen. |
| `src/CnCNew.Ui/Layout/UiScaler.cs:68–69` | `DefaultDisplayWidth/Height` 800×600 | **allgemein**: `DEFAULT_DISPLAY_WIDTH` des Originals für `adjustFontSize`. |
| `src/CnCNew.Ui/Settings/Resolution.cs`, `ShellSupport.cs:25` | Auflösungslisten, Desktopgröße der Testumgebung | **allgemein**, kein Layoutbezug. |

Alle 106 dekorierten Fensternamen, die `src/`, `tools/` und `scripts/` verwenden, gibt es in den Layouts des Masters
(sechs davon sind Präfixe nummerierter Fenster). Keine Stelle im Code nennt ein `*Pro`-Bild.

**Bilder, die der Master nicht hat:** genau eines. Alle 14 Schemata nennen `QueueButtonImage SCBigButton`; kein
`MappedImages`-Eintrag definiert es (im Mod-Bestand ebenso wenig). Das Original bekommt dafür ein Nullbild und lässt die
leeren Warteschlangenplätze ohne Bild. Der Name steht in `MatchController.MissingSchemeImages`; es wird kein Ersatz gezeichnet.

## 4. Was geändert wurde

Reihenfolge der Commits (Branch der Sitzung):

| Commit | Art | Inhalt |
|---|---|---|
| `fix(ui): anchor mode takes a window written with its last pixel as full extent` | Code | `UiScaler.FullExtentSlack` = 1: 0–799 von 800 gilt als volle Ausdehnung; die Leiste reicht wie im Original von Rand zu Rand |
| `test(w3d)`, `test(ui)`, `chore(baselines): rewrite the compatibility baseline …` | Neu gemessen | W3D-Inventar, `Window/`-Dateizahl, Meldungswerte, bekannte fehlende Labels, Kompatibilitätsbasislinie |
| `fix(hud): place the control bar scheme by its own ScreenCreationRes` | Code | Parser liest `ScreenCreationRes`, lehnt ein Schema ohne ab, zählt ungelesene Schlüssel; `SchemeRectOnScreen` |
| `fix(hud): the control bar scheme places and pictures the bar's own windows` | Code | Knöpfe, Logo, Porträt, Vorder-/Hintergrundebenen, kein Kasten für Bildfenster, `MissingSchemeImages` |
| `fix(shell): the retail main menu's custom mission button has a text and is inert` | Code | `Data/Patch.str`, Knopf gesperrt |
| `fix(shell): message boxes put Yes and No at the layout's button places` | Code | `UiWindow.SetDesignPosition`, `gogoMessageBox`-Plätze |
| `test(game): the message position of the retail InGameUI.ini …` | Neu gemessen | 10,22 → 10,10 |
| `chore(baselines): shell screen hashes against the Steam master` | Golden | `tests/CnCNew.Game.Tests/baselines/screens.txt`: 12 von 54 Zeilen neu |
| `chore(baselines): HUD draw list hash against the Steam master` | Golden | `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256` |
| `chore(tools)`, `fix(scripts)`, `docs` | Rest | Kommentar im `UiRenderTool`, Eingabeskript, dieses Dokument |

**Golden-Dateien:** Beide enthalten ausschließlich SHA-256-Werte (`Name@Größe=Hash` bzw. ein Hash), keinen Zeichenlistentext
und keine Zeichenketten aus der Stringtabelle des Spiels. Beide wurden zweimal neu erzeugt, bytegleich. Für das
`UiRenderTool` gibt es **keine** Golden-Datei im Repo (seine Bilder liegen nie im Repo; geprüft wird die Gleichheit zweier
Läufe: 162 von 162 Bildern pixelgleich). Hinweis außerhalb der Golden-Dateien: einige Testquellen vergleichen gegen
englische Anzeigetexte als Literal (zum Beispiel `HudTests.cs`/`MissionTextTests.cs` gegen den Text von
`GUI:CantBuildThere`); das ist älter als diese Arbeit und wurde nicht angefasst.

## 5. Bilder (außerhalb des Repos)

Basisverzeichnis: `%TEMP%\claude\C--Users-cp-RiderProjects-CnCNew\029094c2-0de8-428b-9dfa-9dd14c0f3e8e\scratchpad\vanilla\`

| Verzeichnis | Inhalt |
|---|---|
| `render-mod\` | `UiRenderTool`, alle Szenen, Mod-Bestand (Vergleich) |
| `render-vanilla-before\` | dieselben Szenen, Master, Code vor den Änderungen |
| `render-vanilla-after\`, `render-vanilla-after2\` | Master, Code danach, zwei Läufe (pixelgleich) |
| `client-vanilla-before\`, `client-vanilla-after\` | Client `--smoke-test --scenario exit-america-all` mit Eingabeskript, je 800×600, 1280×720, 1920×1080: `strip-<Größe>\frame-NNNNN.png` (alle 30 Bilder), `shell-<Größe>.png` |
| `compare-mod-vs-vanilla-before.txt` | Pixelvergleich Mod gegen Master vor den Änderungen (112 gleich, 50 verschieden) |

Bilder des Filmstreifens: 30 Leerlauf mit Logo; 60 Planierraupe gewählt, Baumenü, Tooltip; 90 Waffenfabrik mit
Warteschlange, Geld-Tooltip (im Einblenden); 120 Kaserne mit Warteschlange, Energie-Tooltip; 150 Kommandozentrale; 600
Spielmenü. Die aussagekräftigsten Paare:

1. `client-vanilla-before\strip-1280x720\frame-00060.png` ↔ `client-vanilla-after\…\frame-00060.png` – Leiste mittig mit
   schwarzen Löchern gegen Leiste über die ganze Breite mit Knopfspalte, Generalsknopf, Umschalter und Porträt.
2. `…\frame-00030.png` (beide) – ohne Auswahl: schwarzer Kasten gegen Logo der Seite.
3. `render-vanilla-before\shell-quit-1280x720.png` ↔ `render-vanilla-after\shell-quit-1280x720.png` – Ja/Nein versetzt gegen eine Zeile.
4. `render-vanilla-before\flow-single-player-1280x720.png` ↔ `render-vanilla-after\…` – magenta `GUI:CustomMission` gegen beschrifteten, gesperrten Knopf.

## 6. Bewertung je Bildschirm

| Bildschirm | Verglichen mit | Befund vorher | Ursache | Stand |
|---|---|---|---|---|
| Befehlsleiste Leerlauf | `parseScreenRect`, `ControlBarScheme::init/drawBackground` | mittig in 4:3-Breite, Welt links und rechts sichtbar; Knopfspalte links und Reiter rechts oben schwarz; rechts schwarzer Kasten statt Logo | `UiScaler.ClassifyAxis`; fehlende Schemafenster; Logo am Mod-Namen | behoben |
| Planierraupe gewählt | `setPortraitByObject` | schwarzer Kasten im Porträtfenster | `UiDrawBuilder` | behoben: Porträt; Upgrade-Bildchen fehlen weiter |
| Gebäude mit Warteschlange | `populateBuildQueue` | Bilder richtig platziert | – | leere Plätze ohne Bild wie im Original (`SCBigButton` fehlt in den Daten) |
| Mehrere Einheiten | – | **nicht beurteilt**: das Eingabeskript wählte in der Szene nur ein Gebäude | – | offen |
| Geld, Energie | `ControlBarScheme::init`, `W3DPowerDraw` | Geld an der Schemastelle; Energie als einfarbiger Balken | – | Balken ist keine Nachbildung von `W3DPowerDraw` (Kacheln `PowerPointG/Y/R`, logarithmisch, Schieber): offen, nicht mod-bedingt |
| Tooltip der Knöpfe | `ControlBar::populateBuildTooltipLayout` | Zeigertooltip über der Leiste | bewusst (kein `ControlBarPopupDescription.wnd`) | unverändert; verdeckt Knöpfe und Geld, anders als das Original |
| Radar | – | Karte seitentreu im (bei 16:9 gedehnten) Rahmen | – | unverändert; **nicht gegen den Quelltext des Originals geprüft** (`W3DLeftHUDDraw`, `W3DRadar`) |
| Meldungsliste | `InGameUI.ini` | richtig, jetzt ab y = 10 | Datenwert | Tests neu gemessen |
| Generalsfähigkeiten (Kurzleiste), Erfahrungsfenster, Diplomatie, Chat | – | **nicht beurteilbar**: der Client lädt `GenPowersShortcutBar*.wnd`, `GeneralsExpPoints.wnd`, `Diplomacy.wnd`, `InGameChat.wnd` nicht | Funktion nicht gebaut | offen; Generalsknopf, Funk und Signalfeuer melden weiter „not supported“; Erfahrungsbalken (`W3DCommandBarGenExpDraw`) und Angriffsblinken (`triggerRadarAttackGlow`) fehlen |
| Spielmenü (`QuitMenu.wnd`) | Layout | richtig (Logo, fünf Knöpfe, durchscheinend) | – | in Ordnung |
| Hauptmenü | Layout | wie mit der Mod | – | in Ordnung; die Hülle skaliert gleichmäßig mit Rändern (bewusst, `docs/GAME-SHELL.md`), das Original dehnt |
| Einzelspielermenü | Layout, `Data/Patch.str` | magenta `GUI:CustomMission` | Text nur in `Patch.str` | Text da, Knopf gesperrt. **Nicht belegt**, was die Steam-Exe mit dem Knopf tut: der veröffentlichte Quelltext kennt weder die Datei noch das Fenster |
| Skirmish-Einrichtung, Kartenwahl, Optionen, Ladeschirm, Abschlussbildschirm, Laden/Speichern, Credits | Pixelvergleich Mod ↔ Master | pixelgleich | – | unverändert |
| Beenden-Dialog | `gogoMessageBox` | Ja links, Nein eine Zeile tiefer in der Mitte | fehlende Platzierung | behoben |
| Sieg/Niederlage, Missions-Pausenmenü (`PopupSaveLoad.wnd`) | – | **nicht angesehen** (keine Szene); die Tests bestehen | – | offen |

**Entscheidung für den Projektinhaber:** Die Befehlsleiste folgt jetzt dem Original und ist bei 16:9 waagerecht gedehnt
(Bildchen 1,33-fach breiter). Wer sie unverzerrt und mittig will, stellt `Display.UiScale` auf `IntegerMultiple`
(gleichmäßige Skalierung); die Vorgabe `Uniform` → `Anchor` dehnt wie das Original, `Stretch` ebenso.

## 7. Neu gemessene Bestandszahlen (Steam-Master)

| Größe | Steam-Master | zuvor dokumentiert (Mod-Bestand, teils älter) |
|---|---|---|
| Dateien (`AssetTool verify`) | 25.608 | 25.682 |
| INI-Dateien geparst (`IniTool audit`) | 224 von 224, 66.304 Blöcke | – |
| Vollinventar: geladen / ausgeschlossen | 216 / 8 | 228 / 28 |
| Vollinventar: Blöcke / Definitionen / Module / Schlüssel | 65.754 / 12.459 / 16.380 / 332.464 | 65.875 / 12.574 / 16.380 / 333.061 |
| Vollinventar: Referenzen aufgelöst / gesamt / Fallbacks | 19.110 / 20.660 / 1.550 | 18.321 / 19.871 / 1.550 |
| Definitionssnapshot Vollinventar | 144.981.631 Byte, SHA-256 `33B54BDFED4E36D83DA20B1E901ABF0DF64E9D6809DAD2FCFBFFCA7127DA874E` | 144.895.548 Byte, `0A9D10F4…` |
| `--maps exclude`: geladen / ausgeschlossen | 134 / 90 | – |
| `--maps exclude`: Blöcke / Definitionen / Module / Schlüssel / Objekte | 63.626 / 12.435 / 16.065 / 326.696 / 2.115 | 12.550 Definitionen, 2.115 Objekte |
| `--maps exclude`: Referenzen | 18.702 / 20.225 / 1.523 | – |
| Definitionssnapshot `--maps exclude` (zwei Läufe gleich) | 144.364.843 Byte, SHA-256 `3E9AAB0DC388AE8CBF9C15384A6DE024B3F83ACF8EDDD821AA121E38CF6564BE` | 144.284.673 Byte, `2B9D902A…` |
| `MapTool audit` | 150 von 150 Karten; 462 Startpaare, 456 vollständig, 6 teilweise | gleich |
| Kartenskripte | 1.631 Listen, 23.906 Skripte, 2.294 Gruppen, 33.890 Bedingungen, 82.563 Aktionen | gleich |
| `W3dTool audit` | 8.901 Dateien, 70.571 Meshes, 2.094.998 Dreiecke | – |
| `W3dTool audit-skeletons` | 8.894 Dateien, 6.478 Hierarchien, 6.502 HLODs | 8.897 / 6.481 / 6.505 |
| `ImageTool audit-tga` | 804 von 804 | – |
| `PresentationTool object-audit` | 2.115 Objekte, 1.847 mit Modell, 2.928 Draw-Module | – |
| `PresentationTool fx-audit` | 429 FXLists, 1.453 Nuggets, 1.087 Partikelsysteme | – |
| `PresentationTool terrain-audit` | 150 von 150 Karten, 248 Geländetypen, 5 bekannte fehlende Texturen | – |
| `SimTool audit-modules` | 183 von 205 Modultypen; 16.558 von 19.553 Verwendungen (84,7 %) | gleich |
| `SimTool audit-coverage` | 2.115 von 2.115 Objekte, Fingerabdruck `AC622E33080D89EB` | 2.115 von 2.115 |
| Kompatibilitätsbasislinie | 731 Einträge | 751 |
| WND-Bestand | 80 Dateien unter `Window/`, 1.667 Fenster, alle 800×600, 0 Parserdiagnosen | 83 Dateien, 1.814 Fenster (347 bei 1920×1080), 12 Diagnosen |

Protokolle der Messung: `…\scratchpad\vanilla\rebaseline\`.

**Kompatibilitätsbasislinie 751 → 731:** 20 `excluded-file`-Einträge entfallen (`HeaderTemplate.ini`/`Language.ini` von
Brazilian, French, German, Italian, Polish, Spanish; dazu die `…9x.ini` von Chinese und Korean – diese Dateien brachte die
Mod mit). Zwei Einträge ändern die Zahl: `definition 'HeaderTemplate'` 19 → 15 (vier `ControlBarProScrollListBox*`),
`definition 'MappedImage'` 1.486 → 1.369 (117 Blöcke in zwölf Mod-Dateien unter `MappedImages/HandCreated/`: elf `*Pro.ini`
und `BlankTexture.ini`). Nichts kommt hinzu.

**`Window/` 83 → 80:** es fehlen `Menus/GameSpyLoginProfile.wnd_hidemail`, `Menus/NetworkDirectConnect.wnd_hideip`,
`Menus/OptionsMenu.wnd_hideip` (Varianten der Mod). Kein Code lädt sie; `src/CnCNew.Formats/AssetFormat.cs:27` stuft nur
die Endungen ein.

**W3D 8.897 → 8.894:** `Art/W3D/ADMtlPipL.W3D`, `ADMtlPipL1.W3D`, `ADMtlPipL2.W3D` stammten aus `CustomContentMaps.big`.

## 8. Prüfungen am Ende (gemessen 9. Oktober 2026, Steam-Master)

| Programm | ohne Inhalt | mit `--content-dir <Master>` |
|---|---|---|
| `Archives`, `Content`, `IO`, `Images`, `Formats`, `Import` | bestanden | – (kein Realdatenmodus benutzt) |
| `Data.Tests` | bestanden | bestanden (Basislinie 731 Einträge) |
| `W3D.Tests` | bestanden | bestanden |
| `Ui.Tests` | bestanden | bestanden |
| `Presentation.Tests` | bestanden | bestanden |
| `Audio.Tests` | bestanden | bestanden |
| `Simulation.Tests` (Release, ohne `--long`) | bestanden | bestanden |
| `Game.Tests` (Release) | bestanden | bestanden |

`dotnet build CnCNew.slnx` (mit gelöschtem `bin`/`obj` des Clients) und `dotnet build tools/CnCNew.UiRenderTool`: je 0 Warnungen.
`IniTool compat-check`: 731 Einträge stimmen. `scripts/check-dozer-build.ps1` 5 von 5, `check-build-hotkeys.ps1` 3 von 3,
`check-spectator.ps1` bestanden, `audit-scenarios.ps1` alle drei Szenen bestanden – jeweils mit `-ContentDir <Master>`.
Nicht gelaufen: `Simulation.Tests -- --long`, die Referenz- und Eingangsmanifeste (werden an anderer Stelle neu geschrieben),
`scripts/audit-vulnerable.ps1`, `capture-world.ps1`, `capture-text.ps1`.

## 9. Was noch falsch aussieht oder nicht beurteilt werden konnte

- Kein Bild wurde von einem Menschen gesehen; kein Vergleich neben dem laufenden Original.
- Mehrfachauswahl, Sieg/Niederlage und das Pausenmenü der Mission sind nicht angesehen.
- Energiebalken, Erfahrungsbalken, Angriffsblinken, Upgrade-Bildchen, Beschreibungsfenster der Befehlsleiste,
  Generalsfähigkeiten, Diplomatie und Chat sind keine Nachbildung des Originals bzw. nicht gebaut (Abschnitt 6).
- `ButtonCustomMission`: Verhalten der Steam-Exe unbekannt; hier beschriftet und gesperrt.
- Die Hülle (Menüs) skaliert weiter gleichmäßig mit Rändern statt wie das Original zu dehnen – eine frühere, bewusste
  Entscheidung, die mit der Mod nichts zu tun hat.
- `IniTool load-order-report` nennt „excluded files: 8“, listet aber nur zwei; die Basislinie führt acht. Nicht untersucht.
