## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. When the answer is under a Markdown heading, `graphify section "<heading>"` prints that section's text, so the .md need not be opened. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.

## 배포와 GitHub 릴리스 (항상 지킬 것)

사이트·게임 코드를 바꾸면 작업을 끝내기 전에 **매번** 아래를 한다. 사용자가 따로 시키지 않아도, 묻지 않고 한다 (사용자가 맡김).

1. `CHANGELOG.md` 맨 위에 새 버전을 적는다. 사용자가 알아볼 말로 바뀐 내용을 쓴다.
   - 작은 수정(버그 수정, 문구·색 변경): 세 번째 자리 `x.x.1`
   - 큰 수정(새 기능, 화면 추가): 두 번째 자리 `x.1.x`, 세 번째는 0
   - 더 큰 수정(구조·서버·저장 방식 변경): 첫 번째 자리 `1.x.x`, 나머지는 0
2. `git status --short` 로 올라갈 파일을 보고, 비밀 정보(API 키·비밀번호·`appsettings*.json`·`.env`·키 파일)나
   내부 IP·SSH 정보가 든 개인 스크립트가 있으면 `.gitignore` 에 추가한다 (이미 올라간 것은 `git rm --cached`).
3. `powershell -NoProfile -ExecutionPolicy Bypass -File C:6\meta
3. `powershell -NoProfile -ExecutionPolicy Bypass -File C:6\meta
3. `powershell -NoProfile -ExecutionPolicy Bypass -File C:\2026\meta\rasp\ship.ps1` 실행
   → 사이트 배포 → 메인·게임 200 확인 → 커밋·push → 태그 = 버전인 GitHub 릴리스 (설명 = CHANGELOG 그 버전 내용).
   배포나 확인이 실패하면 GitHub 에는 올리지 않고 멈춘다. 실패하면 원인을 고치고 다시 실행.
4. 끝나면 사용자에게 버전, 릴리스 링크(`https://github.com/andomang/domang/releases/tag/버전`), 확인 결과를 알린다.

문서·주석만 바꾼 경우는 버전을 올리지 않고 `ship.ps1 -NoDeploy` 도 하지 않는다 (다음 버전에 같이 올라감).

