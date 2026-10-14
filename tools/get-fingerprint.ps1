<#
.SYNOPSIS
  获取 mod 补丁目标函数的指纹 / Get fingerprints of the methods a mod patches.

.EXAMPLE
  # 中文：看别人的 mod（或自己的 mod）补丁了哪些函数、指纹是多少
  # EN:   See which methods a mod patches and their fingerprints
  .\get-fingerprint.ps1 -Mod "C:\path\SomeMod.dll"

.EXAMPLE
  # 中文：直接看某个游戏函数的指纹和规范化 IL
  # EN:   Fingerprint and normalized IL of one game method
  .\get-fingerprint.ps1 -Method "StationComponent::AddItem"

.EXAMPLE
  # 中文：游戏更新后，对比同一函数的新旧 IL
  # EN:   After a game update, diff one method between two versions
  .\get-fingerprint.ps1 -Method "StationComponent::AddItem" -OldGame "D:\old\Dyson Sphere Program"

.PARAMETER Game
  游戏目录（含 DSPGAME.exe）。不填则自动查找 Steam 安装，或读取环境变量 DSP_GAME_DIR。
  Game folder (contains DSPGAME.exe). Auto-detected from Steam, or env var DSP_GAME_DIR.
#>
param(
    [string]$Mod,
    [string]$Method,
    [string]$Game,
    [string]$OldGame
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Find-GameDir {
    if ($env:DSP_GAME_DIR -and (Test-Path "$env:DSP_GAME_DIR\DSPGAME.exe")) { return $env:DSP_GAME_DIR }
    $roots = @()
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $roots += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object {
                $roots += $_.Matches[0].Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($r in $roots) {
        $p = Join-Path $r 'steamapps\common\Dyson Sphere Program'
        if (Test-Path "$p\DSPGAME.exe") { return $p }
    }
    throw "找不到游戏目录，请用 -Game 指定 / Game folder not found, pass -Game <folder>"
}

# 找到或编译命令行工具 / locate or build the CLI
$root = Split-Path -Parent $PSScriptRoot
$exe = @("$root\bin\harness.exe", "$PSScriptRoot\harness.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    Write-Host "首次使用，正在编译 harness.exe / building harness.exe ..."
    dotnet build "$root\cli" -c Release -o "$root\bin" | Out-Null
    $exe = "$root\bin\harness.exe"
}

if (-not $Game) { $Game = Find-GameDir }
Write-Host "Game: $Game"

if ($Mod) {
    & $exe scan $Mod $Game
}
elseif ($Method -and $OldGame) {
    & $exe diff $OldGame $Game $Method
}
elseif ($Method) {
    & $exe dump $Game $Method
}
else {
    Get-Help $MyInvocation.MyCommand.Path -Examples
}
exit $LASTEXITCODE
