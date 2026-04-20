param(
  [string]$CacheRoot = (Join-Path $PSScriptRoot "..\artifacts\kernel-cache\naif\pck"),
  [string]$OutputRoot = (Join-Path $PSScriptRoot "..\Spice.WebDataGenerator\ReferenceData"),
  [switch]$ForceDownload
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedCacheRoot = Resolve-Path $CacheRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedCacheRoot) {
  $cacheRoot = $resolvedCacheRoot.Path
}
else {
  $cacheRoot = Join-Path $repoRoot "artifacts\kernel-cache\naif\pck"
}

$resolvedOutputRoot = Resolve-Path $OutputRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedOutputRoot) {
  $outputRoot = $resolvedOutputRoot.Path
}
else {
  $outputRoot = Join-Path $repoRoot "Spice.WebDataGenerator\ReferenceData"
}

$generatorProject = Join-Path $repoRoot "Spice.WebDataGenerator\Spice.WebDataGenerator.csproj"

$kernelSpecs = @(
  @{
    FileName = "pck00011.tpc"
    Url = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/pck00011.tpc"
  },
  @{
    FileName = "gm_de440.tpc"
    Url = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/gm_de440.tpc"
  }
)

$bodyIds = @(10, 199, 299, 399, 301, 499, 599, 699, 799, 899)

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$downloadedKernelPaths = @()
foreach ($kernel in $kernelSpecs) {
  $destination = Join-Path $cacheRoot $kernel.FileName
  if ($ForceDownload -or -not (Test-Path -LiteralPath $destination)) {
    Write-Host "Downloading $($kernel.FileName) from $($kernel.Url)"
    Invoke-WebRequest -Uri $kernel.Url -OutFile $destination
  }
  else {
    Write-Host "Using cached $($kernel.FileName)"
  }

  $downloadedKernelPaths += $destination
}

$generatorArgs = @(
  "run",
  "--project", $generatorProject,
  "--",
  "--metadata-only",
  "--profile-name", "current-web-body-metadata",
  "--output", $outputRoot
)

foreach ($kernelPath in $downloadedKernelPaths) {
  $generatorArgs += @("--metadata-kernel", $kernelPath)
}

foreach ($kernel in $kernelSpecs) {
  $generatorArgs += @("--metadata-kernel-source-url", $kernel.Url)
}

foreach ($bodyId in $bodyIds) {
  $generatorArgs += @("--body", "$bodyId")
}

Write-Host "Generating metadata snapshot..."
& dotnet @generatorArgs

$snapshotPath = Join-Path $outputRoot "body-metadata.json"
Write-Host "Updated $snapshotPath"
