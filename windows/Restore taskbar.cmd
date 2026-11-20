@echo off
rem Brings the Windows taskbar back if Dynamic Island hid it and then closed unexpectedly.
echo Restarting Explorer so the taskbar comes back on every screen...
taskkill /f /im explorer.exe >nul 2>&1
start explorer.exe
echo.
echo Done. If the taskbar still hides itself, open Settings ^> Personalization ^> Taskbar ^> Taskbar behaviours
echo and turn off "Automatically hide the taskbar".
pause
