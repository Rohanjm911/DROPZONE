@echo off
title DROPZONE
cd /d "%~dp0"
if exist "%~dp0DropZone.exe" (
    start "" "%~dp0DropZone.exe" %*
) else if exist "%~dp0DropZone-Desktop-Windows-x64\DropZone.exe" (
    start "" "%~dp0DropZone-Desktop-Windows-x64\DropZone.exe" %*
) else (
    echo [ERROR] DropZone executable not found.
    pause
)
