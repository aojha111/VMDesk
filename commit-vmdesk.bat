@echo off
setlocal
cd /d "%~dp0"

git add -A
if errorlevel 1 (
    echo Failed to stage changes.
    exit /b 1
)

git diff --cached --quiet
if not errorlevel 1 (
    echo No changes to commit.
    exit /b 0
)

git commit -m "fix: complete VMDesk desktop app and packaging"
if errorlevel 1 (
    echo Commit failed.
    exit /b 1
)

echo Commit created successfully.
git status --short --branch
endlocal
