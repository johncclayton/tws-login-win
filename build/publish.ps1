#Requires -Version 7
<#
.SYNOPSIS
    Tests, then publishes tws-login-win as one self-contained win-x64 exe with its SHA256SUMS.
.DESCRIPTION
    Output: artifacts\tws-login-win.exe and artifacts\SHA256SUMS (sha256sum format). The same steps run in
    .github\workflows\release.yml.
#>
[CmdletBinding()]
param(
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tws-login-win.sln') -c Release
    if ($LASTEXITCODE) { throw "tests failed ($LASTEXITCODE)" }
}

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\tws-login-win\tws-login-win.csproj') -c Release -o $publish
if ($LASTEXITCODE) { throw "publish failed ($LASTEXITCODE)" }

$exe = Join-Path $artifacts 'tws-login-win.exe'
Copy-Item (Join-Path $publish 'tws-login-win.exe') $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS'), "$hash  tws-login-win.exe`n")

& $exe --version
if ($LASTEXITCODE) { throw "the published exe did not run ($LASTEXITCODE)" }
Write-Host "$exe`n$hash"
