# Spice.IntegrationTests

Integration (golden) tests against JPL planetary ephemeris testpo reference data and corresponding SPK (DE) kernels.

## Overview
These tests:
1. Download (if missing) selected `testpo.<eph>` ASCII reference files.
2. Download (if permitted) corresponding `de<eph>.bsp` (or small variant) SPK kernel under a local cache directory.
3. Parse reference components (position + velocity) and compare interpolated SpiceNet results (SPK Types 2/3) at the same epochs.
4. Assert numerical error bounds using centralized `TolerancePolicy` (Prompt 26.A) with AU / AU/day domain tolerances.

Large ephemerides (>150 MB) are skipped unless explicitly allowed.

## Units & Normalization
The JPL `testpo` files express:
- Position components in Astronomical Units (AU)
- Velocity components in AU/day

SpiceNet state vectors are produced in km (position) and km/s (velocity). For comparison we normalize:
```
position_AU   = positionKm / AU_km
velocity_AU_d = velocityKmPerSec / (AU_km / 86400)   // equivalently velocityKmPerSec * 86400 / AU_km
```
`AU_km` is taken from BSP comments (symbol `AU`) when available; otherwise the IAU exact value (149,597,870.7 km) is used and strictness falls back per policy.

## Body Code Mapping
The original testpo convention uses integer codes (e.g., 3=Earth, 10=Moon) differing from NAIF IDs (399=Earth, 301=Moon). These remaps are now housed in `TestData/BodyMapping.json`:
```
[
  { "testpo": 3,  "naif": 399, "rationale": "Earth mapping" },
  { "testpo": 10, "naif": 301, "rationale": "Moon mapping" }
]
```
The parser loads this file at runtime; inline hard-coded remap logic has been removed.

## Tolerances (Centralized)
The canonical tolerance policy lives in `Spice.Core.TolerancePolicy` and is documented in `docs/Tolerances.md`. The snippet below is a verbatim copy (must remain byte-for-byte identical for future doc sync tests):

# Tolerances (Canonical Snippet)

This snippet is the single source of truth for golden comparison tolerances used by integration tests and documented in READMEs. Any copy elsewhere must be byte?for?byte identical (normalized line endings) and is validated by future doc sync tests.

Tolerance policy (fine?tuned) derives absolute tolerances in AU (position) and AU/day (velocity) from ephemeris series number and AU constant availability:

Policy tiers (when AU constant present in BSP comments):
- Modern High Fidelity (ephemeris > 414 and != 421): position 2e-14 AU, velocity 3e-17 AU/day (strict=true)
- Legacy Series (ephemeris ? 414): position 6e-14 AU, velocity 5e-14 AU/day (strict=false)
- Problematic Special Case (ephemeris == 421): position 2e-12 AU, velocity 5e-15 AU/day (strict=false) – accommodates known residual characteristics of DE421

Fallback (AU constant absent): position 5e-8 AU, velocity 1e-10 AU/day (strict=false)

Derived km & km/s tolerances use shared constants (`Constants.AstronomicalUnitKm`, `Constants.AuPerDayToKmPerSec`).

| Tier | Criteria | Position (AU) | Velocity (AU/day) | Strict |
|------|----------|---------------|-------------------|--------|
| Modern High Fidelity | AU present AND ephemeris > 414 AND ephemeris != 421 | 2e-14 | 3e-17 | Yes |
| Legacy Series | AU present AND ephemeris ? 414 (except 421) | 6e-14 | 5e-14 | No |
| Problematic (DE421) | AU present AND ephemeris = 421 | 2e-12 | 5e-15 | No |
| Fallback (No AU) | AU absent | 5e-8 | 1e-10 | No |

Rationale:
- Empirical residual analysis across supported DE4xx kernels shows distinct clustering; majority of modern kernels permit very tight bounds (2e-14 AU).
- DE421 exhibits larger systematic deviations; a looser dedicated band prevents noisy failures while still detecting regressions.
- Legacy (?414) kernels retain moderately relaxed bounds acknowledging historical numerical differences while remaining far tighter than earlier interim values.
- Absence of an AU constant implies incomplete metadata; wide fallback tolerances applied pending kernel enrichment.

Legacy AU constants: `Constants.LegacyDeAU` records per?ephemeris AU values used historically (sourced from kernel comments) enabling cross validation when BSP lacks explicit AU symbol.

Future Direction:
- Revisit DE421 special case if improved interpolation or time modeling narrows residuals.
- Introduce stats artifact (Prompt 26.C) to auto?propose tighter bounds when observed maxima < 50% of budget over sustained runs.

All tolerance literals are centralized in `TolerancePolicy.Get`; no other code should embed these numeric values.

## Barycentric Composition
Relative states are composed generically via Solar System Barycenter (SSB) chaining when a direct segment (target, center) is absent:
```
state(target, center) = state(target, 0) - state(center, 0)
```
Previous Earth/Moon EMB + EMRAT specific derivation path has been removed as obsolete; any future special handling will be reintroduced only if validated by comparison statistics.

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
| `SPICE_NET_ENABLE_INTEGRATION` | If unset, tests are skipped (opt-in) | Skipped |

## Adding New Ephemeris
1. Append entry to `EphemerisCatalog` (size bytes, preferred BSP variant if small exists).
2. Re-run with `SPICE_EPH_LIST` including the new number.
3. Verify AU symbol is present in BSP comment area for appropriate strictness tier.

## Notes / Limitations
- Only SPK Types 2 & 3 currently parsed (sufficient for planetary DE kernels in scope).
- No light-time, aberration, or relativistic corrections (pure geometric states matching testpo assumptions).
- Time conversion: simple JD?TDB seconds mapping; higher-order TT?TDB modeling pending roadmap item 19.
- Velocity parity still under review for legacy kernels; strict regime emphasizes position fidelity first.

## Future Enhancements
- Code/center inventory & extended mapping manifest.
- Parallelized comparison pass.
- Structured JSON summary (max/mean/RMS) for CI trend tracking.
- Diagnostic CLI (segment coverage + residual inspection).
- Potential tightening of legacy / fallback tiers after stats aggregation.

## Failure Reporting
On assertion failure only (target, center, component) rows exceeding their tolerance are printed, including:
```
Target Center Comp Count MaxErr MeanErr WorstET ReferenceValue PredictedValue
```
Errors are absolute differences in AU (position) or AU/day (velocity).

## Source Data
JPL testpo reference data: https://ssd.jpl.nasa.gov/ftp/eph/planets/test-data/

## Contributing Guidance
- Tolerance changes require updating `TolerancePolicy`, this README, and `docs/Tolerances.md` (kept identical).
- Mapping additions require editing `BodyMapping.json` and adding/adjusting tests.
- Add unit tests for new symbol extraction or mapping logic.