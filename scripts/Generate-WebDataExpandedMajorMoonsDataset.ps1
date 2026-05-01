param(
  [string]$CacheRoot = (Join-Path $PSScriptRoot "..\artifacts\kernel-cache\ssd"),
  [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\web-data\expanded-major-moons"),
  [string]$LskFileName = "naif0012.tls",
  [string]$LskUrl = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/naif0012.tls",
  [switch]$ForceDownload,
  [switch]$BenchmarkConfiguredCadence,
  [switch]$BenchmarkConfiguredChunkYears
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedCacheRoot = Resolve-Path $CacheRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedCacheRoot) {
  $cacheRoot = $resolvedCacheRoot.Path
}
else {
  $cacheRoot = Join-Path $repoRoot "artifacts\kernel-cache\ssd"
}

$resolvedOutputRoot = Resolve-Path $OutputRoot -ErrorAction SilentlyContinue
if ($null -ne $resolvedOutputRoot) {
  $outputRoot = $resolvedOutputRoot.Path
}
else {
  $outputRoot = Join-Path $repoRoot "artifacts\web-data\expanded-major-moons"
}

$generatorProject = Join-Path $repoRoot "Spice.WebDataGenerator\Spice.WebDataGenerator.csproj"
$lskRoot = Join-Path $cacheRoot "lsk"
$pckRoot = Join-Path $cacheRoot "pck"

$spkSpecs = @(
  @{
    FileName = "de440s.bsp"
    RelativePath = "planets\bsp\de440s.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/de440s.bsp"
  },
  @{
    FileName = "mar097.bsp"
    RelativePath = "satellites\bsp\mar097.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/satellites/bsp/mar097.bsp"
  },
  @{
    FileName = "jup365.bsp"
    RelativePath = "satellites\bsp\jup365.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/satellites/bsp/jup365.bsp"
  },
  @{
    FileName = "sat427l.bsp"
    RelativePath = "satellites\bsp\sat427l.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/satellites/bsp/sat427l.bsp"
  },
  @{
    FileName = "ura111.bsp"
    RelativePath = "satellites\bsp\ura111.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/satellites/bsp/ura111.bsp"
  },
  @{
    FileName = "Triton.nep097.30kyr.bsp"
    RelativePath = "satellites\bsp\Triton.nep097.30kyr.bsp"
    Url = "https://ssd.jpl.nasa.gov/ftp/eph/satellites/bsp/Triton.nep097.30kyr.bsp"
  }
)

$bodySpecs = @(
  @{ BodyId = 10; ParentId = $null; CadenceDays = 30; Name = "Sun" },
  @{ BodyId = 199; ParentId = 10; CadenceDays = 3; Name = "Mercury" },
  @{ BodyId = 299; ParentId = 10; CadenceDays = 7; Name = "Venus" },
  @{ BodyId = 399; ParentId = 10; CadenceDays = 7; Name = "Earth" },
  @{ BodyId = 301; ParentId = 399; CadenceDays = 3; Name = "Moon" },
  @{ BodyId = 499; ParentId = 10; CadenceDays = 14; Name = "Mars" },
  @{ BodyId = 401; ParentId = 499; CadenceDays = 1; Name = "Phobos" },
  @{ BodyId = 402; ParentId = 499; CadenceDays = 1; Name = "Deimos" },
  @{ BodyId = 599; ParentId = 10; CadenceDays = 30; Name = "Jupiter" },
  @{ BodyId = 501; ParentId = 599; CadenceDays = 1; Name = "Io" },
  @{ BodyId = 502; ParentId = 599; CadenceDays = 1; Name = "Europa" },
  @{ BodyId = 503; ParentId = 599; CadenceDays = 1; Name = "Ganymede" },
  @{ BodyId = 504; ParentId = 599; CadenceDays = 2; Name = "Callisto" },
  @{ BodyId = 699; ParentId = 10; CadenceDays = 30; Name = "Saturn" },
  @{ BodyId = 601; ParentId = 699; CadenceDays = 1; Name = "Mimas" },
  @{ BodyId = 602; ParentId = 699; CadenceDays = 1; Name = "Enceladus" },
  @{ BodyId = 603; ParentId = 699; CadenceDays = 1; Name = "Tethys" },
  @{ BodyId = 604; ParentId = 699; CadenceDays = 1; Name = "Dione" },
  @{ BodyId = 605; ParentId = 699; CadenceDays = 1; Name = "Rhea" },
  @{ BodyId = 606; ParentId = 699; CadenceDays = 2; Name = "Titan" },
  @{ BodyId = 608; ParentId = 699; CadenceDays = 4; Name = "Iapetus" },
  @{ BodyId = 799; ParentId = 10; CadenceDays = 30; Name = "Uranus" },
  @{ BodyId = 701; ParentId = 799; CadenceDays = 1; Name = "Ariel" },
  @{ BodyId = 702; ParentId = 799; CadenceDays = 1; Name = "Umbriel" },
  @{ BodyId = 703; ParentId = 799; CadenceDays = 2; Name = "Titania" },
  @{ BodyId = 704; ParentId = 799; CadenceDays = 2; Name = "Oberon" },
  @{ BodyId = 705; ParentId = 799; CadenceDays = 1; Name = "Miranda" },
  @{ BodyId = 899; ParentId = 10; CadenceDays = 30; Name = "Neptune" },
  @{ BodyId = 801; ParentId = 899; CadenceDays = 1; Name = "Triton" }
)

$metadataKernelSpecs = @(
  @{
    FileName = "pck00011.tpc"
    Url = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/pck00011.tpc"
  },
  @{
    FileName = "gm_de440.tpc"
    Url = "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/gm_de440.tpc"
  }
)

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $lskRoot | Out-Null
New-Item -ItemType Directory -Force -Path $pckRoot | Out-Null
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

$spkPaths = @()
foreach ($spk in $spkSpecs) {
  $destination = Join-Path $cacheRoot $spk.RelativePath
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
  Ensure-DownloadedFile -Url $spk.Url -Destination $destination -Force:$ForceDownload
  $spkPaths += $destination
}

$lskPath = Join-Path $lskRoot $LskFileName
Ensure-DownloadedFile -Url $LskUrl -Destination $lskPath -Force:$ForceDownload

$metadataKernelPaths = @()
foreach ($kernel in $metadataKernelSpecs) {
  $destination = Join-Path $pckRoot $kernel.FileName
  Ensure-DownloadedFile -Url $kernel.Url -Destination $destination -Force:$ForceDownload
  $metadataKernelPaths += $destination
}

Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$generatorArgs = @(
  "run",
  "--project", $generatorProject,
  "--",
  "--profile-name", "expanded-major-moons",
  "--lsk", $lskPath,
  "--lsk-source-url", $LskUrl,
  "--output", $outputRoot,
  "--start-year", "1901",
  "--end-year", "2100",
  "--chunk-years", "25",
  "--sample-days", "30",
  "--center", "0",
  "--benchmark-truth-hours", "12"
)

foreach ($spkPath in $spkPaths) {
  $generatorArgs += @("--spk", $spkPath)
}

foreach ($spk in $spkSpecs) {
  $generatorArgs += @("--spk-source-url", $spk.Url)
}

foreach ($kernelPath in $metadataKernelPaths) {
  $generatorArgs += @("--metadata-kernel", $kernelPath)
}

foreach ($kernel in $metadataKernelSpecs) {
  $generatorArgs += @("--metadata-kernel-source-url", $kernel.Url)
}

foreach ($body in $bodySpecs) {
  $generatorArgs += @("--body", "$($body.BodyId)")

  if ($body.CadenceDays -ne 30) {
    $generatorArgs += @("--body-cadence", "$($body.BodyId):$($body.CadenceDays)")
  }
}

if ($BenchmarkConfiguredCadence) {
  $generatorArgs += "--benchmark-configured-cadence"
}

if ($BenchmarkConfiguredChunkYears) {
  $generatorArgs += @("--benchmark-configured-chunk-years", "--benchmark-chunk-years", "25,10,5")
}

Write-Host "Generating expanded-major-moons web dataset..."
Write-Host "Profile parent ids are tracked in this script and remain registry-owned in the web runtime schema."
& dotnet @generatorArgs

Write-Host "Updated $outputRoot"
