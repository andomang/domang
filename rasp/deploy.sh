#!/bin/bash
# 증분 배포: 지난 배포 이후 내용(해시)이 바뀐 파일만 전송
# 업로드가 끝까지 성공했을 때만 해시 기록을 갱신함
set -euo pipefail

# Windows에서 Unix 유틸리티(find, sort 등)가 먼저 잡히도록
export PATH="/usr/bin:$PATH"

PUBLISH="/c/2026/meta/rasp/publish"
HASH_FILE="/c/2026/meta/rasp/.deploy-hashes"
REMOTE="domang@192.168.45.7"
REMOTE_DIR="/home/domang/metasearch"
TMP_NEW="$(mktemp)"
TMP_LIST="$(mktemp)"
trap 'rm -f "$TMP_NEW" "$TMP_LIST"' EXIT

cd "$PUBLISH"

echo "Calculating file hashes..."
# Windows용 md5sum은 파일 이름 앞에 바이너리 표시 '*'를 붙이므로 제거 ("해시 경로" 형식으로 통일)
find . -type f -exec md5sum {} + | sed 's/^\([0-9a-f]*\) [ *]/\1 /' > "$TMP_NEW"
echo "Total files: $(wc -l < "$TMP_NEW")"

if [ -f "$HASH_FILE" ]; then
    # 경로별로 이전 해시와 비교: 새로 생겼거나 해시가 다른 파일만 목록에
    awk 'NR==FNR { old[$2] = $1; next } old[$2] != $1 { print $2 }' "$HASH_FILE" "$TMP_NEW" > "$TMP_LIST"
    # 이전엔 있었는데 지금 없는 파일
    DELETED=$(awk 'NR==FNR { now[$2] = 1; next } !($2 in now) { print $2 }' "$TMP_NEW" "$HASH_FILE")
else
    echo "First deploy (no hash record) - uploading everything"
    awk '{ print $2 }' "$TMP_NEW" > "$TMP_LIST"
    DELETED=""
fi

CHANGED_COUNT=$(wc -l < "$TMP_LIST")
DELETED_COUNT=0
[ -n "$DELETED" ] && DELETED_COUNT=$(echo "$DELETED" | wc -l)

if [ "$CHANGED_COUNT" -eq 0 ] && [ "$DELETED_COUNT" -eq 0 ]; then
    echo "No changes detected. Skipping deploy."
    exit 0
fi

echo "Changed/New: $CHANGED_COUNT files, Deleted: $DELETED_COUNT files"
# 바뀐 파일 일부를 보여줌 (확인용)
head -10 "$TMP_LIST" | sed 's/^/  + /'
[ "$CHANGED_COUNT" -gt 10 ] && echo "  ... (그 외 $((CHANGED_COUNT - 10))개)"

# 업로드 전에 전송할 파일이 전부 있는지 먼저 확인 (서비스를 멈추기 전에 실패하도록)
while IFS= read -r f; do
    [ -f "$f" ] || { echo "목록의 파일이 없습니다: $f"; exit 1; }
done < "$TMP_LIST"

ssh "$REMOTE" "sudo systemctl stop metasearch"
# 이 아래에서 무엇이 실패하든 서비스는 다시 켬 (사이트가 꺼진 채로 남지 않게)
trap 'rm -f "$TMP_NEW" "$TMP_LIST"; echo "실패 - 서비스를 다시 켭니다"; ssh "$REMOTE" "chmod +x $REMOTE_DIR/MetaSearch; sudo systemctl start metasearch"' ERR

if [ "$CHANGED_COUNT" -gt 0 ]; then
    echo "Uploading changed files..."
    tar czf - -T "$TMP_LIST" | ssh "$REMOTE" "cd $REMOTE_DIR && tar xzf -"
fi

if [ "$DELETED_COUNT" -gt 0 ]; then
    echo "Removing deleted files..."
    echo "$DELETED" | ssh "$REMOTE" "cd $REMOTE_DIR && xargs -r rm -f"
fi

# 바이너리 실행 권한 복구 (Windows tar는 Unix 실행 권한을 남기지 않음)
ssh "$REMOTE" "chmod +x $REMOTE_DIR/MetaSearch"

echo "Restarting service..."
ssh "$REMOTE" "sudo systemctl start metasearch"

# 여기까지 모두 성공했을 때만 기록 갱신
cp "$TMP_NEW" "$HASH_FILE"
echo "Deploy complete!"
