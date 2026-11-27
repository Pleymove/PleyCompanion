#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineInstaller,
    [Parameter(Mandatory)] [string] $Installer
)

$ErrorActionPreference = 'Stop'
$profileDirectory = Join-Path $env:LOCALAPPDATA 'PleyCompanion'
$installDirectory = Join-Path $env:RUNNER_TEMP 'pley-upgrade-install'
$preservedDirectory = Join-Path $env:LOCALAPPDATA 'PleyCompanion-UpgradeSmoke'
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP -or
    (Test-Path $profileDirectory) -or (Test-Path $installDirectory) -or (Test-Path $preservedDirectory)) {
    throw 'The upgrade smoke test needs an unused PleyCompanion profile on a GitHub-hosted Windows runner.'
}

function Install-PleyCompanion([string] $Setup, [string] $LogFile) {
    $process = Start-Process -FilePath (Resolve-Path $Setup).Path -PassThru -Wait -ArgumentList @(
        '--silent', '--installto', ('"{0}"' -f $installDirectory), '--log', ('"{0}"' -f $LogFile)
    )
    if ($process.ExitCode -ne 0) {
        if (Test-Path $LogFile) { Get-Content $LogFile | Write-Host }
        throw "Installer failed with exit code $($process.ExitCode)."
    }
}

function Assert-StartsWithoutTour {
    $process = Start-Process -FilePath $executable -PassThru
    $log = Join-Path $profileDirectory 'Logs/app.log'
    try {
        for ($iteration = 0; $iteration -lt 60; $iteration++) {
            if ($process.HasExited) { throw "PleyCompanion exited at startup with code $($process.ExitCode)." }
            if ((Test-Path $log) -and (Select-String -Quiet -LiteralPath $log -SimpleMatch 'launch: ready')) { break }
            Start-Sleep -Milliseconds 500
        }
        if (!(Test-Path $log) -or !(Select-String -Quiet -LiteralPath $log -SimpleMatch 'launch: ready')) {
            throw 'PleyCompanion did not report that startup was ready.'
        }
        # Give any tour waiting for WPF startup or for the permission poll time to open.
        Start-Sleep -Seconds 2
        if (Select-String -Quiet -LiteralPath $log -Pattern 'tour: (meet|dictate)') { throw 'The welcome tour started automatically.' }
    }
    finally {
        if (!$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
    }
}

Install-PleyCompanion $BaselineInstaller (Join-Path $env:RUNNER_TEMP 'pley-baseline-setup.log')
$executable = Join-Path $installDirectory 'current/PleyCompanion.exe'
$baselineVersion = [version](Get-Item $executable).VersionInfo.FileVersion
if ($baselineVersion -ne [version]'0.12.1.0') { throw "Expected the 0.12.1 baseline, installed $baselineVersion." }

$shell = Join-Path $profileDirectory 'shell.json'
$preferences = Join-Path $profileDirectory 'preferences.json'
$models = Join-Path $profileDirectory 'Models/Parakeet/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8'
$model = Join-Path $models 'tokens.txt'
New-Item -ItemType Directory -Path $models | Out-Null
'{"language":"de","speechVoice":"mac","token":"ci-fake-token","openaiApiKey":"ci-fake-key","backendUrl":"http://127.0.0.1:1","extra":42}' |
    Set-Content -LiteralPath $shell -Encoding utf8NoBOM
'{"dictation.engine":"moonshine","dictation.language":"en-GB","dictation.key":"rightShift","openclicky.firstRunTour.step":"dictate","isOpenClickyRealtimeVoiceEnabled":false}' |
    Set-Content -LiteralPath $preferences -Encoding utf8NoBOM
'ci-model-file' | Set-Content -LiteralPath $model
$hashes = Get-FileHash -LiteralPath @($shell, $preferences, $model)

Install-PleyCompanion $Installer (Join-Path $env:RUNNER_TEMP 'pley-phase2-setup.log')
$newVersion = [version](Get-Item $executable).VersionInfo.FileVersion
if ($newVersion -le $baselineVersion) { throw "The new installer did not replace $baselineVersion with a later version." }
foreach ($record in $hashes) {
    if ((Get-FileHash -LiteralPath $record.Path).Hash -ne $record.Hash) {
        throw "The installer altered $(Split-Path $record.Path -Leaf)."
    }
}
Assert-StartsWithoutTour
$upgradedShell = Get-Content -LiteralPath $shell -Raw | ConvertFrom-Json
$upgradedPreferences = Get-Content -LiteralPath $preferences -Raw | ConvertFrom-Json
if ($upgradedShell.language -ne 'de' -or $upgradedShell.speechVoice -ne 'mac' -or $upgradedShell.token -ne 'ci-fake-token' -or
    $upgradedShell.openaiApiKey -ne 'ci-fake-key' -or $upgradedShell.extra -ne 42 -or
    $upgradedPreferences.'dictation.engine' -ne 'moonshine' -or $upgradedPreferences.'dictation.language' -ne 'en-GB' -or
    $upgradedPreferences.'dictation.key' -ne 'rightShift' -or (Get-FileHash -LiteralPath $model).Hash -ne $hashes[2].Hash) {
    throw 'The first upgraded launch changed the former profile.'
}
"Upgrade $baselineVersion → $newVersion. Setup preserved all three seeded files byte for byte and the app retained the prior choices on launch."

Move-Item -LiteralPath $profileDirectory -Destination $preservedDirectory
Assert-StartsWithoutTour
$freshShell = Get-Content -LiteralPath $shell -Raw | ConvertFrom-Json
$freshPreferences = Get-Content -LiteralPath $preferences -Raw | ConvertFrom-Json
if ($freshShell.language -ne 'fr' -or $freshShell.speechVoice -ne 'kokoro' -or $freshShell.transcriptionProvider -ne 'parakeet' -or
    $freshPreferences.'dictation.engine' -ne 'parakeet' -or $freshPreferences.'dictation.language' -ne 'fr-FR' -or
    $freshPreferences.'isOpenClickyRealtimeVoiceEnabled' -ne $false) {
    throw 'The new installation did not start with the local French profile.'
}
'Fresh launch. French local voice defaults, with no automatic welcome tour.'
