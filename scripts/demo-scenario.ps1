# Staged single demo. WITHOUT -Scenario nothing starts: the script lists the available scenes and ends (there is no default scene;
# until 7 October 2026 a call without a name silently started 'tank-duel').
#   .\scripts\demo-scenario.ps1                       # list of the scenes
#   .\scripts\demo-scenario.ps1 -Scenario jet-strafe  # starts exactly this scene in a window
# Further parameters: see demo.ps1 (-Size, -TickRate, -Mouse, -SmokeTest -Frames N ...).
& "$PSScriptRoot\demo.ps1" scenario @args
exit $LASTEXITCODE
