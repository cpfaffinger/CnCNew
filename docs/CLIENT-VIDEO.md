# Client: Bink movies

As of 31 October 2026. Register F-02 in `docs/plan/STAGE-1-PORT.md`. Everything measured here ran on one machine; **no human has seen or heard a movie**.

The game plays its movies with RAD's Bink library. The port plays them with a decoder of its own in C#: `src/CnCNew.Video`, base class library only, no native code. The
format's description is public, and so is an implementation under a licence this project can use: the decoder is rewritten from them (section 8).

## 1. What the game ships

Measured with `AssetTool audit-movies` (section 6):

| | |
|---|---|
| Files | 70: 39 in `Data/English/Movies`, 2 in `Data/Movies`, 29 in `ZH_Generals/Data/Movies` |
| Length | 29.4 minutes, 52,603 frames |
| Signature | `BIKi` in all 70 |
| Video flags | 0 in all 70: no alpha plane, no grey-scale flag |
| Sizes | 800 x 600 (campaign, sizzle), 640 x 480 (variants), 720 x 486 (`EA_LOGO`), 400 x 324 (portraits), 96 x 120 (`VS_small`) |
| Audio | none in the 21 small movies, otherwise one track, flags `0x7000`: 16 bit, stereo, DCT, 44,100 or 48,000 Hz |

Only 41 of them are reachable: `Data/INI/Video.ini` names them. The 29 under `ZH_Generals` belong to the game without the expansion, and no line of the data asks for them, but
they are decoded by the check like the others.

## 2. The decoder

- `BinkHeader` reads and checks the container header; it came from `CnCNew.Audio`, where it stood alone.
- `BinkFile` holds the whole file in memory, as the original does (`BinkOpen` with `BINKPRELOADALL`), and finds a frame's packets: for every audio track a 32-bit size and that many
  bytes, then the video packet to the end of the frame. It **refuses** by name what the game does not ship: any revision but `i`, any video flag but alpha, the RDFT audio
  variant. Those paths have no file to test them against, and nothing is guessed.
- `BinkBitReader`: 32-bit little-endian words read from the lowest bit up. A read past the end of the stretch throws.
- `BinkTables` holds the scan order, the sixteen Huffman trees, the sixteen run patterns and the quantisation matrices. They are data of the format and were generated from
  `binkdata.h`, not typed.
- `BinkVideoDecoder`: a plane is a grid of 8 x 8 blocks; the types, colours, patterns, motion values, DC values and run lengths lie in nine bundles that are refilled a row of
  blocks at a time, and a count of 0 closes a bundle for the plane. Block types: skip, 16 x 16 scaled (run, intra, fill, pattern, raw), motion, run, residue, intra DCT, fill, inter
  DCT, pattern, raw. The planes are Y, V, U in the stream (revision `h` on), an alpha plane comes first. Each plane starts with its 32-bit size, which is skipped, and ends on a
  32-bit boundary. Pixel arithmetic **wraps at 256**, as in the sources; it does not saturate.
- `BinkAudioDecoder`: blocks of 2,048 coefficients per channel at 44.1 kHz and above: two floats, a quantiser per critical band, then runs of coefficients of a common bit width.
  `BinkDct` computes the DCT-III by one complex FFT of the same length. The first 128 samples of a block fade over from the end of the one before.
- Colours: the sources call this revision limited-range (`AVCOL_RANGE_MPEG`), so `CopyRgba` converts by BT.601 in limited range; chroma is repeated, not interpolated.

A stream that cannot be decoded throws `InvalidDataException`; a read past a table is turned into the same. It is never a crash of the game.

## 3. The player

`MoviePlayer` is told how much real time passed and decodes what is due: frame n at n / rate seconds, and the movie ends at frames / rate. A late frame is decoded all the same,
because the next one depends on it, but only the newest picture is converted.

`CnCNew.Game.Movies`:

- `MovieCatalog` reads `Video.ini` from the definition graph and looks for a file as `BinkVideoPlayer::open` does: `Data/<language>/Movies`, then `Data/Movies`.
- `MovieDirector` holds what the original keeps in three places: the full-screen movie (`Display`), the radar's (`InGameUI`), and any it opened for a screen. It moves them once per
  drawn frame. What it cannot play is a counted note (`Failed`, `Stats.SkippedMovies` of the audio director, the console's report), never an exception.
- The sound of a movie is decoded as a whole when it opens (at most 215 ms in the all-files runs) and plays as one voice on the voice bus, so it follows the speech volume, at
  0.81 of it: `calculateMovieAudioVolume` is 0.8 of the volume plus a little, because Bink takes 0 for full volume. It cannot be stolen by another voice.
- The clock of a movie starts at the first update after it opened, not at the opening: the last frame's time holds the opening itself, and the sound starts at its end. Picture
  and sound run on the same real clock from then on, the sound about 80 ms behind the call (the output's buffers).

## 4. Where movies play

| Place | Original | Port |
|---|---|---|
| Start of the game | `Intro.cpp`: `EALogoMovie`, 1 s of black, `Sizzle`, then the shell | `MovieIntro`, before the shell and its music. `--no-intro` leaves it out (the original's `-nologo`); a smoke test, `--shell-script` and `--play-movie` show it only with `--intro`. The stages of the community's own logo are left out: they are not this game |
| A mission's load screen | `SinglePlayerLoadScreen::init`: `IntroMovie` in the root window's video buffer | `MissionBriefingScreen`: the root window's video buffer (`UiWindow.Video`), over what the skin draws for the window, under its children, stretched over the window as `W3DGameWinDefaultDraw` does; the objectives wait behind it. The menu theme ends. The mission starts when the movie ends. Without a movie the screen holds for the voice length, as before |
| Generals' Challenge load screen | `ChallengeLoadScreen`: `GeneralsChallengeBackground`, the two portraits and `VSSmall` at their frames | `ChallengeLoadScreen` (Phase 59): the background in the root window, each portrait movie in its window at frame 35, `VSSmall` at frame 98, all keeping their last frame (`UiWindow.Video`, `WINDOW_PLAY_MOVIE_SHOW_LAST_FRAME`); the titles, the typed bios, the reticle, the three calls and the progress bar at the original's frames of the background. The mission starts when the background ends; the original's bar goes on through the loading, the port's has loaded already. Without a movie every piece stands at once with the large portraits (`activatePiecesMinSpec`) |
| Campaign won to its end | `ScoreScreen.cpp`: `FinalVictoryMovie` by `PlayMovieAndBlock` | Modal, full-screen, before the result screens. The shipped `Campaign.ini` has these lines commented out, so nothing plays with the game's data |
| `MOVIE_PLAY_FULLSCREEN` | `Display::playMovie`: over everything, the game goes on under it | The same; `MissionPresentationDirector.Movies` asks the client |
| `MOVIE_PLAY_RADAR` | `InGameUI::playMovie`, drawn by `W3DLeftHUDDraw` | `MovieDirector.Radar`, drawn in the radar's window, whether or not the player has a radar |

**No shipped map asks for a movie:** `MOVIE_PLAY_FULLSCREEN` and `MOVIE_PLAY_RADAR` occur in none of the 82,563 script actions of the 150 maps (`MapTool audit`). The script path is
tested with synthetic missions, and `--play-radar-movie` shows the radar's on request. `InGameUI::playCameoMovie` is a demo key without a movie in the data and was not built.

A full-screen movie keeps its aspect and fills the height or the width (`W3DDisplay::drawScaledVideoBuffer`). Under the intro and a modal movie nothing runs and nothing takes input.

**Escape**, when it comes up, ends the running stage of the intro, a load screen's movie, a modal movie and a script's full-screen movie (`WindowXlat`,
`GameClient::isMovieAbortRequested`, `PlayMovieAndBlock`). The escape of a script's movie does not reach the HUD. The original has no click for this, and neither has the port.

## 5. Movies and the simulation

Movies never touch the simulation. A mission reaches them only through a script output: `MissionPresentationDirector` asks its host for the movie and gets its length back. The
picture then runs on the real clock, but **the scripts hear of the end after that length in game time**, through the same `CompletePresentation` that ends a speech or a camera move.
So the moment a script sees `HAS_FINISHED_VIDEO` depends on the movie's header, not on how fast a client draws.

A run without a window has no host: the movie is its text placeholder and `PresentationLatencyTicks` 0 finishes it at once, as before. A client with a movie it cannot play falls back
to the same placeholder. The client's fallback latency (`LiveMission.ClientPresentationLatencyFrames`, 60 s) still applies, so a script sees a longer movie as finished after 60 s.
The original's `notifyOfCompletedVideo` is commented out in `Display::stopMovie`, so there `HAS_FINISHED_VIDEO` never comes true; the port's behaviour here is older than this
decoder and was not changed.

## 6. The checks

```powershell
dotnet run -c Release --project tools/CnCNew.AssetTool -- audit-movies game/content [--only <part of a path>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- movie-frame game/content <movie path> <frame> <output.png> [--wav <output.wav>]
dotnet run --project tests/CnCNew.Video.Tests [-- --content-dir game/content]
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir game/content --shell --play-movie MD_USA01 --frames 300 --capture <file>
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir game/content --match "Maps/Alpine Assault/Alpine Assault.map" --play-radar-movie VSSmall --frames 40 --capture <file>
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --intro --content-dir game/content --shell --frames 200 --capture <file>
```

`audit-movies` decodes every movie to its last frame and prints size, rate, audio, the SHA-256 over the visible planes and over the samples, and the cost per frame. Its exit code is 1 if
one fails. Files it writes go outside the repository: a decoded frame or sound is an export of the game.

`tests/CnCNew.Video.Tests` builds its files from a bit writer by the format's own rules (`tests/Shared/BinkFixtures.cs`): plane order and colours, filled, DC-only, skipped, moved and
scaled blocks, alpha, damaged frames, the DCT against its definition, a constant audio block and the overlap, the player's clock. With `--content-dir` it decodes four movies twice to the same
checksums. `MissionMovieTests` (run inside the suite `MissionTextTests` of `Game.Tests`) covers the director, the intro, a script's movie, the catalogue and the load screen.

## 7. Measured

The Ryzen 3 PRO 4350GE, Release build, **not quiet**: a long test run was going on beside it. One core, video decode plus conversion to RGBA, without the upload.

| Movie | Size | Frames | Mean per frame | Longest frame | Budget |
|---|---|---|---|---|---|
| `sizzle_review.bik` (the largest file, 23.9 MB) | 800 x 600 | 1,961 | 3.27 ms | 5.34 ms | 33.3 ms |
| `MD_USA01_0.bik` (the longest mission movie) | 800 x 600 | 1,919 | 3.39 ms | 6.22 ms | 33.3 ms |
| the 43 movies of 800 x 600 | 800 x 600 | | 4.82 ms at most | median of their longest 6.3 ms; 57 ms at most | 33.3 ms |

So decoding takes a tenth to a seventh of a frame's time. The long frames are not the movies': frame 0 of a fresh process is the JIT (46 to 63 ms over repeated runs of one movie), and the
rest fell on other movies and frames in every run (87 ms, then 132 ms, then 57 ms as the longest of three runs of all 70) while a test run took the machine. The player only catches up
after one. The first 13-frame portraits show means up to 11.7 ms for the same reason. The sound of a whole movie decodes in at most 215 ms. Three runs of all 70 gave the same checksums.

**The picture was compared by eye** at 800 x 600 and 640 x 480 (a news desk, the logo, a general's portrait): colours and blocks are right. The block-like green patches in the
sizzle around frame 1,000 are the film's own transition; both resolutions show them, in blocks far larger than the codec's. The client's capture of frame 300 of `MD_USA01` equals the
decoder's PNG to one unit in six sampled pixels.

**The sound was compared by numbers:** in `MD_USA01` 3 of 5.6 million samples reach full scale at a mean level of 2,012, so the scale is right; the step between samples at a block's seam
is that of the middle of a block (208 against 210), so the overlap is right. In the client's capture of `EALogoMovie` the mixer's peak is 0.59.

**The texture format:** a movie's picture is uploaded as `R8G8B8A8_UNorm_SRgb`. With a plain view the picture came out lighter (84 became 155), that is, unconverted bytes taken as linear
light. `UiImageTextures` says the opposite for its plain pages, and that was not looked into.

## 8. Sources

Read, understood and rewritten; nothing was copied but the tables, which are data of the format:

- MultimediaWiki: "Bink Container", "Bink Video", "Bink Audio".
- FFmpeg, `libavcodec/bink.c`, `binkdsp.c`, `binkdata.h`, `binkaudio.c`, `libavformat/bink.c` (LGPL 2.1 or later; Konstantin Shishkov, Peter Ross, Daniel Verkamp), read on 31 October
  2026. The inverse DCT of the audio is the definition of its old `dct_calc`, computed after J. Makhoul, *A fast cosine transform in one and two dimensions*, 1980.
- The original's use: `BinkVideoPlayer.cpp`, `Intro.cpp`, `LoadScreen.cpp`, `ScoreScreen.cpp`, `Display.cpp`, `W3DDisplay.cpp`, `W3DControlBar.cpp`, `InGameUI.cpp`, `WindowXlat.cpp`,
  `ScriptActions.cpp`, in GeneralsGameCode and the EA release.

## 9. Not done, not verified

- Nothing of this has been seen or heard by a human. No sound device exists on the development machine: the sound was checked through the mixer's capture.
- Revisions other than `i`, the RDFT audio variant, more than one audio track (the first is played), and the alpha plane on a real file: the game ships none, and alpha is tested on a
  synthetic file only.
- Chroma is not interpolated; RAD's own conversion was not compared.
- Picture and sound are not locked to the device's position; they share the real clock.
- Opening a movie decodes its whole sound on the main thread, a hitch of up to 215 ms, which matters only for a movie in a running mission, and no shipped map has one.
