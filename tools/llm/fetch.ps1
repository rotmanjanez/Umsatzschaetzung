param(
    [string]$Dir = (Join-Path $PSScriptRoot "..\..\third_party\llama\models"),
    [ValidateSet("win-x64", "win-arm64")][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }),
    [string]$GgufSplit = (Join-Path $PSScriptRoot "..\..\third_party\llama\build\$Arch\llama-gguf-split.exe")
)
$ErrorActionPreference = "Stop"

# Windows Installer rejects any file of 2 GB or more, so the weights ship as GGUF
# shards. llama.cpp reads split.count from the first shard and opens the rest.
$base = "gemma-4-E2B_q4_0-it"
$url = "https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf/resolve/main/$base.gguf"
$sha256 = "fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634"
$shards = 3
$splitMaxSize = "1700M"

New-Item -ItemType Directory -Force $Dir | Out-Null
if (Test-Path (Join-Path $Dir ("{0}-00001-of-{1:d5}.gguf" -f $base, $shards))) { exit 0 }

$whole = Join-Path $Dir "$base.gguf"
if (-not (Test-Path $whole)) {
    Write-Host "fetch $url"
    Invoke-WebRequest -Uri $url -OutFile "$whole.part"
    Move-Item "$whole.part" $whole
}
$got = (Get-FileHash $whole -Algorithm SHA256).Hash.ToLowerInvariant()
if ($got -ne $sha256) { throw "sha256 mismatch for ${whole}: got $got want $sha256" }

if (-not (Test-Path $GgufSplit)) { throw "$GgufSplit not found: run third_party\llama\build.ps1 -Arch $Arch first" }
Write-Host "split $base.gguf into shards below $splitMaxSize"
& $GgufSplit --split --split-max-size $splitMaxSize $whole (Join-Path $Dir $base)
if ($LASTEXITCODE -ne 0) { throw "gguf-split failed" }

$produced = @(Get-ChildItem $Dir -Filter "$base-*-of-*.gguf")
if ($produced.Count -ne $shards) { throw "expected $shards shards, got $($produced.Count): adjust splitMaxSize" }
Remove-Item $whole
