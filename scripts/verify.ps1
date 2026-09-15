# Static verification only: no app instances, diagnostics, repairs, or restarts.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
$parsedVersion = [version]$releaseVersion
$expectedVersion = '{0}.{1}.{2}.0' -f $parsedVersion.Major, $parsedVersion.Minor, [Math]::Max(0, $parsedVersion.Build)
$exeName = 'NetworkResetter-' + $releaseVersion + '.exe'
$zipName = 'NetworkResetter-' + $releaseVersion + '-portable.zip'
$outputDirectory = Join-Path $projectRoot 'dist'

Add-Type -AssemblyName System.Xaml,PresentationFramework,PresentationCore,WindowsBase,System.IO.Compression.FileSystem
$xamlPath = Join-Path $projectRoot 'src\MainWindow.xaml'
$reader = New-Object System.Xaml.XamlXmlReader($xamlPath)
try {
    while ($reader.Read()) {
        if ($reader.NodeType -eq [System.Xaml.XamlNodeType]::StartObject -and $reader.Type.IsUnknown) { throw ('Unknown XAML type: ' + $reader.Type.Name) }
        if ($reader.NodeType -eq [System.Xaml.XamlNodeType]::StartMember -and $reader.Member.IsUnknown) { throw ('Unknown XAML member: ' + $reader.Member.Name) }
    }
} finally { $reader.Close() }
[xml]$layout = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
$source = Get-Content -LiteralPath (Join-Path $projectRoot 'src\NetworkResetter.cs') -Raw -Encoding UTF8
$names = @($layout.SelectNodes('//*[@*[local-name()="Name"]]') | ForEach-Object { $_.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml') })
if (@($names | Group-Object | Where-Object Count -gt 1).Count) { throw 'Duplicate XAML names.' }
foreach ($match in [regex]::Matches($source, 'Find<[^>]+>\("([^"]+)"\)')) {
    if ($names -notcontains $match.Groups[1].Value) { throw ('Missing control: ' + $match.Groups[1].Value) }
}

foreach ($script in @('build.ps1', 'scripts\verify.ps1')) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot $script), [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count) { throw ($parseErrors -join "`n") }
}
foreach ($launcher in @('open_gui.bat')) {
    if (-not ([IO.File]::ReadAllText((Join-Path $projectRoot $launcher))).Contains($exeName)) { throw "$launcher must reference $exeName." }
}

$exePath = Join-Path $outputDirectory $exeName
# Reflection-only loading reads metadata; it cannot execute the entry point.
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($exePath)
if ($assembly.GetName().Version.ToString() -ne $expectedVersion) { throw 'EXE version does not match VERSION.' }
foreach ($resource in @('MainWindow.xaml', 'Logo.png')) {
    if ($assembly.GetManifestResourceNames() -notcontains $resource) { throw "Missing resource: $resource" }
}
$checksumLines = @(Get-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt'))
if ($checksumLines.Count -ne 2) { throw 'Expected EXE and ZIP checksums.' }
foreach ($fileName in @($exeName, $zipName)) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $outputDirectory $fileName) -Algorithm SHA256).Hash
    if ($checksumLines -notcontains ($hash + '  ' + $fileName)) { throw "Invalid checksum for $fileName." }
}

$expectedEntries = @($exeName, 'README.md', 'LICENSE', 'CHANGELOG.md', 'networks-logo.png', 'SHA256SUMS.txt')
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $outputDirectory $zipName))
try {
    if ($archive.Entries.Count -ne $expectedEntries.Count) { throw 'Unexpected package contents.' }
    foreach ($entryName in $expectedEntries) {
        $entry = $archive.GetEntry($entryName)
        if ($null -eq $entry) { throw "Missing package entry: $entryName" }
        $stream = $entry.Open()
        try {
            if ($entryName -eq 'SHA256SUMS.txt') {
                $textReader = New-Object IO.StreamReader($stream)
                try { $embeddedChecksum = $textReader.ReadToEnd().Trim() } finally { $textReader.Dispose() }
                $exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
                if ($embeddedChecksum -ne ($exeHash + '  ' + $exeName)) { throw 'Invalid embedded EXE checksum.' }
            } else {
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $sha.Dispose() }
                $originalPath = if ($entryName -eq $exeName) { $exePath } else { Join-Path $projectRoot $entryName }
                if ($entryHash -ne (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash) { throw "Package entry differs from source: $entryName" }
            }
        } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }

Write-Host "Static verification passed for version $releaseVersion."
Write-Host 'Checked XAML schema/control references, script syntax, launcher path, EXE metadata, resources, license, and package hashes.'
Write-Host 'No application or network operations were executed.'
