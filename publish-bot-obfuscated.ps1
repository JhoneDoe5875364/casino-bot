# KingTigerBet 봇 난독화 배포 스크립트
#
# 순서: 빌드 → Obfuscar 난독화(Services.* 만) → 난독화 dll을 빌드출력에 덮기
#       → dotnet publish --no-build 로 "재컴파일 없이" 단일파일 번들.
# 사용:  powershell -ExecutionPolicy Bypass -File .\publish-bot-obfuscated.ps1 [출력폴더]
#        (출력폴더 생략 시 ..\publish 로 배포)
# 필요:  Obfuscar 전역도구 설치 —  dotnet tool install --global Obfuscar.GlobalTool
#
# 참고:  난독화 매핑(원래이름 ↔ 바뀐이름)은 obf_out\Mapping.txt 에 남는다.
#        배포엔 넣지 말고, 난독화된 스택트레이스를 되돌릴 때만 로컬에서 쓴다.

$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $proj

$rid = 'win-x64'
$bin = "bin\Release\net10.0-windows\$rid"
$out = if ($args.Count -ge 1) { $args[0] } else { "..\publish" }

Write-Host "[1/4] 빌드 서버 종료(파일 잠금 방지)..." -ForegroundColor Cyan
dotnet build-server shutdown | Out-Null

Write-Host "[2/4] 빌드 (Release/$rid)..." -ForegroundColor Cyan
dotnet build -c Release -r $rid -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

Write-Host "[3/4] 난독화 (Obfuscar)..." -ForegroundColor Cyan
obfuscar.console obfuscar.xml
if ($LASTEXITCODE -ne 0) { throw "난독화 실패 (obfuscar.console 이 PATH에 있는지 확인)" }
Copy-Item -Force "obf_out\PragmaticBot.dll" "$bin\PragmaticBot.dll"

Write-Host "[4/4] 단일파일 퍼블리시(--no-build) → $out ..." -ForegroundColor Cyan
dotnet publish -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true --no-build -o $out -nologo -v m
if ($LASTEXITCODE -ne 0) { throw "퍼블리시 실패" }

Write-Host "완료: 난독화된 단일파일이 '$out' 에 생성되었습니다." -ForegroundColor Green
Write-Host "      (역난독화 매핑: obf_out\Mapping.txt — 배포 금지, 로컬 보관)" -ForegroundColor DarkGray
