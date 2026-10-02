# 魔改版 SAM 构建脚本
# 用法: powershell -File build.ps1
# 构建完会自动把产物部署到根目录（也就是你平时双击 SAM.Picker.exe 的位置），
# 避免出现"改了代码但跑的还是旧 exe"的情况。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '_src\dotnet\dotnet.exe'
$work = Join-Path $root 'work'
$upload = Join-Path $work 'upload'

if (-not (Test-Path $dotnet)) {
    throw "找不到便携版 .NET SDK: $dotnet"
}

# GenerateResourceUsePreserializedResources: 新版 SDK 编译 .NET Framework 的 .resx 图标资源所必需
#
# 下面两个开关都是为了**不让编译进程常驻**：
#   -nodeReuse:false              → 不留 MSBuild 工作节点
#   -p:UseSharedCompilation=false → 不留 Roslyn 编译器服务器（VBCSCompiler.dll）
# 这些常驻进程会一直锁着 obj/ 里的文件，导致**整个文件夹删不掉**（已实际踩过）。
& $dotnet build (Join-Path $work 'SAM.sln') `
    -c Release `
    -p:Platform=x86 `
    -p:GenerateResourceUsePreserializedResources=true `
    -nodeReuse:false `
    -p:UseSharedCompilation=false `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "构建失败 (exit $LASTEXITCODE)"
}

# ---- 部署到根目录 ----
Get-Process 'SAM.Game', 'SAM.Picker' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$files = @(
    'SAM.Game.exe', 'SAM.Game.exe.config',
    'SAM.Picker.exe', 'SAM.Picker.exe.config',
    'SAM.API.dll',
    'System.Resources.Extensions.dll', 'System.Memory.dll', 'System.Buffers.dll',
    'System.Numerics.Vectors.dll', 'System.Runtime.CompilerServices.Unsafe.dll'
)

foreach ($file in $files) {
    $source = Join-Path $upload $file
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $root $file) -Force
    }
}

Write-Host ''
Write-Host '构建成功，已部署到根目录：'
Get-ChildItem $root -Filter 'SAM.*' |
    Where-Object { $_.Extension -in '.exe', '.dll' } |
    ForEach-Object {
        $version = (Get-Item $_.FullName).VersionInfo.ProductVersion
        [pscustomobject]@{
            Name    = $_.Name
            Version = $version
            Size    = $_.Length
        }
    } |
    Format-Table -AutoSize

Write-Host '发布打包目录（如需发 Release）：' (Join-Path $root '_src\release')
