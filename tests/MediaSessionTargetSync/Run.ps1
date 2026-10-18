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
$sources = "$main`n$switcher"
$methods = foreach ($name in @(
    'GetActiveMediaSession', 'PinSession', 'RefreshFilteredMedia', 'RefreshSeekbarPosition',
    'MediaManager_OnFocusedSessionChanged', 'MediaManager_OnAnySessionOpened',
    'MediaManager_OnAnySessionClosed'
)) {
    # Compile the actual method bodies, including the unfixed bodies when -Revision is supplied.
    $pattern = "(?ms)^    (?:public|private) [^\r\n]+\b$name\([^\r\n]*\)\r?\n    \{.*?^    \}"
    $match = [regex]::Match($sources, $pattern)
    if ($match.Success) { $match.Value }
}
$events = 'OnFocusedSessionChanged|OnAnySessionOpened|OnAnySessionClosed'
$attach = [regex]::Matches($main, "(?m)^\s*mediaManager\.(?:$events) \+= [^;]+;") |
    ForEach-Object { $_.Value.Trim() }
$detach = [regex]::Matches($main, "(?m)^\s*mediaManager\.(?:$events) -= [^;]+;") |
    ForEach-Object { $_.Value.Trim() }
$constructor = $main.Substring(0, $main.IndexOf('    private async Task CheckForExperimentsOnStartupAsync'))
$initialRefresh = if ($constructor.Contains('RefreshFilteredMedia();')) { 'RefreshFilteredMedia();' } else { '' }
$generated = @"
using FluentFlyout.Classes.Settings;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
$($methods -join "`n")
    private void AttachMediaEvents() { $($attach -join ' ') $initialRefresh }
    private void DetachMediaEvents() { $($detach -join ' ') }
}
"@
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'obj') -Force | Out-Null
Set-Content (Join-Path $PSScriptRoot 'obj/SessionMethods.cs') $generated -Encoding utf8
& dotnet run --project (Join-Path $PSScriptRoot 'MediaSessionTargetSync.csproj') --configuration Release
exit $LASTEXITCODE
