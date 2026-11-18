param([string] $Destination = (Join-Path $PSScriptRoot '../release/Source-Code/KeyFlowPads-Source.zip'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force (Split-Path -Parent $Destination) | Out-Null
if (Test-Path -LiteralPath $Destination) {
    Remove-Item -LiteralPath $Destination
}
$archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @('Directory.Build.props', '.gitignore', 'README.md', 'LICENSES.md') | ForEach-Object {
        Get-Item -Force -LiteralPath (Join-Path $root $_)
    }
    $files += @('src', 'tests', 'scripts', 'installer', 'assets') | ForEach-Object {
        Get-ChildItem -File -Recurse -LiteralPath (Join-Path $root $_) |
            Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
    }
    foreach ($file in $files) {
        $entry = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Fastest)
    }
} finally {
    $archive.Dispose()
}
