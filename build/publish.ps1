#Requires -Version 7
<#
.SYNOPSIS
    Tests, then publishes tws-login-win as two single-file win-x64 exes, with their SHA256SUMS.
.DESCRIPTION
    Output in artifacts\:
      tws-login-win.exe        self-contained (about 37 MB): runs on any Windows x64, no .NET needed
      tws-login-win-net10.exe  framework-dependent (a few hundred KB): needs the .NET 10 runtime (x64)
      SHA256SUMS               both hashes, sha256sum format
    The same steps run in .github\workflows.
#>
[CmdletBinding()]
param(
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$project = Join-Path $root 'src/tws-login-win/tws-login-win.csproj'
$exe = Join-Path $artifacts 'tws-login-win.exe'
$net10 = Join-Path $artifacts 'tws-login-win-net10.exe'

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tws-login-win.sln') -c Release
    if ($LASTEXITCODE) { throw "tests failed ($LASTEXITCODE)" }
}

Remove-Item $publish, $exe, $net10 -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $project -c Release -o (Join-Path $publish 'self-contained')
if ($LASTEXITCODE) { throw "self-contained publish failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $publish 'self-contained/tws-login-win.exe') $exe

dotnet publish $project -c Release -o (Join-Path $publish 'net10') -p:SelfContained=false -p:EnableCompressionInSingleFile=false
if ($LASTEXITCODE) { throw "framework-dependent publish failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $publish 'net10/tws-login-win.exe') $net10

$sums = foreach ($file in $exe, $net10) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path $file -Leaf)"
}
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS'), ($sums -join "`n") + "`n")

foreach ($file in $exe, $net10) {
    & $file --version
    if ($LASTEXITCODE) { throw "$file did not run ($LASTEXITCODE)" }
}
Get-Content (Join-Path $artifacts 'SHA256SUMS')
