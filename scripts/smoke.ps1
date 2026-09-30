<#
.SYNOPSIS
  curl walk over the running API, in the order the app uses it:
  health -> otp request -> otp verify -> pin (login, or set for a new account) -> me -> patients
  -> patient summary -> timeline -> codelists -> rules -> config -> nearby, then the session-2 walk:
  patient shares Ram (grant) -> provider logs in -> redeem -> add visit -> presign / PUT / complete a
  document -> timeline + summary -> patient reads the audit log, then the session-3 walk on a throwaway
  profile of a separate smoke account (the seeded family stays as it is): register a pregnancy ->
  reminders scheduled -> red ANC contact (triage + nearest referral, its reminders cancelled) ->
  delivery (pregnancy closed, remaining reminders cancelled) -> timeline -> the seeded reminders ->
  the mock SMS outbox.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1
  powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1 -BaseUrl http://192.168.1.20:5000/api/v1
#>
param(
    [string]$BaseUrl = 'http://127.0.0.1:5000/api/v1',
    [string]$Phone = '+9779801000001',
    [string]$Pin = '1234',
    [string]$ProviderPhone = '+9779801000002',
    [string]$SmokePhone = '+9779801999001'
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

function Send-Bytes {
    param([string]$Url, [byte[]]$Bytes, [string]$ContentType)
    $file = Join-Path $tmp 'upload.bin'
    [IO.File]::WriteAllBytes($file, $Bytes)
    # Exactly what the app's upload worker does: PUT the bytes to uploadUrl with uploadHeaders.
    $status = & curl.exe -s -S -o NUL -w '%{http_code}' -X PUT -H "Content-Type: $ContentType" --data-binary "@$file" $Url
    return [int]$status
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

# ---- Session 2: grants, visits, documents, audit ------------------------------------------------
$ramId = 'p_a1a1a1a1-0000-4000-8000-000000000002'

$r = Invoke-Api POST '/grants' @{ patientId = $ramId; scope = 'append'; ttlMinutes = 10 } $access
Step 'POST /grants (patient shares Ram)' $r { param($d) "grant=$($d.grant.id) scope=$($d.grant.scope) qr=$($d.qrPayload.Substring(0, 12))..." }
$qr = $r.Body.data.qrPayload

$r = Invoke-Api POST '/auth/pin/login' @{ phone = $ProviderPhone; pin = $Pin }
Step 'POST /auth/pin/login (provider)' $r { param($d) "user=$($d.user.name) role=$($d.user.role) facility=$($d.user.facilityName)" }
$provider = $r.Body.data.accessToken

$r = Invoke-Api POST '/grants/redeem' @{ qrPayload = $qr } $provider
Step 'POST /grants/redeem' $r {
    param($d) "patient=$($d.patient.name) accessUntil=$($d.grant.accessUntil) timeline=$($d.timeline.Count) visits=$($d.summary.visitCount)"
}

$visitId = 'v_' + [guid]::NewGuid()
$visit = @{
    id = $visitId; visitAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
    chiefComplaintCode = 'CC_FOLLOW_UP'; vitals = @{ bpSys = 134; bpDia = 84; weightKg = 71.0 }
    diagnosisCodes = @('E11', 'I10'); notes = 'Smoke test visit'; advice = 'Continue medicines'
    followUpAt = (Get-Date).AddDays(30).ToString('yyyy-MM-dd'); referral = $null
    prescriptions = @(@{ id = 'rx_smoke_1'; drugCode = 'METFORMIN_500'; dose = '1 tab'; frequency = 'BD'; durationDays = 30 })
    supersedesId = $null
}
$r = Invoke-Api POST "/patients/$ramId/visits" $visit $provider
Step 'POST /patients/:id/visits' $r { param($d) "visit=$($d.visit.id) by=$($d.visit.providerName) drug=$($d.visit.prescriptions[0].drugName)" }

$docId = 'd_' + [guid]::NewGuid()
$jpeg = [byte[]](0xFF, 0xD8, 0xFF, 0xE0) + [byte[]](1..2000 | ForEach-Object { $_ % 256 }) + [byte[]](0xFF, 0xD9)
$r = Invoke-Api POST '/documents/presign' @{
    id = $docId; patientId = $ramId; type = 'lab'; title = 'Smoke test lab report'
    takenAt = (Get-Date).ToString('yyyy-MM-dd'); contentType = 'image/jpeg'; sizeBytes = $jpeg.Length
} $provider
Step 'POST /documents/presign' $r { param($d) "status=$($d.document.status) uploadUrl=$(([Uri]$d.uploadUrl).GetLeftPart('Path'))" }

$putStatus = Send-Bytes $r.Body.data.uploadUrl $jpeg 'image/jpeg'
$ok = $putStatus -ge 200 -and $putStatus -lt 300
if (-not $ok) { $script:failures++ }
Write-Host ('[{0}] {1,-34} HTTP {2}  {3} bytes' -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), 'PUT  uploadUrl', $putStatus, $jpeg.Length)

$r = Invoke-Api POST "/documents/$docId/complete" $null $provider
Step 'POST /documents/:id/complete' $r { param($d) "status=$($d.document.status) version=$($d.document.version) downloadUrl=$([bool]$d.document.downloadUrl)" }

$r = Invoke-Api GET "/patients/$ramId/timeline?limit=5" $null $provider
Step 'GET  /patients/:id/timeline' $r { param($d) (($d.items | ForEach-Object { "$($_.kind): $($_.title)" }) -join ' | ') }

$r = Invoke-Api GET "/patients/$ramId" $null $provider
Step 'GET  /patients/:id (summary)' $r {
    param($d) $s = $d.summary
    "visits=$($s.visitCount) meds=$(($s.currentMedicines | ForEach-Object { $_.drugCode }) -join ',') lastBP=$($s.lastVitals.bpSys)/$($s.lastVitals.bpDia)"
}

$r = Invoke-Api GET "/patients/$ramId/audit?limit=6" $null $access
Step 'GET  /patients/:id/audit (owner)' $r { param($d) (($d.items | ForEach-Object { $_.action }) -join ', ') }

# ---- Session 3: pregnancy -> red contact -> delivery -> reminders ---------------------------------
# A separate account owns a fresh profile on every run, so Sita's seeded pregnancy is never touched.
$r = Invoke-Api POST '/auth/otp/request' @{ phone = $SmokePhone }
$otp = if ($r.Body.data.demoOtp) { $r.Body.data.demoOtp } else { '123456' }
$r = Invoke-Api POST '/auth/otp/verify' @{ phone = $SmokePhone; otp = $otp }
if ($r.Body.data.hasPin) {
    $r = Invoke-Api POST '/auth/pin/login' @{ phone = $SmokePhone; pin = $Pin }
} else {
    $r = Invoke-Api POST '/auth/pin/set' @{ pin = $Pin; name = 'Smoke Test' } $r.Body.data.tempToken
}
Step 'smoke account (otp + pin)' $r { param($d) "user=$($d.user.id) phone=$($d.user.phone) role=$($d.user.role)" }
$smoke = $r.Body.data.accessToken

$motherId = 'p_' + [guid]::NewGuid()
$r = Invoke-Api POST '/patients' @{
    id = $motherId; name = 'Smoke Mother'; sex = 'female'; dob = '1998-05-14'; emergencyContactPhone = '+9779801999002'
} $smoke
Step 'POST /patients (smoke mother)' $r { param($d) "patient=$($d.patient.id) emergencyContactPhone=$($d.patient.emergencyContactPhone)" }

$r = Invoke-Api POST '/grants' @{ patientId = $motherId; scope = 'append'; ttlMinutes = 10 } $smoke
$r = Invoke-Api POST '/grants/redeem' @{ qrPayload = $r.Body.data.qrPayload } $provider
Step 'POST /grants + /grants/redeem' $r { param($d) "patient=$($d.patient.name) scope=$($d.grant.scope) pregnancy=$([bool]$d.pregnancy)" }

# LMP 200 days ago = week 28: contacts 1-3 are past, contact 4 (week 30) is due in 10 days.
$today = (Get-Date).ToUniversalTime().Date
$pregnancyId = 'pg_' + [guid]::NewGuid()
$r = Invoke-Api POST "/patients/$motherId/pregnancies" @{
    id = $pregnancyId; lmp = $today.AddDays(-200).ToString('yyyy-MM-dd'); edd = $null
    gravida = 2; para = 1; riskFactors = @(); birthPlan = $null
} $provider
Step 'POST /patients/:id/pregnancies' $r {
    param($d) "pregnancy=$($d.pregnancy.id) edd=$($d.pregnancy.edd) week=$([int][math]::Floor($d.pregnancy.gestationalAgeDays / 7)) risk=$($d.pregnancy.riskLevel) contacts=$($d.ancContacts.Count) next=#$($d.pregnancy.nextContact.contactNo) due $($d.pregnancy.nextContact.dueAt)"
}

$r = Invoke-Api GET "/patients/$motherId/reminders?limit=200" $null $provider
Step 'GET  /patients/:id/reminders' $r {
    param($d) $g = $d.items | Group-Object kind | ForEach-Object { "$($_.Name)=$($_.Count)" }
    "items=$($d.items.Count) ($($g -join ' ')) first: $($d.items[0].dueAt) -> $($d.items[0].recipientRole) | $($d.items[0].messageEn)"
}
$before = $r.Body.data.items.Count

# The A.4 example: BP 150/95 with severe headache -> red, refer now.
$r = Invoke-Api PUT "/pregnancies/$pregnancyId/contacts/4" @{
    findings = @{ weightKg = 58; bpSys = 150; bpDia = 95; fundalHeightCm = 29; fhrBpm = 142; hbGdl = 9.2; urineProtein = 'trace'; ifaGiven = $true; fetalMovement = 'normal' }
    dangerSigns = @('SEVERE_HEADACHE_BLURRED_VISION')
    referral = @{ facilityId = 'f_0002'; facilityName = 'Rapti Provincial Hospital'; reason = 'Suspected pre-eclampsia'; urgency = 'urgent' }
} $provider
Step 'PUT  /pregnancies/:id/contacts/4' $r {
    param($d) "triage=$($d.ancContact.triageLevel) reasons=[$($d.ancContact.triageReasons -join '; ')] nearestReferral=$($d.nearestReferral.name) ($($d.nearestReferral.distanceKm) km)"
}

$r = Invoke-Api GET "/patients/$motherId/reminders?limit=200" $null $provider
Step 'GET  /patients/:id/reminders' $r { param($d) "items=$($d.items.Count) (was $before; contact 4's pending reminders cancelled)" }

$r = Invoke-Api POST "/pregnancies/$pregnancyId/delivery" @{
    id = 'dl_' + [guid]::NewGuid(); deliveredAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
    place = 'hospital'; mode = 'normal'; outcome = 'live_birth'; babyWeightKg = 2.9; babySex = 'female'; complications = @()
} $provider
Step 'POST /pregnancies/:id/delivery' $r {
    param($d) "delivery=$($d.delivery.id) place=$($d.delivery.place) outcome=$($d.delivery.outcome) pregnancy.status=$($d.pregnancy.status)"
}

$r = Invoke-Api GET "/pregnancies/$pregnancyId" $null $smoke
Step 'GET  /pregnancies/:id (owner)' $r {
    param($d) "status=$($d.pregnancy.status) contacts=$($d.ancContacts.Count) (recorded only) delivery=$([bool]$d.delivery) reminders=$($d.reminders.Count)"
}

$r = Invoke-Api GET "/patients/$motherId/timeline?limit=10" $null $smoke
Step 'GET  /patients/:id/timeline' $r { param($d) (($d.items | ForEach-Object { "$($_.kind)$(if ($_.badge) { "[$($_.badge)]" }): $($_.title)" }) -join ' | ') }

$sitaId = 'p_a1a1a1a1-0000-4000-8000-000000000001'
$r = Invoke-Api GET "/patients/$sitaId/reminders?limit=200" $null $access
Step 'GET  /patients/Sita/reminders' $r {
    param($d) $g = $d.items | Group-Object status | ForEach-Object { "$($_.Name)=$($_.Count)" }
    $sent = $d.items | Where-Object { $_.status -eq 'sent' } | Select-Object -First 1
    "items=$($d.items.Count) ($($g -join ' ')) sent: $($sent.sentAt) -> $($sent.recipientPhone) | $($sent.messageNp)"
}

$r = Invoke-Api GET "/patients/$ramId/reminders?limit=200" $null $access
Step 'GET  /patients/Ram/reminders' $r {
    param($d) $last = $d.items | Select-Object -Last 1
    "items=$($d.items.Count) latest: $($last.kind) $($last.dueAt) $($last.status) | $($last.messageEn)"
}

# Development + Features:SmsMode=mock only; anywhere else the outbox is not mapped.
$r = Invoke-Api GET '/dev/sms.json'
if ($r.Status -eq 404) {
    Write-Host ('[SKIP] {0,-34} HTTP 404  mock SMS outbox is not mapped on this server' -f 'GET  /dev/sms.json')
} else {
    Step 'GET  /dev/sms.json' $r { param($d) "items=$($d.items.Count) newest: $($d.items[0].sentAt) -> $($d.items[0].to) | $($d.items[0].text)" }
}

Write-Host ('-' * 100)
if ($script:failures -gt 0) {
    Write-Host "SMOKE FAILED: $($script:failures) step(s) failed" -ForegroundColor Red
    exit 1
}
Write-Host 'SMOKE PASSED' -ForegroundColor Green
