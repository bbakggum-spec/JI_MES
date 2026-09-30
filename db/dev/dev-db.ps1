# JI_MES 개발용 MariaDB 인스턴스 관리 (운영 DB와 분리)
#   - 포트 3307, 127.0.0.1 전용, root 비밀번호 없음 (개발 전용 — 운영/공유 금지)
#   - 데이터: db/dev/.devdata (Git 제외)
#   - 기존 운영 서비스(3306, bbakggum)는 건드리지 않음
#
# 사용법:  powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 <init|start|stop|status|apply|smoke|seed|reset>
#   init   데이터 폴더 생성 → 시작 → DDL 적용
#   apply  bbakggum_v2 를 지우고 DDL 재적용
#   smoke  DDL 적용 후 db/test/smoke_scenario.sql 실행 (오류 0건 확인)
#   seed   DDL 적용 후 db/dev/seed_dev.sql 실행 (화면 확인용 개발 데이터)
#   reset  중지 → 데이터 폴더 삭제 → init

param(
    [ValidateSet('init', 'start', 'stop', 'status', 'apply', 'smoke', 'seed', 'reset')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'
$NoSsl = '--skip-ssl-verify-server-cert'   # 로컬 무암호 연결 경고 억제

$Bin     = 'C:\Program Files\MariaDB 11.6\bin'
$Port    = 3307
$DevDir  = $PSScriptRoot
$DbDir   = Split-Path -Parent $DevDir
$Data    = Join-Path $DevDir '.devdata'
$LogFile = Join-Path $DevDir 'dev-db.log'
$Ddl     = Join-Path $DbDir 'bbakggum_v2_DDL_V3.sql'
$Smoke   = Join-Path $DbDir 'test\smoke_scenario.sql'
$Seed    = Join-Path $DevDir 'seed_dev.sql'

function Invoke-Client([string[]]$ClientArgs) {
    $ErrorActionPreference = 'Continue'
    & "$Bin\mariadb.exe" -h 127.0.0.1 -P $Port -u root $NoSsl --default-character-set=utf8mb4 @ClientArgs
    if ($LASTEXITCODE -ne 0) { throw "mariadb client exited with $LASTEXITCODE" }
}

function Test-Running {
    $ErrorActionPreference = 'Continue'
    & "$Bin\mariadb-admin.exe" -h 127.0.0.1 -P $Port -u root $NoSsl ping 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Start-Db {
    if (Test-Running) { Write-Host "이미 실행 중 (port $Port)"; return }
    if (-not (Test-Path $Data)) { throw "데이터 폴더 없음 — init 먼저 실행" }
    Start-Process -FilePath "$Bin\mariadbd.exe" `
        -ArgumentList "--defaults-file=`"$Data\my.ini`"", '--bind-address=127.0.0.1', '--console' `
        -WindowStyle Hidden -RedirectStandardError $LogFile | Out-Null
    for ($i = 0; $i -lt 30; $i++) {
        if (Test-Running) { Write-Host "시작됨 (port $Port)"; return }
        Start-Sleep -Milliseconds 500
    }
    throw "시작 실패 — $LogFile 확인"
}

function Stop-Db {
    if (-not (Test-Running)) { Write-Host '실행 중 아님'; return }
    $ErrorActionPreference = 'Continue'
    & "$Bin\mariadb-admin.exe" -h 127.0.0.1 -P $Port -u root $NoSsl shutdown 2>$null
    Write-Host '중지됨'
}

function Invoke-SqlFile([string]$Path) {
    $p = $Path -replace '\\', '/'
    $ErrorActionPreference = 'Continue'
    $out = & "$Bin\mariadb.exe" -h 127.0.0.1 -P $Port -u root $NoSsl --default-character-set=utf8mb4 --abort-source-on-error -e "source $p" 2>&1
    $out | Where-Object { $_ -notmatch 'ssl-verify-server-cert' } | ForEach-Object { "$_" }
    $errors = @($out | Where-Object { "$_" -match '^ERROR \d+' })
    if ($LASTEXITCODE -ne 0 -or $errors.Count -gt 0) { throw "SQL 실행 실패: $Path`n$($errors -join "`n")" }
}

function Apply-Ddl {
    Invoke-Client @('-e', 'DROP DATABASE IF EXISTS bbakggum_v2')
    Invoke-SqlFile $Ddl
    $ErrorActionPreference = 'Continue'
    $counts = & "$Bin\mariadb.exe" -h 127.0.0.1 -P $Port -u root $NoSsl -N -e `
        "SELECT CONCAT(table_type, ' ', COUNT(*)) FROM information_schema.tables WHERE table_schema='bbakggum_v2' GROUP BY table_type"
    Write-Host "DDL 적용 완료: $($counts -join ', ')"
}

switch ($Action) {
    'init' {
        if (Test-Path $Data) { throw "이미 초기화됨 — reset 사용" }
        & "$Bin\mariadb-install-db.exe" "--datadir=$Data" "--port=$Port"
        Start-Db
        Apply-Ddl
    }
    'start'  { Start-Db }
    'stop'   { Stop-Db }
    'status' { if (Test-Running) { Write-Host "실행 중 (port $Port)" } else { Write-Host '중지 상태' } }
    'apply'  { Start-Db; Apply-Ddl }
    'smoke'  {
        Start-Db; Apply-Ddl
        Invoke-SqlFile $Smoke
        Write-Host '스모크 시나리오 완료 (오류 없음)'
    }
    'seed'   {
        Start-Db; Apply-Ddl
        Invoke-SqlFile $Seed
        Write-Host '개발 시드 적용 완료'
    }
    'reset'  {
        Stop-Db
        Start-Sleep -Seconds 1
        if (Test-Path $Data) { Remove-Item -Recurse -Force $Data }
        & $PSCommandPath init
    }
}
