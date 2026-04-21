param(
  [string]$CacheRoot = (Join-Path $PSScriptRoot "..\artifacts\kernel-cache\naif"),
  [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\web-data\baseline-de440s-ssb"),
  [string]$SpkFileName = "de440s.bsp",
  [string]$SpkUrl = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/de440s.bsp",
  [string]$LskFileName = "naif0012.tls",
  [string]$LskUrl = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/naif0012.tls",
  [switch]$ForceDownload
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedCacheRoot = Resolve-Path $CacheRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedCacheRoot) {
  $cacheRoot = $resolvedCacheRoot.Path
}
else {
  $cacheRoot = Join-Path $repoRoot "artifacts\kernel-cache\naif"
}

$resolvedOutputRoot = Resolve-Path $OutputRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedOutputRoot) {
  $outputRoot = $resolvedOutputRoot.Path
}
else {
  $outputRoot = Join-Path $repoRoot "artifacts\web-data\baseline-de440s-ssb"
}

$generatorProject = Join-Path $repoRoot "Spice.WebDataGenerator\Spice.WebDataGenerator.csproj"
$metadataScript = Join-Path $repoRoot "scripts\Update-WebDataMetadataSnapshot.ps1"

$spkRoot = Join-Path $cacheRoot "spk\planets"
$lskRoot = Join-Path $cacheRoot "lsk"
$pckRoot = Join-Path $cacheRoot "pck"

New-Item -ItemType Directory -Force -Path $spkRoot | Out-Null
New-Item -ItemType Directory -Force -Path $lskRoot | Out-Null
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Ensure-DownloadedFile {
  param(
    [Parameter(Mandatory = $true)][string]$Url,
    [Parameter(Mandatory = $true)][string]$Destination,
    [switch]$Force
  )

  if ($Force -or -not (Test-Path -LiteralPath $Destination)) {
    Write-Host "Downloading $(Split-Path -Leaf $Destination) from $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Destination
  }
  else {
    Write-Host "Using cached $(Split-Path -Leaf $Destination)"
  }
}

$spkPath = Join-Path $spkRoot $SpkFileName
$lskPath = Join-Path $lskRoot $LskFileName

Ensure-DownloadedFile -Url $SpkUrl -Destination $spkPath -Force:$ForceDownload
Ensure-DownloadedFile -Url $LskUrl -Destination $lskPath -Force:$ForceDownload

Write-Host "Refreshing metadata cache..."
& $metadataScript -CacheRoot $pckRoot -ForceDownload:$ForceDownload

$pckPath = Join-Path $pckRoot "pck00011.tpc"
$gmPath = Join-Path $pckRoot "gm_de440.tpc"

Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$generatorArgs = @(
  "run",
  "--project", $generatorProject,
  "--",
  "--spk", $spkPath,
  "--spk-source-url", $SpkUrl,
  "--lsk", $lskPath,
  "--lsk-source-url", $LskUrl,
  "--profile-name", "baseline-de440s-ssb-25y-mixed-cadence",
  "--metadata-kernel", $pckPath,
  "--metadata-kernel-source-url", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/pck00011.tpc",
  "--metadata-kernel", $gmPath,
  "--metadata-kernel-source-url", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/gm_de440.tpc",
  "--output", $outputRoot,
  "--start-year", "1950",
  "--end-year", "2050",
  "--chunk-years", "25",
  "--sample-days", "30",
  "--center", "0",
  "--body-cadence", "199:3",
  "--body-cadence", "299:7",
  "--body-cadence", "399:7",
  "--body-cadence", "301:3",
  "--body-cadence", "499:14"
)

Write-Host "Generating baseline web dataset..."
& dotnet @generatorArgs

Write-Host "Updated $outputRoot"
