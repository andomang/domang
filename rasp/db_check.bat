@echo off
echo ============================================
echo  Meta Search DB 상태 확인
echo ============================================
echo.

echo [1/4] PostgreSQL 서비스 상태...
ssh domang@192.168.0.24 "sudo systemctl is-active postgresql"
echo.

echo [2/4] 데이터베이스 연결 테스트...
ssh domang@192.168.0.24 "psql -h localhost -U metauser -d meta -c 'SELECT 1 AS connection_ok;'"
echo.

echo [3/4] 테이블 목록 + 행 수...
ssh domang@192.168.0.24 "psql -h localhost -U metauser -d meta -c \"SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;\""
ssh domang@192.168.0.24 "psql -h localhost -U metauser -d meta -c \"SELECT 'members' AS tbl, COUNT(*) AS cnt FROM members UNION ALL SELECT 'bbs', COUNT(*) FROM bbs UNION ALL SELECT 'user_ratings', COUNT(*) FROM user_ratings UNION ALL SELECT 'notifications', COUNT(*) FROM notifications UNION ALL SELECT 'searchhistory', COUNT(*) FROM searchhistory;\""
echo.

echo [4/4] 최근 백업 파일...
ssh domang@192.168.0.24 "ls -lht /home/domang/db_backup/ | head -6"
echo.

echo ============================================
echo  확인 완료
echo ============================================
pause
