param(
    [string] $Dotnet = 'dotnet',
    [string] $InnoCompiler = 'ISCC.exe',
    [string] $PublishDirectory,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = if ($PublishDirectory) { [IO.Path]::GetFullPath($PublishDirectory) } else { Join-Path $root 'artifacts/publish' }
$release = Join-Path $root 'release'
$app = Join-Path $root 'src/KeyFlowPads.App/KeyFlowPads.App.csproj'
$installerDirectory = Join-Path $release 'Installer'
$portableDirectory = Join-Path $release 'Portable'
New-Item -ItemType Directory -Force $work,$installerDirectory,$portableDirectory | Out-Null

& $Dotnet publish $app -c Release -r win-x64 --self-contained -o $work -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'Publishing the Windows app failed.' }
Copy-Item (Join-Path $root 'LICENSES.md') $work -Force
Copy-Item (Join-Path $root 'assets/licenses') $work -Recurse -Force

if (!$SkipInstaller) {
    & $InnoCompiler "/DPublishDir=$work" "/DReleaseDir=$installerDirectory" (Join-Path $root 'installer/KeyFlowPads.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    $binary = (Get-Item -LiteralPath (Join-Path $work 'KeyFlowPads.exe')).VersionInfo
    $version = "$($binary.FileMajorPart).$($binary.FileMinorPart).$($binary.FileBuildPart)"
    $setup = Join-Path $installerDirectory "KeyFlowPads-$version-Setup.exe"
    Copy-Item -LiteralPath $setup -Destination (Join-Path $installerDirectory 'KeyFlowPads-Setup.exe') -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $portableDirectory 'KeyFlowPads-Portable-Windows-x64.zip'
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive
}
[IO.Compression.ZipFile]::CreateFromDirectory($work, $archive, [IO.Compression.CompressionLevel]::Fastest, $false)
