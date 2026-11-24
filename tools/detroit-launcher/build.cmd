@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the Windows C# compiler ^(csc.exe^).
  exit /b 1
)
if not exist "dist" mkdir "dist"
if not exist "dist\mod" mkdir "dist\mod"
"%CSC%" /nologo /target:winexe /optimize+ /out:"dist\DBH FSR-DLSS Launcher.exe" /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "Launcher.cs"
if errorlevel 1 exit /b 1
echo Built dist\DBH FSR-DLSS Launcher.exe
echo Copy the built add-ons (renodx-detroitbecomehuman*.addon64) and the two amd_fidelityfx_*_vk.dll files into dist\mod\ next to it.
