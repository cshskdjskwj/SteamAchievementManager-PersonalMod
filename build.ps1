# 魔改版 SAM 构建脚本
# 用法: powershell -File build.ps1
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '_src\dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "找不到便携版 .NET SDK: $dotnet"
}

# GenerateResourceUsePreserializedResources: 新版 SDK 编译 .NET Framework 的 .resx 图标资源所必需
& $dotnet build (Join-Path $root 'SAM.sln') `
    -c Release `
    -p:Platform=x86 `
    -p:GenerateResourceUsePreserializedResources=true `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "构建失败 (exit $LASTEXITCODE)"
}

Write-Host ''
Write-Host '构建成功，产物在 upload\ 目录：'
Get-ChildItem (Join-Path $root 'upload') -Filter 'SAM.*' |
    Where-Object { $_.Extension -in '.exe', '.dll' } |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
