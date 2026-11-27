param([string]$Revision)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Read-Source([string]$path) {
    if ($Revision) {
        $result = & git -C $repoRoot show "${Revision}:$path"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read $path at $Revision" }
        return $result -join "`n"
    }
    return Get-Content (Join-Path $repoRoot $path) -Raw
}

$main = Read-Source 'FluentFlyoutWPF/MainWindow.xaml.cs'
$switcher = Read-Source 'FluentFlyoutWPF/MainWindow.SessionSwitcher.cs'
$methods = foreach ($name in @('RestoreSystemFollowOnNewPlayback', 'CaptureSessionPlaybackStates', 'ForgetSessionPlaybackState', 'GetSessionPlayingState', 'PinSession')) {
    $pattern = "(?ms)^    (?:public|private) [^\r\n]+\b$name\([^)]*\)\r?\n    \{.*?^    \}"
    $match = [regex]::Match($switcher, $pattern)
    if ($match.Success) { $match.Value }
}
if (-not ($methods -match 'void RestoreSystemFollowOnNewPlayback\(')) { throw 'Auto-follow method not found' }
$fields = [regex]::Matches($switcher, '(?m)^    private readonly [^\r\n]+;') |
    ForEach-Object { $_.Value }
$subscriptions = [regex]::Matches($main, '(?m)^\s*mediaManager\.OnAnySession(?:Opened|Closed) \+= (?:RestoreSystemFollowOnNewPlayback|ForgetSessionPlaybackState);') |
    ForEach-Object { $_.Value.Trim() }
$unsubscriptions = [regex]::Matches($main, '(?m)^\s*mediaManager\.OnAnySession(?:Opened|Closed) -= (?:RestoreSystemFollowOnNewPlayback|ForgetSessionPlaybackState);') |
    ForEach-Object { $_.Value.Trim() }
$constructor = $main.Substring(0, $main.IndexOf('    private async Task CheckForExperimentsOnStartupAsync'))
$initialCapture = if ($constructor.Contains('CaptureSessionPlaybackStates();')) { 'CaptureSessionPlaybackStates();' } else { '' }
$callback = [regex]::Match($main, '(?ms)^    private void CurrentSession_OnPlaybackStateChanged\([^)]*\)\r?\n    \{.*?^    \}').Value
$callbackCalls = [regex]::Matches($callback, '(?:RestoreSystemFollowOnNewPlayback|pauseOtherMediaSessionsIfNeeded)\([^;]*\);') |
    ForEach-Object { $_.Value -replace '\bmediaSession\b', 'session' -replace '\bplaybackInfo\b', 'eventInfo' }
if (-not ($callbackCalls -match 'RestoreSystemFollowOnNewPlayback\(')) { throw 'Playback callback is not connected to auto-follow' }
$generated = @"
using FluentFlyout.Classes.Settings;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
$($fields -join "`n")
$($methods -join "`n")
    private void InitializeAutoFollow() { $initialCapture $($subscriptions -join ' ') }
    public void Observe(MediaSession session, GlobalSystemMediaTransportControlsSessionPlaybackInfo? eventInfo = null) { $($callbackCalls -join ' ') }
    public void DetachAutoFollow() { $($unsubscriptions -join ' ') }
}
"@
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'obj') -Force | Out-Null
Set-Content (Join-Path $PSScriptRoot 'obj/AutoFollowMethods.cs') $generated -Encoding utf8
& dotnet run --project (Join-Path $PSScriptRoot 'MediaSessionAutoFollow.csproj') --configuration Release
exit $LASTEXITCODE
