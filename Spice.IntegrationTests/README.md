# Spice.IntegrationTests

Integration (golden) tests against JPL planetary ephemeris testpo reference data and corresponding SPK (DE) kernels.

## Overview
These tests:
1. Download (if missing) selected `testpo.<eph>` ASCII reference files.
2. Download (if permitted) corresponding `de<eph>.bsp` (or small variant) SPK kernel under a local cache directory.
3. Parse reference states (position+velocity) and compare interpolated SpiceNet results (SPK Types 2/3) at the same epochs.
4. Assert numerical error bounds (initial target):
   - |?r| ? 1e-6 km
   - |?v| ? 1e-9 km/s

Large ephemerides (>150 MB) are skipped unless explicitly allowed.

## Cache Layout (gitignored)
```
Spice.IntegrationTests/
  TestData/
    cache/
      de440/
        testpo.440
        de440.bsp (or de440s.bsp if preferred)
        meta.json
```

## Environment Variables
| Variable | Purpose | Default |
|----------|---------|---------|
| SPICE_EPH_LIST | Comma separated ephemeris numbers (e.g. `440,430`) to test | Predefined small set (?150MB) |
| SPICE_ALLOW_LARGE_KERNELS | If set (any value) permits downloading >150MB BSPs | Disabled |
| SPICE_TESTPO_MAX_STATES | Max states sampled per ephemeris | 50 |
| SPICE_INTEGRATION_CACHE | Override cache root path | Project `TestData/cache` |
| SPICE_NET_ENABLE_INTEGRATION | If unset, tests are skipped (opt?in) | Skipped |

## Adding New Ephemeris
1. Append entry to `EphemerisCatalog` (size bytes, preferred BSP file name if small variant exists).
2. Re-run tests with updated `SPICE_EPH_LIST` if limiting scope.

## Notes / Limitations
- Only SPK Types 2 & 3 currently parsed; acceptable for DE series planetary kernels.
- No light-time or aberration corrections (pure geometric states) matching testpo expectations.
- Time conversion uses direct JD?TDB seconds mapping (testpo times are TDB midnights).

## Future Enhancements
- Parallelized comparison pass (per ephemeris) to reduce wall clock time.
- Persist summary statistics for regression trending.
- Optional CSPICE cross-check (if user supplies toolkit).