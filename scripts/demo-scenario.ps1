# Gestellte Einzeldemo. OHNE -Scenario startet nichts: das Skript listet die vorhandenen Szenen und endet (es gibt keine Standardszene;
# bis 7. Oktober 2026 startete ein Aufruf ohne Namen stillschweigend 'tank-duel').
#   .\scripts\demo-scenario.ps1                       # Liste der Szenen
#   .\scripts\demo-scenario.ps1 -Scenario jet-strafe  # startet genau diese Szene im Fenster
# Weitere Parameter: siehe demo.ps1 (-Size, -TickRate, -Mouse, -SmokeTest -Frames N ...).
& "$PSScriptRoot\demo.ps1" scenario @args
exit $LASTEXITCODE
