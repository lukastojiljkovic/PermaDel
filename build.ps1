#Requires -Version 5.1
<#
.SYNOPSIS
    Tests PermaDel, publishes a self-contained x64 build together with the File Explorer extension and compiles the
    Inno Setup installer.
.OUTPUTS
    artifacts\installer\PermaDel-<version>-Setup.exe
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Step([string] $name, [scriptblock] $command) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE) { throw "$name failed with exit code $LASTEXITCODE." }
}

function Find-Tool([string[]] $candidates, [string] $missingMessage) {
    $tool = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $tool) { throw $missingMessage }
    return $tool
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = Find-Tool @(if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\amd64\MSBuild.exe' }) `
    'MSBuild with the C++ workload was not found. Install it with: winget install Microsoft.VisualStudio.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"'
$makeappx = Find-Tool @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | ForEach-Object FullName) `
    'MakeAppx.exe was not found. It ships with the Windows SDK, which the C++ workload installs.'
$iscc = Find-Tool @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") `
    'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup'

$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
$publishDir = "$root\artifacts\publish\win-x64"
$packageDir = "$root\artifacts\sparse-package"
$extensionDir = "$root\src\PermaDel.ShellExtension"

# The forensic tests need administrator rights and are run separately (see README).
Invoke-Step 'Test' { dotnet test "$root\tests\PermaDel.Core.Tests" -c Release --nologo --filter 'Category!=Forensics' }

foreach ($dir in $publishDir, $packageDir) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
Invoke-Step 'Publish app' { dotnet publish "$root\src\PermaDel\PermaDel.csproj" -c Release -r win-x64 -p:Platform=x64 -p:PublishReadyToRun=true -o $publishDir --nologo }

# The self-contained build redistributes the .NET runtime and the Windows App SDK, whose licenses and notices must ship with it.
Write-Host '==> Collect third-party licenses' -ForegroundColor Cyan
$packages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$licenseDir = New-Item -ItemType Directory -Force "$publishDir\licenses"
$libraries = (Get-Content "$publishDir\PermaDel.deps.json" -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name
foreach ($library in $libraries -match '^(runtimepack\.Microsoft\.NETCore\.App\.Runtime\.|Microsoft\.WindowsAppSDK\.)') {
    $name, $libraryVersion = ($library -replace '^runtimepack\.', '') -split '/'
    Get-ChildItem "$packages\$($name.ToLowerInvariant())\$libraryVersion" -File |
        Where-Object Name -match '^(license|notice|third-party-notices)\.txt$' |
        ForEach-Object { Copy-Item $_.FullName "$licenseDir\$name.$($_.Name.ToUpperInvariant())" }
}
if (-not (Get-ChildItem $licenseDir)) { throw 'No third-party license files were found in the NuGet package cache.' }

Invoke-Step 'Build File Explorer extension' { & $msbuild "$extensionDir\PermaDel.ShellExtension.vcxproj" -p:Configuration=Release -p:Platform=x64 -nologo -v:minimal }
Copy-Item "$extensionDir\bin\x64\Release\PermaDel.ShellExtension.dll" $publishDir

# The sparse package contains only its manifest; /nv skips validating files that live in the external location.
New-Item -ItemType Directory -Force $packageDir | Out-Null
(Get-Content "$extensionDir\AppxManifest.xml" -Raw) -creplace 'Version="1\.0\.0\.0"', "Version=`"$version.0`"" |
    Set-Content "$packageDir\AppxManifest.xml" -Encoding UTF8
Invoke-Step 'Package File Explorer extension' { & $makeappx pack /d $packageDir /p "$publishDir\PermaDel.ShellExtension.msix" /nv /o }

Invoke-Step 'Installer' { & $iscc /Q "/DAppVersion=$version" "$root\installer\PermaDel.iss" }

Write-Host "Installer: $root\artifacts\installer\PermaDel-$version-Setup.exe" -ForegroundColor Green
