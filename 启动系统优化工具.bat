@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%SystemOptimizerLite.exe"
set "RUNTIME_URL=https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
set "WORK_DIR=%TEMP%\SystemOptimizerLiteRuntime"
set "INSTALLER=%WORK_DIR%\windowsdesktop-runtime-8-win-x64.exe"
set "LOG_FILE=%WORK_DIR%\install-runtime.log"
set "RUNTIME_CHECK_FILE=%WORK_DIR%\runtimes.txt"

if not exist "%APP_EXE%" (
    echo [ERROR] SystemOptimizerLite.exe was not found.
    echo Path: "%APP_EXE%"
    pause
    exit /b 1
)

call :HasDesktopRuntime
if "%HAS_RUNTIME%"=="1" goto StartApp

if /i "%~1"=="--install-runtime" goto InstallRuntime

echo [INFO] .NET 8 Desktop Runtime was not found.
echo [INFO] Requesting administrator permission to install it silently...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--install-runtime' -Verb RunAs"
if errorlevel 1 (
    echo [ERROR] Could not request administrator permission.
    echo [INFO] Opening the official Microsoft download page...
    start "" "%RUNTIME_URL%"
    pause
    exit /b 1
)
exit /b 0

:InstallRuntime
call :IsAdmin
if not "%IS_ADMIN%"=="1" (
    echo [ERROR] Administrator permission is required.
    pause
    exit /b 1
)

if not exist "%WORK_DIR%" mkdir "%WORK_DIR%" >nul 2>nul
echo [%DATE% %TIME%] Install started. > "%LOG_FILE%"
echo [INFO] Downloading .NET 8 Desktop Runtime...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri $env:RUNTIME_URL -OutFile $env:INSTALLER -UseBasicParsing" >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto DownloadFailed

if not exist "%INSTALLER%" goto DownloadFailed
for %%I in ("%INSTALLER%") do set "INSTALLER_SIZE=%%~zI"
if "%INSTALLER_SIZE%"=="" goto DownloadFailed
if %INSTALLER_SIZE% LSS 10000000 goto DownloadFailed

echo [INFO] Verifying Microsoft signature...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$sig=Get-AuthenticodeSignature -LiteralPath $env:INSTALLER; if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft') { exit 7 }" >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto SignatureFailed

echo [INFO] Installing .NET 8 Desktop Runtime silently...
"%INSTALLER%" /install /quiet /norestart /log "%LOG_FILE%"
set "INSTALL_EXIT=%ERRORLEVEL%"
if "%INSTALL_EXIT%"=="0" goto VerifyInstall
if "%INSTALL_EXIT%"=="3010" goto VerifyInstall
if "%INSTALL_EXIT%"=="1638" goto VerifyInstall
goto InstallFailed

:VerifyInstall
call :HasDesktopRuntime
if not "%HAS_RUNTIME%"=="1" goto InstallFailed
del /f /q "%INSTALLER%" >nul 2>nul
goto StartApp

:StartApp
start "" "%APP_EXE%"
exit /b 0

:DownloadFailed
echo [ERROR] Download failed or the downloaded file is incomplete.
echo [INFO] Log: "%LOG_FILE%"
echo [INFO] Opening the official Microsoft download page...
start "" "%RUNTIME_URL%"
pause
exit /b 1

:SignatureFailed
echo [ERROR] The runtime installer signature is invalid or not from Microsoft.
echo [INFO] The installer was not executed.
echo [INFO] Log: "%LOG_FILE%"
pause
exit /b 1

:InstallFailed
echo [ERROR] .NET 8 Desktop Runtime installation did not complete.
echo [INFO] Exit code: %INSTALL_EXIT%
echo [INFO] Log: "%LOG_FILE%"
echo [INFO] Opening the official Microsoft download page...
start "" "%RUNTIME_URL%"
pause
exit /b 1

:HasDesktopRuntime
set "HAS_RUNTIME=0"
if not exist "%WORK_DIR%" mkdir "%WORK_DIR%" >nul 2>nul
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    "%ProgramFiles%\dotnet\dotnet.exe" --list-runtimes > "%RUNTIME_CHECK_FILE%" 2>nul
    findstr /I /C:"Microsoft.WindowsDesktop.App 8." "%RUNTIME_CHECK_FILE%" >nul 2>nul && set "HAS_RUNTIME=1"
)
if "%HAS_RUNTIME%"=="1" exit /b 0
if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    "%ProgramFiles(x86)%\dotnet\dotnet.exe" --list-runtimes > "%RUNTIME_CHECK_FILE%" 2>nul
    findstr /I /C:"Microsoft.WindowsDesktop.App 8." "%RUNTIME_CHECK_FILE%" >nul 2>nul && set "HAS_RUNTIME=1"
)
if "%HAS_RUNTIME%"=="1" exit /b 0
for %%P in ("%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App" "%ProgramFiles(x86)%\dotnet\shared\Microsoft.WindowsDesktop.App") do (
    if exist "%%~P" (
        for /d %%V in ("%%~P\8.*") do if exist "%%~fV" set "HAS_RUNTIME=1"
    )
)
exit /b 0

:IsAdmin
set "IS_ADMIN=0"
net session >nul 2>nul
if "%ERRORLEVEL%"=="0" set "IS_ADMIN=1"
exit /b 0
