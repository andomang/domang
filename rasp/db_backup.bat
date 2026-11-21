@echo off
echo ============================================
echo  Meta Search DB 수동 백업
echo ============================================
echo.

echo [1/2] 백업 실행 중...
ssh domang@192.168.0.24 "pg_dump -h localhost -U metauser -d meta > /home/domang/db_backup/meta_manual_%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%.sql 2>&1 && echo 'Backup OK' || echo 'Backup FAILED'"
echo.

echo [2/2] 백업 파일 확인...
ssh domang@192.168.0.24 "ls -lht /home/domang/db_backup/ | head -6"
echo.

echo ============================================
echo  백업 완료
echo ============================================
pause
