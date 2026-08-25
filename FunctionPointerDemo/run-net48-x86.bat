@echo off
REM 在 Windows 侧直接运行，绕过 WSL2 interop 把 32 位 exe 拉成 64 位进程的限制
cd /d "%~dp0"
echo ============ .NET Framework 4.x (net48) - x86 ============
bin\Debug\net48\FunctionPointerDemo.exe
echo.
echo 说明: 请确认上方 "进程位数(Is64BitProcess)" 为 False，即真正 32 位进程。
pause
