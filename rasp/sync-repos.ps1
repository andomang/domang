# ============================================================
# sync-repos.ps1 - domang 저장소의 일부를 프로젝트별 저장소로 나눠 올리기 + 릴리스
#   ship.ps1 이 domang 을 push 한 뒤 부름 (따로 실행해도 됨)
#   * 올릴 파일 = domang 에서 git 이 관리하는 파일만 (비밀 설정·에셋은 .gitignore 라 자동 제외)
#   * 저장소 사본은 C:\2026\repos\<이름> 에 두고, 고른 파일과 똑같아지게 맞춘 뒤(지워진 파일도 지움) 커밋·push
#   * 저장소 CHANGELOG.md 맨 위 버전의 태그가 없으면 릴리스 생성 (태그 = 버전, 설명 = 그 버전 내용)
#   실행: powershell -NoProfile -ExecutionPolicy Bypass -File C:\2026\meta\rasp\sync-repos.ps1
# ============================================================
$ErrorActionPreference = 'Stop'
# git 출력(한글 경로)을 UTF-8 로 읽음 — core.quotepath=false 와 함께 써야 한글 파일명이 깨지지 않음
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\2026\meta'; $work = 'C:\2026\repos'
$gh = @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $gh) { throw 'GitHub CLI(gh)가 없습니다.' }
$domangVer = ([regex]::Match((Get-Content "$root\CHANGELOG.md" -Encoding UTF8 | Where-Object { $_ -match '^## \d' } | Select-Object -First 1), '\d+\.\d+\.\d+')).Value

# 드라이브 파일 (메타서치 쪽에서는 빠짐)
$driveFiles = @('rasp/MetaSearch/DriveApi.cs', 'rasp/MetaSearch/Services/DriveService.cs',
  'rasp/MetaSearch/Pages/Drive.cshtml', 'rasp/MetaSearch/Pages/Drive.cshtml.cs',
  'rasp/MetaSearch/Pages/DriveFiles.cshtml', 'rasp/MetaSearch/Pages/DriveFiles.cshtml.cs',
  'rasp/MetaSearch/Pages/DriveIndex.cshtml', 'rasp/MetaSearch/Pages/DriveIndex.cshtml.cs')
# 저장소별: 원본 경로, 앞에서 떼어 낼 경로, 뺄 파일(정규식), 덮어쓸 README·CHANGELOG 폴더, 건드리지 않을 폴더
$comps = @(
  @{ repo = 'pokemon_astera'; src = @('pokemon'); strip = 'pokemon/'; exclude = '^pokemon/AI_HANDOFF_PROMPT\.md$'; docs = $null; keep = @() },
  @{ repo = 'drive'; src = $driveFiles; strip = 'rasp/MetaSearch/'; exclude = '^$'; docs = 'rasp/repos/drive'; keep = @() },
  @{ repo = 'meta-search'; src = @('rasp/MetaSearch'); strip = 'rasp/MetaSearch/'; exclude = '^(' + (($driveFiles | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')$'; docs = 'rasp/repos/meta-search'; keep = @('legacy/') }
)

foreach ($c in $comps) {
  $dir = Join-Path $work $c.repo
  Write-Host "== $($c.repo)"
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $work | Out-Null; & $gh repo clone "andomang/$($c.repo)" $dir -- -q; if ($LASTEXITCODE -ne 0) { throw "$($c.repo) 저장소를 받지 못했습니다." } }
  # 사본은 매번 원본과 똑같이 맞추는 용도 → 지난번에 실패하고 남은 변경은 버림 (커밋된 것은 유지)
  git -C $dir rev-parse -q --verify HEAD *> $null
  if ($LASTEXITCODE -eq 0) { git -C $dir reset -q --hard HEAD; git -C $dir pull -q --rebase }
  # 올릴 파일 목록: 저장소 안 경로 → domang 안 경로
  $want = @{}
  Push-Location $root
  foreach ($f in (git -c core.quotepath=false ls-files -- $c.src)) { if ($f -notmatch $c.exclude) { $want[$f.Substring($c.strip.Length)] = $f } }
  if ($c.docs) { foreach ($f in (git -c core.quotepath=false ls-files -- $c.docs)) { $want[[IO.Path]::GetFileName($f)] = $f } }
  Pop-Location
  # 사본에서 이제 없는 파일 지움 (keep 폴더는 그대로)
  foreach ($f in (git -C $dir -c core.quotepath=false ls-files)) {
    if ($want.ContainsKey($f)) { continue }
    if ($c.keep | Where-Object { $f.StartsWith($_) }) { continue }
    Remove-Item -LiteralPath (Join-Path $dir $f) -Force -ErrorAction SilentlyContinue
  }
  # 고른 파일 복사
  foreach ($k in $want.Keys) {
    $dst = Join-Path $dir $k
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $want[$k]) $dst -Force
  }
  # 이 저장소의 버전
  $log = Get-Content (Join-Path $dir 'CHANGELOG.md') -Encoding UTF8
  $head = $log | Where-Object { $_ -match '^## (\d+\.\d+\.\d+)' } | Select-Object -First 1
  $ver = ([regex]::Match($head, '\d+\.\d+\.\d+')).Value
  git -C $dir add -A
  git -C $dir diff --cached --quiet
  if ($LASTEXITCODE -ne 0) {
    git -C $dir commit -q -m "$ver`: domang $domangVer 에서 동기화" -m 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
    git -C $dir push -q origin HEAD
    if ($LASTEXITCODE -ne 0) { throw "$($c.repo) 에 올리지 못했습니다." }
    Write-Host "  올림 ($ver)"
  } else { Write-Host '  바뀐 파일 없음' }
  # 릴리스: 그 버전 태그가 없을 때만
  git -C $dir fetch -q --tags
  if (-not (git -C $dir tag --list $ver)) {
    $start = [array]::IndexOf($log, $head); $notes = @()
    for ($i = $start + 1; $i -lt $log.Count; $i++) { if ($log[$i] -match '^## ') { break }; $notes += $log[$i] }
    $nf = New-TemporaryFile; [IO.File]::WriteAllText($nf, (($notes -join "`n").Trim()), (New-Object Text.UTF8Encoding($false)))   # BOM 없이
    & $gh release create $ver --repo "andomang/$($c.repo)" --title "$($c.repo) $ver" --notes-file $nf --target (git -C $dir rev-parse HEAD)
    Remove-Item $nf
    if ($LASTEXITCODE -ne 0) { throw "$($c.repo) 릴리스를 만들지 못했습니다." }
  } else { Write-Host "  릴리스 $ver 는 이미 있음" }
}
Write-Host '[완료] 프로젝트별 저장소 동기화'
