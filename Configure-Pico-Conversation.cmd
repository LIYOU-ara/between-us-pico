@echo off
setlocal EnableExtensions DisableDelayedExpansion
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Hardware\Configure-Conversation.ps1" %*
exit /b %errorlevel%
