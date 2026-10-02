# 启动魔改版 SAM 并针对指定 appId 打开成就管理器（调试用）
# 用法: powershell -File run.ps1 -AppId 250900
param(
    [long]$AppId = 250900,
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($Exe)) {
    $Exe = Join-Path $root 'upload\SAM.Game.exe'
}

if (-not (Test-Path $Exe)) {
    throw "找不到 $Exe，请先运行 build.ps1"
}

Get-Process 'SAM.Game' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$p = Start-Process $Exe -ArgumentList $AppId -PassThru
Write-Host "已启动 SAM.Game.exe (pid=$($p.Id)) appId=$AppId"
Start-Sleep -Seconds 8
Get-Process -Id $p.Id -ErrorAction SilentlyContinue |
    Select-Object ProcessName, Id, MainWindowTitle |
    Format-Table -AutoSize
