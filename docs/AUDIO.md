# Audio: decoders, catalogue, selection and software mixer

As of: 30 September 2026 (package D of Phase 8)

`CnCNew.Audio` is the device-independent audio layer of the presentation. It decodes the
original formats, types the audio INI from the `CompiledDefinitionGraph`, selects sounds
deterministically and mixes everything in a software mixer to stereo float PCM. The layer knows
neither Stride nor a playback API: the Stride client calls `SoftwareMixer.Render(Span<float>)`
from its audio thread and passes the block on to `DynamicSoundSource`.

Dependencies: `CnCNew.Data` (catalogue) and the NuGet package `NLayer` (MP3, see
"New dependency NLayer"). No reference to `CnCNew.Content`: files come in as a `Stream` or
through callbacks (`Func<string, bool> fileExists`, `Func<string, Stream> open`), typically
from `ContentFileSystem.Exists`/`OpenRead`.

## Entry points

| Type | Purpose |
| --- | --- |
| `PcmBuffer`, `PcmConvert` | decoded float PCM (interleaved, [-1, 1]); mono/stereo, linear resampling |
| `Wav.WavReader.Decode` | RIFF/WAVE: PCM 8/16/24/32 bit, IEEE float, IMA ADPCM (0x11), MS ADPCM (0x02), `WAVE_FORMAT_EXTENSIBLE` |
| `Mp3.Mp3PcmStream`, `Mp3.Mp3Decoder.DecodeAll` | MPEG-1/2/2.5 Layer I–III, streaming (music) or complete |
| `AudioFileDecoder.Decode` / `OpenStream` | format selection by signature, not by extension |
| `IPcmStream`, `PcmBufferStream` | pull source for music (`Read(Span<float>)`, `Rewind()`) |
| `Catalog.AudioCatalog.FromGraph(graph, fileExists, options)` | typed events, music, dialogue, `AudioSettings`, `MiscAudio`, `Report` |
| `Catalog.AudioCatalogReport.ToLines()` | deterministic audit report (for the PresentationTool) |
| `Selection.SoundSelector.Pick(info, rng, history, timeOfDay)` | deterministic sound selection, pitch/volume shift, delay, attack/decay |
| `Selection.SeededRng` | the audio layer's own random stream |
| `Mixing.SoftwareMixer` | voices, buses, 3D, stealing, loops, fades, music crossfade, `Render` |
| `Mixing.PlayRequest.ForEvent` / `ForBuffer` | playback request from a plan or a raw buffer |
| `Mixing.SoundCache` | decoded effects/speech by virtual path, errors remembered once |
| `Mixing.Microphone.Place` | listener position and zoom volume like `AudioManager::update` |
| `Movies.BinkHeader.Read` / `Parse` | header and frame index of `.bik`, no decoding |

Naming decision: the typed audio definitions are called `AudioEventInfo` (one record for all
three INI kinds, like `AudioEventInfo` of the reference), not `AudioEventDefinition`/`MusicTrackDefinition`/
`DialogEventDefinition`. These names are already taken by `CnCNew.Data.Definitions`; types of the same name
would become ambiguous in every file with both `using`s. The kind is in `AudioEventInfo.Type`
(`SoundEffect`, `Music`, `Streaming`); `AudioCatalog.SoundEffects`/`MusicTracks`/`DialogEvents`
deliver the views.

## Formats (verified against the real content)

Measured with `dotnet run -c Release --project tests/CnCNew.Audio.Tests -- --content-dir game/content`:
8,531 of 8,531 audio files under `Data/Audio` decode (989.1 MiB, 553.6 minutes of audio,
47.2 s, roughly 700 times real time).

| Folder | Files | IMA ADPCM | PCM 16 bit | MP3 | Minutes |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Data/Audio/Sounds` | 1,974 | 802 | 1,172 | – | 68.8 |
| `Data/Audio/Sounds/English` | 2,562 | 32 | 2,530 | – | 61.9 |
| `Data/Audio/Speech` | 29 | 4 | 25 | – | 3.0 |
| `Data/Audio/Speech/English` | 3,910 | 2,387 | 1,523 | – | 286.8 |
| `Data/Audio/Tracks` | 56 | – | – | 56 | 133.1 |

Sample rates 22,050 and 44,100 Hz, mono and stereo; the music is 55× stereo 44.1 kHz and 1× mono
22.05 kHz. MS ADPCM, 8-bit PCM, float and extensible do not occur in the content and are covered only by
synthetic tests.

### WAV reader

The basis is the Microsoft specifications "Multimedia Programming Interface and Data
Specifications 1.0" (RIFF, `fmt `, `fact`, `data`, word alignment), "Multimedia Standards Update"
(1994; `WAVE_FORMAT_ADPCM` and `WAVE_FORMAT_DVI_ADPCM`) and the IMA recommendation "Recommended
Practices for Enhancing Digital Audio Compatibility in Multimedia Systems" (step and index tables).
The decoders are rewritten from these.

- IMA ADPCM: per block and channel a 4-byte header (int16 start value, step index, reserve), then
  4-byte words per channel with 8 samples, lower nibble first. Samples per block
  `(BlockAlign − 4·channels)·2/channels + 1`. A step index above 88 is clamped and reported.
- MS ADPCM: coefficient pairs from the `fmt ` extension, block header predictor index/delta/Sample1/
  Sample2, output Sample2, Sample1, then nibbles (upper first, channels alternating), prediction
  `(s1·c1 + s2·c2) >> 8`, delta adaptation with lower limit 16. A predictor index outside the
  table is an error.
- `fact`: shortens the padded last ADPCM block to the exact length.

Errors (exception, never a silent fallback): no RIFF/WAVE, missing `fmt `/`data` chunk, duplicate
chunks, unknown encoding, more than two channels, inconsistent block alignment. Tolerances
(diagnostic with a stable code, in `WavReadOptions.StrictMode` an `InvalidDataException`):

| Code | Behaviour |
| --- | --- |
| `riff-size-mismatch` | RIFF size does not match the file length; chunks are read anyway |
| `truncated-data` / `truncated-chunk` | chunk shorter than announced; the whole frames/blocks present are decoded |
| `partial-frame` / `partial-block` | remaining bytes without a whole frame or block header are discarded |
| `unpadded-odd-chunk` | odd chunk without pad byte (recognised by the next chunk identifier) |
| `trailing-bytes` | remaining bytes after the last chunk |
| `fact-exceeds-data` | `fact` announces more frames than the data contain; what is there is played |
| `adpcm-samples-per-block` | smaller `wSamplesPerBlock` than the block holds |
| `ima-step-index-clamped` | step index > 88 in the block header |
| `extension-mismatch` | extension and content contradict each other (`AudioFileDecoder`) |

Exactly one tolerance occurs in the real content: 26 IMA ADPCM files (`agul01*`, `bgralo*`, `gfirlo*`,
`iroclo*`) have a `fact` value that lies up to one block above the whole blocks present;
the last partial block is missing in the file. Other chunks (`LIST`, `cue ` etc.) are listed and
skipped.

### MP3 and streaming

Music is not decoded completely: `Mp3PcmStream` holds only the current MPEG frame
(at most 1,152 samples per channel) and delivers `Read(Span<float>)`; `Rewind()` jumps to the
beginning for loops. Effects and speech are decoded completely and held in the `SoundCache`. The
real test streams all 56 tracks completely (133.1 minutes).

### Resampling

`PcmConvert.Resample` and the mixer interpolate linearly. This is low-distortion enough for
22/44.1 kHz game material but has no anti-aliasing filter: when downsampling, components
above the new Nyquist frequency fold back, when upsampling the top octave is slightly attenuated.
Pitch shift changes pitch and tempo at the same time, as in Miles (`AIL_set_sample_playback_rate` × pitch).

## New dependency NLayer

- **Why:** the music exists exclusively as MP3 (56 files). The BCL contains no
  MP3 decoder; a Layer III decoder of our own (Huffman tables, requantisation, stereo processing,
  IMDCT, polyphase filter bank) would be a multiple of the rest of the package in code and testing effort.
- **Package:** `NLayer` 3.0.0 (firmly pinned in `src/CnCNew.Audio/CnCNew.Audio.csproj`), fully
  managed MPEG-1/2/2.5 Layer I–III decoder, authors Mark Heath and Andrew Ward,
  https://github.com/naudio/NLayer.
- **Licence:** MIT (licence expression `MIT` in the nuspec), compatible with the project.
- **Dependencies:** no transitive packages (assets `net8.0` and `netstandard2.0`; what is used is
  `net8.0` with the `Span<float>` overloads).
- **Check:** `dotnet add … package NLayer --version 3.0.0` restores online in this environment;
  `dotnet build src/CnCNew.Audio` and the test build run with 0 warnings.
- **Alternatives:** NAudio (bound to Windows/ACM, pulls in more), MP3Sharp (LGPL), external
  FFmpeg processes (runtime dependency outside the repo, not deterministically testable), a decoder
  of our own (see above). A swap stays local: NLayer is used only in `Mp3PcmStream`.

## Bink movies

`BinkHeader` reads only the header, audio track table and frame index of the Bink 1 container, described after
the public format documentation (MultimediaWiki "Bink Container"; the same fields are documented by
the demuxer `read_header` in FFmpeg's `libavformat/bink.c`): signature `BIK` + revision letter
(`b`…`k`), file size − 8, frame count, largest frame, width, height, FPS numerator/denominator, video flags,
audio track count, per track maximum decode size, rate/flags (0x1000 DCT, 0x2000 stereo, 0x4000 16 bit)
and track ID, then `Frames + 1` offsets (bit 0 = keyframe). What is checked is the file size, plausible
dimensions, FPS ≠ 0, a strictly increasing index and that the last offset is the end of the file. Bink 2 (`KB2`)
is explicitly rejected.

Real content: 70 movies (39 under `Data/English/Movies`, 2 under `Data/Movies`, 29 under
`ZH_Generals/Data/Movies`), all revision `BIKi`, together 29.4 minutes; 800×600 (campaign and
briefing movies, 30 fps, 44.1 kHz stereo DCT audio), 640×480 variants, 720×486 (`EA_LOGO`, 48 kHz),
400×324 (20 generals' portraits without audio, 13 frames) and 96×120 (`VS_small`, without audio).

**Limit:** Bink video and Bink audio are proprietary codecs; without an external decoder (Bink SDK or
a licensed runtime) the movies cannot be played, nor can their sound. The client
uses the header data (dimensions, duration) to show a silent placeholder for the duration of the movie or to
skip the movie. That is not compatibility but a named gap.

## Catalogue (`AudioCatalog`)

The compiler carries audio blocks as generically typed fields (`AudioEvent`, `DialogEvent`,
`MusicTrack`) or as compatibility definitions (`AudioSettings`, `MiscAudio`); its
heuristic reads e.g. `PitchShift = -10 10` as a single angle. The catalogue therefore re-reads, for every
field, the original text (`DefinitionValue.OriginalText`) with the parse tables of the reference:

- Parse table `AudioEventInfo::m_audioEventInfo` (`INIAudioEventInfo.cpp`): `Volume`,
  `VolumeShift`, `MinVolume`, `LowPassCutoff` via `parsePercentToReal` (percent → ratio, `%`
  optional); `PitchShift` via `parsePitchShift` to factors `1 + p/100`; `Delay` via `parseDelay`
  (milliseconds min/max); `Limit`, `LoopCount` (`parseInt`, number prefix like `sscanf`); `Priority`
  (`parseIndexList` over LOWEST…CRITICAL); `Type`/`Control` (`parseBitString32`: `NONE` clears,
  plain names replace, `+X`/`-X` change the inherited bits); `Sounds`, `SoundsMorning/Evening/Night`,
  `Attack`, `Decay` (`parseSoundsList`, separators whitespace, comma, `=`); `MinRange`, `MaxRange`;
  `Filename`.
- Templates: every event starts as a copy of `DefaultSoundEffect`, `DefaultDialog` or
  `DefaultMusicTrack` (`INI::parseAudioEventDefinition`/`parseDialogDefinition`/
  `parseMusicTrackDefinition`). If a template is missing, the kind starts at zero values
  (`missing-template`).
- Redefinition: `AudioManager::newAudioEventInfo` delivers the existing event, which is then overwritten with the
  template; so only the last block counts. The compiler, in contrast, merges
  blocks – the catalogue discards fields of earlier blocks and counts `redefinition-reset`. All three kinds
  share one namespace; with the same name the block loaded last wins
  (`cross-kind-redefinition`).
- Paths like `AudioEventRTS::generateFilename`/`generateFilenamePrefix`/`adjustForLocalization`:
  `AudioRoot\folder\Name` (+ `.SoundsExtension` only for sound effects; music and dialogue play
  `Filename` literally), where `AudioRoot\folder\<language>\Name` is preferred if the file
  exists. Folders: `SoundsFolder` (effects), `StreamingFolder` (dialogue), `MusicFolder` (music).
  Files not found remain `AudioSoundFile.VirtualPath = null` and are listed by name in the report.
- `AudioSettings` (table `audioSettingsFieldParseTable`, `GameAudio.cpp`): all keys except
  `Preferred3DHW1…4`/`Preferred3DSW` are mandatory (`missing-key`, because the reference would read
  uninitialised memory); speaker names against `TheSpeakerTypes`. `Relative2DVolume` is applied to the
  default volumes like `OptionPreferences::getSoundVolume`/`get3DSoundVolume`
  (`Effective2DSoundVolume`, `Effective3DSoundVolume`).
- `MiscAudio` (`INIMiscAudio.cpp`, 36 known keys): hook → event name; unknown names and
  events that cannot be resolved are counted or listed. `NoSound` is the explicit silence.
- Unknown keys (`unknown-key:<Key>`), invalid values (`invalid-value`, the field keeps the
  template value), additional tokens (`extra-tokens`), residual text after numbers (`trailing-text`),
  impermissible ranges (`invalid-range`), `Filename` on effects (`filename-ignored`) and `Sounds`
  on music/dialogue (`sounds-ignored`) are counted per code, never silently discarded.

Real content (`--maps exclude`): 1,409 sound effects, 68 music tracks, 2,568 dialogue events (+ 3
templates), 7,927 file references, of which 7,842 resolved (5,116 localised under `…/English/`), 6,423
distinct files, **85 not resolvable**, 43 silent events (empty `Sounds`), 35 MiscAudio hooks
without an open reference, **0 diagnostics** (no unknown keys, no invalid values, no
redefinitions). The 85 missing files are data errors of the original (the reference plays
nothing there), e.g. `EvaGLA_FundsTransferred.Filename = egtrnfnd` without extension, 17 demo dialogues
`DemoJune*.wav`, 13 `Taunts_GLABoss1xx`, the music track `Silence` (inherits `NoFilename`) and individual
unit voices. The complete list is printed by `AudioCatalogReport.ToLines()`.

## Selection (`SoundSelector`)

Order of the random draws like `generateFilename` + `generatePlayInfo`: sound index (only
RANDOM), delay, pitch shift, volume shift, attack index, decay index. If a range is empty
(min = max), nothing is drawn.

- RANDOM: index uniformly distributed; with more than two sounds a repetition of the previous index
  is replaced by the next one (`which == m_playingAudioIndex && size > 2`). With two sounds
  repetitions are possible as in the original.
- Without RANDOM (the task description calls this ORDERED; the reference has no such flag): round
  robin from the first sound, state per event instance in `SoundSelectionState` (corresponds to
  `m_playingAudioIndex`, start −1).
- ALL: plays the whole list from the selected index. The reference declares ALL, but its
  `AudioEventRTS::advanceNextPlayPortion` never acts on it (the branch is overwritten by the following
  `if (!m_decayName.isEmpty())`). CnCNew implements the recognisable intention; this is a
  deliberate deviation, named here (175 uses in the content, mostly `loop all random`).
- Volume shift draws between `1 + VolumeShift` and 1, pitch between `PitchShiftMin/Max`, delay
  between `DelayMin/Max` (milliseconds). `LoopCount`: without LOOP once; with LOOP 0 = endless,
  n = n passes (`decreaseLoopCount`/`hasMoreLoops`).
- Time of day: `SoundsMorning/Evening/Night` are selected when the time of day supplied by the caller matches,
  otherwise `Sounds`. The reference parses the variants but selects them nowhere.
- Limit (`CheckLimit`, `MilesAudioManager::doesViolateLimit`): 0 = unlimited; at the limit an
  INTERRUPT event replaces the oldest instance, others are discarded.
- `SeededRng`: xorshift64* (Marsaglia 2003, multiplier after Vigna 2016), start via a
  SplitMix64 step. Own stream like `GameAudioRandomValue` of the reference, so that audio does not touch the
  logic randomness; no `Random.Shared`. The first values for seed 1 are recorded as
  regression values in the test.

## Software mixer

**Threading:** control methods (`Play`, `Stop`, `SetVoicePosition`, `SetListener`, `SetBusVolume`,
`SetMasterVolume`, `PlayMusic`, `StopMusic`, `StopAll`) only put commands into a lock-free
`ConcurrentQueue`; handles are assigned immediately. `Render` runs on exactly one audio thread, applies
all commands at the start of the block and then mixes. Counters (`ActiveVoiceCount`, `PeakLeft/Right`,
`Statistics`, `IsActive`) are published as a snapshot at the end of the block.

**Determinism:** no thread of its own, no device, no clock. The same command sequence and the same
block sizes yield bit-identical output (test: two runs of a script with events, stops,
listener movement and music crossfade, SHA-256 of the output bytes). Float arithmetic is reproducible on the same
machine and runtime; Debug and Release build delivered the same hashes in the test.

**Voices and pools:** positional (3D, `SampleCount3D`), flat (2D, `SampleCount2D`) and stream
(dialogue, `StreamCount`); music runs outside the pools. Admission after `SoundManager::canPlayNow`:

1. Volume below `MinSampleVolume` → discarded (`AudioManager::addAudioEvent`, "muted").
2. Positional, cullable and distance ≥ `MaxRange` → discarded.
3. Limit per `LimitGroup` (event name) in the same pool, see above.
4. Free channel → play.
5. Otherwise steal the voice with the lowest priority below one's own, in a tie the
   oldest (`findLowestPrioritySound`).
6. Otherwise an INTERRUPT event replaces an instance of its own; otherwise discarded.

GLOBAL and CRITICAL events are not cullable (`canPlayNow`, `playAnyways`). During playback
cullable 3D voices are removed as soon as `volume × attenuation` falls below `MinSampleVolume`.
The object-related speech rule (`violatesVoice`: a unit does not speak twice at the same time) and
the player/ally/enemy/shroud filters (`shouldPlayLocally`, `ST_SHROUDED`) belong in the
presentation, which knows objects and the local player.

**3D:** attenuation `DistanceModel.Inverse` like `MilesAudioManager::getEffectiveVolume` (full up to
`MinRange`, then `min/d`, silent from `MaxRange`); `Linear` is the variant commented out there.
GLOBAL events use `GlobalMinRange/GlobalMaxRange`. Panning: cosine between the source direction and
the listener's right axis (Forward × Up; default +Y forward, Z up like `m_listenerOrientation`), from that
equal power θ = (pan + 1)·π/4, gains (cos θ, sin θ); left² + right² = 1, centre −3 dB. 3D sources
are mixed to mono before panning (Miles 3D samples are mono). `Microphone.Place`
implements the microphone placement and the zoom volume from `AudioManager::update`; the client
multiplies the zoom factor onto the Sfx bus.

**Buses:** `Music`, `Sfx` (3D effects), `Ui` (2D effects), `Voice` (dialogue) and additionally `Ambient`
for drawable ambient loops, plus master. `MixerOptions.FromSettings` sets the default values from
`AudioSettings.ini` (music 55 %, speech 70 %, 3D 80 %, 2D 80 % × (1 − 10 %) = 72 % in the content).
`PlayRequest.ForEvent` assigns: music → Music, dialogue → Voice (stream pool), effect positional →
Sfx, otherwise → Ui; volume = event volume × volume shift, pitch = pitch shift.

**Playback:** every voice is a sequence of segments (delay silence → attack → body → per loop a newly
selected body after its own delay → decay, like `notifyOfAudioCompletion`/`startNextLoop`). Loops
select with the mixer RNG from a copy of the selection state, as the reference does with its copy of
`AudioEventRTS`. The resampler interpolates across segment boundaries, loop seams are
gapless (sample-accurate at pitch 1). Gains are ramped linearly per block (no zipper noise on
movement or bus change); fade-in/-out linear. Music streams from `IPcmStream`, loops via `Rewind`
and crossfades linearly on `PlayMusic(..., crossfadeSeconds)`. Stolen voices end immediately as in
the reference.

**Measurement:** the real test plays 40 catalogue events at random positions plus two music tracks with
1 s crossfade: 6.0 s of audio in 0.41 s including the decoding of 141 sounds, two runs bit-identical.

## Tests

`tests/CnCNew.Audio.Tests` (console program, synthetic fixtures): WAV PCM variants and
extensible; tolerances and strict mode; IMA ADPCM hand vector, stereo layout, round trip against an
independent test encoder, `fact` shortening; MS ADPCM hand vectors; conversion/resampling; MP3 stream
from ten synthetic silent Layer III frames with `Rewind`; catalogue from a small INI set
(templates, redefinition, bits, paths, diagnostics); selection (repetition protection, round robin, ALL,
ranges, time of day, limit, RNG regression values); mixer (attenuation, panning, microphone, stealing,
limits, culling, loop seam, fades, crossfade, event segments, determinism); Bink header. With
`--content-dir` the real tests are added (decoding of all files, movie list, catalogue report,
mixer smoke test).

## Limits

- Bink movies cannot be played (see above); the client shows a silent placeholder or
  skips.
- Miles-specific effects are not reproduced: EAX/reverb, HRTF/surround providers
  (`Preferred3DHW*`, speaker types are only read), occlusion low-pass (`LowPassCutoff` is
  typed but not applied), Miles delay filter (delay is implemented as silence before the start or before
  every loop). Output is always stereo.
- No limiter: clipping is hard-limited to [−1, 1] and counted (`ClippedSamples`; in the
  smoke test 1,753 samples with 40 overlapping events plus music).
- ALL follows the intention instead of the (ineffective) reference code; time-of-day variants are selectable
  although the reference never uses them.
- During a delay a voice already occupies its channel (the reference keeps it in the
  request list for that long).
- Linear resampling without anti-aliasing.
- `POSTDELAY` is typed but, as in the reference, has no effect.
