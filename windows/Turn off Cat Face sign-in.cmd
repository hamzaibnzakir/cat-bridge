@echo off
rem Removes the Cat Face sign-in tile (your password and PIN are unaffected).
powershell -NoProfile -Command "Start-Process -FilePath \"%~dp0FaceHelper.exe\" -ArgumentList \"--uninstall\" -Verb RunAs -Wait"
echo Cat Face sign-in removed. You can close this window.
pause
