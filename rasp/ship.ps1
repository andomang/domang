# ============================================================
# ship.ps1 - domang.org 배포 + GitHub 업로드 + 릴리스를 한 번에
#   1) CHANGELOG.md 맨 위 "## 버전" 에서 버전을 읽음 (이미 있는 태그면 중단 = 버전 올리기를 잊은 것)
#   2) 사이트 배포 (deploy.bat: 게임 복사·묶기 → 빌드 → 라즈베리파이 업로드)
#   3) 사이트가 열리는지 확인 (메인·게임 200)
#   4) 바뀐 파일 커밋·push (appsettings.json 이 끼면 중단)
#   5) GitHub 릴리스 생성: 태그 = 버전, 설명 = CHANGELOG 의 그 버전 내용
#   실행: powershell -NoProfile -ExecutionPolicy Bypass -File C:\2026\meta\rasp\ship.ps1 [-NoDeploy]
#   처음 한 번은 'gh auth login' 필요
# ============================================================
param([switch]$NoDeploy)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location 'C:\2026\meta'

# --- 1) 버전 ---
$log = Get-Content 'CHANGELOG.md' -Encoding UTF8
$head = $log | Where-Object { $_ -match '^## (\d+\.\d+\.\d+)' } | Select-Object -First 1
if (-not $head) { throw 'CHANGELOG.md 에서 "## 버전" 줄을 찾지 못했습니다.' }
$version = ([regex]::Match($head, '^## (\d+\.\d+\.\d+)')).Groups[1].Value
git fetch -q --tags
if (git tag --list $version) { throw "태그 $version 이 이미 있습니다. CHANGELOG.md 에 새 버전을 먼저 적어 주세요." }
# 릴리스 설명 = 이 버전 제목 다음 줄부터 다음 "## " 전까지
$start = [array]::IndexOf($log, $head)
$notes = @(); for ($i = $start + 1; $i -lt $log.Count; $i++) { if ($log[$i] -match '^## ') { break }; $notes += $log[$i] }
$notesText = ($notes -join "`n").Trim()
Write-Host "버전 $version"

$gh = @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $gh) { throw 'GitHub CLI(gh)가 없습니다.' }
& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "GitHub 로그인이 안 되어 있습니다. 'gh auth login' 후 다시 실행하세요." }

# --- 2) 배포 (deploy.bat 의 pause 는 입력이 없으면 바로 넘어감) ---
if (-not $NoDeploy) {
    Write-Host '[배포] 빌드하고 라즈베리파이에 올리는 중...'
    cmd /c 'C:\2026\meta\rasp\deploy.bat < NUL'
    if ($LASTEXITCODE -ne 0) { throw '배포에 실패했습니다. 위 메시지를 확인하세요. (GitHub 에는 올리지 않았습니다)' }
    # --- 3) 확인: 서비스가 다시 뜰 때까지 최대 60초 ---
    $ok = $false
    for ($t = 0; $t -lt 12 -and -not $ok; $t++) {
        Start-Sleep -Seconds 5
        try {
            $a = (Invoke-WebRequest 'https://domang.org/' -UseBasicParsing -TimeoutSec 15).StatusCode
            $b = (Invoke-WebRequest 'https://domang.org/pokemon' -UseBasicParsing -TimeoutSec 15).StatusCode
            $ok = ($a -eq 200 -and $b -eq 200)
        } catch { }
    }
    if (-not $ok) { throw '배포 후 사이트가 열리지 않습니다. 서버를 확인하세요. (GitHub 에는 올리지 않았습니다)' }
    Write-Host '[확인] 사이트 정상 (메인·게임 200)'
}

# --- 4) 커밋·push ---
git add .
if (git diff --cached --name-only | Where-Object { $_ -match 'appsettings\.json$' }) {
    git reset -q
    throw 'appsettings.json 이 올라가려고 합니다 (API 키). 중단했습니다.'
}
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    $msgFile = New-TemporaryFile
    $first = ($notes | Where-Object { $_ -match '^- ' } | Select-Object -First 1) -replace '^- ', ''
    Set-Content $msgFile -Encoding UTF8 -Value @("$version`: $first", '', $notesText, '', 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>')
    git commit -q -F $msgFile
    Remove-Item $msgFile
}
git push -q origin main
if ($LASTEXITCODE -ne 0) { throw "GitHub 에 올리지 못했습니다 (GitHub 에 이 PC 에 없는 변경이 있으면 'git pull --rebase' 후 다시). 릴리스는 만들지 않았습니다." }

# --- 5) 릴리스 ---
$notesFile = New-TemporaryFile
Set-Content $notesFile -Encoding UTF8 -Value $notesText
& $gh release create $version --repo andomang/domang --title "domang.org $version" --notes-file $notesFile --target (git rev-parse HEAD)
Remove-Item $notesFile
if ($LASTEXITCODE -ne 0) { throw '릴리스를 만들지 못했습니다.' }
Write-Host "[완료] 배포 · GitHub 업로드 · 릴리스 $version"
