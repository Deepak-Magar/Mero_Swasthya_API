<#
.SYNOPSIS
  One command for local development: database up -> migrate -> seed -> run the API on :5000.

.DESCRIPTION
  * Docker available  -> `docker compose up -d` (postgres:16 on 5433, redis:7, minio).
  * Docker missing    -> a private PostgreSQL cluster in .localpg\ on port 5433 (user/password swc/swc),
                         created with the PostgreSQL binaries already installed on this machine. It never
                         touches a PostgreSQL service you already run on 5432.
  Then `dotnet run -- --seed` applies every module's migrations and the idempotent seed, and the API
  starts with the `http` launch profile (http://0.0.0.0:5000, Development).

.PARAMETER NoRun      Prepare the database only; do not start the API.
.PARAMETER StopDb     Stop the local cluster / compose services and exit.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\dev.ps1
#>
param(
    [switch]$NoRun,
    [switch]$StopDb
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'src\MeroSwasthya.Api'
$localPg = Join-Path $root '.localpg'
$port = 5433

function Find-PgBin {
    $cmd = Get-Command pg_ctl -ErrorAction SilentlyContinue
    if ($cmd) { return Split-Path -Parent $cmd.Source }
    $candidates = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_ctl.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
    if ($candidates) { return Split-Path -Parent $candidates[0].FullName }
    return $null
}

function Test-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    & docker info *> $null
    return ($LASTEXITCODE -eq 0)
}

function Start-LocalPostgres {
    $bin = Find-PgBin
    if (-not $bin) { throw 'Neither Docker nor PostgreSQL binaries were found. Install Docker Desktop or PostgreSQL 16+.' }
    $pgCtl = Join-Path $bin 'pg_ctl.exe'
    $psql = Join-Path $bin 'psql.exe'

    if (-not (Test-Path (Join-Path $localPg 'PG_VERSION'))) {
        Write-Host "Initialising local PostgreSQL cluster in $localPg ..."
        $pwFile = [IO.Path]::GetTempFileName()
        Set-Content -Path $pwFile -Value 'swc' -NoNewline -Encoding ascii
        & (Join-Path $bin 'initdb.exe') -D $localPg -U swc --pwfile=$pwFile -A scram-sha-256 -E UTF8 --locale=C | Out-Null
        Remove-Item $pwFile
    }

    & $pgCtl -D $localPg status *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Starting local PostgreSQL on port $port ..."
        & $pgCtl -D $localPg -o "-p $port" -l (Join-Path $localPg 'server.log') -w start | Out-Null
    }

    $env:PGPASSWORD = 'swc'
    $exists = & $psql -h 127.0.0.1 -p $port -U swc -d postgres -tAc "select 1 from pg_database where datname='swc'"
    if ($exists -ne '1') { & $psql -h 127.0.0.1 -p $port -U swc -d postgres -c 'create database swc' | Out-Null }
    Write-Host "PostgreSQL ready: Host=127.0.0.1;Port=$port;Database=swc;Username=swc"
}

$useDocker = Test-Docker

if ($StopDb) {
    if ($useDocker) { docker compose -f (Join-Path $root 'docker-compose.yml') down }
    elseif (Test-Path $localPg) { & (Join-Path (Find-PgBin) 'pg_ctl.exe') -D $localPg stop }
    return
}

if ($useDocker) {
    Write-Host 'Docker found: starting postgres, redis, minio with docker compose ...'
    docker compose -f (Join-Path $root 'docker-compose.yml') up -d --wait
} else {
    Write-Host 'Docker not available: using a local PostgreSQL cluster instead.'
    Start-LocalPostgres
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'

Write-Host 'Applying migrations and seed ...'
dotnet run --project $api --launch-profile http -- --seed
if ($LASTEXITCODE -ne 0) { throw 'Migration / seed failed.' }

if ($NoRun) { return }

Write-Host 'Starting API on http://0.0.0.0:5000 (Swagger: http://127.0.0.1:5000/swagger) ...'
dotnet run --project $api --launch-profile http
