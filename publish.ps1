<#
.SYNOPSIS
    Builds DS1 Lua Studio and copies everything needed to run it into a single folder.

.DESCRIPTION
    "dotnet build" leaves the runnable app spread across six bin\Release folders, and only the
    App project produces an executable. This publishes everything into one place so there is a
    single obvious DS1LuaStudio.exe to run.

    Framework dependent (default) is small but needs the .NET 8 Desktop Runtime installed.
    Use -SelfContained for a larger folder that runs on any Windows x64 machine.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SelfContained
    .\publish.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\DS1LuaStudio.App\DS1LuaStudio.App.csproj'
$output = Join-Path $root 'dist\DarkSoulsLuaStudio'

Write-Host "Restoring..." -ForegroundColor DarkGray
dotnet restore $project --disable-parallel | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host "Publishing ($Configuration, self-contained=$($SelfContained.IsPresent))..." -ForegroundColor Cyan
$args = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-m:1',
    '-p:RestoreDisableParallel=true',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-o', $output,
    '--nologo'
)
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Sanity check: the entry point and the native Lua compiler must both be present.
$exe = Join-Path $output 'DS1LuaStudio.exe'
$native = Join-Path $output 'LuaNative\lua502.dll'

if (-not (Test-Path $exe))    { throw "Expected executable was not produced: $exe" }
if (-not (Test-Path $native)) { throw "The Lua 5.0 compiler (lua502.dll) is missing - scripts could not be recompiled." }

$size = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "Ready to run:" -ForegroundColor Green
Write-Host "  $exe"
Write-Host ""
Write-Host ("  {0} files, {1:N1} MB" -f (Get-ChildItem $output -Recurse -File).Count, $size)
if (-not $SelfContained) {
    Write-Host "  Requires the .NET 8 Desktop Runtime (https://dotnet.microsoft.com/download/dotnet/8.0)" -ForegroundColor DarkGray
} else {
    Write-Host "  Self-contained: runs on any Windows x64 machine, no .NET install needed." -ForegroundColor DarkGray
}
