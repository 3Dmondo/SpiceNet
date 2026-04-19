# Web Data Generator

Status: Compact web-data format benchmarked

## Goal

Provide a dedicated `SpiceNet` CLI that turns SPICE kernel inputs into web-ready ephemeris and metadata assets for `solar-system-web`.

## Current Step

The repository now includes a dedicated auxiliary project named `Spice.WebDataGenerator`.

At this step the CLI:

- loads an SPK kernel and optionally loads an LSK kernel
- validates the requested generation window and output path
- defaults to a 1950 through 2050 benchmark window with 50-year chunk planning
- defaults to the Sun, planets, and Moon body set
- emits a manifest plus chunk JSON files
- samples position and velocity for the selected bodies relative to a center body
- supports per-body cadence overrides while keeping shared chunk boundaries
- emits minified web assets rather than inspection-oriented pretty JSON
- stores body names, source ids, and cadence metadata in the manifest instead of repeating them in every chunk
- stores chunk sample data as flattened numeric state arrays instead of per-sample objects
- falls back to planetary barycenter query ids when a requested display body is not directly available in the kernel
- can benchmark Mercury interpolation error across multiple sample cadences while also generating real output files for size inspection
- can benchmark all selected bodies across multiple sample cadences and record both raw and gzip-compressed output sizes
- can benchmark one configured mixed-cadence export and validate it body by body against live `SpiceNet` queries
- can benchmark several shared chunk durations for one configured mixed-cadence profile and compare both total and per-chunk download sizes

Current limitations:

- UTC to TDB conversion is currently approximate and ignores leap seconds
- kernel-derived metadata extraction is not implemented yet
- numeric precision is still emitted using default JSON double formatting without extra size tuning

## Initial CLI Shape

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de441t.bsp `
  --output .\artifacts\web-data `
  --start-year 1950 `
  --end-year 2050 `
  --chunk-years 50 `
  --sample-days 365 `
  --center 10
```

Optional repeated body override and optional LSK path:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de441t.bsp `
  --lsk .\kernels\naif0012.tls `
  --output .\artifacts\web-data `
  --sample-days 180 `
  --body 10 --body 399 --body 301
```

Per-body cadence override:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de440s.bsp `
  --output .\artifacts\web-data\mixed-cadence `
  --center 0 `
  --sample-days 30 `
  --body-cadence 199:3 `
  --body-cadence 299:7 `
  --body-cadence 399:7 `
  --body-cadence 301:3 `
  --body-cadence 499:14
```

Mercury cadence benchmark mode:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de440s.bsp `
  --output .\artifacts\web-data\mercury-benchmark `
  --benchmark-mercury `
  --benchmark-cadences 90,30,14,7,3 `
  --benchmark-truth-hours 12
```

All-bodies benchmark mode:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de440s.bsp `
  --output .\artifacts\web-data\body-benchmark `
  --center 0 `
  --benchmark-bodies `
  --benchmark-cadences 14,7,3,1 `
  --benchmark-truth-hours 12
```

Configured mixed-cadence benchmark mode:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de440s.bsp `
  --output .\artifacts\web-data\mixed-cadence-benchmark-ssb `
  --center 0 `
  --sample-days 30 `
  --body-cadence 199:3 `
  --body-cadence 299:7 `
  --body-cadence 399:7 `
  --body-cadence 301:3 `
  --body-cadence 499:14 `
  --benchmark-configured-cadence `
  --benchmark-truth-hours 12
```

Configured chunk-duration benchmark mode:

```powershell
dotnet run --project Spice.WebDataGenerator -- `
  --spk .\kernels\de440s.bsp `
  --output .\artifacts\web-data\mixed-chunk-year-benchmark-ssb `
  --center 0 `
  --sample-days 30 `
  --body-cadence 199:3 `
  --body-cadence 299:7 `
  --body-cadence 399:7 `
  --body-cadence 301:3 `
  --body-cadence 499:14 `
  --benchmark-configured-chunk-years `
  --benchmark-chunk-years 50,25,20,10 `
  --benchmark-truth-hours 12
```

## Current Output Shape

- `manifest.json` is minified and captures:
  - schema version
  - source SPK path and optional LSK path
  - coverage years, shared chunk duration, default cadence, and center body id
  - one explicit runtime-layout section describing chunk boundary time encoding, sample timestamp reconstruction, sample value layout, units, and interpolation intent
  - one body table with display ids, display names, resolved source ids, source names, and actual sample cadence
  - one chunk table with file names plus UTC and approximate TDB coverage boundaries
- `chunk-<start>-<end>.json` is minified and stores:
  - schema version
  - center body id
  - approximate chunk start and end TDB seconds past J2000
  - one entry per body with the display body id and a flattened numeric sample array
- flattened sample arrays use `[x, y, z, vx, vy, vz, ...]` in kilometers and kilometers per second
- sample timestamps are reconstructed from manifest chunk boundaries plus the per-body cadence, with the last sample always landing exactly on the chunk end
- the browser should treat the manifest runtime-layout section as the source of truth for interpreting chunk payloads instead of relying on implicit conventions
- `mercury-benchmark.json` summarizes interpolation error and generated byte size for each tested cadence. Benchmark runs also emit one generated dataset per cadence under `sample-<n>d/`.
- `body-benchmark.json` summarizes interpolation error for every selected body per cadence and records both raw and gzip output sizes.
- `configured-cadence-benchmark.json` summarizes interpolation error for one configured mixed-cadence export and records the generated raw and gzip output sizes.
- `configured-chunk-year-benchmark.json` summarizes the same mixed-cadence profile across several shared chunk durations and records both total output size and the largest per-chunk gzip size.

Historical note:

- the earlier Mercury and all-bodies cadence sections below were produced before this compact output rewrite
- their interpolation error readings are still useful for choosing cadences
- their byte-size figures should be treated as historical inspection-format numbers rather than the current delivery-format baseline

Runtime-contract note:

- the compact schema is now explicit enough to be a reasonable Milestone 5 runtime contract candidate
- provenance and benchmark-report fields are still primarily generator-side diagnostics and should not be treated as the browser-facing hot-path contract

## de440s Mercury Benchmark Snapshot

Benchmark inputs:

- SPK: cached `de440s.bsp`
- coverage: 1950 through 2050
- chunk duration: 50 years
- truth sampling for error measurement: every 12 hours
- interpolation: cubic Hermite from sampled position and velocity
- output size: full current body set with a uniform cadence for all bodies
- compressed size: approximate gzip size computed per generated JSON file

| Cadence | Mercury max error | Mercury mean error | Raw bytes | Gzip bytes |
|---------|-------------------|--------------------|-----------|------------|
| 90 days | 126,239,873 km | 62,979,219 km | 1,588,065 | 294,446 |
| 30 days | 9,078,743 km | 2,174,912 km | 4,740,394 | 861,952 |
| 14 days | 662,443 km | 129,039 km | 10,144,063 | 1,833,572 |
| 7 days | 46,304 km | 8,528 km | 20,268,754 | 3,616,194 |
| 3 days | 1,613 km | 292 km | 47,280,691 | 8,293,560 |

Provisional reading:

- `90` and `30` day cadences are far too coarse for Mercury.
- `14` days is much better, but still likely too loose if we want confident smooth interpolation during faster motion.
- `7` days looks like the first cadence that feels plausibly acceptable for Mercury in a visual explorer.
- `3` days is excellent for Mercury, but the current uniform full-body JSON output becomes heavy quickly.

Current recommendation:

- treat `7` days as the provisional Mercury target for the current web-data format
- keep `3` days as the high-quality reference point if later visual testing shows `7` days is still too coarse
- do not generalize Mercury's result to the Moon; the Moon will likely need its own denser benchmark
- revisit the size numbers once the generator supports body-specific cadence, because these benchmark files use one uniform cadence for every body

## de440s All-Bodies SSB Benchmark Snapshot

Benchmark inputs:

- SPK: cached `de440s.bsp`
- center: solar system barycenter (`0`)
- coverage: 1950 through 2050
- chunk duration: 50 years
- truth sampling for error measurement: every 12 hours
- interpolation: cubic Hermite from sampled position and velocity
- output size: full current body set with a uniform cadence for all bodies

Selected results:

| Cadence | Mercury max error | Moon max error | Venus max error | Earth max error | Per-chunk gzip | Total gzip |
|---------|-------------------|----------------|-----------------|-----------------|----------------|------------|
| 14 days | 662,443 km | 120,327 km | 6,968 km | 2,935 km | about 970 KB | 1,940,689 B |
| 7 days | 46,304 km | 10,554 km | 437 km | 221 km | about 1.9 MB | 3,830,454 B |
| 3 days | 1,613 km | 400 km | 15 km | 8 km | about 4.4 MB | 8,792,975 B |
| 1 day | 20 km | 5 km | 0.18 km | 0.10 km | about 12.9 MB | 25,843,943 B |

Outer-planet note:

- Even at `14` days, Mars barycenter error stays around `334 km`, and Jupiter plus the remaining outer planets are effectively negligible for a visual explorer at this stage.

Current recommendation after the all-bodies SSB benchmark:

- yes, use different sampling frequencies per body
- yes, consider smaller chunk sizes than `50` years for the browser download units
- no, do not introduce different chunk durations per body in the first implementation unless simpler options fail

Reasoning:

- the inner fast bodies dominate interpolation error
- a uniform cadence good enough for Mercury and the Moon makes the full dataset much heavier than the slower bodies require
- a uniform `50` year chunk is workable for benchmarking, but `3` day sampling produces chunks that are too heavy for comfortable browser downloads
- keeping one shared chunk timeline while varying body cadence is a simpler first design than mixing cadence and chunk duration per body

## de440s Mixed-Cadence SSB Benchmark Snapshot

Benchmark inputs:

- SPK: cached `de440s.bsp`
- center: solar system barycenter (`0`)
- coverage: 1950 through 2050
- chunk duration: 50 years
- default cadence: `30` days
- per-body overrides:
  - Mercury: `3` days
  - Venus: `7` days
  - Earth: `7` days
  - Moon: `3` days
  - Mars: `14` days
  - Sun and outer planets: fallback default `30` days
- truth sampling for error measurement: every `12` hours
- interpolation: cubic Hermite from sampled position and velocity

Size summary:

- total raw size: `4,957,314` bytes
- total gzip size: `2,403,771` bytes
- `chunk-1950-2000.json`: `2,478,802` raw bytes, `1,201,705` gzip bytes
- `chunk-2000-2050.json`: `2,476,332` raw bytes, `1,201,202` gzip bytes

Selected body results:

| Body | Cadence | Max error | Mean error |
|------|---------|-----------|------------|
| Sun | 30 days | 2 km | 0 km |
| Mercury | 3 days | 1,613 km | 292 km |
| Venus | 7 days | 437 km | 221 km |
| Moon | 3 days | 400 km | 124 km |
| Earth | 7 days | 221 km | 57 km |
| Mars | 14 days | 334 km | 97 km |
| Jupiter | 30 days | 11 km | 4 km |

Current reading:

- this mixed cadence profile now looks materially more realistic for browser delivery because the compact shape removes much of the JSON overhead from the earlier inspection format
- the result is still a little heavy for ideal just-in-time browser downloads at about `1.20 MB gzip` per `50` year chunk
- the next useful lever is shared chunk duration before revisiting more aggressive cadence tuning
- one shared chunk timeline plus per-body cadence still looks like the right first implementation path

## de440s Mixed-Cadence Shared Chunk Benchmark Snapshot

Benchmark inputs:

- SPK: cached `de440s.bsp`
- center: solar system barycenter (`0`)
- coverage: 1950 through 2050
- default cadence: `30` days
- per-body overrides:
  - Mercury: `3` days
  - Venus: `7` days
  - Earth: `7` days
  - Moon: `3` days
  - Mars: `14` days
  - Sun and outer planets: fallback default `30` days
- truth sampling for error measurement: every `12` hours
- interpolation: cubic Hermite from sampled position and velocity

Measured results:

| Chunk years | Chunk count | Total gzip | Largest chunk gzip | Mercury max error | Moon max error | Earth max error |
|-------------|-------------|------------|--------------------|-------------------|----------------|-----------------|
| 50 | 2 | 2,403,771 B | 1,201,705 B | 1,613 km | 400 km | 221 km |
| 25 | 4 | 2,408,839 B | 602,234 B | 1,613 km | 400 km | 221 km |
| 20 | 5 | 2,409,488 B | 481,866 B | 1,613 km | 400 km | 221 km |
| 10 | 10 | 2,419,912 B | 242,055 B | 1,613 km | 400 km | 221 km |

Current reading:

- shrinking the shared chunk size does not change interpolation quality for this export profile, as expected
- the compact format cuts the current `25` year baseline from about `812 KB gzip` per chunk in the older inspection format down to about `602 KB gzip`
- `25` years still looks like the strongest first delivery target because it cuts the largest download under `1 MB gzip` while increasing total gzip by only about `5 KB` over the `50` year case
- `20` and `10` years keep shrinking individual downloads, but the added request count starts to look less attractive for the first implementation
- a shared `25` year chunk plan is now the leading baseline for the web delivery format

## Planned Next Steps

1. Add kernel-derived physical metadata extraction with radii, axial tilt, and rotation period prioritized first.
2. Add local cache and CI kernel-acquisition documentation once the first generation workflow is implemented.
3. Record stronger source provenance and determinism details in the manifest output.
4. Evaluate whether numeric rounding or alternate packing is worth the added complexity after the metadata step.
5. Defer proper LSK-backed time conversion to a later milestone after the web data shape is settled.
