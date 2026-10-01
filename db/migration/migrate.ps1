# 구 DB → bbakggum_v2 이관 실행 (설계 §25)
#   db/migration/*.sql 을 이름 순서로 실행: 00 설정 → 01 준비 → 10 기준정보 → 20 공정·표준 → 30 수주 → 40 작업 → 50 품질 → 60 출하 → 90 검증
#   원본 = 같은 서버의 bbakggum_legacy (구 DB 덤프를 이름만 바꿔 복원한 것). 운영 bbakggum 은 읽지도 쓰지도 않는다.
#   검증(90)에 FAIL 이 있으면 오류로 끝난다. 결과: bbakggum_mig.verify_result / bbakggum_mig.issue
#
# 사용법:
#   개발:  powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 migrate [-Fresh]   (이 스크립트를 3307 로 호출)
#   전환:  powershell -ExecutionPolicy Bypass -File db\migration\migrate.ps1 -Server <호스트> -Port <포트> -User <계정> -AskPassword
param(
    [string]$Server = '127.0.0.1',
    [int]$Port = 3307,
    [string]$User = 'root',
    [switch]$AskPassword,
    [string]$Bin = 'C:\Program Files\MariaDB 11.6\bin'
)

$ErrorActionPreference = 'Stop'
$client = Join-Path $Bin 'mariadb.exe'
if (-not (Test-Path $client)) { throw "mariadb 클라이언트 없음: $client (-Bin 으로 지정)" }

$hadPwd = Test-Path Env:MYSQL_PWD
if ($AskPassword) {
    # 비밀번호는 화면·명령행에 남기지 않고 이 실행 동안만 환경변수로 클라이언트에 전달
    $secure = Read-Host "$User@${Server}:$Port 비밀번호" -AsSecureString
    $env:MYSQL_PWD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

try {
    $files = Get-ChildItem $PSScriptRoot -Filter '*.sql' | Sort-Object Name
    foreach ($f in $files) {
        Write-Host "── $($f.Name)"
        $p = $f.FullName -replace '\\', '/'
        $ErrorActionPreference = 'Continue'
        $out = & $client -h $Server -P $Port -u $User --skip-ssl-verify-server-cert --default-character-set=utf8mb4 --abort-source-on-error -e "source $p" 2>&1
        $ErrorActionPreference = 'Stop'
        $out | Where-Object { $_ -notmatch 'ssl-verify-server-cert' } | ForEach-Object { "$_" }
        $errors = @($out | Where-Object { "$_" -match '^ERROR \d+' })
        if ($LASTEXITCODE -ne 0 -or $errors.Count -gt 0) { throw "이관 실패: $($f.Name)`n$($errors -join "`n")" }
    }
    Write-Host '이관 완료 (검증 통과)'
}
finally {
    if ($AskPassword -and -not $hadPwd) { Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue }
}
