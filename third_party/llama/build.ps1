param(
    [ValidateSet("win-x64", "win-arm64")][string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" })
)
$ErrorActionPreference = "Stop"

$LlamaVersion = "b10931"
$LlamaSha256 = "e93174a1e64baa22703f79235fe528332dfdc07ae871d87cf84ac9c671483fe7"
$LlamaUrl = "https://github.com/ggml-org/llama.cpp/archive/refs/tags/$LlamaVersion.tar.gz"

$Root = $PSScriptRoot
$Src = Join-Path $Root "llama.cpp"
$Build = Join-Path $Root "build"
$Out = Join-Path $Build $Arch
$Stage = Join-Path $Build "stage\$Arch"
$Work = Join-Path $Build "work\$Arch"

# llama.cpp rejects MSVC on ARM (ggml/src/ggml-cpu/CMakeLists.txt), so clang-cl is
# the compiler for both targets: one toolchain, one set of flags.
$Triple = if ($Arch -eq "win-arm64") { "aarch64-pc-windows-msvc" } else { "x86_64-pc-windows-msvc" }
$SystemProcessor = if ($Arch -eq "win-arm64") { "ARM64" } else { "AMD64" }
$HostArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
$TargetArch = if ($Arch -eq "win-arm64") { "arm64" } else { "x64" }
$VcVarsArch = if ($HostArch -eq $TargetArch) { $TargetArch } else { "${HostArch}_${TargetArch}" }

# Only a real cross build may set CMAKE_SYSTEM_NAME: it turns on CMAKE_CROSSCOMPILING,
# and the Vulkan backend runs vulkan-shaders-gen on the host during the build.
$CrossFlags = if ($HostArch -eq $TargetArch) { @() } else {
    @("-DCMAKE_SYSTEM_NAME=Windows",
      "-DCMAKE_SYSTEM_PROCESSOR=$SystemProcessor",
      "-DCMAKE_C_COMPILER_TARGET=$Triple",
      "-DCMAKE_CXX_COMPILER_TARGET=$Triple")
}

function Import-VcVars {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "Visual Studio Build Tools not found: install the 'Desktop development with C++' workload" }
    $vsPath = & $vswhere -products * -latest -property installationPath
    if (-not $vsPath) { throw "no Visual Studio installation with C++ tools found" }
    $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvarsall.bat"
    if (-not (Test-Path $vcvars)) { throw "vcvarsall.bat not found in $vsPath" }
    cmd /c "`"$vcvars`" $VcVarsArch && set" | ForEach-Object {
        if ($_ -match '^([^=]+?)=(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] -ErrorAction SilentlyContinue }
    }
}

function Fetch-Source {
    if (Test-Path (Join-Path $Src "CMakeLists.txt")) { return }
    $dl = Join-Path $Build "dl"
    New-Item -ItemType Directory -Force $dl | Out-Null
    $tarball = Join-Path $dl "llama-$LlamaVersion.tar.gz"
    if (-not (Test-Path $tarball)) {
        Write-Host "fetch $LlamaUrl"
        Invoke-WebRequest -Uri $LlamaUrl -OutFile "$tarball.part"
        Move-Item "$tarball.part" $tarball
    }
    $got = (Get-FileHash -Algorithm SHA256 $tarball).Hash.ToLowerInvariant()
    if ($got -ne $LlamaSha256) { throw "sha256 mismatch for ${tarball}: got $got want $LlamaSha256" }
    New-Item -ItemType Directory -Force $Src | Out-Null
    tar -xzf $tarball -C $Src --strip-components=1
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }
}

function Invoke-Cmake {
    & cmake @args
    if ($LASTEXITCODE -ne 0) { throw "cmake failed: $args" }
}

Import-VcVars

if (Test-Path (Join-Path $env:ProgramFiles "LLVM\bin\clang-cl.exe")) {
    $env:Path = "$env:Path;$(Join-Path $env:ProgramFiles 'LLVM\bin')"
}
if ($env:VULKAN_SDK) { $env:Path = "$env:Path;$(Join-Path $env:VULKAN_SDK 'Bin')" }

if (-not (Get-Command clang-cl -ErrorAction SilentlyContinue)) { throw "clang-cl not found: install LLVM (https://llvm.org) or the VS 'C++ Clang tools for Windows' component" }
if (-not $env:VULKAN_SDK) { throw "VULKAN_SDK is not set: install the Vulkan SDK (https://vulkan.lunarg.com)" }
if (-not (Get-Command glslc -ErrorAction SilentlyContinue)) { throw "glslc not found on PATH: add $env:VULKAN_SDK\Bin" }

Fetch-Source

$Generator = if (Get-Command ninja -ErrorAction SilentlyContinue) { @("-G", "Ninja") } else { @() }
$CpuFlags = if ($Arch -eq "win-arm64") { @() } else { @("-DGGML_AVX=ON", "-DGGML_AVX2=ON", "-DGGML_FMA=ON", "-DGGML_F16C=ON") }

Invoke-Cmake @Generator -S $Src -B (Join-Path $Work "llama") `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang-cl `
    -DCMAKE_CXX_COMPILER=clang-cl `
    -DCMAKE_INSTALL_PREFIX="$Stage" `
    -DCMAKE_INSTALL_LIBDIR=lib `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    -DBUILD_SHARED_LIBS=OFF `
    -DLLAMA_CURL=OFF `
    -DLLAMA_BUILD_COMMON=OFF `
    -DLLAMA_BUILD_EXAMPLES=OFF `
    -DLLAMA_BUILD_TESTS=OFF `
    -DLLAMA_BUILD_TOOLS=OFF `
    -DLLAMA_BUILD_SERVER=OFF `
    -DLLAMA_BUILD_APP=OFF `
    -DGGML_OPENMP=OFF `
    -DGGML_BACKEND_DL=OFF `
    -DGGML_CPU_ALL_VARIANTS=OFF `
    -DGGML_CCACHE=OFF `
    -DGGML_NATIVE=OFF `
    -DGGML_VULKAN=ON `
    @CrossFlags @CpuFlags
Invoke-Cmake --build (Join-Path $Work "llama") --config Release --target install

Invoke-Cmake @Generator -S $Root -B (Join-Path $Work "ausbeute_llm") `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang-cl `
    -DCMAKE_CXX_COMPILER=clang-cl `
    -DCMAKE_PREFIX_PATH="$Stage" `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    -DCMAKE_RUNTIME_OUTPUT_DIRECTORY="$Out" `
    -DCMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE="$Out" `
    @CrossFlags
Invoke-Cmake --build (Join-Path $Work "ausbeute_llm") --config Release

# gguf-split shards the weights below the 2 GB per-file limit of Windows Installer.
# It is a build-time tool: CPU only, and only that target is built.
Invoke-Cmake @Generator -S $Src -B (Join-Path $Work "tools") `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang-cl `
    -DCMAKE_CXX_COMPILER=clang-cl `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    -DCMAKE_RUNTIME_OUTPUT_DIRECTORY="$Out" `
    -DCMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE="$Out" `
    -DBUILD_SHARED_LIBS=OFF `
    -DLLAMA_CURL=OFF `
    -DLLAMA_BUILD_COMMON=ON `
    -DLLAMA_BUILD_EXAMPLES=OFF `
    -DLLAMA_BUILD_TESTS=OFF `
    -DLLAMA_BUILD_TOOLS=ON `
    -DLLAMA_BUILD_SERVER=OFF `
    -DLLAMA_BUILD_APP=OFF `
    -DGGML_OPENMP=OFF `
    -DGGML_BACKEND_DL=OFF `
    -DGGML_CPU_ALL_VARIANTS=OFF `
    -DGGML_CCACHE=OFF `
    -DGGML_NATIVE=OFF `
    -DGGML_VULKAN=OFF `
    @CrossFlags @CpuFlags
Invoke-Cmake --build (Join-Path $Work "tools") --config Release --target llama-gguf-split

Get-Item (Join-Path $Out "ausbeute_llm.dll"), (Join-Path $Out "llama-gguf-split.exe")
