<#
.SYNOPSIS
  curl walk over the running API, in the order the app uses it:
  health -> otp request -> otp verify -> pin (login, or set for a new account) -> me -> patients
  -> patient summary -> timeline -> codelists -> rules -> config.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1
  powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1 -BaseUrl http://192.168.1.20:5000/api/v1
#>
param(
    [string]$BaseUrl = 'http://127.0.0.1:5000/api/v1',
    [string]$Phone = '+9779801000001',
    [string]$Pin = '1234'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$script:failures = 0
$tmp = New-Item -ItemType Directory -Force -Path (Join-Path ([IO.Path]::GetTempPath()) 'swc-smoke')

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)

    $curlArgs = @('-s', '-S', '-X', $Method, "$BaseUrl$Path", '-w', "`n%{http_code}")
    if ($Token) { $curlArgs += @('-H', "Authorization: Bearer $Token") }
    if ($null -ne $Body) {
        $file = Join-Path $tmp 'body.json'
        [IO.File]::WriteAllText($file, ($Body | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding($false)))
        $curlArgs += @('-H', 'Content-Type: application/json; charset=utf-8', '--data-binary', "@$file")
    }
    $raw = & curl.exe @curlArgs
    $lines = @($raw -split "`n")
    $status = [int]$lines[-1]
    $json = ($lines[0..($lines.Length - 2)] -join "`n")
    return [pscustomobject]@{ Status = $status; Body = ($json | ConvertFrom-Json); Raw = $json }
}

function Step {
    param([string]$Name, $Response, [scriptblock]$Summary)
    $ok = $Response.Status -ge 200 -and $Response.Status -lt 300 -and $Response.Body.ok -eq $true
    if (-not $ok) { $script:failures++ }
    $tag = if ($ok) { 'PASS' } else { 'FAIL' }
    $detail = if ($ok) { & $Summary $Response.Body.data } else { $Response.Raw }
    Write-Host ('[{0}] {1,-34} HTTP {2}  {3}' -f $tag, $Name, $Response.Status, $detail)
}

Write-Host "Smoke test against $BaseUrl"
Write-Host ('-' * 100)

$r = Invoke-Api GET '/health'
Step 'GET  /health' $r { param($d) "status=$($d.status) database=$($d.database) modules=$($d.modules.Count)" }

$r = Invoke-Api POST '/auth/otp/request' @{ phone = $Phone }
Step 'POST /auth/otp/request' $r { param($d) "otpSentTo=$($d.otpSentTo) expiresInSec=$($d.expiresInSec) demoOtp=$($d.demoOtp)" }
$otp = if ($r.Body.data.demoOtp) { $r.Body.data.demoOtp } else { '123456' }

$r = Invoke-Api POST '/auth/otp/verify' @{ phone = $Phone; otp = $otp }
Step 'POST /auth/otp/verify' $r { param($d) "hasPin=$($d.hasPin) isNewUser=$($d.isNewUser) tempToken=$($d.tempToken.Substring(0, 16))..." }
$verify = $r.Body.data

if ($verify.hasPin) {
    $r = Invoke-Api POST '/auth/pin/login' @{ phone = $Phone; pin = $Pin }
    Step 'POST /auth/pin/login' $r { param($d) "user=$($d.user.id) role=$($d.user.role) refreshToken=$($d.refreshToken.Length) chars" }
} else {
    $r = Invoke-Api POST '/auth/pin/set' @{ pin = $Pin; name = 'Smoke Test' } $verify.tempToken
    Step 'POST /auth/pin/set' $r { param($d) "user=$($d.user.id) role=$($d.user.role)" }
}
$access = $r.Body.data.accessToken
$refresh = $r.Body.data.refreshToken

$r = Invoke-Api POST '/auth/refresh' @{ refreshToken = $refresh }
Step 'POST /auth/refresh' $r { param($d) "rotated=$($d.refreshToken -ne $refresh)" }
if ($r.Body.ok) { $access = $r.Body.data.accessToken }

$r = Invoke-Api GET '/me' $null $access
Step 'GET  /me' $r { param($d) "name=$($d.user.name) phone=$($d.user.phone) role=$($d.user.role)" }

$r = Invoke-Api GET '/patients' $null $access
Step 'GET  /patients' $r { param($d) "items=$($d.items.Count): " + (($d.items | ForEach-Object { "$($_.name) v$($_.version)" }) -join ', ') }
$patients = $r.Body.data.items

$ram = $patients | Where-Object { $_.id -eq 'p_a1a1a1a1-0000-4000-8000-000000000002' } | Select-Object -First 1
$target = if ($ram) { $ram } else { $patients | Select-Object -First 1 }
if ($target) {
    $r = Invoke-Api GET "/patients/$($target.id)" $null $access
    Step "GET  /patients/:id ($($target.name.Split(' ')[0]))" $r {
        param($d) "allergies=$($d.summary.allergies -join ',') problems=$(($d.summary.activeProblems | ForEach-Object { $_.code }) -join ',') visitCount=$($d.summary.visitCount)"
    }

    $r = Invoke-Api GET "/patients/$($target.id)/timeline?limit=50" $null $access
    Step 'GET  /patients/:id/timeline' $r { param($d) "items=$($d.items.Count) nextBefore=$($d.nextBefore)" }
}

$r = Invoke-Api GET '/codelists'
Step 'GET  /codelists' $r {
    param($d) $g = $d.items | Group-Object kind | ForEach-Object { "$($_.Name)=$($_.Count)" }; "version=$($d.version) " + ($g -join ' ')
}

$r = Invoke-Api GET '/rules'
Step 'GET  /rules' $r { param($d) "version=$($d.version) ancWeeks=$(($d.ancSchedule | ForEach-Object { $_.weekTarget }) -join ',') dangerSigns=$($d.dangerSigns.Count)" }

$r = Invoke-Api GET '/config'
Step 'GET  /config' $r { param($d) "smsMode=$($d.smsMode) otpDemo=$($d.otpDemo) aiSummaryEnabled=$($d.aiSummaryEnabled) rulesVersion=$($d.rulesVersion)" }

$r = Invoke-Api GET '/facilities/nearby?lat=28.03&lng=82.49&birthing=true&limit=3' $null $access
Step 'GET  /facilities/nearby' $r { param($d) (($d.items | ForEach-Object { "$($_.id) $($_.distanceKm)km" }) -join ', ') }

Write-Host ('-' * 100)
if ($script:failures -gt 0) {
    Write-Host "SMOKE FAILED: $($script:failures) step(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'SMOKE PASSED' -ForegroundColor Green
