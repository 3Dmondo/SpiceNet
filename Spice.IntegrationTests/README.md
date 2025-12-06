# Spice.IntegrationTests

Integration tests against JPL planetary ephemeris `testpo` reference data and corresponding SPK (DE) kernels.

Purpose
- Download (if missing) selected `testpo.<eph>` ASCII reference files.
- Optionally download BSP kernels (size limit enforced).
- Parse reference components and compare interpolated results from the `Spice` library at the same epochs.
- Assert numerical error bounds using centralized tolerance policy.

Notes
- This project is not published; it serves validation only.
- Cache layout and environment variables are documented in `docs/integrationTests.md`.