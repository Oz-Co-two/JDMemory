# JDMemory のリリースに添付する物を作る（blnext の tools/make_release.py と同じ考え方）。
#
# 使い方:  powershell -ExecutionPolicy Bypass -File tools\make_release.ps1
# 出力:    dist\release\JDMemory-v〇.〇.〇.zip   （Plugins\JDMemory.dll だけ。手で入れる人・インストーラーの更新用）
#          dist\release\JDMemoryInstaller.exe   （MOD を同梱したインストーラー）
#          dist\release\update.json             （インストーラーとゲーム内の「新しい版」の知らせが読む）
#
# - 版は Directory.Build.props の <Version>。manifest.json の "version" と CHANGELOG.md の先頭の「## v〇.〇.〇」が同じでないと止まる。
# - update.json の更新内容は CHANGELOG.md のその版の行頭の太字（- **〜**）。
# - できた zip と update.json は、インストーラーと同じ検査（UpdateClient.ParseManifest・ExtractPackage）に通してから終わる。
# - 公開（GitHub へのタグ・リリース作成）はこの後に手で行う。利用者の指示を得てから。
#   update.json を添付し忘れると、古いインストーラー・ゲーム内の知らせが新しい版を見つけられない。

param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist\release'

function Fail([string]$message) {
    Write-Host "止めました: $message" -ForegroundColor Red
    exit 1
}

# ---- 版をそろえる -----------------------------------------------------------
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8)
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "Directory.Build.props の Version が読めません（$version）" }
$tag = "v$version"

$manifest = Get-Content (Join-Path $root 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.version -ne $version) { Fail "manifest.json の version（$($manifest.version)）が $version と違います" }

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$heads = @($changelog | Where-Object { $_ -match '^## v\d+\.\d+\.\d+' })
if ($heads.Count -eq 0 -or $heads[0].Trim() -ne "## $tag") { Fail "CHANGELOG.md の先頭の版が「## $tag」ではありません" }
$notes = @()
$inSection = $false
foreach ($line in $changelog) {
    if ($line -match '^## ') { if ($inSection) { break }; $inSection = ($line.Trim() -eq "## $tag"); continue }
    if ($inSection -and $line -match '^- \*\*(.+?)\*\*') { $notes += $Matches[1] }
}
if ($notes.Count -eq 0) { Fail "CHANGELOG.md の $tag に行頭の太字（- **〜**）がありません" }

# ---- ビルド -----------------------------------------------------------------
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not $SkipBuild) {
    Write-Host "MOD をビルドしています…"
    & dotnet build (Join-Path $root 'JDMemory.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'MOD のビルドに失敗しました' }
    Write-Host "インストーラーをビルドしています…"
    & dotnet build (Join-Path $root 'installer\JDMemoryInstaller.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'インストーラーのビルドに失敗しました' }
}
$dll = Join-Path $root 'bin\Release\JDMemory.dll'
$exe = Join-Path $root 'installer\bin\Release\JDMemoryInstaller.exe'
foreach ($f in $dll, $exe) { if (-not (Test-Path $f)) { Fail "$f がありません" } }
$dllVersion = (Get-Item $dll).VersionInfo.FileVersion
if ($dllVersion -ne "$version.0") { Fail "ビルドした JDMemory.dll の版（$dllVersion）が $version と違います（-SkipBuild を外してください）" }

# ---- 出力 -------------------------------------------------------------------
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out -File | Where-Object { $_.Name -like 'JDMemory-*.zip' -or $_.Name -in 'update.json', 'JDMemoryInstaller.exe' } |
    Remove-Item -Force -Confirm:$false

Add-Type -AssemblyName System.IO.Compression
$zipName = "JDMemory-$tag.zip"
$zipPath = Join-Path $out $zipName
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('Plugins/JDMemory.dll', [IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        try { $bytes = [IO.File]::ReadAllBytes($dll); $entryStream.Write($bytes, 0, $bytes.Length) } finally { $entryStream.Dispose() }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

$exeOut = Join-Path $out 'JDMemoryInstaller.exe'
Copy-Item $exe $exeOut

function FileEntry([string]$path) {
    [ordered]@{
        name   = [IO.Path]::GetFileName($path)
        sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        size   = (Get-Item $path).Length
    }
}

$update = [ordered]@{
    name         = 'JDMemory'
    format       = 1
    version      = $tag
    gameVersions = @($manifest.gameVersion)
    zip          = FileEntry $zipPath
    installer    = FileEntry $exeOut
    notes        = $notes
}
$json = $update | ConvertTo-Json -Depth 5
$updatePath = Join-Path $out 'update.json'
[IO.File]::WriteAllText($updatePath, $json + "`n", (New-Object Text.UTF8Encoding $false))

# ---- インストーラーと同じ検査に通す -------------------------------------------
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exeOut))
$bf = [Reflection.BindingFlags]'Static,NonPublic,Public'
$client = $asm.GetType('JDMemoryInstaller.UpdateClient')
try {
    $parsed = $client.GetMethod('ParseManifest', $bf).Invoke($null, @(, [IO.File]::ReadAllBytes($updatePath)))
    if (-not $client.GetMethod('CanDownload', $bf).Invoke($null, @($parsed))) { Fail '作った update.json はインストーラーが自動で取れる形になっていません' }
    $package = $client.GetMethod('ExtractPackage', $bf).Invoke($null, @($parsed, [IO.File]::ReadAllBytes($zipPath)))
} catch {
    $inner = if ($_.Exception.InnerException) { $_.Exception.InnerException.Message } else { $_.Exception.Message }
    Fail "作った物がインストーラーの検査に通りません: $inner"
}
$extracted = $package.GetType().GetField('Dll').GetValue($package)
$original = [IO.File]::ReadAllBytes($dll)
if ($extracted.Length -ne $original.Length -or [Convert]::ToBase64String($extracted) -ne [Convert]::ToBase64String($original)) {
    Fail 'zip から取り出した JDMemory.dll がビルドした物と違います'
}
$bundled = $asm.GetType('JDMemoryInstaller.ModPackage').GetMethod('LoadBundled', $bf).Invoke($null, @())
if ($bundled.GetType().GetField('Version').GetValue($bundled) -ne $version) { Fail 'インストーラーに同梱した MOD の版が違います' }

# ---- 結果 -------------------------------------------------------------------
Write-Host ""
Write-Host "作成しました（$tag）:" -ForegroundColor Green
foreach ($f in $zipPath, $exeOut, $updatePath) {
    $item = Get-Item $f
    Write-Host ("  {0,-28} {1,8:N0} bytes" -f $item.Name, $item.Length)
}
Write-Host "  更新内容: $($notes -join ' / ')"
Write-Host ""
Write-Host "公開する時（利用者の指示を得てから）は、3つともリリースに添付する:"
Write-Host "  gh release create $tag `"dist/release/$zipName`" `"dist/release/JDMemoryInstaller.exe`" `"dist/release/update.json`" --title $tag --latest --notes-file <ノート>"
