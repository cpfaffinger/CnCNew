<#
.SYNOPSIS
    A person at the menus, as a script, in the window client: "Host game" and joining it, a match and the way back into the room; and a skirmish against the
    computer through the server embedded in the game (docs/GAME-SHELL.md sections 15 and 16).

.DESCRIPTION
    Starts on this machine the Release window client three or four times, each driven by --shell-script (CnCNew.Game.Shell.ShellScript: every step waits for what a
    person would see - a screen, a text, a button that can be pressed - and then moves the pointer and clicks or types; the device's mouse and keyboard are ignored):

      host   main menu > Multiplayer > Network > "Host": a server inside the game on a loopback port of this run; a locked room; waits for the guest; starts.
      wary   a client whose list of known servers names another key for that address: it must refuse the server with both fingerprints and say nothing to it.
      guest  joins the address, is asked about the server's key and says yes, sees the room in the list, is refused with a wrong password and told so, enters with
             the right one, takes the open seat, chats, accepts.
      local  main menu > Single Player > Skirmish > Start: a match against the computer through the embedded server.

    and checks
      - every script ran through: each step's condition came true within its time limit (a failed step names what the shell showed instead);
      - host and guest played the same match: the same last tick, the same session hash there and the same hash chain over every tick;
      - both gave orders (--net-order-every: one unit of the viewer is ordered to move every two seconds through the command sink the HUD uses), and the match
        ended by its own rules - no message of a lost connection or a desync;
      - both stood in the room again afterwards;
      - the files of the visit (known servers, player key, host key, recent addresses) lie in the directories of this run and nowhere else.
    What this does not show: what a human sees. The pictures of every state are pinned by NetScreenTests; this check is the same screens in the real client with
    real sockets, its text measure and its frame loop.

    The window clients need a display; without one the check ends with exit code 3 (not run). Logs go to a directory outside the repository.

.EXAMPLE
    pwsh scripts/check-net-lobby.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$MatchSeconds = 20,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-lobby'),
    [switch]$NoBuild,
    [switch]$SkipLocal,
    [int]$TimeoutSeconds = 420
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it (scripts/dev-content.ps1)."
    exit 2
}
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.Screen]::AllScreens.Count -eq 0) {
    Write-Host 'NOT RUN: this machine has no display; the window client cannot open its window.'
    exit 3
}
if (-not $NoBuild) {
    dotnet build -c Release src/CnCNew.Client.Stride -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building src/CnCNew.Client.Stride failed.'; exit 1 }
}
$clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' | Select-Object -First 1
if (-not $clientDll) { Write-Host 'Release client not found; start without -NoBuild.'; exit 1 }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }

# A loopback port the operating system has free right now.
$probe = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 0))
$port = $probe.Client.LocalEndPoint.Port
$probe.Close()

function Write-Script([string]$name, [string[]]$lines) {
    $path = Join-Path $OutDir "$name.script.txt"
    [IO.File]::WriteAllLines($path, $lines)
    $path
}
function Start-Client([string]$name, [string]$script, [string[]]$extra) {
    $arguments = @($clientDll.FullName, '--content-dir', $ContentDir, '--size', '1024x768', '--mute', '--net-dir', (Join-Path $OutDir $name), '--net-port', "$port",
        '--shell-script', $script, '--shell-seconds', "$($TimeoutSeconds - 60)") + $extra
    Start-Process -FilePath 'dotnet' -ArgumentList ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $OutDir "$name.log") -RedirectStandardError (Join-Path $OutDir "$name.err")
}
function Wait-Client($process, [string]$name, [int]$seconds) {
    if (-not $process.WaitForExit($seconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Fail "$name did not end within $seconds s"
        return
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { Fail "$name ended with exit code $($process.ExitCode)" }
}
function Log([string]$name) { $path = Join-Path $OutDir "$name.log"; if (Test-Path $path) { Get-Content $path } else { @() } }
function Check-Script([string]$name) {
    $summary = Log $name | Where-Object { $_ -cmatch '^shell-script summary:' } | Select-Object -Last 1
    if (-not $summary) { Fail "${name}: no summary of its script (see $name.log, $name.err)"; return }
    if ($summary -cnotmatch 'failed=0 finished=1') {
        Fail "${name}: $summary"
        Log $name | Where-Object { $_ -cmatch 'shell-script: FAILED' } | ForEach-Object { Write-Host "      $_" }
    }
    else { Write-Host "   ${name}: $summary" }
}

$toAddress = @(
    'wait screen MainMenuScreen', 'expect enabled MainMenu.wnd:ButtonMultiplayer', 'click MainMenu.wnd:ButtonMultiplayer', 'wait disabled MainMenu.wnd:ButtonOnline',
    'click MainMenu.wnd:ButtonNetwork', 'wait screen NetworkDirectConnectScreen')
$matchAndBack = @(
    'limit 180', 'wait screen NetLoadingScreen', 'expect text MultiplayerLoadScreen.wnd:StaticTextPlayer0 equals Alice', 'expect text MultiplayerLoadScreen.wnd:StaticTextPlayer1 equals Bob',
    'wait phase InMatch', 'wait match tick atleast 300', 'wait phase Results', 'wait screen ScoreScreenScreen', 'click ScoreScreen.wnd:ButtonOk', 'wait screen NetRoomScreen',
    # Who is back first opens the room again and is its host: either may sit in either seat. The last wait keeps both there until each has seen the other.
    'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice|Bob', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Alice|Bob', 'wait seconds 4')
$hostScript = Write-Script 'host' ($toAddress + @(
    'type NetworkDirectConnect.wnd:EditPlayerName Alice',
    'click NetworkDirectConnect.wnd:ButtonHost',
    'wait screen NetLobbyScreen',
    'wait text LanLobbyMenu.wnd:StaticTextGameInfo contains No room is open',
    'click LanLobbyMenu.wnd:ButtonHost',
    'wait screen NetHostPopupScreen',
    'type PopupHostGame.wnd:TextEntryGameName Friday night',
    'type PopupHostGame.wnd:TextEntryGamePassword sesam',
    'click PopupHostGame.wnd:ButtonCreateGame',
    'wait screen NetRoomScreen',
    'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice',
    'expect text LanGameOptionsMenu.wnd:ButtonStart equals PLAY GAME',
    'limit 240',
    'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob',
    'limit 30',
    'wait list LanGameOptionsMenu.wnd:ListboxChatWindowLanGame contains Bob: hello Alice',
    'type LanGameOptionsMenu.wnd:TextEntryChat hello Bob',
    'enter LanGameOptionsMenu.wnd:TextEntryChat',
    'wait shown LanGameOptionsMenu.wnd:ButtonAccept1',
    'click LanGameOptionsMenu.wnd:ButtonStart') + $matchAndBack)
$waryScript = Write-Script 'wary' ($toAddress + @(
    'type NetworkDirectConnect.wnd:EditPlayerName Mallory',
    'type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:$PORT',
    'click NetworkDirectConnect.wnd:ButtonJoin',
    'limit 30',
    'wait screen MessageBoxScreen',
    'expect text MessageBox.wnd:StaticTextTitle equals This is not the server you know',
    'expect text MessageBox.wnd:StaticTextMessage contains You know 0123 4567 89AB CDEF',
    'expect text MessageBox.wnd:StaticTextMessage contains Nothing was sent to it',
    'click MessageBox.wnd:ButtonOk',
    'wait screen NetworkDirectConnectScreen'))
$guestScript = Write-Script 'guest' ($toAddress + @(
    'type NetworkDirectConnect.wnd:EditPlayerName Bob',
    'type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:$PORT',
    'click NetworkDirectConnect.wnd:ButtonJoin',
    'limit 30',
    'wait screen MessageBoxScreen',
    'expect text MessageBox.wnd:StaticTextTitle equals New server',
    'expect text MessageBox.wnd:StaticTextMessage contains You have not met the server at 127.0.0.1:$PORT',
    'click MessageBox.wnd:ButtonYes',
    'wait screen NetLobbyScreen',
    'pick LanLobbyMenu.wnd:ListboxGames [locked] Friday night',
    'wait text LanLobbyMenu.wnd:StaticTextGameInfo contains Needs a password',
    'click LanLobbyMenu.wnd:ButtonJoin',
    'wait screen NetJoinPopupScreen',
    'type PopupJoinGame.wnd:TextEntryGamePassword Sesam',
    'enter PopupJoinGame.wnd:TextEntryGamePassword',
    'wait screen MessageBoxScreen',
    'expect text MessageBox.wnd:StaticTextTitle equals Cannot enter the room',
    'expect text MessageBox.wnd:StaticTextMessage contains That is not the room''s password',
    'click MessageBox.wnd:ButtonOk',
    'wait screen NetLobbyScreen',
    'pick LanLobbyMenu.wnd:ListboxGames [locked] Friday night',
    'click LanLobbyMenu.wnd:ButtonJoin',
    'wait screen NetJoinPopupScreen',
    'type PopupJoinGame.wnd:TextEntryGamePassword sesam',
    'enter PopupJoinGame.wnd:TextEntryGamePassword',
    'wait screen NetRoomScreen',
    'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob',
    'expect text LanGameOptionsMenu.wnd:ButtonStart equals ACCEPT',
    'expect disabled LanGameOptionsMenu.wnd:ButtonSelectMap',
    'type LanGameOptionsMenu.wnd:TextEntryChat hello Alice',
    'enter LanGameOptionsMenu.wnd:TextEntryChat',
    'wait list LanGameOptionsMenu.wnd:ListboxChatWindowLanGame contains Alice: hello Bob',
    'click LanGameOptionsMenu.wnd:ButtonStart') + $matchAndBack)
$localScript = Write-Script 'local' @(
    'wait screen MainMenuScreen', 'click MainMenu.wnd:ButtonSinglePlayer', 'click MainMenu.wnd:ButtonSkirmish', 'wait screen SkirmishSetupScreen',
    'click SkirmishGameOptionsMenu.wnd:ButtonStart', 'limit 120', 'wait phase InMatch', 'wait match tick atleast 600', 'wait match units atleast 1')

# The wary client knows another key for the address: a file of this run, written before it starts.
$waryDir = Join-Path $OutDir 'wary'
New-Item -ItemType Directory -Force -Path $waryDir | Out-Null
$bogus = '0123456789ABCDEF' * 4
[IO.File]::WriteAllText((Join-Path $waryDir 'known-servers.json'), (@{ "127.0.0.1:$port" = $bogus } | ConvertTo-Json))

Write-Host "== Host game on 127.0.0.1:$port, a wary client, a guest$(if (-not $SkipLocal) { ', a local skirmish' }) (logs in $OutDir)"
$watch = [Diagnostics.Stopwatch]::StartNew()
$common = @('--net-match-seconds', "$MatchSeconds", '--net-order-every', '120')
$hostProcess = Start-Client 'host' $hostScript $common
# The host needs its time to load and to stand in its room; the others' scripts wait for nothing that is not there, so they start when the server answers.
$deadline = $watch.Elapsed.TotalSeconds + 120
while ($watch.Elapsed.TotalSeconds -lt $deadline -and -not (Test-Path (Join-Path $OutDir 'host/host-key.pem'))) { Start-Sleep -Milliseconds 500 }
if (-not (Test-Path (Join-Path $OutDir 'host/host-key.pem'))) { Fail 'the host did not start its server within 120 s' }
Start-Sleep -Seconds 3
$waryProcess = Start-Client 'wary' $waryScript @('--shell-var', "PORT=$port", '--no-transitions')
$guestProcess = Start-Client 'guest' $guestScript ($common + @('--shell-var', "PORT=$port", '--no-transitions'))
$localProcess = if ($SkipLocal) { $null } else { Start-Client 'local' $localScript @('--no-transitions') }
Wait-Client $waryProcess 'wary' 180
Wait-Client $guestProcess 'guest' $TimeoutSeconds
Wait-Client $hostProcess 'host' 60
if ($localProcess) { Wait-Client $localProcess 'local' 240 }

foreach ($name in @('host', 'wary', 'guest') + $(if ($SkipLocal) { @() } else { @('local') })) { Check-Script $name }

# ---- The same match on both.
function Match-Line([string]$name) { Log $name | Where-Object { $_ -cmatch 'net-match: end tick' } | Select-Object -Last 1 }
$hostLine = Match-Line 'host'
$guestLine = Match-Line 'guest'
if (-not $hostLine -or -not $guestLine) { Fail 'host or guest reported no match' }
else {
    $pattern = 'end tick (?<tick>\d+) hash (?<hash>[0-9A-F]{16}) chain (?<chain>[0-9A-F]{16}) player (?<player>\d+) winner \d+ over (?<over>\w+) left (?<left>\w+) orders (?<orders>\d+) intents (?<intents>\d+) stalls \d+ note (?<note>.*)$'
    $h = [regex]::Match($hostLine, $pattern)
    $g = [regex]::Match($guestLine, $pattern)
    if (-not $h.Success -or -not $g.Success) { Fail "the match lines cannot be read: '$hostLine' / '$guestLine'" }
    else {
        Write-Host "   host : $($hostLine -replace '^.*net-match: ', '')"
        Write-Host "   guest: $($guestLine -replace '^.*net-match: ', '')"
        if ($h.Groups['tick'].Value -ne $g.Groups['tick'].Value -or [int]$h.Groups['tick'].Value -ne $MatchSeconds * 60) { Fail "the match did not end at tick $($MatchSeconds * 60) on both ($($h.Groups['tick'].Value), $($g.Groups['tick'].Value))" }
        if ($h.Groups['hash'].Value -ne $g.Groups['hash'].Value) { Fail 'the session hashes at the end differ' }
        if ($h.Groups['chain'].Value -ne $g.Groups['chain'].Value) { Fail 'the hash chains over the match differ' }
        if ($h.Groups['player'].Value -ne '1' -or $g.Groups['player'].Value -ne '2') { Fail "host and guest are not players 1 and 2 ($($h.Groups['player'].Value), $($g.Groups['player'].Value))" }
        foreach ($side in @(@('host', $h), @('guest', $g))) {
            $m = $side[1]
            if ($m.Groups['over'].Value -ne 'yes' -or $m.Groups['left'].Value -ne 'no' -or $m.Groups['note'].Value -ne 'none') { Fail "$($side[0]): the match did not end by its own rules ($($m.Value))" }
            if ([int]$m.Groups['orders'].Value -lt 3 -or [int]$m.Groups['intents'].Value -lt 3) { Fail "$($side[0]): fewer than three orders went to the server ($($m.Groups['orders'].Value) given, $($m.Groups['intents'].Value) sent)" }
        }
    }
}

# ---- The files of the visit lie in the run's directories, and say what they should.
$guestKnown = Join-Path $OutDir 'guest/known-servers.json'
if (-not (Test-Path $guestKnown)) { Fail 'the guest did not remember the server it said yes to' }
else {
    $known = Get-Content $guestKnown -Raw | ConvertFrom-Json -AsHashtable
    if ($known.Count -ne 1 -or -not $known.ContainsKey("127.0.0.1:$port")) { Fail "the guest's known servers are not exactly the one it met ($($known.Keys -join ', '))" }
}
$waryKnown = Get-Content (Join-Path $waryDir 'known-servers.json') -Raw | ConvertFrom-Json -AsHashtable
if ($waryKnown["127.0.0.1:$port"] -ne $bogus) { Fail 'the wary client changed the key it knew' }
foreach ($file in 'host/host-key.pem', 'host/player-key.pem', 'guest/player-key.pem', 'host/recent-servers.txt', 'guest/recent-servers.txt') {
    if (-not (Test-Path (Join-Path $OutDir $file))) { Fail "$file was not written in the run's directory" }
}
if (-not $SkipLocal) {
    # The local skirmish's script ends in the match: the run is over when the ticks advanced and the viewer has its units.
    if (-not (Log 'local' | Where-Object { $_ -cmatch 'step \d+ ok .*wait match units atleast 1' })) { Fail 'the local skirmish did not reach a running match with units of the viewer' }
}

Write-Host ("== {0} after {1:F0} s" -f $(if ($failed -eq 0) { 'ok' } else { "$failed problem(s)" }), $watch.Elapsed.TotalSeconds)
exit ([Math]::Min($failed, 1))
