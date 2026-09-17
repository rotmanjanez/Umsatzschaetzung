param(
    [string]$Version = "dev",
    [ValidateSet("win-x64", "win-arm64")][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }),
    [string]$Manufacturer = "Janez Rotman",
    [string]$Pfx = "",
    [string]$PfxPassword = $env:UMSATZ_PFX_PASSWORD,
    [string[]]$SignArgs = @(),
    [string]$Timestamp = "http://timestamp.digicert.com",
    [switch]$NoInstaller
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$dist = Join-Path $root "dist\$Arch"
$models = Join-Path $root "models"

function Resolve-SignTool {
    if ($script:tool) { return $script:tool }
    $onPath = @(Get-Command signtool.exe -CommandType Application -ErrorAction Ignore)[0]
    if ($onPath) { $script:tool = $onPath.Source; return $script:tool }
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
    $script:tool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") -Directory -ErrorAction Ignore |
        Where-Object { $_.Name -as [version] } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "$hostArch\signtool.exe" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $script:tool) { throw "signtool.exe not found; install the Windows SDK" }
    $script:tool
}

function Sign([string[]]$files) {
    if ($SignArgs.Count -eq 0) { return }
    & (Resolve-SignTool) sign /fd SHA256 /td SHA256 /tr $Timestamp /d Umsatzschätzung @SignArgs @files
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}

if ($Pfx -ne "") {
    if (-not (Test-Path $Pfx)) { throw "$Pfx not found" }
    if ([string]::IsNullOrEmpty($PfxPassword)) { throw "-PfxPassword or UMSATZ_PFX_PASSWORD required" }
    $SignArgs = @("/f", (Resolve-Path $Pfx).Path, "/p", $PfxPassword) + $SignArgs
}

$msiVersion = "0.0.0"
if ($Version -match '^v?(\d+\.\d+\.\d+)') { $msiVersion = $Matches[1] }

Remove-Item -Recurse -Force $dist -ErrorAction Ignore
dotnet publish (Join-Path $root "src\Umsatzschätzung.App") -c Release -r $Arch --self-contained -p:PublishSingleFile=true -p:Version=$msiVersion -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Copy-Item (Join-Path $PSScriptRoot "LICENSES.txt") $dist

foreach ($required in "umsatzschätzung.exe", "WebView2Loader.dll", "e_sqlite3.dll", "pdfium.dll", "LICENSES.txt",
                      "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "PenImc_cor3.dll", "vcruntime140_cor3.dll") {
    if (-not (Test-Path (Join-Path $dist $required))) { throw "$required missing from $dist" }
}
foreach ($required in "tagger.int8.onnx", "vocab.json", "merges.txt", "byte_to_unicode.json", "spec.json") {
    $file = Get-Item (Join-Path $models "b\$required") -ErrorAction Ignore
    if (-not $file) { throw "models\b\$required missing; run git lfs pull" }
    if ($file.Length -lt 1kb -and (Get-Content $file -First 1) -like "version https://git-lfs*") {
        throw "models\b\$required is an LFS pointer; run git lfs pull"
    }
}
Sign (Get-ChildItem $dist -Include *.exe, *.dll -Recurse).FullName
if ($NoInstaller) { Get-ChildItem $dist; exit 0 }

$out = Join-Path $root "dist\msi"
Remove-Item -Recurse -Force $out -ErrorAction Ignore
New-Item -ItemType Directory $out | Out-Null
$wixArch = if ($Arch -eq "win-arm64") { "arm64" } else { "x64" }
wix build -arch $wixArch -culture de-DE `
    -d "Version=$msiVersion" -d "Manufacturer=$Manufacturer" -d "Dist=$dist" -d "Models=$models" `
    -o (Join-Path $out "umsatzschaetzung-$msiVersion-$Arch.msi") (Join-Path $PSScriptRoot "umsatzschätzung.wxs")
if ($LASTEXITCODE -ne 0) { throw "wix failed" }
Sign (Get-ChildItem $out -Filter *.msi).FullName

if ($SignArgs.Count -gt 0) { Copy-Item (Join-Path $PSScriptRoot "publisher.cer") $out }

@(Get-ChildItem $out -File -Exclude *.wixpdb) | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name
} | Tee-Object (Join-Path $out "SHA256SUMS.txt")
