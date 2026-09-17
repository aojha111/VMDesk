@echo off
setlocal EnableExtensions
title VMDesk - Commit and Push
cd /d "%~dp0"

echo.
echo ========================================
echo VMDesk commit and push
echo ========================================
echo Repository: %CD%
echo.

where git >nul 2>&1
if errorlevel 1 goto :git_missing

git rev-parse --show-toplevel >nul 2>&1
if errorlevel 1 goto :not_repo

echo [1/4] Staging changes...
git add -A
if errorlevel 1 goto :stage_failed

git config user.email "aojha111@users.noreply.github.com"
if errorlevel 1 goto :identity_failed

git diff --cached --quiet
if errorlevel 1 goto :commit_changes
echo No uncommitted changes found.
goto :push_changes

:commit_changes
echo [2/4] Creating commit...
git commit -m "fix: complete VMDesk desktop app and packaging"
if errorlevel 1 goto :commit_failed

:push_changes
echo [3/4] Pushing to origin/master...
git push -u origin master
if errorlevel 1 goto :push_failed

echo.
echo [4/4] SUCCESS: commit and push completed.
git status --short --branch
goto :finish

:git_missing
echo ERROR: Git is not installed or is not on PATH.
goto :fail

:not_repo
echo ERROR: This folder is not a Git repository.
goto :fail

:stage_failed
echo ERROR: Could not stage changes.
goto :fail

:identity_failed
echo ERROR: Could not configure the GitHub no-reply commit email.
goto :fail

:commit_failed
echo ERROR: Commit failed. Review the message above.
goto :fail

:push_failed
echo ERROR: Push failed. Check authentication, network access, and the remote message above.
goto :fail

:finish
echo.
pause
endlocal
exit /b 0

:fail
echo.
pause
endlocal
exit /b 1
