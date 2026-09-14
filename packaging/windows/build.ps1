param(
    [string]$Version = "dev",
    [ValidateSet("win-x64", "win-arm64")][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }),
    [string]$Manufacturer = "Janez Rotman",
    [string]$ModelDir = "",
    [string]$Pfx = "",
    [string]$PfxPassword = $env:UMSATZ_PFX_PASSWORD,
    [string[]]$SignArgs = @(),
    [string]$Timestamp = "http://timestamp.digicert.com",
    [switch]$SkipLlm,
    [switch]$NoInstaller
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$dist = Join-Path $root "dist\$Arch"
if ($ModelDir -eq "") { $ModelDir = Join-Path $root "third_party\llama\models" }
$modelBase = "gemma-4-E2B_q4_0-it"

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

if (-not $SkipLlm) { & (Join-Path $root "third_party\llama\build.ps1") -Arch $Arch }

Remove-Item -Recurse -Force $dist -ErrorAction Ignore
dotnet publish (Join-Path $root "src\Umsatzschätzung.App") -c Release -r $Arch --self-contained -p:PublishSingleFile=true -p:Version=$msiVersion -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Copy-Item (Join-Path $root "third_party\llama\build\$Arch\umsatzschaetzung_llm.dll") $dist
Copy-Item (Join-Path $PSScriptRoot "LICENSES.txt") $dist

foreach ($required in "umsatzschätzung.exe", "WebView2Loader.dll", "umsatzschaetzung_llm.dll", "e_sqlite3.dll", "LICENSES.txt",
                      "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "PenImc_cor3.dll", "vcruntime140_cor3.dll") {
    if (-not (Test-Path (Join-Path $dist $required))) { throw "$required missing from $dist" }
}
Sign (Get-ChildItem $dist -Include *.exe, *.dll -Recurse).FullName
if ($NoInstaller) { Get-ChildItem $dist; exit 0 }

& (Join-Path $root "tools\llm\fetch.ps1") -Dir $ModelDir -Arch $Arch
$shards = @(Get-ChildItem $ModelDir -Filter "$modelBase-*-of-*.gguf" | Sort-Object Name)
if ($shards.Count -ne 3) { throw "expected 3 model shards in $ModelDir, found $($shards.Count)" }

$out = Join-Path $root "dist\msi"
Remove-Item -Recurse -Force $out -ErrorAction Ignore
New-Item -ItemType Directory $out | Out-Null
$wixArch = if ($Arch -eq "win-arm64") { "arm64" } else { "x64" }
wix build -arch $wixArch -culture de-DE `
    -d "Version=$msiVersion" -d "Manufacturer=$Manufacturer" -d "Dist=$dist" `
    -d "ModelShard1=$($shards[0].FullName)" -d "ModelShard2=$($shards[1].FullName)" -d "ModelShard3=$($shards[2].FullName)" `
    -o (Join-Path $out "umsatzschätzung-$msiVersion-$Arch.msi") (Join-Path $PSScriptRoot "umsatzschätzung.wxs")
if ($LASTEXITCODE -ne 0) { throw "wix failed" }
Sign (Get-ChildItem $out -Filter *.msi).FullName

if ($SignArgs.Count -gt 0) { Copy-Item (Join-Path $PSScriptRoot "publisher.cer") $out }

@(Get-ChildItem $out -File -Exclude *.wixpdb) | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name
} | Tee-Object (Join-Path $out "SHA256SUMS.txt")
