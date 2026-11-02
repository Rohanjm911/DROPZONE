@echo off
title DROPZONE Desktop (Portable)
cd /d "%~dp0"
dotnet DropZone.UI.dll %*
