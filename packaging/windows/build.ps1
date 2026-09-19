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
$models = Join-Path $dist "models"

# Der Lizenzdialog des Installationspakets will RTF; LIZENZ bleibt die einzige Quelle.
function Write-LicenseRtf([string]$source, [string]$target) {
    $rtf = [Text.StringBuilder]::new()
    [void]$rtf.Append('{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}\fs18 ')
    foreach ($line in [IO.File]::ReadAllLines($source, [Text.Encoding]::UTF8)) {
        foreach ($c in $line.ToCharArray()) {
            if ($c -in '\', '{', '}') { [void]$rtf.Append('\').Append($c) }
            elseif ([int]$c -gt 127) { [void]$rtf.Append('\u').Append([int]$c).Append('?') }
            else { [void]$rtf.Append($c) }
        }
        [void]$rtf.Append("\par`r`n")
    }
    [void]$rtf.Append('}')
    [IO.File]::WriteAllText($target, $rtf.ToString(), [Text.Encoding]::ASCII)
}

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
dotnet publish (Join-Path $root "src\Umsatzschaetzung.App") -c Release -r $Arch --self-contained -p:PublishSingleFile=true -p:Version=$msiVersion -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Copy-Item (Join-Path $PSScriptRoot "LICENSES.txt") $dist
Copy-Item (Join-Path $root "LICENSE") (Join-Path $dist "LICENSE.txt")
Copy-Item (Join-Path $root "LIZENZ") (Join-Path $dist "LIZENZ.txt")

foreach ($required in "umsatzschätzung.exe", "e_sqlite3.dll", "pdfium.dll", "onnxruntime.dll",
                      "av_libglesv2.dll", "libSkiaSharp.dll", "libHarfBuzzSharp.dll",
                      "LICENSES.txt", "LICENSE.txt", "LIZENZ.txt") {
    if (-not (Test-Path (Join-Path $dist $required))) { throw "$required missing from $dist" }
}
foreach ($required in "belegtagger\belegtagger.int8.onnx", "belegtagger\vocab.json", "belegtagger\merges.txt",
                      "belegtagger\byte_to_unicode.json", "belegtagger\spec.json", "v6\PP-OCRv6_det_small.onnx") {
    if (-not (Test-Path (Join-Path $models $required))) { throw "models\$required missing from $dist; the build fetches it, see Models.targets" }
}
Sign (Get-ChildItem $dist -Include *.exe, *.dll -Recurse).FullName
if ($NoInstaller) { Get-ChildItem $dist; exit 0 }

$out = Join-Path $root "dist\msi"
Remove-Item -Recurse -Force $out -ErrorAction Ignore
New-Item -ItemType Directory $out | Out-Null
$wixArch = if ($Arch -eq "win-arm64") { "arm64" } else { "x64" }
$licenseRtf = Join-Path $root "dist\lizenz.rtf"
Write-LicenseRtf (Join-Path $root "LIZENZ") $licenseRtf
foreach ($ext in "WixToolset.UI.wixext", "WixToolset.Util.wixext") {
    wix extension add -g $ext
    if ($LASTEXITCODE -ne 0) { throw "wix extension add $ext failed" }
}
wix build -arch $wixArch -culture de-DE -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
    -d "Version=$msiVersion" -d "Manufacturer=$Manufacturer" -d "Dist=$dist" -d "Models=$models" -d "LicenseRtf=$licenseRtf" `
    -o (Join-Path $out "umsatzschaetzung-$msiVersion-$Arch.msi") (Join-Path $PSScriptRoot "umsatzschätzung.wxs")
if ($LASTEXITCODE -ne 0) { throw "wix failed" }
Sign (Get-ChildItem $out -Filter *.msi).FullName

if ($SignArgs.Count -gt 0) { Copy-Item (Join-Path $PSScriptRoot "publisher.cer") $out }

@(Get-ChildItem $out -File -Exclude *.wixpdb) | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name
} | Tee-Object (Join-Path $out "SHA256SUMS.txt")
