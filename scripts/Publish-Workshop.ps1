[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$GameDir,
    [string]$Python = 'python',
    [string]$DotNet = 'dotnet',
    [string]$SteamCmd = 'steamcmd'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$release = & (Join-Path $PSScriptRoot 'Validate-Release.ps1') -GameDir $GameDir -Python $Python -DotNet $DotNet
Write-Host "发布版本：$($release.Version)"
Write-Host "安装包：$($release.Archive)"
Write-Host "上传目录：$($release.ContentFolder)"
Write-Host "上传配置：$($release.UploadFile)"
if ($ValidateOnly) { return }

$steam = Get-Command $SteamCmd -ErrorAction Stop
Write-Host '目标：Steam Workshop 3833606510，应用 4348910，可见性为公开。'
$confirmation = Read-Host '完成 GAMEPLAY_CHECKLIST.md 的游戏内验收后，输入 PUBLISH 上传此版本'
if ($confirmation -cne 'PUBLISH') { throw '上传确认未通过。' }
$steamUser = Read-Host '请输入 Steam 登录账户名'
if ([string]::IsNullOrWhiteSpace($steamUser) -or $steamUser -notmatch '^[A-Za-z0-9_]+$') {
    throw '请输入有效的 Steam 登录账户名。'
}
Write-Host '请在 SteamCMD 中输入密码和 Steam Guard 验证码。'
& $steam.Source +login $steamUser +workshop_build_item $release.UploadFile +quit
if ($LASTEXITCODE -ne 0) { throw "SteamCMD 退出代码：$LASTEXITCODE" }
Write-Host 'SteamCMD 已结束，请检查其上传结果及创意工坊页面。'
