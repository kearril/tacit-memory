#Requires -Version 7
<#
.SYNOPSIS
    Builds the release package: a self-contained win-x64 publish with the license files, zipped, with its SHA-256.

.DESCRIPTION
    Publishes src/TacitMemory.Cli self-contained for win-x64 into <Output>/tacit, writes THIRD-PARTY-NOTICES.txt beside
    the executable and copies LICENSE and NOTICE there, then zips the folder as tacit-<version>-win-x64.zip with a
    .sha256 file beside it. The version is the one the built executable reports, which MinVer takes from the git tags.
    Needs the .NET 11 SDK, Bun and the repository's local dotnet tools (dotnet tool restore).

.PARAMETER Output
    The folder to build into. It is emptied first.

.OUTPUTS
    The folder that was zipped, the zip and the version, as one object.
#>
param(
    [Parameter(Mandatory)] [string] $Output
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path $PSScriptRoot -Parent
$runtime = 'win-x64'
$Output = [System.IO.Path]::GetFullPath($Output)

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
$publish = Join-Path $Output 'tacit'
New-Item -ItemType Directory -Force $publish | Out-Null

# The tools' own output goes to the host, so the build log shows it and the script's output stays the one result object.
dotnet publish (Join-Path $root 'src/TacitMemory.Cli') -c Release -r $runtime --self-contained true -o $publish -nologo | Out-Host

# The notices list what the package carries: the NuGet packages of the CLI project with their whole dependency graph,
# the web packages the frontend build recorded, and the parts that come from neither. Only licenses whose full text the
# notices carry are allowed (SQLite's blessing is a public-domain dedication and has none to carry).
$nugetJson = Join-Path $Output 'nuget-licenses.json'
# dotnet finds the local tool manifest from the working directory, so the tool runs from the repository root.
Push-Location $root
try {
    dotnet tool run nuget-license `
        -i (Join-Path $root 'src/TacitMemory.Cli/TacitMemory.Cli.csproj') `
        -t `
        -a 'MIT;Apache-2.0;blessing' `
        -ignore 'MinVer' `
        -override (Join-Path $PSScriptRoot 'nuget-license-overrides.json') `
        -o Json `
        -fo $nugetJson | Out-Host
}
finally { Pop-Location }
$nuget = Get-Content $nugetJson -Raw | ConvertFrom-Json
$web = Get-Content (Join-Path $root 'web/licenses/third-party.json') -Raw | ConvertFrom-Json

$apache = Get-Content (Join-Path $root 'LICENSE') -Raw
$sqliteVecMit = Get-Content (Join-Path $root 'lib/sqlite-vec/LICENSE-MIT') -Raw
# The MIT text without its title and copyright line, for the packages whose metadata carries only a copyright statement.
$permission = $sqliteVecMit.IndexOf('Permission is hereby granted')
if ($permission -lt 0) { throw 'lib/sqlite-vec/LICENSE-MIT no longer holds the MIT permission text.' }
$mit = $sqliteVecMit.Substring($permission)

$notices = [System.Text.StringBuilder]::new()
function Line([string] $text = '') { [void]$notices.Append($text).Append("`n") }
function Heading([string] $text) { Line; Line $text; Line ('=' * $text.Length); Line }

Line 'TacitMemory third-party notices'
Line
Line 'TacitMemory is licensed under the Apache License 2.0 (see LICENSE). It ships with the components listed below,'
Line 'each under its own license. The full text of each license follows the lists.'

Heading '.NET runtime and ASP.NET Core'
Line 'The package is self-contained: it carries the .NET runtime and ASP.NET Core.'
Line 'License: MIT. Copyright (c) .NET Foundation and Contributors.'
Line 'https://github.com/dotnet/runtime'
Line 'https://github.com/dotnet/aspnetcore'
Line 'Their own third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and'
Line 'https://github.com/dotnet/aspnetcore/blob/main/THIRD-PARTY-NOTICES.txt'

Heading 'sqlite-vec (vec0.dll)'
Line 'Version 0.1.9. Dual licensed under MIT or Apache-2.0; used here under MIT.'
Line 'https://github.com/asg017/sqlite-vec'
Line
Line $sqliteVecMit.TrimEnd()

Heading 'NuGet packages'
foreach ($package in $nuget | Sort-Object PackageId) {
    Line "$($package.PackageId) $($package.PackageVersion)"
    Line "  License: $($package.License)"
    if ($package.Copyright) { Line "  $($package.Copyright)" }
    if ($package.PackageProjectUrl) { Line "  $($package.PackageProjectUrl)" }
}

Heading 'Web interface packages'
foreach ($package in $web | Sort-Object name) {
    Line "$($package.name) $($package.version)"
    Line "  License: $($package.license)"
    if ($package.homepage) { Line "  $($package.homepage)" }
}

Heading 'License texts of the web interface packages'
foreach ($package in $web | Sort-Object name) {
    Line "---- $($package.name) $($package.version) ----"
    Line
    if ($package.licenseText) { Line $package.licenseText.TrimEnd() }
    elseif ($package.license -in 'MIT', 'Apache-2.0') { Line "$($package.license); the package ships no license file, so the $($package.license) text at the end applies." }
    else { throw "$($package.name) is under $($package.license) but ships no license text to include." }
    if ($package.noticeText) { Line; Line $package.noticeText.TrimEnd() }
    Line
}

Heading 'MIT License'
Line 'Applies to the packages above listed under MIT, with the copyright statement given beside each.'
Line
Line $mit.Trim()

Heading 'Apache License 2.0'
Line 'Applies to the packages above listed under Apache-2.0.'
Line
Line $apache.Trim()

[System.IO.File]::WriteAllText((Join-Path $publish 'THIRD-PARTY-NOTICES.txt'), $notices.ToString())
Copy-Item (Join-Path $root 'LICENSE') $publish
Copy-Item (Join-Path $root 'NOTICE') $publish
Remove-Item $nugetJson

$version = "$(& (Join-Path $publish 'tacit.exe') --version)".Trim()
$zip = Join-Path $Output "tacit-$version-$runtime.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$zip.sha256", "$hash  $(Split-Path $zip -Leaf)`n")

[pscustomobject]@{ Folder = $publish; Zip = $zip; Version = $version }
