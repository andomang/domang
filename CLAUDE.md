## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. When the answer is under a Markdown heading, `graphify section "<heading>"` prints that section's text, so the .md need not be opened. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.

## 배포와 GitHub 릴리스 (항상 지킬 것)

사이트·게임 코드를 바꾸면 작업을 끝내기 전에 **매번** 아래를 준비하고, **올리기 전에 사용자에게 묻는다.**
내가 고친 경우뿐 아니라, 사용자가 직접 고치거나 새로 추가했다고 말하거나 `git status` 에 올리지 않은 코드 변경이 보이면 그것도 같은 순서로 한다.

1. `CHANGELOG.md` 맨 위에 새 버전을 적는다. 사용자가 알아볼 말로 바뀐 내용을 쓴다.
   - 작은 수정(버그 수정, 문구·색 변경): 세 번째 자리 `x.x.1`
   - 큰 수정(새 기능, 화면 추가): 두 번째 자리 `x.1.x`, 세 번째는 0
   - 더 큰 수정(구조·서버·저장 방식 변경): 첫 번째 자리 `1.x.x`, 나머지는 0
   바뀐 파일이 속한 저장소(아래 "프로젝트별 저장소")의 변경 기록에도 새 버전을 적는다.
2. `git status --short` 로 올라갈 파일을 보고, 비밀 정보(API 키·비밀번호·`appsettings*.json`·`.env`·키 파일)나
   내부 IP·SSH 정보가 든 개인 스크립트가 있으면 `.gitignore` 에 추가한다 (이미 올라간 것은 `git rm --cached`).
3. **사용자에게 묻는다**: "이렇게 올릴까요?" 와 함께
   - 버전 (domang `x.y.z` + 해당 저장소별 버전)
   - 릴리스에 들어갈 바뀐 내용 (CHANGELOG 그 버전 내용 그대로)
   - 올라갈 곳 (사이트 배포 · domang · pokemon_astera / drive / meta-search 중 해당)
   승인하면 4번, 고칠 점을 말하면 반영하고 다시 묻는다. 승인 없이 배포·push·릴리스하지 않는다.
4. `powershell -NoProfile -ExecutionPolicy Bypass -File C:\2026\meta\rasp\ship.ps1` 실행 (PowerShell 툴, 타임아웃 600000)
   → 사이트 배포 → 메인·게임 200 확인 → 커밋·push → 태그 = 버전인 GitHub 릴리스 (설명 = CHANGELOG 그 버전 내용)
   → 프로젝트별 저장소 동기화·릴리스. 배포나 확인이 실패하면 GitHub 에는 올리지 않고 멈춘다. 실패하면 원인을 고치고 다시 실행.
5. 끝나면 사용자에게 버전, 릴리스 링크(`https://github.com/andomang/<저장소>/releases/tag/버전`), 확인 결과를 알린다.

문서·주석만 바꾼 경우는 버전을 올리지 않고 `ship.ps1 -NoDeploy` 도 하지 않는다 (다음 버전에 같이 올라감).

## 프로젝트별 저장소 (항상 지킬 것)

domang 은 전체를 담고, 아래 부분은 `rasp\ship.ps1` 마지막 단계(`rasp\sync-repos.ps1`)가 각 저장소에도 나눠 올리고 릴리스를 만든다.

| 저장소 | 올라가는 파일 | 그 저장소의 변경 기록 |
|---|---|---|
| andomang/pokemon_astera | `pokemon/` (AI 인수인계 문서 제외) | `pokemon/CHANGELOG.md` |
| andomang/drive | `DriveApi.cs`, `Services/DriveService.cs`, `Pages/Drive*.cshtml(.cs)` | `rasp/repos/drive/CHANGELOG.md` (README 도 같은 폴더) |
| andomang/meta-search | `rasp/MetaSearch` 에서 드라이브 파일을 뺀 나머지 (`legacy/` 는 예전 WebForms 버전, 건드리지 않음) | `rasp/repos/meta-search/CHANGELOG.md` |

- 코드를 바꾸면 domang `CHANGELOG.md` 와 함께, 바뀐 파일이 속한 저장소의 변경 기록 맨 위에도 새 버전을 적는다 (버전은 저장소마다 따로, 규칙은 같음).
  그 저장소 변경 기록에 새 버전이 없으면 파일은 올라가지만 릴리스는 생기지 않는다.
- 드라이브 기능은 `DriveApi.cs` 에 둔다 (Program.cs 에 다시 넣지 않음). 새 드라이브 파일을 만들면 `sync-repos.ps1` 의 `$driveFiles` 에도 추가한다.
- 저장소 사본은 `C:\2026\repos\<이름>` 에 있다.

