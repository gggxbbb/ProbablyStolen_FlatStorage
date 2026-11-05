@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Workshop.ps1" %*
set "flatStoragePublishExit=%ERRORLEVEL%"
pause
exit /b %flatStoragePublishExit%
