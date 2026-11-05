<#
  Builds the Tidy Yard installer and an update package with Velopack.

  Usage:   .\release.ps1 -Version 1.0.1
  Output:  .\releases\  (Setup.exe, the full package, a delta package when a previous release is there, and the feed files)

  To publish an update, upload everything in .\releases\ to the update address (see UpdateService.DefaultFeed).
  The version must go up with every release. User data in %APPDATA%\TidyYard is never part of the package.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Output = (Join-Path $PSScriptRoot 'releases')
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$publish = Join-Path $PSScriptRoot 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Could not restore the vpk tool.' }

# Framework-dependent: Velopack installs the .NET 8 desktop runtime on machines that lack it.
dotnet publish TidyYard.csproj -c Release -r win-x64 --self-contained false -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

dotnet vpk pack `
    --runtime win-x64 `
    --packId TidyYard `
    --packVersion $Version `
    --packDir $publish `
    --mainExe TidyYard.exe `
    --packTitle 'Tidy Yard' `
    --packAuthors 'Tidy Yard' `
    --icon (Join-Path $PSScriptRoot 'Assets\Logo\TidyYard.ico') `
    --framework net8.0-x64-desktop `
    --outputDir $Output
if ($LASTEXITCODE -ne 0) { throw 'Packing failed.' }

Write-Host "Done. Installer and update files are in $Output"
