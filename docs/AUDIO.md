# Audio: Decoder, Katalog, Auswahl und Software-Mixer

Stand: 30. September 2026 (Paket D der Phase 8)

`CnCNew.Audio` ist die geräteunabhängige Audioschicht der Präsentation. Sie dekodiert die
Originalformate, typisiert die Audio-INI aus dem `CompiledDefinitionGraph`, wählt Sounds
deterministisch aus und mischt alles in einem Software-Mixer zu Stereo-Float-PCM. Die Schicht kennt
weder Stride noch eine Wiedergabe-API: Der Stride-Client ruft `SoftwareMixer.Render(Span<float>)`
aus seinem Audiothread auf und reicht den Block an `DynamicSoundSource` weiter.

Abhängigkeiten: `CnCNew.Data` (Katalog) und das NuGet-Paket `NLayer` (MP3, siehe
„Neue Abhängigkeit NLayer“). Keine Referenz auf `CnCNew.Content`: Dateien kommen als `Stream` bzw.
über Rückrufe (`Func<string, bool> fileExists`, `Func<string, Stream> open`) herein, typischerweise
aus `ContentFileSystem.Exists`/`OpenRead`.

## Einstiegspunkte

| Typ | Zweck |
| --- | --- |
| `PcmBuffer`, `PcmConvert` | dekodiertes Float-PCM (interleaved, [-1, 1]); Mono/Stereo, lineares Resampling |
| `Wav.WavReader.Decode` | RIFF/WAVE: PCM 8/16/24/32 Bit, IEEE-Float, IMA-ADPCM (0x11), MS-ADPCM (0x02), `WAVE_FORMAT_EXTENSIBLE` |
| `Mp3.Mp3PcmStream`, `Mp3.Mp3Decoder.DecodeAll` | MPEG-1/2/2.5 Layer I–III, streamend (Musik) oder vollständig |
| `AudioFileDecoder.Decode` / `OpenStream` | Formatwahl nach Signatur, nicht nach Endung |
| `IPcmStream`, `PcmBufferStream` | Pull-Quelle für Musik (`Read(Span<float>)`, `Rewind()`) |
| `Catalog.AudioCatalog.FromGraph(graph, fileExists, options)` | typisierte Events, Musik, Dialoge, `AudioSettings`, `MiscAudio`, `Report` |
| `Catalog.AudioCatalogReport.ToLines()` | deterministischer Auditbericht (für den PresentationTool) |
| `Selection.SoundSelector.Pick(info, rng, history, timeOfDay)` | deterministische Soundwahl, Pitch/Volume-Shift, Delay, Attack/Decay |
| `Selection.SeededRng` | eigener Zufallsstrom der Audioschicht |
| `Mixing.SoftwareMixer` | Stimmen, Busse, 3D, Stealing, Loops, Fades, Musik-Crossfade, `Render` |
| `Mixing.PlayRequest.ForEvent` / `ForBuffer` | Wiedergabeauftrag aus einem Plan bzw. Rohpuffer |
| `Mixing.SoundCache` | dekodierte Effekte/Sprache nach virtuellem Pfad, Fehler einmalig gemerkt |
| `Mixing.Microphone.Place` | Hörerposition und Zoom-Lautstärke wie `AudioManager::update` |
| `Movies.BinkHeader.Read` / `Parse` | Kopf und Frameindex von `.bik`, keine Dekodierung |

Namensentscheidung: Die typisierten Audiodefinitionen heißen `AudioEventInfo` (ein Record für alle
drei INI-Arten, wie `AudioEventInfo` der Referenz), nicht `AudioEventDefinition`/`MusicTrackDefinition`/
`DialogEventDefinition`. Diese Namen belegt bereits `CnCNew.Data.Definitions`; gleichnamige Typen
würden in jeder Datei mit beiden `using`s mehrdeutig. Die Art steht in `AudioEventInfo.Type`
(`SoundEffect`, `Music`, `Streaming`); `AudioCatalog.SoundEffects`/`MusicTracks`/`DialogEvents`
liefern die Sichten.

## Formate (am Realbestand verifiziert)

Gemessen mit `dotnet run -c Release --project tests/CnCNew.Audio.Tests -- --content-dir game/content`:
8.531 von 8.531 Audiodateien unter `Data/Audio` dekodieren (989,1 MiB, 553,6 Minuten Audio,
47,2 s, rund 700-fache Echtzeit).

| Ordner | Dateien | IMA-ADPCM | PCM 16 Bit | MP3 | Minuten |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Data/Audio/Sounds` | 1.974 | 802 | 1.172 | – | 68,8 |
| `Data/Audio/Sounds/English` | 2.562 | 32 | 2.530 | – | 61,9 |
| `Data/Audio/Speech` | 29 | 4 | 25 | – | 3,0 |
| `Data/Audio/Speech/English` | 3.910 | 2.387 | 1.523 | – | 286,8 |
| `Data/Audio/Tracks` | 56 | – | – | 56 | 133,1 |

Abtastraten 22.050 und 44.100 Hz, mono und stereo; die Musik ist 55× Stereo 44,1 kHz und 1× Mono
22,05 kHz. MS-ADPCM, 8-Bit-PCM, Float und Extensible kommen im Bestand nicht vor und sind nur durch
synthetische Tests abgedeckt.

### WAV-Leser

Grundlage sind die Microsoft-Spezifikationen „Multimedia Programming Interface and Data
Specifications 1.0“ (RIFF, `fmt `, `fact`, `data`, Wortausrichtung), „Multimedia Standards Update“
(1994; `WAVE_FORMAT_ADPCM` und `WAVE_FORMAT_DVI_ADPCM`) und die IMA-Empfehlung „Recommended
Practices for Enhancing Digital Audio Compatibility in Multimedia Systems“ (Step- und Indextabellen).
Die Decoder sind danach neu geschrieben.

- IMA-ADPCM: pro Block und Kanal ein 4-Byte-Kopf (int16-Startwert, Step-Index, Reserve), danach
  4-Byte-Worte je Kanal mit 8 Samples, unteres Nibble zuerst. Samples pro Block
  `(BlockAlign − 4·Kanäle)·2/Kanäle + 1`. Ein Step-Index über 88 wird geklemmt und gemeldet.
- MS-ADPCM: Koeffizientenpaare aus der `fmt `-Erweiterung, Blockkopf Prädiktorindex/Delta/Sample1/
  Sample2, Ausgabe Sample2, Sample1, dann Nibbles (oberes zuerst, Kanäle abwechselnd), Vorhersage
  `(s1·c1 + s2·c2) >> 8`, Delta-Adaption mit Untergrenze 16. Ein Prädiktorindex außerhalb der
  Tabelle ist ein Fehler.
- `fact`: kürzt den aufgefüllten letzten ADPCM-Block auf die exakte Länge.

Fehler (Exception, nie stiller Rückfall): kein RIFF/WAVE, fehlender `fmt `/`data`-Chunk, doppelte
Chunks, unbekannte Kodierung, mehr als zwei Kanäle, inkonsistente Blockausrichtung. Toleranzen
(Diagnose mit stabilem Code, in `WavReadOptions.StrictMode` eine `InvalidDataException`):

| Code | Verhalten |
| --- | --- |
| `riff-size-mismatch` | RIFF-Größe passt nicht zur Dateilänge; Chunks werden trotzdem gelesen |
| `truncated-data` / `truncated-chunk` | Chunk kürzer als angekündigt; vorhandene ganze Frames/Blöcke werden dekodiert |
| `partial-frame` / `partial-block` | Restbytes ohne ganzen Frame bzw. Blockkopf werden verworfen |
| `unpadded-odd-chunk` | ungerader Chunk ohne Pad-Byte (erkannt am nächsten Chunk-Kennzeichen) |
| `trailing-bytes` | Restbytes hinter dem letzten Chunk |
| `fact-exceeds-data` | `fact` kündigt mehr Frames an, als die Daten enthalten; es wird gespielt, was da ist |
| `adpcm-samples-per-block` | kleineres `wSamplesPerBlock` als der Block fasst |
| `ima-step-index-clamped` | Step-Index > 88 im Blockkopf |
| `extension-mismatch` | Endung und Inhalt widersprechen sich (`AudioFileDecoder`) |

Im Realbestand tritt genau eine Toleranz auf: 26 IMA-ADPCM-Dateien (`agul01*`, `bgralo*`, `gfirlo*`,
`iroclo*`) haben einen `fact`-Wert, der bis zu einen Block über den vorhandenen ganzen Blöcken liegt;
der letzte Teilblock fehlt in der Datei. Andere Chunks (`LIST`, `cue ` usw.) werden aufgelistet und
übersprungen.

### MP3 und Streaming

Musik wird nicht vollständig dekodiert: `Mp3PcmStream` hält nur den aktuellen MPEG-Frame
(höchstens 1.152 Samples je Kanal) und liefert `Read(Span<float>)`; `Rewind()` springt für Loops an
den Anfang. Effekte und Sprache werden vollständig dekodiert und im `SoundCache` gehalten. Der
Realtest streamt alle 56 Tracks vollständig (133,1 Minuten).

### Resampling

`PcmConvert.Resample` und der Mixer interpolieren linear. Das ist verzerrungsarm genug für
22/44,1-kHz-Spielmaterial, hat aber kein Anti-Aliasing-Filter: Beim Heruntertasten falten Anteile
über der neuen Nyquist-Frequenz zurück, beim Hochtasten wird die oberste Oktave leicht gedämpft.
Pitch-Shift ändert wie in Miles (`AIL_set_sample_playback_rate` × Pitch) Tonhöhe und Tempo zugleich.

## Neue Abhängigkeit NLayer

- **Warum:** Die Musik liegt ausschließlich als MP3 vor (56 Dateien). Die BCL enthält keinen
  MP3-Decoder; ein eigener Layer-III-Decoder (Huffman-Tabellen, Requantisierung, Stereo-Verarbeitung,
  IMDCT, Polyphasen-Filterbank) wäre ein Vielfaches des übrigen Pakets an Code und Prüfaufwand.
- **Paket:** `NLayer` 3.0.0 (fest gepinnt in `src/CnCNew.Audio/CnCNew.Audio.csproj`), vollständig
  verwalteter MPEG-1/2/2.5-Layer-I–III-Decoder, Autoren Mark Heath und Andrew Ward,
  https://github.com/naudio/NLayer.
- **Lizenz:** MIT (Lizenzausdruck `MIT` im nuspec), mit dem Projekt verträglich.
- **Abhängigkeiten:** keine transitiven Pakete (Assets `net8.0` und `netstandard2.0`; genutzt wird
  `net8.0` mit den `Span<float>`-Überladungen).
- **Prüfung:** `dotnet add … package NLayer --version 3.0.0` stellt in dieser Umgebung online wieder
  her; `dotnet build src/CnCNew.Audio` und der Testbuild laufen mit 0 Warnungen.
- **Alternativen:** NAudio (Windows-/ACM-gebunden, zieht mehr mit), MP3Sharp (LGPL), externe
  FFmpeg-Prozesse (Laufzeitabhängigkeit außerhalb des Repos, nicht deterministisch prüfbar), eigener
  Decoder (siehe oben). Ein Tausch bleibt lokal: NLayer wird nur in `Mp3PcmStream` benutzt.

## Bink-Filme

`BinkHeader` liest nur Kopf, Audiospurtabelle und Frameindex des Bink-1-Containers, beschrieben nach
der öffentlichen Formatdokumentation (MultimediaWiki „Bink Container“; dieselben Felder dokumentiert
der Demuxer `read_header` in FFmpegs `libavformat/bink.c`): Signatur `BIK` + Revisionsbuchstabe
(`b`…`k`), Dateigröße − 8, Frameanzahl, größter Frame, Breite, Höhe, FPS-Zähler/-Nenner, Videoflags,
Audiospuranzahl, je Spur maximale Dekodiergröße, Rate/Flags (0x1000 DCT, 0x2000 Stereo, 0x4000 16 Bit)
und Spur-ID, dann `Frames + 1` Offsets (Bit 0 = Keyframe). Geprüft werden Dateigröße, plausible
Maße, FPS ≠ 0, streng steigender Index und dass der letzte Offset das Dateiende ist. Bink 2 (`KB2`)
wird ausdrücklich abgelehnt.

Realbestand: 70 Filme (39 unter `Data/English/Movies`, 2 unter `Data/Movies`, 29 unter
`ZH_Generals/Data/Movies`), alle Revision `BIKi`, zusammen 29,4 Minuten; 800×600 (Kampagnen- und
Briefingfilme, 30 fps, 44,1 kHz Stereo-DCT-Audio), 640×480-Varianten, 720×486 (`EA_LOGO`, 48 kHz),
400×324 (20 Generals-Porträts ohne Audio, 13 Frames) und 96×120 (`VS_small`, ohne Audio).

**Grenze:** Bink-Video und Bink-Audio sind proprietäre Codecs; ohne externen Decoder (Bink-SDK bzw.
eine lizenzierte Laufzeit) können die Filme nicht abgespielt werden, auch ihr Ton nicht. Der Client
nutzt die Kopfdaten (Maße, Dauer), um einen stummen Platzhalter für die Filmdauer zu zeigen oder den
Film zu überspringen. Das ist keine Kompatibilität, sondern eine benannte Lücke.

## Katalog (`AudioCatalog`)

Der Compiler führt Audioblöcke als generisch typisierte Felder (`AudioEvent`, `DialogEvent`,
`MusicTrack`) bzw. als Kompatibilitätsdefinitionen (`AudioSettings`, `MiscAudio`); seine
Heuristik liest z. B. `PitchShift = -10 10` als einzelnen Winkel. Der Katalog liest deshalb für jedes
Feld den Originaltext (`DefinitionValue.OriginalText`) mit den Parsetabellen der Referenz neu:

- Parsetabelle `AudioEventInfo::m_audioEventInfo` (`INIAudioEventInfo.cpp`): `Volume`,
  `VolumeShift`, `MinVolume`, `LowPassCutoff` über `parsePercentToReal` (Prozent → Verhältnis, `%`
  optional); `PitchShift` über `parsePitchShift` zu Faktoren `1 + p/100`; `Delay` über `parseDelay`
  (Millisekunden min/max); `Limit`, `LoopCount` (`parseInt`, Zahlpräfix wie `sscanf`); `Priority`
  (`parseIndexList` über LOWEST…CRITICAL); `Type`/`Control` (`parseBitString32`: `NONE` löscht,
  Klarnamen ersetzen, `+X`/`-X` ändern die geerbten Bits); `Sounds`, `SoundsMorning/Evening/Night`,
  `Attack`, `Decay` (`parseSoundsList`, Trenner Leerraum, Komma, `=`); `MinRange`, `MaxRange`;
  `Filename`.
- Vorlagen: Jedes Event beginnt als Kopie von `DefaultSoundEffect`, `DefaultDialog` bzw.
  `DefaultMusicTrack` (`INI::parseAudioEventDefinition`/`parseDialogDefinition`/
  `parseMusicTrackDefinition`). Fehlt eine Vorlage, beginnt die Art bei Nullwerten
  (`missing-template`).
- Redefinition: `AudioManager::newAudioEventInfo` liefert das bestehende Event, das danach mit der
  Vorlage überschrieben wird; es zählt also nur der letzte Block. Der Compiler dagegen verschmilzt
  Blöcke – der Katalog verwirft Felder früherer Blöcke und zählt `redefinition-reset`. Alle drei Arten
  teilen einen Namensraum; bei gleichem Namen gewinnt der zuletzt geladene Block
  (`cross-kind-redefinition`).
- Pfade wie `AudioEventRTS::generateFilename`/`generateFilenamePrefix`/`adjustForLocalization`:
  `AudioRoot\Ordner\Name` (+ `.SoundsExtension` nur für Soundeffekte; Musik und Dialoge spielen
  `Filename` wörtlich), wobei `AudioRoot\Ordner\<Sprache>\Name` bevorzugt wird, wenn die Datei
  existiert. Ordner: `SoundsFolder` (Effekte), `StreamingFolder` (Dialoge), `MusicFolder` (Musik).
  Nicht gefundene Dateien bleiben `AudioSoundFile.VirtualPath = null` und stehen namentlich im Bericht.
- `AudioSettings` (Tabelle `audioSettingsFieldParseTable`, `GameAudio.cpp`): alle Schlüssel außer
  `Preferred3DHW1…4`/`Preferred3DSW` sind Pflicht (`missing-key`, denn die Referenz läse
  uninitialisierten Speicher); Lautsprechernamen gegen `TheSpeakerTypes`. `Relative2DVolume` wird wie
  `OptionPreferences::getSoundVolume`/`get3DSoundVolume` auf die Standardlautstärken angewandt
  (`Effective2DSoundVolume`, `Effective3DSoundVolume`).
- `MiscAudio` (`INIMiscAudio.cpp`, 36 bekannte Schlüssel): Hook → Eventname; unbekannte Namen und
  nicht auflösbare Events werden gezählt bzw. aufgelistet. `NoSound` ist die explizite Stille.
- Unbekannte Schlüssel (`unknown-key:<Key>`), ungültige Werte (`invalid-value`, das Feld behält den
  Vorlagenwert), Zusatztokens (`extra-tokens`), Resttext hinter Zahlen (`trailing-text`),
  unzulässige Bereiche (`invalid-range`), `Filename` bei Effekten (`filename-ignored`) und `Sounds`
  bei Musik/Dialog (`sounds-ignored`) werden je Code gezählt, nie still verworfen.

Realbestand (`--maps exclude`): 1.409 Soundeffekte, 68 Musiktracks, 2.568 Dialogevents (+ 3
Vorlagen), 7.927 Dateiverweise, davon 7.842 aufgelöst (5.116 lokalisiert unter `…/English/`), 6.423
verschiedene Dateien, **85 nicht auflösbar**, 43 stumme Events (leere `Sounds`), 35 MiscAudio-Hooks
ohne offenen Verweis, **0 Diagnosen** (keine unbekannten Schlüssel, keine ungültigen Werte, keine
Redefinitionen). Die 85 fehlenden Dateien sind Datenfehler des Originals (die Referenz spielt dort
nichts), z. B. `EvaGLA_FundsTransferred.Filename = egtrnfnd` ohne Endung, 17 Demo-Dialoge
`DemoJune*.wav`, 13 `Taunts_GLABoss1xx`, der Musiktrack `Silence` (erbt `NoFilename`) und einzelne
Einheitenstimmen. Die vollständige Liste gibt `AudioCatalogReport.ToLines()` aus.

## Auswahl (`SoundSelector`)

Reihenfolge der Zufallsziehungen wie `generateFilename` + `generatePlayInfo`: Soundindex (nur
RANDOM), Delay, Pitch-Shift, Volume-Shift, Attack-Index, Decay-Index. Ist ein Bereich leer
(min = max), wird nicht gezogen.

- RANDOM: Index gleichverteilt; bei mehr als zwei Sounds wird eine Wiederholung des vorigen Index
  durch den nächsten ersetzt (`which == m_playingAudioIndex && size > 2`). Bei zwei Sounds sind
  Wiederholungen wie im Original möglich.
- Ohne RANDOM (die Aufgabenstellung nennt das ORDERED; die Referenz hat kein solches Flag): Round
  Robin ab dem ersten Sound, Zustand je Eventinstanz in `SoundSelectionState` (entspricht
  `m_playingAudioIndex`, Start −1).
- ALL: spielt die ganze Liste ab dem gewählten Index. Die Referenz deklariert ALL, ihr
  `AudioEventRTS::advanceNextPlayPortion` wirkt aber nie darauf (der Zweig wird vom folgenden
  `if (!m_decayName.isEmpty())` überschrieben). CnCNew setzt die erkennbare Absicht um; das ist eine
  bewusste, hier benannte Abweichung (175 Verwendungen im Bestand, meist `loop all random`).
- Volume-Shift zieht zwischen `1 + VolumeShift` und 1, Pitch zwischen `PitchShiftMin/Max`, Delay
  zwischen `DelayMin/Max` (Millisekunden). `LoopCount`: ohne LOOP einmal; mit LOOP 0 = endlos,
  n = n Durchläufe (`decreaseLoopCount`/`hasMoreLoops`).
- Tageszeit: `SoundsMorning/Evening/Night` werden bei passender, vom Aufrufer gelieferter Tageszeit
  gewählt, sonst `Sounds`. Die Referenz parst die Varianten, wählt sie aber nirgends aus.
- Limit (`CheckLimit`, `MilesAudioManager::doesViolateLimit`): 0 = unbegrenzt; am Limit ersetzt ein
  INTERRUPT-Event die älteste Instanz, andere werden verworfen.
- `SeededRng`: xorshift64* (Marsaglia 2003, Multiplikator nach Vigna 2016), Start über einen
  SplitMix64-Schritt. Eigener Strom wie `GameAudioRandomValue` der Referenz, damit Audio den
  Logikzufall nicht berührt; kein `Random.Shared`. Die ersten Werte für Seed 1 sind als
  Regressionswerte im Test festgehalten.

## Software-Mixer

**Threading:** Steuermethoden (`Play`, `Stop`, `SetVoicePosition`, `SetListener`, `SetBusVolume`,
`SetMasterVolume`, `PlayMusic`, `StopMusic`, `StopAll`) legen nur Befehle in eine lock-freie
`ConcurrentQueue`; Handles werden sofort vergeben. `Render` läuft auf genau einem Audiothread, wendet
zu Blockbeginn alle Befehle an und mischt dann. Zählwerte (`ActiveVoiceCount`, `PeakLeft/Right`,
`Statistics`, `IsActive`) werden am Blockende als Schnappschuss veröffentlicht.

**Determinismus:** kein eigener Thread, kein Gerät, keine Uhr. Gleiche Befehlsfolge und gleiche
Blockgrößen ergeben bitgleiche Ausgabe (Test: zwei Läufe eines Skripts mit Events, Stops,
Hörerbewegung und Musik-Crossfade, SHA-256 der Ausgabebytes). Float-Rechnung ist auf gleicher
Maschine und Laufzeit reproduzierbar; Debug- und Release-Build lieferten im Test dieselben Hashes.

**Stimmen und Pools:** positional (3D, `SampleCount3D`), flach (2D, `SampleCount2D`) und Stream
(Dialoge, `StreamCount`); Musik läuft außerhalb der Pools. Aufnahme nach `SoundManager::canPlayNow`:

1. Lautstärke unter `MinSampleVolume` → verworfen (`AudioManager::addAudioEvent`, „muted“).
2. Positional, kullbar und Abstand ≥ `MaxRange` → verworfen.
3. Limit je `LimitGroup` (Eventname) im selben Pool, siehe oben.
4. Freier Kanal → spielen.
5. Sonst die Stimme mit der niedrigsten Priorität unterhalb der eigenen stehlen, bei Gleichstand die
   älteste (`findLowestPrioritySound`).
6. Sonst ersetzt ein INTERRUPT-Event eine eigene Instanz; sonst verworfen.

GLOBAL- und CRITICAL-Events sind nicht kullbar (`canPlayNow`, `playAnyways`). Während der Wiedergabe
werden kullbare 3D-Stimmen entfernt, sobald `Lautstärke × Dämpfung` unter `MinSampleVolume` fällt.
Die objektbezogene Sprachregel (`violatesVoice`: eine Einheit spricht nicht zweimal gleichzeitig) und
die Spieler-/Verbündeten-/Gegner-/Shroud-Filter (`shouldPlayLocally`, `ST_SHROUDED`) gehören in die
Präsentation, die Objekte und den lokalen Spieler kennt.

**3D:** Dämpfung `DistanceModel.Inverse` wie `MilesAudioManager::getEffectiveVolume` (voll bis
`MinRange`, dann `min/d`, ab `MaxRange` still); `Linear` ist die dort auskommentierte Variante.
GLOBAL-Events nutzen `GlobalMinRange/GlobalMaxRange`. Panning: Kosinus zwischen Quellrichtung und
Hörer-Rechtsachse (Forward × Up; Standard +Y vorn, Z oben wie `m_listenerOrientation`), daraus
Equal-Power θ = (pan + 1)·π/4, Gains (cos θ, sin θ); links² + rechts² = 1, Mitte −3 dB. 3D-Quellen
werden vor dem Panning zu Mono gemischt (Miles-3D-Samples sind mono). `Microphone.Place`
implementiert die Mikrofonplatzierung und die Zoom-Lautstärke aus `AudioManager::update`; der Client
multipliziert den Zoomfaktor auf den Sfx-Bus.

**Busse:** `Music`, `Sfx` (3D-Effekte), `Ui` (2D-Effekte), `Voice` (Dialoge) und zusätzlich `Ambient`
für Drawable-Ambientloops, dazu Master. `MixerOptions.FromSettings` setzt die Standardwerte aus
`AudioSettings.ini` (Musik 55 %, Sprache 70 %, 3D 80 %, 2D 80 % × (1 − 10 %) = 72 % im Bestand).
`PlayRequest.ForEvent` ordnet zu: Musik → Music, Dialog → Voice (Stream-Pool), Effekt positional →
Sfx, sonst → Ui; Lautstärke = Eventvolumen × Volume-Shift, Pitch = Pitch-Shift.

**Wiedergabe:** Jede Stimme ist eine Segmentfolge (Delay-Stille → Attack → Körper → je Loop neu
gewählter Körper nach eigenem Delay → Decay, wie `notifyOfAudioCompletion`/`startNextLoop`). Loops
wählen mit dem Mixer-RNG aus einer Kopie des Auswahlzustands, wie die Referenz mit ihrer Kopie von
`AudioEventRTS`. Der Resampler interpoliert über Segmentgrenzen hinweg, Loop-Nahtstellen sind
lückenlos (bei Pitch 1 sampelgenau). Gains werden pro Block linear gerampt (kein Zipper-Rauschen bei
Bewegung oder Buswechsel); Fade-in/-out linear. Musik streamt aus `IPcmStream`, loopt per `Rewind`
und blendet bei `PlayMusic(..., crossfadeSeconds)` linear über. Gestohlene Stimmen enden sofort wie in
der Referenz.

**Messung:** Der Realtest spielt 40 Katalogevents an Zufallspositionen plus zwei Musiktracks mit
1 s Crossfade: 6,0 s Audio in 0,41 s inklusive Dekodierung von 141 Sounds, zwei Läufe bitgleich.

## Tests

`tests/CnCNew.Audio.Tests` (Konsolenprogramm, synthetische Fixtures): WAV-PCM-Varianten und
Extensible; Toleranzen und Strict-Modus; IMA-ADPCM-Handvektor, Stereo-Layout, Round Trip gegen einen
unabhängigen Testencoder, `fact`-Kürzung; MS-ADPCM-Handvektoren; Konvertierung/Resampling; MP3-Stream
aus zehn synthetischen stillen Layer-III-Frames mit `Rewind`; Katalog aus einem kleinen INI-Satz
(Vorlagen, Redefinition, Bits, Pfade, Diagnosen); Auswahl (Wiederholungsschutz, Round Robin, ALL,
Bereiche, Tageszeit, Limit, RNG-Regressionswerte); Mixer (Dämpfung, Panning, Mikrofon, Stealing,
Limits, Culling, Loop-Naht, Fades, Crossfade, Eventsegmente, Determinismus); Bink-Kopf. Mit
`--content-dir` kommen die Realtests hinzu (Dekodierung aller Dateien, Filmliste, Katalogbericht,
Mixer-Smoke-Test).

## Grenzen

- Bink-Filme sind nicht abspielbar (siehe oben); der Client zeigt einen stummen Platzhalter oder
  überspringt.
- Miles-spezifische Effekte werden nicht nachgebildet: EAX/Hall, HRTF-/Surround-Provider
  (`Preferred3DHW*`, Lautsprechertypen werden nur gelesen), Occlusion-Tiefpass (`LowPassCutoff` wird
  typisiert, aber nicht angewandt), Miles-Delayfilter (Delay wird als Stille vor dem Start bzw. vor
  jedem Loop umgesetzt). Ausgabe ist immer Stereo.
- Kein Limiter: Übersteuerung wird hart auf [−1, 1] begrenzt und gezählt (`ClippedSamples`; im
  Smoke-Test 1.753 Samples bei 40 überlappenden Events plus Musik).
- ALL folgt der Absicht statt dem (wirkungslosen) Referenzcode; Tageszeitvarianten sind wählbar,
  obwohl die Referenz sie nie nutzt.
- Während eines Delays belegt eine Stimme bereits ihren Kanal (die Referenz hält sie so lange in der
  Anfrageliste).
- Lineares Resampling ohne Anti-Aliasing.
- `POSTDELAY` wird typisiert, hat aber wie in der Referenz keine Wirkung.
