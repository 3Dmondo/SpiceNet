# Spice.IntegrationTests

Integration (golden) tests against JPL planetary ephemeris testpo reference data and corresponding SPK (DE) kernels.

## Overview
These tests:
1. Download (if missing) selected `testpo.<eph>` ASCII reference files.
2. Download (if permitted) corresponding `de<eph>.bsp` (or small variant) SPK kernel under a local cache directory.
3. Parse reference components (position + velocity) and compare interpolated SpiceNet results (SPK Types 2/3) at the same epochs.
4. Assert numerical error bounds using centralized `TolerancePolicy` (referenced indirectly; see linked tolerance documentation).

Large ephemerides (>150 MB) are skipped unless explicitly allowed.

## Units & Normalization
JPL `testpo` files:
- Position: AU
- Velocity: AU/day

SpiceNet returns km and km/s. Normalization:
```
position_AU   = positionKm / AU_km
velocity_AU_d = velocityKmPerSec / (AU_km / 86400)
```
`AU_km` taken from BSP comments (symbol `AU`) when present; else canonical IAU value.

## Body Code Mapping
Remaps (e.g. 3?399 Earth, 10?301 Moon) externalized in `TestData/BodyMapping.json` and applied by the parser. No inline remap logic remains.

## Tolerances
The single authoritative tolerance specification lives only in `../docs/Tolerances.md`. This README intentionally does not duplicate the table. All tests obtain limits via `TolerancePolicy.Get(ephemerisNumber, hasAuConstant)`; no hard-coded tolerance literals appear here.

## Barycentric Composition
Generic SSB chaining:
```
state(target, center) = state(target, 0) - state(center, 0)
```
No special Earth/Moon path retained.

## Cache Layout (gitignored)
```
Spice.IntegrationTests/
  TestData/
    cache/
      de440/
        testpo.440
        de440.bsp
        comparison_stats.440.json (generated)
```

## Environment Variables
| Variable | Purpose | Default |
|----------|---------|---------|
| `SPICE_EPH_LIST` | Comma separated ephemeris numbers | Curated small set |
| `SPICE_ALLOW_LARGE_KERNELS` | Permit downloading >150MB BSPs | Disabled |
| `SPICE_TESTPO_MAX_STATES` | Max components sampled per ephemeris | 50 |
| `SPICE_INTEGRATION_CACHE` | Override cache root | `TestData/cache` |
| `SPICE_NET_ENABLE_INTEGRATION` | Enable integration tests | Off (skip) |

## Adding New Ephemeris
1. Add catalog entry.
2. Include number in `SPICE_EPH_LIST`.
3. Confirm AU symbol presence (affects tolerance tier).

## Stats Artifact
Each comparison run emits per-ephemeris JSON (`comparison_stats.<eph>.json`) with deterministic key ordering for future regression tracking.

## Failure Reporting
On assertion failure only, offending (target, center, component) rows exceeding tolerance are listed with worst sample details.

## Source Data
JPL testpo: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/

## Contributing Guidance
- Modify tolerances only via `TolerancePolicy` and update `docs/Tolerances.md` (no duplication here).
- Extend mapping via JSON + add validation tests.
- Keep stats schema stable; evolve additively.