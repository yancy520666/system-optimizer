@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%SystemOptimizerLite.exe"
set "RUNTIME_URL=https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
set "WORK_DIR=%TEMP%\SystemOptimizerLiteRuntime"
set "INSTALLER=%WORK_DIR%\windowsdesktop-runtime-8-win-x64.exe"
set "DOWNLOAD_FILE=%INSTALLER%.download"
set "LOG_FILE=%WORK_DIR%\install-runtime.log"
set "RUNTIME_CHECK_FILE=%WORK_DIR%\runtimes.txt"
set "RUNTIME_INSTALLED_THIS_RUN=0"

call :Header
call :Step "检查软件文件"
if not exist "%APP_EXE%" (
    call :Fail "未找到 SystemOptimizerLite.exe"
    echo 目标位置："%APP_EXE%"
    call :Hold
    exit /b 1
)
call :Ok "软件文件已就绪"

call :Step "检查 .NET 8 Desktop Runtime"
call :HasDesktopRuntime
if "%HAS_RUNTIME%"=="1" (
    call :Ok "已检测到 .NET 8 Desktop Runtime"
    goto StartApp
)
call :Warn "未检测到 .NET 8 Desktop Runtime，将自动修复"

if /i "%~1"=="--install-runtime" goto InstallRuntime

call :Step "请求管理员权限"
echo 运行环境修复需要管理员权限。请在系统弹窗中选择“是”。
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--install-runtime' -Verb RunAs"
if errorlevel 1 (
    call :Fail "无法请求管理员权限"
    start "" "%RUNTIME_URL%"
    call :Hold
    exit /b 1
)
exit /b 0

:InstallRuntime
call :Step "确认管理员权限"
call :IsAdmin
if not "%IS_ADMIN%"=="1" (
    call :Fail "需要管理员权限才能安装运行环境"
    call :Hold
    exit /b 1
)
call :Ok "管理员权限已确认"

if not exist "%WORK_DIR%" mkdir "%WORK_DIR%" >nul 2>nul
echo [%DATE% %TIME%] Runtime repair started. > "%LOG_FILE%"
echo 日志位置："%LOG_FILE%"

call :Step "下载 Microsoft .NET 8 Desktop Runtime"
echo 正在开始下载必要运行环境，请保持网络连接。
echo 下方为实时下载百分比；中断或校验失败时不会使用不完整文件。
call :DownloadRuntime
if errorlevel 1 goto DownloadFailed

if not exist "%INSTALLER%" goto DownloadFailed
for %%I in ("%INSTALLER%") do set "INSTALLER_SIZE=%%~zI"
if "%INSTALLER_SIZE%"=="" goto DownloadFailed
if %INSTALLER_SIZE% LSS 10000000 goto DownloadFailed
call :Ok ".NET 8 Desktop Runtime 已成功下载，正在验证签名"

call :Step "验证 Microsoft 数字签名"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$sig=Get-AuthenticodeSignature -LiteralPath $env:INSTALLER; if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft') { exit 7 }" >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto SignatureFailed
call :Ok "数字签名有效"

call :Step "静默安装运行环境"
echo 正在安装，请稍候。这通常需要几分钟。
"%INSTALLER%" /install /quiet /norestart /log "%LOG_FILE%"
set "INSTALL_EXIT=%ERRORLEVEL%"
if "%INSTALL_EXIT%"=="0" goto VerifyInstall
if "%INSTALL_EXIT%"=="3010" goto VerifyInstall
if "%INSTALL_EXIT%"=="1638" goto VerifyInstall
goto InstallFailed

:VerifyInstall
call :Step "复检运行环境"
call :HasDesktopRuntime
if not "%HAS_RUNTIME%"=="1" goto InstallFailed
del /f /q "%INSTALLER%" >nul 2>nul
set "RUNTIME_INSTALLED_THIS_RUN=1"
call :Ok ".NET 8 Desktop Runtime 已成功安装"
goto StartApp

:StartApp
if "%RUNTIME_INSTALLED_THIS_RUN%"=="1" (
    echo.
    echo 必要运行环境已准备完成。按 Enter 打开 System Optimizer Lite...
    set /p "OPEN_CONFIRM="
)
call :Step "启动 System Optimizer Lite"
start "" "%APP_EXE%"
call :Ok "软件已启动"
exit /b 0

:DownloadRuntime
where curl.exe >nul 2>nul
if errorlevel 1 (
    call :Fail "当前 Windows 缺少 curl，无法显示实时下载进度"
    echo 请通过微软下载页安装 .NET 8 Desktop Runtime 后重新运行。
    exit /b 1
)
del /f /q "%DOWNLOAD_FILE%" >nul 2>nul
echo [%DATE% %TIME%] Download started. >> "%LOG_FILE%"
curl.exe --location --fail --show-error --retry 2 --retry-delay 2 --connect-timeout 20 --output "%DOWNLOAD_FILE%" "%RUNTIME_URL%"
if errorlevel 1 (
    echo [%DATE% %TIME%] Download failed. >> "%LOG_FILE%"
    del /f /q "%DOWNLOAD_FILE%" >nul 2>nul
    exit /b 1
)
if not exist "%DOWNLOAD_FILE%" exit /b 1
for %%I in ("%DOWNLOAD_FILE%") do set "DOWNLOAD_SIZE=%%~zI"
if "%DOWNLOAD_SIZE%"=="" exit /b 1
if %DOWNLOAD_SIZE% LSS 10000000 exit /b 1
move /y "%DOWNLOAD_FILE%" "%INSTALLER%" >nul
if errorlevel 1 exit /b 1
echo [%DATE% %TIME%] Download completed: %DOWNLOAD_SIZE% bytes. >> "%LOG_FILE%"
echo.
echo [ 下载完成 ] 已下载 %DOWNLOAD_SIZE% 字节。
exit /b 0

:DownloadFailed
call :Fail "必要 .NET 8 环境下载失败或文件不完整，软件未启动"
echo 可检查网络后重新运行此程序。
echo 日志位置："%LOG_FILE%"
echo 正在打开微软官方下载页面...
start "" "%RUNTIME_URL%"
call :Hold
exit /b 1

:SignatureFailed
call :Fail "运行环境安装包签名无效，未执行该安装包"
echo 日志位置："%LOG_FILE%"
call :Hold
exit /b 1

:InstallFailed
call :Fail ".NET 8 Desktop Runtime 安装未完成"
echo 返回代码：%INSTALL_EXIT%
echo 日志位置："%LOG_FILE%"
echo 正在打开微软官方下载页面...
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
    if exist "%%~P" for /d %%V in ("%%~P\8.*") do if exist "%%~fV" set "HAS_RUNTIME=1"
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
echo  System Optimizer Lite - 环境自检与修复
echo ============================================================
echo  软件目录："%APP_DIR%"
echo  工作目录："%WORK_DIR%"
echo.
exit /b 0

:Step
echo.
echo [ 检查 ] %~1...
exit /b 0

:Ok
echo [ 完成 ] %~1
exit /b 0

:Warn
echo [ 提示 ] %~1
exit /b 0

:Fail
echo [ 失败 ] %~1
exit /b 0

:Hold
echo.
echo 按任意键关闭此窗口。
pause >nul
exit /b 0
