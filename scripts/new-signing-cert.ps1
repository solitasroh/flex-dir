#Requires -Version 7
# 사내 배포용 자체 서명 코드서명 인증서를 만든다 (사용자 결정 2026-08-12).
#
# **이것이 무엇을 고치고 무엇을 못 고치는지 먼저 읽는다.**
#
#   고친다  : Authenticode 서명이 붙는다. 신뢰가 깔린 기계에서 UAC·속성 대화상자가
#             '알 수 없는 게시자' 대신 이 인증서의 이름을 보여준다. vpk 의
#             "472 file(s) will not be signed" 경고가 사라진다.
#   못 고친다: **SmartScreen 은 그대로일 수 있다.** SmartScreen 은 신뢰 사슬이 아니라
#             마이크로소프트가 가진 평판으로 돈다. 사내에서만 신뢰하는 인증서는 그쪽에
#             아무 평판이 없다. GitHub 에서 받은 파일에는 Mark-of-the-Web 이 붙어
#             SmartScreen 검사를 타므로, 그 경로에서는 여전히 경고가 날 수 있다.
#             사내 공유 폴더나 Intune 으로 돌리면 MOTW 가 붙지 않아 이 문제를 피한다.
#
# 공개 CA 인증서(Azure Trusted Signing·OV)와 갈리는 지점이 정확히 저기다. 외부 배포가
# 생기면 그때 다시 판단한다.
#
# **인증서를 잃으면 되돌릴 수 없다.** 이미 배포한 신뢰가 새 인증서와 맞지 않아 사내
# 기계를 전부 다시 돌아야 한다. -BackupPassword 로 .pfx 를 반드시 남기고, 그 파일은
# 저장소가 아니라 안전한 곳에 둔다 (.gitignore 가 signing/ 을 막는다).

[CmdletBinding()]
param(
    # 사용자에게 '게시자' 로 보이는 이름이다.
    [string]$Subject = 'CN=Rootech, O=Rootech, C=KR',

    # 만료되면 그날부터 새 서명을 못 한다. 이미 서명한 것은 타임스탬프가 있으면 계속 유효하다.
    [int]$Years = 5,

    # 주면 .pfx 를 함께 낸다. 안 주면 개인 키가 이 기계의 인증서 저장소에만 산다.
    [string]$BackupPassword
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'signing'

New-Item -ItemType Directory -Force $outDir | Out-Null

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears($Years)

Write-Host "인증서를 만들었다." -ForegroundColor Green
Write-Host "  주체    : $($cert.Subject)"
Write-Host "  지문    : $($cert.Thumbprint)"
Write-Host "  만료    : $($cert.NotAfter.ToString('yyyy-MM-dd'))"

# 공개 인증서. 사내 기계가 신뢰하려면 이 파일을 신뢰할 수 있는 루트 인증 기관에 넣는다.
# 자체 서명이라 이것 자체가 루트다 — 중간 인증서가 없다.
$cer = Join-Path $outDir 'flex-dir-public.cer'
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
Write-Host "  공개    : $cer"

if ($BackupPassword) {
    $pfx = Join-Path $outDir 'flex-dir-signing.pfx'
    $secure = ConvertTo-SecureString $BackupPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure -Force | Out-Null
    Write-Host "  백업    : $pfx  (저장소 밖 안전한 곳으로 옮긴다)" -ForegroundColor Yellow
}
else {
    Write-Host "  백업    : 없음 — -BackupPassword 를 주면 .pfx 를 낸다" -ForegroundColor Yellow
}

Write-Host ''
Write-Host '다음:' -ForegroundColor Cyan
Write-Host "  1. 서명해 굽는다 :  ./scripts/pack.ps1 -SignThumbprint $($cert.Thumbprint)"
Write-Host '  2. 사내 신뢰 배포 :  GPO — 컴퓨터 구성 > 정책 > Windows 설정 > 보안 설정 >'
Write-Host '                       공개 키 정책 > 신뢰할 수 있는 루트 인증 기관 에'
Write-Host "                       $cer 를 넣는다 (신뢰할 수 있는 게시자 에도 함께 넣으면"
Write-Host '                       정책이 막는 경로에서 프롬프트가 하나 더 준다)'
Write-Host '  3. 이 기계에서만 시험하려면:'
Write-Host "                       Import-Certificate -FilePath $cer ``"
Write-Host '                         -CertStoreLocation Cert:\LocalMachine\Root   (관리자 권한)'
