@echo off
echo ============================================
echo  Meta Search 사이트 상태 확인
echo ============================================
echo.

echo [1/4] MetaSearch 서비스 상태...
ssh domang@192.168.0.24 "sudo systemctl is-active metasearch"
echo.

echo [2/4] HTTP 응답 확인 (localhost:5000)...
ssh domang@192.168.0.24 "curl -s -o /dev/null -w 'HTTP %%{http_code}\n' http://localhost:5000/"
echo.

echo [3/4] 최근 에러 로그...
ssh domang@192.168.0.24 "sudo journalctl -u metasearch --since '30 min ago' --no-pager --priority=err 2>/dev/null | tail -10 || echo '에러 없음'"
echo.

echo [4/4] 디스크 사용량...
ssh domang@192.168.0.24 "df -h / | tail -1"
echo.

echo ============================================
echo  확인 완료
echo ============================================
pause
