@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%SystemOptimizerLite.exe"
set "RUNTIME_URL=https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
set "WORK_DIR=%TEMP%\SystemOptimizerLiteRuntime"
set "INSTALLER=%WORK_DIR%\windowsdesktop-runtime-8-win-x64.exe"
set "LOG_FILE=%WORK_DIR%\install-runtime.log"
set "RUNTIME_CHECK_FILE=%WORK_DIR%\runtimes.txt"

call :Header
call :Step "Checking application files"
if not exist "%APP_EXE%" (
    call :Fail "SystemOptimizerLite.exe was not found"
    echo.
    echo Target: "%APP_EXE%"
    call :Hold
    exit /b 1
)
call :Ok "Application file is ready"

call :Step "Checking .NET 8 Desktop Runtime"
call :HasDesktopRuntime
if "%HAS_RUNTIME%"=="1" (
    call :Ok ".NET 8 Desktop Runtime is ready"
    goto StartApp
)
call :Warn ".NET 8 Desktop Runtime was not found"

if /i "%~1"=="--install-runtime" goto InstallRuntime

call :Step "Requesting administrator permission"
echo The runtime repair needs administrator permission.
echo Please choose Yes in the UAC prompt.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--install-runtime' -Verb RunAs"
if errorlevel 1 (
    call :Fail "Could not request administrator permission"
    echo Opening the official Microsoft download page...
    start "" "%RUNTIME_URL%"
    call :Hold
    exit /b 1
)
exit /b 0

:InstallRuntime
call :Step "Verifying administrator permission"
call :IsAdmin
if not "%IS_ADMIN%"=="1" (
    call :Fail "Administrator permission is required"
    call :Hold
    exit /b 1
)
call :Ok "Administrator permission confirmed"

if not exist "%WORK_DIR%" mkdir "%WORK_DIR%" >nul 2>nul
echo [%DATE% %TIME%] Install started. > "%LOG_FILE%"
echo Log: "%LOG_FILE%"

call :Step "Downloading Microsoft .NET 8 Desktop Runtime"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri $env:RUNTIME_URL -OutFile $env:INSTALLER -UseBasicParsing" >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto DownloadFailed

if not exist "%INSTALLER%" goto DownloadFailed
for %%I in ("%INSTALLER%") do set "INSTALLER_SIZE=%%~zI"
if "%INSTALLER_SIZE%"=="" goto DownloadFailed
if %INSTALLER_SIZE% LSS 10000000 goto DownloadFailed
call :Ok "Runtime installer downloaded"

call :Step "Verifying Microsoft signature"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$sig=Get-AuthenticodeSignature -LiteralPath $env:INSTALLER; if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft') { exit 7 }" >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto SignatureFailed
call :Ok "Digital signature is valid"

call :Step "Installing runtime in silent mode"
echo Please wait. This may take a few minutes.
"%INSTALLER%" /install /quiet /norestart /log "%LOG_FILE%"
set "INSTALL_EXIT=%ERRORLEVEL%"
if "%INSTALL_EXIT%"=="0" goto VerifyInstall
if "%INSTALL_EXIT%"=="3010" goto VerifyInstall
if "%INSTALL_EXIT%"=="1638" goto VerifyInstall
goto InstallFailed

:VerifyInstall
call :Step "Rechecking runtime environment"
call :HasDesktopRuntime
if not "%HAS_RUNTIME%"=="1" goto InstallFailed
del /f /q "%INSTALLER%" >nul 2>nul
call :Ok ".NET 8 Desktop Runtime is installed"
goto StartApp

:StartApp
call :Step "Starting System Optimizer Lite"
start "" "%APP_EXE%"
call :Ok "Application started"
exit /b 0

:DownloadFailed
call :Fail "Download failed or the downloaded file is incomplete"
echo Log: "%LOG_FILE%"
echo Opening the official Microsoft download page...
start "" "%RUNTIME_URL%"
call :Hold
exit /b 1

:SignatureFailed
call :Fail "Runtime installer signature is invalid"
echo The installer was not executed.
echo Log: "%LOG_FILE%"
call :Hold
exit /b 1

:InstallFailed
call :Fail ".NET 8 Desktop Runtime installation did not complete"
echo Exit code: %INSTALL_EXIT%
echo Log: "%LOG_FILE%"
echo Opening the official Microsoft download page...
start "" "%RUNTIME_URL%"
call :Hold
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

:Header
cls
echo ============================================================
echo  System Optimizer Lite - Environment Check and Repair
echo ============================================================
echo  App directory: "%APP_DIR%"
echo  Work folder:   "%WORK_DIR%"
echo.
exit /b 0

:Step
echo.
echo [ CHECK ] %~1...
exit /b 0

:Ok
echo [  OK   ] %~1
exit /b 0

:Warn
echo [ WARN  ] %~1
exit /b 0

:Fail
echo [ FAIL  ] %~1
exit /b 0

:Hold
echo.
echo Press any key to close this window.
pause >nul
exit /b 0
