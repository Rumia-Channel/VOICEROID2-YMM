@echo off
rem Build the fake aitalked.dll (test double for VOICEROID2-YMM verification).
rem Visual Studio is located via vswhere (no hardcoded paths).
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" goto found_vswhere
echo vswhere not found: %VSWHERE%
exit /b 1
:found_vswhere
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not "%VSDIR%"=="" goto found_vs
echo Visual Studio C++ toolchain not found
exit /b 1
:found_vs
call "%VSDIR%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
if not exist build mkdir build
cl /nologo /O2 /LD /EHsc fake.cpp /Fe:build\aitalked.dll /Fo:build\ /Fd:build\fake.pdb
if errorlevel 1 exit /b 1
echo built: %CD%\build\aitalked.dll
