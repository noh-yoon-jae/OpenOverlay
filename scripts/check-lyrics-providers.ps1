param(
    [Parameter(Position = 0)] [string] $TrackId,
    [Parameter(Position = 1)] [string] $Title,
    [Parameter(Position = 2)] [string] $Artist
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Title)) { $Title = Read-Host 'Song title' }
if ([string]::IsNullOrWhiteSpace($Artist)) { $Artist = Read-Host 'Artist (multiple artists can be separated with commas)' }
if ([string]::IsNullOrWhiteSpace($TrackId)) { $TrackId = Read-Host 'Spotify track ID (22 characters)' }

$TrackId = $TrackId.Trim()
if ($TrackId -notmatch '^[A-Za-z0-9]{22}$') { throw 'Track ID must be exactly 22 letters or digits. Do not paste a full URL or URI.' }
if ([string]::IsNullOrWhiteSpace($Title) -or [string]::IsNullOrWhiteSpace($Artist)) { throw 'Song title and artist are required.' }
Write-Host 'Enter the Spotify sp_dc cookie for this one-time diagnostic. It will not be displayed or saved.' -ForegroundColor Yellow
$secureCookie = Read-Host 'sp_dc' -AsSecureString
$cookiePointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureCookie)
$cookie = $null

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src/OpenOverlay.Service/OpenOverlay.Service.csproj'
$requestId = [guid]::NewGuid().ToString('N')
$track = @{
    id = $(if ($TrackId) { $TrackId } else { "diagnostic-$requestId" })
    title = $Title.Trim()
    artists = @($Artist.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    album = ''
    durationMs = 0
}
try {
$cookie = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($cookiePointer)
if ([string]::IsNullOrWhiteSpace($cookie) -or $cookie -match '[\x00-\x1F\x7F;]') { throw 'The sp_dc value is empty or contains unsupported characters.' }
$request = @{ id = $requestId; method = 'lyrics.providers.checkAll'; params = @{ track = $track; spotifySessionCookie = $cookie } } | ConvertTo-Json -Depth 8 -Compress

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = 'dotnet'
$startInfo.Arguments = "run --project `"$project`" --no-launch-profile --verbosity quiet"
$startInfo.WorkingDirectory = $root
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
$startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo

    if (-not $process.Start()) { throw 'Could not start the .NET lyrics checker.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Close()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()

    $response = $null
    foreach ($line in ($stdout -split "`r?`n")) {
        if (-not $line.StartsWith('{')) { continue }
        try { $candidate = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($candidate.id -eq $requestId) { $response = $candidate; break }
    }
    if ($process.ExitCode -ne 0 -and -not $response) {
        if ($stderr) { Write-Host $stderr -ForegroundColor DarkGray }
        throw "The .NET checker exited with code $($process.ExitCode)."
    }
    if (-not $response) {
        if ($stderr) { Write-Host $stderr -ForegroundColor DarkGray }
        throw 'No provider comparison response was received. Check that the .NET 10 SDK is installed and the solution builds.'
    }
    if ($response.error) { throw $response.error.message }

    $results = @($response.result)
    Write-Host "`n$($Title.Trim()) — $($Artist.Trim())" -ForegroundColor Cyan
    $results | Select-Object displayName, outcome, syncLevel, lineCount | Format-Table -AutoSize
    foreach ($result in $results) {
        if ($result.preview.Count -gt 0) {
            Write-Host "[$($result.displayName)] $($result.outcome) / $($result.syncLevel)" -ForegroundColor Green
            $result.preview | ForEach-Object { Write-Host "  $_" }
        } elseif ($result.message) {
            Write-Host "[$($result.displayName)] $($result.outcome): $($result.message)" -ForegroundColor DarkGray
        }
    }
}
finally {
    if ($cookiePointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($cookiePointer) }
    if ($null -ne $cookie) { $cookie = $null }
    if ($null -ne $secureCookie) { $secureCookie.Dispose() }
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($process) { $process.Dispose() }
}
Write-Host "`nOnly other providers enabled in Settings are queried; Spotify uses only the sp_dc value entered for this run and does not require a stored session." -ForegroundColor DarkGray
