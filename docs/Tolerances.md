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
