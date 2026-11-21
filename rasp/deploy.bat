@echo off
echo [0/2] Copying game files (C:\2026\meta\pokemon -^> wwwroot\pokemon)...
REM Game source of truth is C:\2026\meta\pokemon. Only index.html and game\ are deployed.
REM Asset files are NOT deployed here; the server reads them from /mnt/ssd/pokemon on the Pi.
if not exist C:\2026\meta\rasp\MetaSearch\wwwroot\pokemon mkdir C:\2026\meta\rasp\MetaSearch\wwwroot\pokemon
copy /Y C:\2026\meta\pokemon\index.html C:\2026\meta\rasp\MetaSearch\wwwroot\pokemon\index.html >nul
robocopy C:\2026\meta\pokemon\game C:\2026\meta\rasp\MetaSearch\wwwroot\pokemon\game /MIR /NJH /NJS /NFL /NDL >nul
if %errorlevel% geq 8 (
    echo Game copy failed!
    pause
    exit /b 1
)

REM Bundle the game scripts into one precompressed file (fewer requests, smaller download)
node C:\2026\meta\rasp\bundle-pokemon.mjs C:\2026\meta\rasp\MetaSearch\wwwroot\pokemon
if %errorlevel% neq 0 (
    echo Bundle failed!
    pause
    exit /b 1
)

echo [1/2] Building...
REM Clean the output folder so files deleted from the project are not deployed again
if exist C:\2026\meta\rasp\publish rmdir /s /q C:\2026\meta\rasp\publish
dotnet publish C:\2026\meta\rasp\MetaSearch -c Release -r linux-arm64 --self-contained true -o C:\2026\meta\rasp\publish
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b 1
)

echo [2/2] Deploying...
"C:\Program Files\Git\usr\bin\bash.exe" C:\2026\meta\rasp\deploy.sh
if %errorlevel% neq 0 (
    echo Deploy failed!
    pause
    exit /b 1
)

echo Done!
pause
