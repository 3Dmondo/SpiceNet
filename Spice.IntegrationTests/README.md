# Spice.IntegrationTests

Integration (golden) tests against JPL planetary ephemeris testpo reference data and corresponding SPK (DE) kernels.

## Overview
These tests:
1. Download (if missing) selected `testpo.<eph>` ASCII reference files.
2. Download (if permitted) corresponding `de<eph>.bsp` (or small variant) SPK kernel under a local cache directory.
3. Parse reference components (position + velocity) and compare interpolated SpiceNet results (SPK Types 2/3) at the same epochs.
4. Assert numerical error bounds expressed primarily in AU (positions) and AU/day (velocities) with dynamic fallback tolerance logic.

Large ephemerides (>150 MB) are skipped unless explicitly allowed.

## Units & Normalization
The JPL `testpo` files express:
- Position components in Astronomical Units (AU)
- Velocity components in AU/day

SpiceNet state vectors are produced in km (position) and km/s (velocity). For comparison we normalize:
```
position_AU   = state.PositionKm / AU_km
velocity_AU_d = state.VelocityKmPerSec / (AU_km / 86400)   // equivalently velocityKmPerSec * 86400 / AU_km
```
`AU_km` is extracted from the BSP comment area (symbol `AU`) when available.

## Current Tolerance Model
Strict tolerances (AU constant found in BSP comments):
- Position: `1e-13` AU  (? 1.50e-5 km ? 1.5 cm)
- Velocity: `1e-16` AU/day (? 1.73e-13 km/s)

Relaxed tolerances (AU constant missing): multipliers ×10,000
- Position: `1e-9` AU  (? 149.6 m)
- Velocity: `1e-12` AU/day (? 1.73e-9 km/s)

Additional early ephemeris relaxation (ephemeris number starting with `2`) multiplies the *relaxed* bounds by a further ×100 (overall ×1,000,000 vs strict):
- Position: `1e-7` AU  (? 14.96 km)
- Velocity: `1e-10` AU/day (? 1.73e-11 km/s)

These adaptive rules prevent spurious failures for legacy or incomplete kernels while retaining centimeter-level targets when full metadata (AU) is present.

> Long-term goal: converge on universal km / km/s bounds of ?1e-6 km and ?1e-9 km/s once all scaling & special-case handling is stabilized.

## Earth / Moon Special Handling
`EphemerisService` derives Earth and Moon barycentric states using the Earth-Moon barycenter (EMB) and the mass ratio `EMRAT` (also parsed from BSP comments):
```
Earth_bary = EMB - Moon_geo / (1 + EMRAT)
Moon_bary  = Earth_bary + Moon_geo
```
This reduces large residuals for testpo target/center pairs involving Earth (testpo code 3) and Moon (10). The parser remaps:
- Earth: 3 ? 399
- Moon:  10 ? 301
prior to state resolution.

## Cache Layout (gitignored)
```
Spice.IntegrationTests/
  TestData/
    cache/
      de440/
        testpo.440
        de440.bsp (or de440s.bsp)
        meta.json
```

## Environment Variables
| Variable | Purpose | Default |
|----------|---------|---------|
| `SPICE_EPH_LIST` | Comma separated ephemeris numbers (e.g. `440,430`) to test | Predefined small set (? ~150MB) |
| `SPICE_ALLOW_LARGE_KERNELS` | If set (any value) permits downloading >150MB BSPs | Disabled |
| `SPICE_TESTPO_MAX_STATES` | Max components sampled per ephemeris (lines) | 50 |
| `SPICE_INTEGRATION_CACHE` | Override cache root path | Project `TestData/cache` |
| `SPICE_NET_ENABLE_INTEGRATION` | If unset, tests are skipped (opt?in) | Skipped |

## Adding New Ephemeris
1. Append entry to `EphemerisCatalog` (size bytes, preferred BSP variant if small exists).
2. Re-run with `SPICE_EPH_LIST` including the new number.
3. Verify AU & EMRAT are present in the BSP comment area for strict tolerance mode.

## Notes / Limitations
- Only SPK Types 2 & 3 currently parsed (sufficient for planetary DE kernels in scope).
- No light?time, aberration, or relativistic corrections (pure geometric states matching testpo assumptions).
- Time conversion: simple JD?TDB seconds mapping; higher-order TT?TDB modeling pending roadmap item 19.
- Velocity parity still under review for legacy kernels; strict regime emphasizes position fidelity first.

## Future Enhancements
- Code/center inventory & external mapping manifest.
- Parallelized comparison pass.
- Structured JSON summary (max/mean/RMS) for CI trend tracking.
- Diagnostic CLI (segment coverage + residual inspection).
- Tightened universal km-based tolerances after full validation.

## Failure Reporting
On assertion failure only (target, center, component) rows exceeding their dynamic tolerance are printed, including:
```
Target Center Comp Count MaxErr MeanErr WorstET ReferenceValue PredictedValue
```
Errors are absolute differences in AU (position) or AU/day (velocity).

## Source Data
JPL testpo reference data: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/

## Contributing Guidance
- Keep tolerance changes minimal & document rationale.
- Avoid hard-coding AU unless absolutely necessary; prefer parsed symbol.
- Add unit tests for new symbol extraction or special-case logic.