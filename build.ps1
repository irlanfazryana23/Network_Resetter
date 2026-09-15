# Compile and package only. Never launches the app or network commands.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler was not found.' }
$releaseVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
if ($releaseVersion -notmatch '^\d+\.\d+(?:\.\d+)?$') { throw 'VERSION must contain major.minor or major.minor.patch.' }
$parsedVersion = [version]$releaseVersion
$assemblyVersion = '{0}.{1}.{2}.0' -f $parsedVersion.Major, $parsedVersion.Minor, [Math]::Max(0, $parsedVersion.Build)
$source = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\NetworkResetter.cs'))
foreach ($attribute in @('AssemblyVersion', 'AssemblyFileVersion')) {
    if (-not $source.Contains('[assembly: ' + $attribute + '("' + $assemblyVersion + '")]')) { throw "$attribute must match VERSION ($assemblyVersion)." }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src\app.manifest') -Raw
if ($manifest.assembly.assemblyIdentity.version -ne $assemblyVersion) { throw 'App manifest version must match VERSION.' }
$executableName = 'NetworkResetter-' + $releaseVersion + '.exe'
$outputDirectory = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/warnaserror+', '/utf8output',
    ('/out:' + (Join-Path $outputDirectory $executableName)),
    ('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')),
    ('/win32icon:' + (Join-Path $projectRoot 'assets\networks.ico')),
    ('/resource:' + (Join-Path $projectRoot 'src\MainWindow.xaml') + ',MainWindow.xaml'),
    ('/resource:' + (Join-Path $projectRoot 'networks-logo.png') + ',Logo.png'),
    ('/reference:' + (Join-Path $frameworkRoot 'System.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Core.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'System.Xaml.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'WPF\WindowsBase.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $frameworkRoot 'WPF\PresentationFramework.dll')),
    (Join-Path $projectRoot 'src\NetworkResetter.cs'),
    (Join-Path $projectRoot 'src\NetworkChecks.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit code $LASTEXITCODE)." }
$packageFiles = @((Join-Path $outputDirectory $executableName))
foreach ($document in @('README.md', 'LICENSE', 'CHANGELOG.md', 'networks-logo.png')) {
    $destination = Join-Path $outputDirectory $document
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $destination -Force
    $packageFiles += $destination
}
$executable = Join-Path $outputDirectory $executableName
$digest = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
$checksumPath = Join-Path $outputDirectory 'SHA256SUMS.txt'
Set-Content -LiteralPath $checksumPath -Value ($digest + '  ' + $executableName) -Encoding ASCII
$packageFiles += $checksumPath
$archiveName = 'NetworkResetter-' + $releaseVersion + '-portable.zip'
$archivePath = Join-Path $outputDirectory $archiveName
Compress-Archive -LiteralPath $packageFiles -DestinationPath $archivePath -Force
# The ZIP contains its EXE checksum; the standalone file also covers the ZIP.
$archiveDigest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
Add-Content -LiteralPath $checksumPath -Value ($archiveDigest + '  ' + $archiveName) -Encoding ASCII
Write-Host 'Build complete. No application or network commands were executed.'
Write-Host $executable
