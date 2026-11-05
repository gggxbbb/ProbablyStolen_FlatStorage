[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$Python = 'python',
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($GameDir)) { $GameDir = Join-Path $root '..\..' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll') -PathType Leaf)) {
    throw "游戏程序集缺失：$managed"
}

function Invoke-ReleaseCommand {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "命令执行失败（$LASTEXITCODE）：$Command" }
}

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'ProbablyStolen_FlatStorage.csproj') -Raw
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.xml') -Raw
$version = [string]$project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($manifest.Manifest.ModVersion -ne $version -or $manifest.Manifest.ID -ne 'flat_storage' -or
    @($manifest.Manifest.GameVersions.Version).Count -ne 1 -or
    [string]$manifest.Manifest.GameVersions.Version -cne '0.46D') {
    throw '项目版本、模组清单或支持的游戏版本无效。'
}

$pythonEnvironment = Join-Path $root '.work\release-python'
$releasePython = Join-Path $pythonEnvironment 'Scripts\python.exe'
if (!(Test-Path -LiteralPath $releasePython -PathType Leaf)) {
    Invoke-ReleaseCommand $Python @('-m', 'venv', $pythonEnvironment)
}
Invoke-ReleaseCommand $releasePython @('-m', 'pip', 'install', '--disable-pip-version-check', '--no-cache-dir',
    '-r', (Join-Path $PSScriptRoot 'requirements-release.txt'))

$releaseId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$releaseRoot = Join-Path $root ".work\releases\$releaseId"
$content = Join-Path $releaseRoot 'ProbablyStolen_FlatStorage'
New-Item -ItemType Directory -Path $content -Force | Out-Null
Push-Location -LiteralPath $root
try {
    Invoke-ReleaseCommand $DotNet @('build', '-c', 'Release', "-p:GameDir=$GameDir", "-p:ModInstallDir=$content")
    Invoke-ReleaseCommand $DotNet @('run', '--project', 'Tests/FlatStorage.Tests.csproj', "-p:ManagedDir=$managed", '--', $root, $managed)
    $itemJson = & $DotNet msbuild '-getItem:ModFile' "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw '无法读取实际打包资源。' }
    $items = ($itemJson -join "`n" | ConvertFrom-Json).Items.ModFile
    foreach ($item in $items) {
        $packaged = Join-Path $content $item.Identity
        if (!(Test-Path -LiteralPath $packaged -PathType Leaf) -or
            (Get-FileHash -LiteralPath $item.FullPath).Hash -ne (Get-FileHash -LiteralPath $packaged).Hash) {
            throw "打包资源缺失或内容不同：$($item.Identity)"
        }
    }
    foreach ($language in @('zh', 'en')) {
        $localization = Join-Path $content "Localization\$language.csv"
        $rows = @(Import-Csv -LiteralPath $localization -Header Key, Value)
        if ($rows.Count -eq 0 -or @($rows | Where-Object { [string]::IsNullOrWhiteSpace($_.Key) -or [string]::IsNullOrWhiteSpace($_.Value) }).Count -gt 0 -or
            @($rows | Group-Object Key | Where-Object Count -gt 1).Count -gt 0) {
            throw "本地化内容为空或键值无效：$localization"
        }
    }
    $dll = Join-Path $content 'ProbablyStolen_FlatStorage.dll'
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
    if ($assemblyVersion -ne "$version.0") { throw "程序集版本与发布版本不同：$assemblyVersion" }
    Invoke-ReleaseCommand $releasePython @((Join-Path $PSScriptRoot 'prepare_workshop.py'), '--root', $root,
        '--content', $content, '--output', $releaseRoot, '--version', $version)

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $archive = Join-Path $dist "FlatStorage-$version.zip"
    Compress-Archive -LiteralPath $content -DestinationPath $archive -Force
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $expectedFiles = @(Get-ChildItem -LiteralPath $content -File -Recurse)
        $zipFiles = @($zip.Entries | Where-Object { $_.Name.Length -gt 0 })
        if ($zipFiles.Count -ne $expectedFiles.Count) { throw '压缩包文件数量与发布目录不同。' }
        foreach ($file in $expectedFiles) {
            $relative = 'ProbablyStolen_FlatStorage/' + [IO.Path]::GetRelativePath($content, $file.FullName).Replace('\', '/')
            $entry = $zip.GetEntry($relative)
            if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "压缩包内容不完整：$relative" }
        }
    } finally { $zip.Dispose() }
    [pscustomobject]@{
        Version = $version
        ContentFolder = $content
        PreviewFile = Join-Path $releaseRoot 'preview.jpg'
        UploadFile = Join-Path $releaseRoot 'upload.vdf'
        Archive = $archive
    }
} finally { Pop-Location }
