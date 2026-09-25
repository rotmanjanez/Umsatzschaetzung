$ErrorActionPreference = "Stop"
$work = Join-Path ([IO.Path]::GetTempPath()) "umsatz-upgrade"
$dist = Join-Path $work "dist"
$key = "HKLM:\SOFTWARE\Umsatzschätzung"
$localStore = Join-Path $env:ProgramData "Umsatzschätzung\store"
$failures = [Collections.Generic.List[string]]::new()

Remove-Item -Recurse -Force $work -ErrorAction Ignore
New-Item -ItemType Directory $dist | Out-Null
Copy-Item (Join-Path $env:WINDIR "notepad.exe") (Join-Path $dist "umsatzschätzung.exe")
Set-Content (Join-Path $dist "LICENSE.txt") "test"
Set-Content (Join-Path $work "lizenz.rtf") '{\rtf1 test}'

$wixVersion = (wix --version) -replace '\+.*', ''
wix extension add -g "WixToolset.UI.wixext/$wixVersion"
if ($LASTEXITCODE -ne 0) { throw "wix extension add failed" }

$msi = @{}
foreach ($version in "1.0.0", "1.0.1", "1.0.2") {
    $msi[$version] = Join-Path $work "umsatzschaetzung-$version.msi"
    wix build -arch x64 -culture de-DE -ext WixToolset.UI.wixext `
        -d "Version=$version" -d "Manufacturer=Test" -d "Dist=$dist" -d "Models=$dist" -d "LicenseRtf=$(Join-Path $work 'lizenz.rtf')" `
        -o $msi[$version] (Join-Path $PSScriptRoot "umsatzschätzung.wxs")
    if ($LASTEXITCODE -ne 0) { throw "wix build $version failed" }
}

function Msiexec([string]$action, [string]$version, [string[]]$properties = @()) {
    $log = Join-Path $work "$action-$version-$([guid]::NewGuid()).log"
    $p = Start-Process msiexec -Wait -PassThru -ArgumentList (@("/$action", "`"$($msi[$version])`"", "/qn", "/l*v", "`"$log`"") + $properties)
    if ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 80
        throw "msiexec /$action $version exited with $($p.ExitCode)"
    }
}

function Expect([string]$case, [string]$name, [string]$expected) {
    $actual = (Get-ItemProperty $key -ErrorAction Ignore).$name
    if ($actual -ne $expected) { $failures.Add("${case}: $name is '$actual', expected '$expected'") }
    else { Write-Host "ok  ${case}: $name = $actual" }
}

function Expect-LocalStore([string]$case) {
    if (-not (Test-Path $localStore)) { $failures.Add("${case}: $localStore is gone"); return }
    $users = (Get-Acl $localStore).Access | Where-Object {
        $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq "S-1-5-32-545" -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::CreateFiles)
    }
    if (-not $users) { $failures.Add("${case}: Users cannot write to $localStore") }
    else { Write-Host "ok  ${case}: $localStore is writable for Users" }
}

$case = "share"
Msiexec i "1.0.0" @('STORE="\\server\regeln"', 'CASEDIR="D:\Prüfungen"')
Msiexec i "1.0.1"
Expect $case Store "\\server\regeln"
Expect $case CaseDir "D:\Prüfungen"
Msiexec i "1.0.2" @('STORE="\\server\neu"')
Expect "$case, override" Store "\\server\neu"
Expect "$case, override" CaseDir "D:\Prüfungen"
Msiexec x "1.0.2"

$case = "local"
Msiexec i "1.0.0" @("STORELOCAL=1", "STORE=`"$localStore`"")
Expect-LocalStore "$case, install"
Msiexec i "1.0.1"
Expect $case Store $localStore
Expect-LocalStore $case
Msiexec x "1.0.1"

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL $_" }
    exit 1
}
