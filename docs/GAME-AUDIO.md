# Audio (AudioDirector, Eva-Ansage, Musik, Stride-Ausgabe)

Stand: 4. Oktober 2026 (Paket S5 vom 30. September 2026; Verdrahtung im Client: Phase 16, Punkt 1)

> **Stand der Verdrahtung (4. Oktober 2026, `docs/DEMO-FINDINGS.md` #7):** bis Phase 15 beschrieb dieses Dokument nur eine **Bibliothek** – `AudioDirector` und `AudioOutput` wurden im Stride-Client
> nirgends erzeugt, die Demos waren stumm. Seit Phase 16, Punkt 1 erzeugt `ClientRuntime` den Ton für `--match`, `--mission`, `--scenario` und `--shell` (Abschnitt „Verdrahtung im Client“).
> **Belegt ist das durch Zähler, Tests und reproduzierbare WAV-Mitschnitte – nicht durch Hören:** niemand hat das Ergebnis bisher an einem Lautsprecher beurteilt (Lautstärkeverhältnisse,
> Verständlichkeit, Latenz, ob das Gerät überhaupt klingt). Wo unten „spielt“ steht, heißt das: Die Anfrage erreicht den Mixer und ist im Mitschnitt enthalten.

`CnCNew.Game.Audio` macht das Spiel hörbar: Es übersetzt `SoundCue`s (aus `CnCNew.Presentation.Fx`), Auswahl-/Befehls-
Ereignisse der Präsentation und den Logiktakt der laufenden Partie (`CnCNew.Game.Live.LiveMatch`) in Befehle für den
gerätefreien `SoftwareMixer` (`CnCNew.Audio`). `CnCNew.Client.Stride/Audio/AudioOutput.cs` liest den Mixer aus und
schreibt ihn über Strides natives Audiolayer (`Stride.Audio`) auf das OS-Ausgabegerät. Zwischen beiden Enden liegt
keine weitere Kopplung: `CnCNew.Game.Audio` kennt kein Stride, `AudioOutput` kennt keine Spielregeln.

## Einstiegspunkte für die Integration

| Typ | Aufgabe |
|---|---|
| `AudioDirector.Create(data, options?, settings?)` | Baut den Mixer aus `AudioSettings.ini` (`MixerOptions.FromSettings`), lädt `Eva.ini`, liefert einen `IDisposable` Director |
| `director.Update(live, dt)` | Pro gerendertem Frame: Hörerposition aus der Kamera, Pause, `live.DrainSoundCues()` einreihen, Eva-Takt, Musiksequenz, Ambient, Busvolumina |
| `director.PlayMenuMusic()` / `StartMatchMusic(side)` / `StopMusic()` | Musikzustand (Menü vs. Partie) |
| `director.PlayUi(name)` | Nicht positionierte Events (Klicks, `MiscAudio`-Hooks) |
| `director.OnUnitSelected(template, count)` / `OnUnitOrdered(template, kind)` | Einheiten-Ansagen (siehe unten) |
| `director.NotifyInsufficientFunds()` / `OnMovieRequested(name)` | Hooks, die `LiveMatch` nicht selbst auslöst (siehe Lücken) |
| `director.ApplySettings(settings)` | Lautstärkeregler des Optionsbildschirms |
| `director.Mixer` / `director.Stats` | `SoftwareMixer` für die Ausgabeschicht, Zähler für jede nicht gespielte Anfrage |
| `director.Muted` | Mastervolumen 0, ohne die Regler anzufassen; Anfragen und Zähler laufen weiter |
| `GameAudio.Create(data, settings?)` | Ein Objekt je Client: `EnterShell()`, `Attach(live, hud)`/`Detach()`, `Update(dt)`, `PlayUi(name)`, `PlayHook(miscAudioKey)`, `ApplySettings`, `Muted`, `Report()` |
| `OfflineAudioRenderer(mixer, wavPath?)` | Rendert den Mixer ohne Gerät (`Pump(dt)`), optional in eine WAV-Datei; `Finish()` liefert die SHA-256 der Samples |
| `AudioOutput(mixer)` (`CnCNew.Client.Stride`) | `Start()` öffnet das Gerät und startet den Streaming-Thread, `Dispose()` räumt auf |
| `ClientAudio` (`CnCNew.Client.Stride`) | Wählt den Abnehmer des Mixers (Gerät, Mitschnitt, verwerfen) und druckt den Audiobericht |

Über dem Director liegt `GameAudio` (`CnCNew.Game/Audio/GameAudio.cs`, kein Stride): ein Objekt je Clientprozess, das den Director
besitzt und ihn an das hängt, was gerade auf dem Bildschirm ist (Shell oder laufendes Spiel). Der Client spricht nur mit `GameAudio`
(Abschnitt „Verdrahtung im Client“).

Der Integrator ruft `AudioDirector.Update` einmal pro `LiveMatch.Advance`-Aufruf und reicht dieselbe `LiveMatch`-Instanz
hinein. `Update` liest die Kamera (`live.Camera.Target`/`Eye`), das Terrain (`live.Heights`), die Pause
(`live.Paused`) und die seit dem letzten Aufruf ausgeführten Effekt-Soundcues (`live.DrainSoundCues()`) selbst; nur
Auswahl/Befehle, EVA-Ereignisse ohne Soundcue-Ursprung und Filmsequenzen kommen nicht aus `LiveMatch` und müssen von
der UI-/Eingabeschicht explizit gemeldet werden (`OnUnitSelected`, `OnUnitOrdered`, `NotifyInsufficientFunds`,
`OnMovieRequested`).

## Verdrahtung im Client (Phase 16, Punkt 1)

**Lebensdauer.** `ClientRuntime` erzeugt in seinem Konstruktor genau ein `ClientAudio` (`CnCNew.Client.Stride/Audio/ClientAudio.cs`) und gibt es in
`Dispose` frei. `ClientAudio` besitzt ein `GameAudio` (Director + Mixer) und **genau einen** Abnehmer des Mixers:

| Aufruf | Abnehmer | Gerät geöffnet |
|---|---|---|
| normal (Fenster) | `AudioOutput` (Strides Audiolayer, eigener Streaming-Thread) | ja |
| normal, Gerät lässt sich nicht öffnen | eine Konsolenzeile `warning: no audio device available, the game runs silently (…)`, danach `OfflineAudioRenderer` ohne Datei (rendert und verwirft) | nein |
| `--smoke-test` | `OfflineAudioRenderer` ohne Datei | nein |
| `--audio-capture <verzeichnis>` (mit oder ohne `--smoke-test`) | `OfflineAudioRenderer`, schreibt `<verzeichnis>/audio.wav` (16 Bit, Stereo, Mixerrate) | nein |

Der Ersatzabnehmer ist nötig, nicht kosmetisch: ohne `Render`-Aufrufe würde die Befehlswarteschlange des Mixers nie geleert und die Zähler blieben stehen.
„Kein Gerät geöffnet“ gilt für den Code dieses Projekts; ob Strides eigenes `AudioSystem` (Teil jeder `Stride.Engine.Game`-Instanz) beim Start ein Gerät anfasst, ist **nicht untersucht**.
`--mute` startet stumm (`GameAudio.Muted`). Pro Frame ruft `ClientRuntime.Update` nach dem Spiel- bzw. Shell-Schritt `ClientAudio.Update(sekunden)`:
erst `GameAudio.Update` (mit laufendem Spiel `AudioDirector.Update(live, dt)`, sonst `AudioDirector.Tick(null, dt)` für Musik und Busse), dann – ohne Gerät –
`OfflineAudioRenderer.Pump(dt)`.

**Quellen.** Was den Director erreicht und woher:

| Quelle | Weg | Zähler (`AudioSource`) |
|---|---|---|
| Simulationsereignisse (Schüsse, Explosionen/Tode über FXList-`Sound`-Nuggets, Bau, Produktion fertig, Upgrade, Spezialkraft, Betreten/Verlassen, Geld, `VoiceCreated`/`VoiceTaskComplete`/`VoiceFear`, abgelehnte Befehle, **Einnahme-Tick**) | `LiveGame.Advance` führt je Tick `FxExecutor.ExecuteEvents` aus; `SoundCueDeriver` leitet die Cues ab (Tabelle in `docs/PRESENTATION-FX.md`); `AudioDirector.Update` holt sie mit `live.DrainSoundCues()` | `Simulation` |
| EVA (`BaseUnderAttack`, `UnitLost`, `BuildingLost`, `UpgradeComplete` aus Cues; `LowPower` je Tick aus der Energiebilanz; `InsufficientFunds` aus einer abgelehnten Bestellung) | Cues der Art `Eva` → `EvaAnnouncer`; `InsufficientFunds` über `MatchController.CommandFeedbackObserved` | `Eva` |
| Einheitenstimme bei Auswahl | `SelectionModel.SelectionChanged` des HUD: die erste **eigene** Einheit, die neu in die Auswahl kommt, spricht (`VoiceSelect`/`VoiceGroupSelect`); eine schrumpfende Auswahl schweigt | `UnitVoice` |
| Einheitenstimme bei Befehl | `MatchController.CommandFeedbackObserved` (jede `CommandFeedback` des Frames): `MoveMarker`/`WaypointMarker` → `VoiceMove`, `AttackMarker` → `VoiceAttack`, `EnterMarker` → `VoiceEnter`; Sprecher ist die erste eigene ausgewählte Einheit | `UnitVoice` |
| UI im Spiel | `MatchController.ButtonClicked` (Befehlsleiste, Spielmenü) → MiscAudio `GUIClickSound`; `InvalidOrder`/`Unsupported` → MiscAudio `NoCanDoSound` | `Ui` |
| UI in der Shell | `PlayUiSoundOutput` der Shell (`GUIClick`, `GUIComboBoxClick`) → `GameAudio.PlayUi` | `Ui` |
| Umgebungsschleifen (`SoundAmbient`) | `AudioDirector.UpdateAmbient` aus dem projizierten Frame | `Ambient` |
| Musik | Shell: `Shell` in Schleife (`GameAudio.EnterShell`, auch nach Spielende); Spiel: gemischte `Game_<SEITE>_*`-Liste der Spielerseite (`GameAudio.Attach` → `StartMatchMusic`) | `Music` |
| Regler | `SettingsChangedOutput` der Shell → `GameAudio.ApplySettings` (wirkt mit dem nächsten gerenderten Block) | – |

**Einnahme-Tick.** Die Simulation meldet `SimEventKind.CaptureFlash` (`Entity` = Gebäude, `Other` = einnehmende Einheit) jedes Mal, wenn das Gebäude während der
Einnahme aufblitzt. `SoundCueDeriver` macht daraus einen positionierten Effekt-Cue mit dem Eventnamen aus MiscAudio `DefectorTimerTickSound` (Retail: `DefectorTimerTick`,
`world shrouded everyone`) an der Position des **Gebäudes**, ohne Spielerindex – wie `SpecialAbilityUpdate::continuePreparation` (`SpecialAbilityUpdate.cpp`), das dem Event die
Objekt-Id des Ziels gibt. Fehlt der MiscAudio-Schlüssel, heißt der Cue wie der Schlüssel und landet in `UnresolvedEvents` statt zu schweigen.

**Stummschaltung und Regler.** `GameSettings.Audio` hat fünf Regler und **kein** Stummfeld, der Optionsbildschirm keinen Stummschalter. Stumm ist deshalb ein Laufzeitzustand
(`AudioDirector.Muted`/`GameAudio.Muted`, `--mute`): Mastervolumen 0, alle Anfragen werden weiter gestellt, gemischt und gezählt (ein stummer Lauf hat dieselben Zähler).
Die Regler wirken beim Übernehmen im Optionsbildschirm der Shell; ein Schieben ohne Übernehmen ist nicht hörbar (die Shell meldet erst dann `SettingsChangedOutput`).
`--match`/`--mission`/`--scenario` starten mit `GameSettings.Default`, nicht mit den gespeicherten Einstellungen.

**Messbarkeit.** Beim Beenden (auch im `--smoke-test`) druckt der Client den Audiobericht (`ClientAudio.Finish`, Zeilen beginnen mit `audio `):

- `audio output:` Abnehmer; ohne Gerät Frames, Spitzenpegel und SHA-256 der gerenderten 16-Bit-Samples, mit Gerät die Unterläufe;
- `audio source <Quelle>: requested, started, ended, dropped` je `AudioSource` – *requested*: Anfragen, die den Director erreichten; *started*: an den Mixer übergeben;
  *ended*: vom Mixer nicht mehr gespielt (fertig, gestoppt, verdrängt **oder vom Mixer abgewiesen** – die Aufteilung steht nur mixerweit in der nächsten Zeile);
  *dropped*: nie eine Stimme geworden (unbekanntes Event, fehlende Datei, Entfernung, Ratenbegrenzung, nicht für den lokalen Spieler, fehlender Stimmschlüssel …).
  Eine EVA-Anfrage aus einem Cue zählt bei `Simulation` als *requested*, aber weder als gestartet noch als verworfen: sie läuft unter `Eva` weiter;
- `audio mixer:` gerenderte Frames, gestartete/beendete/gestoppte/verdrängte/abgewiesene Stimmen, geclippte Samples;
- `audio outcomes:` jedes Ergebnis nach Namen (`played`, `dropped:*`, `eva:requested`, `note:*`);
- `audio started events:` gestartete Stimmen je Eventname (hier steht z. B. `DefectorTimerTick=46`);
- `audio unknown event names`, `audio missing or undecodable sound files` (Pfad und Fehler), `audio missing unit voice keys`, `audio unknown eva events`,
  `audio unknown MiscAudio keys`, `audio skipped movies` – jeweils nach Namen, `none`, wenn leer.

**Reproduzierbarer Mitschnitt.** `OfflineAudioRenderer.Pump(dt)` rendert je Frame genau die Frames, die bis zur neuen Gesamtzeit fehlen, als **einen** Mixerblock. Mit festen
Zeitschritten (`--smoke-test`: 1/30 s) sind Blockgrößen und Befehlsfolge in jedem Lauf gleich, der Mixer ist dafür bitidentisch. Damit das auch für den Client gilt, liest
`ClientRuntime` in einem Lauf mit festen Zeitschritten **keine Geräteeingabe**: vorher verschob die zufällige Position der echten Maus (Randscrollen) die Kamera und damit den
Hörer, und zwei Läufe unterschieden sich genau in den positionierten Klängen (gemessen: Musik identisch, Unterschiede nur im Zeitraum der Einnahme-Ticks). Gemessen am
4. Oktober 2026 (Release, `--scenario capture-building --smoke-test --frames 1500 --audio-capture <dir>`): zwei Läufe, dieselbe Datei. Die konkreten Prüfsummen hängen von
Daten, Szene, Bildzahl und Rechner/Laufzeit ab (Gleitkomma des Mixers) und werden deshalb nicht als Sollwert geführt. Ein Lauf mit echter Frame-Zeit (`--audio-capture` ohne
`--smoke-test`) ist **nicht** reproduzierbar; die Geräteausgabe ohnehin nicht (eigener Thread, Blockgrenzen nach Gerätetakt).

**Gemessen (4. Oktober 2026, Release, gerätefrei; neu messen):**

| Lauf | Simulation (req/start/end/drop) | Sonstiges | Mixer | fehlend/unbekannt |
|---|---|---|---|---|
| `--scenario capture-building --smoke-test --frames 1500` | 50 / 49 / 49 / 0 | `DefectorTimerTick=49`, EVA 1 (`EvaUSA_UpgradeComplete`), Musik 1 (`Game_USA_05`) | 51 gestartet, 36 fertig, 14 durch `Limit` ersetzt, 0 geclippt, Spitze 0,71 | 0 |
| `--match "Maps/Alpine Assault/Alpine Assault.map" --smoke-test` (90 Bilder) | 2 / 0 / 0 / 1 | EVA 1, Musik 1 | 2 gestartet, Spitze 0,67 | 0 |
| derselbe Match, `--autoplay --select-own --frames 2700` | 60 / 35 / 35 / 24 (`not-local` 13, `ui-other-player` 11) | Auswahlstimme 1 (`CommandCenterUSASelect`), Umgebung 2, EVA 1, Musik 1 | 40 gestartet, **189 geclippte Samples, Spitze 1,00** | 0 |

Auffällig und nicht bewertet: die EVA-Ansage „Upgrade complete“ gleich zu Spielbeginn (ein zum Start vergebenes Upgrade meldet `UpgradeCompleted`), elf `NoCanDoSound` im
Autoplay-Lauf (abgelehnte Befehle der KI, die den Menschenplatz spielt) und das Clipping im längeren Match. Ob das so klingen soll, kann nur eine Hörprobe entscheiden.

**Tests** (`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`, Präfix „Wiring“): `CaptureFlash` → Cue am Gebäude → gestartete Stimme und fehlender Schlüssel → unbekanntes Event;
Auswahl/Befehl/Klick/Ablehnung → Director; fehlende Datei je Quelle gezählt, kein Ersatzton; Stumm und Regler auf 0 ergeben digitale Stille bei gleichen Zählern, Aufheben wirkt
im nächsten Block; Offline-Rendering ohne Gerät wirft nicht und schreibt bei festen Schritten zweimal dieselbe WAV-Datei; Realdaten: die Szene `capture-building` läuft mit HUD und
`GameAudio`, der Tick startet, der Ranger antwortet auf die Auswahl, und der Welt-Hash ist mit und ohne Ton derselbe.

**Nicht verdrahtet / offen:**

- **Skript-Ton in Missionen** ist seit Phase 16, Punkt 5 angeschlossen (`MissionPresentationDirector.Audio` ← `GameAudio.PlayScripted`): `SPEECH_PLAY`, `SOUND_PLAY_NAMED`,
  `PLAY_SOUND_EFFECT` und `PLAY_SOUND_EFFECT_AT` spielen das benannte Ereignis ohne Position (`AudioDirector.PlayUi`, Quelle `Ui` im Bericht), `MUSIC_SET_TRACK` startet das Stück
  (`AudioDirector.SetMusicTrack`), danach läuft die Playlist der Seite weiter. **Weiter offen:** die Lautstärke- und Typschalter der Skripte (`SOUND_SET_VOLUME`, `MUSIC_SET_VOLUME`,
  `AUDIO_OVERRIDE_VOLUME_TYPE`, `SOUND_DISABLE_TYPE`, `EVA_SET_ENABLED_DISABLED` …), die Position von `PLAY_SOUND_EFFECT_AT`, Klanglängen für `HAS_FINISHED_AUDIO` (Platzhalter 3 s); Zähler und
  Tabelle in `docs/SCRIPT-ENGINE.md` §7a.
- **Klänge der Spezialfähigkeit selbst** (`UnpackSound`, `PackSound`, `TriggerSound`, `PrepSoundLoop` von `SpecialAbilityUpdate`; Retail: Black Lotus, Hacker, Colonel Burton –
  die Infanterie-Einnahme hat keine) und die Abschlussstimmen (`VoiceCaptureBuildingComplete` …): die Simulation meldet die Zeitpunkte nicht als Ereignis.
- `VoiceGarrison`/`VoiceGuard` bei Befehlen (die Rückmeldung unterscheidet Betreten und Besetzen nicht), Gebäudeauswahl (Gebäude ohne `VoiceSelect` erscheinen als
  `dropped:no-voice-key`), die Terroristen-im-Auto-Stimmen aus MiscAudio.
- Die Shell-Konstante `GUISliderMoved` gibt es im Retailbestand nicht (dort `GUISliderBeep`); sie wird derzeit von keinem Bildschirm gesendet und würde als unbekanntes Event gezählt.
- Der Optionsdialog im laufenden Spiel (`MenuAction.Options`) ist im Client nicht verdrahtet, also auch keine Regler dort.
- Der Fehlerpfad von `AudioOutput` (Gerät lässt sich nicht öffnen) hat keinen automatischen Test – dafür bräuchte es einen Rechner ohne Audiogerät; getestet ist der
  gerätefreie Ersatzweg.
- Referenzstelle des Klick-Klangs: `MiscAudio.h` beschreibt `m_guiClickSound` als „snd to play when user presses button in GUI“; die Aufrufstelle im Original wurde nicht
  gesucht, die Regel „Befehlsleisten-/Menüknopf → `GUIClickSound`“ ist eine eigene.

## Bus-Zuordnung

Der Mixer kennt fünf Busse (`AudioBus`: `Music`, `Sfx`, `Voice`, `Ambient`, `Ui`); die vier Regler des Optionsbildschirms
(`GameSettings.Audio`: `Master`, `Music`, `Effects`, `Voice`, `Ambient`) werden so verrechnet (`AudioDirector.SyncBuses`):

| Bus | Formel | Begründung |
|---|---|---|
| `Music` | `Music × (pausiert ? 0.4 : 1)` | Duck beim Pausieren, kein Stopp (Menü bleibt hörbar) |
| `Sfx` | `Effects × Zoom` | 3D-Effekte folgen dem Kamerazoom wie `AudioManager::update`/`set3DVolumeAdjustment` |
| `Ambient` | `Ambient × Zoom` | dieselbe Zoomkurve, eigener Regler |
| `Voice` | `Voice` | Sprache/EVA/Einheiten-Ansagen, kein Zoom (Referenz: `DefaultSpeechVolume` ist nicht 3D) |
| `Ui` | `Effects × Effective2DSoundVolume/DefaultSoundVolume` | erhält das Retail-Verhältnis aus `Relative2DVolume` zwischen 2D- und 3D-Effekten, ohne den Zoom |

`Master` geht unverändert an `SoftwareMixer.SetMasterVolume`. Änderungen werden nur gesendet, wenn sie um mehr als
0,002 vom zuletzt gesendeten Wert abweichen (Rauschunterdrückung für die Befehlswarteschlange).

Welcher Bus ein Effekt-Soundcue bekommt, entscheidet `PlayRequest.ForEvent` in `CnCNew.Audio` (Musik → `Music`,
Streaming/Dialog → `Voice`, sonst `Sfx` bei gesetzter Position, sonst `Ui`); `AudioDirector.Dispatch` überschreibt das
für `SoundCueKind.Voice`-Cues explizit auf `Voice` (Fear/Created/TaskComplete-Ansagen bleiben aber positioniert).

## Aufnahme eines Soundcues (`AudioDirector.Dispatch`)

1. **Eva/Musik/UI-Cues abzweigen.** `SoundCueKind.Eva` geht an den `EvaAnnouncer` (`Request`, siehe unten) statt an den
   Mixer; ein Cue für einen anderen Spieler als `view.Human` wird verworfen und gezählt (`dropped:eva-other-player`).
   `SoundCueKind.Music` wird nicht unterstützt (die Musiksequenz läuft eigenständig, siehe unten) und gezählt.
   `SoundCueKind.UiSound` (aus `FxCatalog.MiscAudio`, auch für KI-Spieler emittiert) wird verworfen, wenn `Player`
   weder System noch der Mensch ist.
2. **Katalogeintrag auflösen.** Ein unbekannter Name landet in `Stats.UnresolvedEvents` (gezählt nach Name), nie
   stillschweigend verworfen.
3. **`shouldPlayLocally`** (Port von `AudioManager::shouldPlayLocally`, `GameAudio.cpp`, ohne Beobachtermodus – der
   Mensch ist immer der lokale Spieler): keine der Flaggen `PLAYER`/`ALLIES`/`ENEMIES`/`EVERYONE` gesetzt oder
   `EVERYONE` gesetzt → immer hörbar; `PLAYER` ohne Besitzer bei einem UI-Sound → hörbar (Referenz-Sonderfall für
   spielerlose UI-Klänge); kein Besitzer sonst → `dropped:no-owner`; `PLAYER` → nur der Besitzer selbst;
   `ALLIES` → nur ein verbündeter, nicht der eigene Spieler; `ENEMIES` → nur ein gegnerischer Spieler; sonst
   `dropped:not-local`. Musik ist immer lokal. UI-Sounds werden nicht gegen diese Regel geprüft (sie haben in der
   Referenz ohnehin meist `PLAYER UI` oder `EVERYONE`, und ihr `SoundCueKind` trägt bereits die Systemregel).
4. **Shroud.** Eine positionierte, nicht als `SHROUDED` markierte Anfrage wird verworfen, wenn `view.IsShrouded`
   die Position als für den Menschen unsichtbar meldet (`dropped:shrouded`).
5. **Vorab-Abstand/-Lautstärke.** `Spatial.Attenuation` mit den Katalogreichweiten schätzt die Lautstärke am Hörer;
   unterhalb `MinSampleVolume` oder jenseits `MaxRange` wird schon hier verworfen (`dropped:distance`/`dropped:quiet`),
   bevor die Datei überhaupt dekodiert wird. `GLOBAL`- und `CRITICAL`-Events sind davon ausgenommen wie im Mixer.
6. **Auswahl und Wiedergabe.** `SoundSelector.Pick` mit dem eigenen `SeededRng` des Directors und einem
   `SoundSelectionState` pro Eventnamen wählt Variante/Delay/Pitch/Lautstärke; `PlayRequest.ForEvent` dekodiert über
   `SoundCache.GetOrLoad` (fehlgeschlagene Dateien zählen als `dropped:no-decodable-sound`, keine leeren Ersatztöne).

Der `SoundCueRateLimiter` (siehe `docs/PRESENTATION-FX.md`) filtert, bevor `Dispatch` läuft: höchstens 3 Cues je
Eventname in 10 Ticks, höchstens 48 Cues je Tick (`AudioDirectorOptions.CueLimits`), ohne Distanzschnitt (der eigene
Schnitt kommt aus Schritt 5). Während einer Pause werden alle eingehenden Cues verworfen und als `dropped:paused`
gezählt, nicht verarbeitet.

## EVA-Ansage (`EvaCatalog`, `EvaAnnouncer`)

`Eva.ini` hat in `CnCNew.Data` kein typisiertes Schema; `EvaCatalog.Parse` liest die Rohdatei direkt (Zeilenparser,
Kommentare ab `;`, unbekannte Schlüssel werfen mit Zeilennummer). Eine Wiederholung desselben `EvaEvent`-Namens
verschmilzt in dasselbe Objekt (letzter Block gewinnt je Feld, wie `EvaCheckInfo`s In-Place-Update in der Referenz);
das ist eine eigene, dokumentierte Vereinfachung, da Retail keinen solchen Fall enthält. `TimeBetweenChecksMS` und
`ExpirationTimeMS` werden wie im Simulationskern zu Logikframes aufgerundet (`ceil(ms · 30 / 1000)`); `-1` bei
`ExpirationTimeMS` bedeutet wie im Original-Kommentar ("`-1` means FOREVER", `BeaconDetected`) niemals verfallen
(`int.MaxValue` Frames). Standardwerte ohne explizites Feld: `Priority=1`, `FramesBetweenChecks=900`,
`FramesToExpire=150` (`EvaCheckInfo::EvaCheckInfo`).

`EvaAnnouncer` portiert `Eva::update`/`processPlayingMessages` (`Eva.cpp`) als reinen Taktgeber:

- `Request(name)` markiert ein Ereignis für die nächste Auswertung (`Eva::setShouldPlay`); ein unbekannter Name wird
  abgelehnt und gezählt (`UnknownEvents`), nie stillschweigend ignoriert.
- `Update(frame, lowPower, speaking, side)` läuft **pro simuliertem Logiktick** (nicht pro gerendertem Frame – ein
  gerendertes Frame kann durch `LiveMatch`s Aufholmechanismus mehrere Ticks umfassen; der Director erkennt das über
  `view.Tick` und wertet höchstens einmal je neuer Ticknummer aus). Ab Frame&nbsp;2 werden markierte Ereignisse als
  Prüfungen eingereiht (kein Duplikat je Ereignis, solange eine Prüfung noch läuft), Flags werden bei jedem Aufruf
  geleert. Abgelaufene, noch nicht gespielte Prüfungen werden gezählt (`Expired`) und entfernt. Solange `speaking`
  wahr ist, wird nichts ausgewählt. Sonst gewinnt die höchste Priorität unter den noch nicht gespielten Prüfungen;
  hat das Ereignis für `side` keine `SideSounds`, wird das gezählt (`SideMissing`) und nichts gespielt – die Prüfung
  gilt trotzdem als "gespielt" und startet ihre Abklingzeit, genau wie im Original.
- Abweichung von der Referenz: Ereignisse werden in Dateireihenfolge statt in `EvaMessage`-Enum-Reihenfolge
  besucht (bei Prioritätsgleichstand kann das die Auswahl ändern; im Retail-Bestand hat kein Paar gleichzeitig
  wartender Ereignisse dieselbe Priorität, daher ohne beobachtbaren Unterschied). Die Tonvariante wird mit dem
  eigenen `SeededRng` des Directors gewählt, nicht mit dem Audio-RNG der Referenz.
- `LowPower` wird aus `SimulationWorld.EnergyProductionOf(Human) < 0` abgeleitet (`Energy::hasSufficientPower`-Ersatz)
  und jeden Tick geprüft wie in der Referenz, unabhängig von `Request`.

Vom Director abgedeckte EVA-Ereignisse: `BuildingLost`, `UnitLost`, `BaseUnderAttack`, `UpgradeComplete` (alle über
`SoundCueDeriver`, siehe `docs/PRESENTATION-FX.md`), `LowPower` (jeden Takt geprüft) und `InsufficientFunds`
(`NotifyInsufficientFunds`, da `LiveMatch` Kaufablehnungen nicht als `SimEvent` meldet). **Nicht ableitbar und daher
nicht ausgelöst** (in `Eva.ini` vorhanden, aber ohne Quelle in der bisherigen Simulation/Präsentation):
Superwaffen-Ereignisse (`SuperweaponDetected/Launched/Ready_*`), `AllyUnderAttack`, `BeaconDetected`,
`Enemy/OwnBlackLotusDetected`, `Enemy/OwnJarmenKellDetected`, `Enemy/OwnColonelBurtonDetected`, `GeneralLevelUp`,
`VehicleStolen`, `BuildingStolen`, `BuildingBeingStolen`, `BuildingSabotaged`, `CashStolen`,
`SuperweaponLaunched_Enemy_Sneak_Attack`, `SuperweaponLaunched_Enemy_GPS_Scrambler`. Sie bleiben in `EvaCatalog`
geladen (Audit/Realdatentest zeigen sie als nie angefragt), sind aber kein Fehler dieses Pakets – die zugrunde
liegenden Simulationsereignisse existieren noch nicht.

## Musik (`MusicPlaylist`)

`Music.ini` enthält keine Playlist-Reihenfolge (die Referenz wählt Titel aus dem WW-Skript); die Wiedergabefolge ist
daher eine eigene, dokumentierte Entscheidung: `MusicPlaylist.ForSide` sammelt alle `Game_<SIDE>_*`-Titel mit
aufgelöster Datei (ohne `Game_Silent`), `Next()` liefert eine mit dem Director-Seed deterministisch Fisher-Yates-
gemischte Reihenfolge, die beim Erschöpfen neu gemischt wird und dabei den zuletzt gespielten Titel nicht sofort
wiederholt. Der Präfix je Seite: `America* → Game_USA_`, `China* → Game_CHI_`, `GLA* → Game_GLA_`; eine unbekannte
Seite (z. B. `Observer`) fällt auf alle aufgelösten `Game_*`-Titel zurück (`UsesAllSides`, gezählt als
`note:music-all-sides`).

`AudioDirector.PlayMenuMusic()` spielt `Shell` in Schleife; `StartMatchMusic(side)` startet die Sequenz ohne Schleife
je Titel, mit `MusicGapSeconds` (Standard 3&nbsp;s) Pause zwischen zwei Titeln. Titelende wird erkannt, sobald der
Mixer die Stimme nach dem nächsten `Render` nicht mehr als aktiv führt (`IsActive` bzw. der `RenderedFrames`-Vergleich
bei noch nicht gerendertem Request, da `IsActive` erst nach dem nächsten `Render` aktualisiert wird). Ein
fehlschlagender Titel (Datei fehlt, dekodiert nicht) wird gezählt (`MusicFailed`) und der nächste versucht; scheitert
eine volle Runde, gilt Musik als nicht verfügbar (`Stats.MusicAvailable = false`), ohne weitere Versuche in
`Update` – kein stiller Endlosversuch.

## Ambiente Drawable-Sounds

`Drawable::startAmbientSound` (Referenz) wird als periodische Neubewertung nachgebildet
(`AudioDirector.UpdateAmbient`, alle `AmbientUpdateSeconds` = 0,25&nbsp;s): Für jedes sichtbare, nicht geisterhafte
`DrawableInstance` mit `SoundAmbient` im Objektkatalog und innerhalb seiner (ggf. globalen) Reichweite wird nach
Abstand und Entity-/Szenerie-Id sortiert; die nächsten `MaxAmbientVoices` (Standard 12) behalten eine schleifende
positionierte Stimme auf dem `Ambient`-Bus, die der Position folgt (`SetVoicePosition`, nur bei Bewegung über
0,5&nbsp;Einheiten) und mit kurzem Fade endet, sobald das Drawable die Auswahl verlässt. Ein nicht schleifendes
Ambient-Event wird frühestens nach `TimeBetweenDrawableSounds` erneut versucht (auch bei Fehlschlag), damit ein
dauerhaft fehlschlagender Ton nicht jede Aktualisierung neu versucht wird.

## Einheiten-Ansagen

`OnUnitSelected(template, count)` spielt `VoiceGroupSelect`, wenn mehr als eine Einheit ausgewählt ist **und** das
Objekt diesen Schlüssel hat (Referenzregel: fehlt der Schlüssel, bleibt es bei `VoiceSelect`), sonst `VoiceSelect`.
`OnUnitOrdered(template, kind)` bildet `VoiceMove`/`VoiceAttack`/`VoiceGuard`/`VoiceEnter`/`VoiceGarrison` ab. Beide
spielen 2D auf dem `Voice`-Bus (`Streaming = true`, kein Positionspool) und lösen die vorherige Einheiten-Ansage mit
einem kurzen Fade ab (`UnitVoiceFadeSeconds`, Standard 0,08&nbsp;s) – eine eigene Vereinfachung: die Referenz spielt
Ansagen pro Einheit unabhängig, dieser Client lässt zu einer Zeit nur eine "sprechen", was Massenselektionen hörbar
macht, ohne den Voice-Pool zu fluten. Ein Objekt ohne den passenden Schlüssel wird gezählt
(`Stats.MissingUnitVoices`, nach Schlüsselname), nie stillschweigend übersprungen. `VoiceFear` (aus
`SoundCueKind.Voice`-Cues von `SimEventKind.VoiceFearRequested`), `VoiceCreated`/`VoiceTaskComplete` bleiben
positioniert auf dem `Voice`-Bus, da sie aus der Simulation kommen und einen Weltort haben.

## Filmsequenzen (Bink)

`OnMovieRequested(name)` existiert, weil Menü/Kampagne Filmsequenzen anfordern werden, sobald diese Pakete stehen.
Die Laufzeit enthält **keinen Bink-Decoder** (das Binärformat ist proprietär und ohne die offizielle Bink-SDK nicht
rechtmäßig zu dekodieren); jeder Aufruf wird gezählt (`Stats.SkippedMovies`, nach Filmname) und tut sonst nichts –
kein Platzhalterton, kein Fake. Das ist eine bekannte, dauerhafte Grenze dieses Projekts, kein Implementierungsfehler
dieses Pakets.

## Stride-Ausgabe (`CnCNew.Client.Stride/Audio/AudioOutput.cs`)

Der Mixer rendert bereits fertiges interleaved-stereo Float-PCM; die Ausgabeschicht trägt deshalb keinen Zustand pro
Stimme. `MixerSoundSource` (eine `Stride.Audio.DynamicSoundSource`) füllt bei jedem Aufruf von Strides eigenem
Streaming-Worker-Thread (`DynamicSoundSource`'s statischer Hintergrundtask) genau einen Block
(`SoftwareMixer.Render`) von 882 Frames (20&nbsp;ms bei 44100&nbsp;Hz); vier Puffer (`BufferCount`) ergeben rund
80&nbsp;ms Ausgabelatenz. Das Float→Short-Clamping passiert hier, nicht im Mixer (der bleibt geräteunabhängig).

**Auflösung der Konstruktionsreihenfolge:** Strides öffentliche API verlangt für `SoundInstance` bereits eine fertige
`DynamicSoundSource`, während `DynamicSoundSource`s eigener Konstruktor selbst eine `SoundInstance` erwartet – ein
Zirkel. Da `DynamicSoundSource` die Instanz in seinem Konstruktor nur als Feld ablegt und sie erst bei der
Wiedergabe dereferenziert, konstruiert `AudioOutput.Start()` `MixerSoundSource` zunächst mit `null!`, baut daraus die
echte `SoundInstance` (die nur `MaxNumberOfBuffers` braucht, eine von `MixerSoundSource` überschriebene Eigenschaft
ohne Bezug zur Instanz) und übergibt sie über `MixerSoundSource.BindInstance` zurück, bevor `Play()` aufgerufen wird.

Ein eigener, niedrigpriorer Thread (`AudioOutput.Pump`) ruft `AudioEngine.Update()` der eigenen Engine alle 10&nbsp;ms auf; Strides
Dokumentation verlangt dafür "denselben Thread wie die Hauptschleife". Daran hat die Verdrahtung in Phase 16 nichts geändert
(nicht auf die Hauptschleife umgezogen, weil das Ergebnis ohne Hörprobe nicht beurteilbar wäre). Das native Nachfüllen der Ringpuffer
selbst übernimmt Strides eigener `DynamicSoundSource`-Hintergrundtask, unabhängig von diesem Thread.

**Kein Gerät verfügbar:** `AudioEngine`-Konstruktion (native Bibliothek, Gerätezugriff) kann fehlschlagen (fehlende
`libstrideaudio`, keine Hardware). `Start()` fängt das an dieser einen Gerätegrenze ab (`catch (Exception) when
(exception is not OutOfMemoryException)`, siehe `docs`-Konvention für Gerätegrenzen in `CnCNew.Audio.Mp3PcmStream`),
zählt es in `DeviceFailures`, merkt sich den Grund in `FailureReason` und lässt den Client stumm weiterlaufen; `IsAvailable` bleibt `false`, kein Zustand wird
vorgetäuscht. Der Fehlerzweig fasst `Stride.Audio` selbst nicht mehr an (früher protokollierte er über `AudioEngine.Logger` – bei fehlender nativer Bibliothek
hätte genau dieser Zugriff erneut geworfen); die eine Konsolenwarnung druckt `ClientAudio`. **Unterläufe** (`Underruns`, in `MixerSoundSource`): Da OpenAL/AudioLayer aus Managed-Code keinen
direkten Unterlaufzähler liefert, misst `MixerSoundSource` die tatsächliche Zeit zwischen Füllaufrufen gegen die
erwartete Zeit aus den bereits gelieferten Frames; ein Rückstand von mehr als einer Pufferdauer zählt als ein
Unterlauf und synchronisiert die Messung neu. Das ist eine Näherung (kein Beleg, dass das native Gerät wirklich
stotterte), aber ein Signal, nicht Stille.

## Realdatentest (`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`)

Der Realdatentest (`--content-dir`) lädt `GameData`, erstellt eine `LiveMatch` (Alpine Assault, beide Seiten
computergesteuert), lässt `AudioDirector.Update` und `SoftwareMixer.Render` 600 gerenderte Frames (20&nbsp;s
Spielzeit) parallel zu `LiveMatch.Advance` laufen und rendert dabei denselben Ablauf zweimal unabhängig. Geprüft
wird: SHA-256 der gesamten Ausgabe ist zwischen beiden Läufen byteidentisch (Determinismus), der Ausschlagpegel
(Peak) liegt über einer Hörbarkeitsschwelle (keine Stille), die Cue-Auflösung wird gezählt statt verschwiegen
(`Stats.UnresolvedEvents`), und ein Renderblock (33&nbsp;ms Ausgabe) braucht deutlich weniger als 33&nbsp;ms
Rechenzeit. Ein Mitschnitt der ersten drei Sekunden geht als 16-Bit-Stereo-WAV in das Scratchpad der Session (Pfad
über die Umgebungsvariable `CNCNEW_AUDIO_SCRATCH`, sonst `%TEMP%`), niemals in das Repository.
