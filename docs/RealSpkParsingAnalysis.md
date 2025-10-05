# Real SPK Parsing Failure Analysis and Remediation Strategy

## Summary of the Symptom
Running the temporary diagnostic `Program.cs` against a known-valid real SPK kernel yields:
```
No covering segment found for supplied epoch.
```
Debugging shows that the parsed `SpkSegment` objects report **zero records** (or none are produced), so no state evaluation is possible. The root cause lies in the implementation of the real DAF/SPK readers (`FullDafReader` + `RealSpkKernelParser`). The current logic misinterprets the binary layout of SPK Type 2 & 3 segments (and partially the DAF summary records), producing incorrect coefficient spans and record counts.

---
## High?Level Cause Map
| Layer | Issue | Consequence |
|-------|-------|-------------|
| DAF File Record Parsing | Control words (NEXT, PREV, NSUM) treated as 32-bit ints at byte offsets 0,8,16 instead of 8?byte double words (the NAIF spec stores them as double precision values) | Potential misalignment of subsequent summary decoding; fragile but may appear to work for some little-endian files by accident |
| Summary Record Unpacking | Assumes direct 32-bit integer fields; does not explicitly treat first 3 *double* words; integer packing logic ok, but relies on initial misinterpretation | Risk of silent corruption with some kernels or on big-endian input |
| Segment Enumeration | De?duplicates by name (`seenNames`) which can hide later higher-priority overlapping segments having identical names | Potentially discards valid segments |
| Endianness Handling | Only little-endian doubles actually handled in element reads; big-endian branch incomplete (only summary path partially guarded) | Big-endian kernels would parse garbage; may appear as zero records |
| SPK Segment Data Range Usage | For Type 2/3 the segment data layout is: N records followed by a 4-double directory (INIT, INTLEN, RSIZE, N). Parser treats **the entire address span** as pure records | Record inference fails (extra 4 doubles make totalDoubles % perRecord != 0) ? zero records inferred |
| Record Structure Inference | Brute force search 50?1 for a divisor of totalDoubles; does not subtract trailer, does not validate RSIZE, N | Returns 0/0/0/0 for valid segments |
| Lazy Record Header Reads | Uses `initial + r * per` as starting 1-based address for record header but does not adjust for (possibly) trailer or verify bounds; also assumes data source addressing semantics | Incorrect MID/RADIUS extraction or out-of-range reads |
| Address / Byte Mapping | ReadDoubleRange recomputes record & word each iteration: correct in principle, but always uses little-endian conversion (no branch for `_isLittleEndian == false`) | Wrong values on big-endian kernels |
| MID/RADIUS Extraction | Extracts first two doubles of each *assumed* record; because records mis-counted, arrays stay empty or mis-sized | Evaluator sees `RecordCount == 0` |

---
## Canonical SPK Type 2 / 3 Segment Layout (Per NAIF SPK Required Reading)
For Types 2 & 3 (uniform record intervals):
```
Record[0]
Record[1]
...
Record[N-1]
INIT
INTLEN
RSIZE
N
```
Where each Record has:
- MID
- RADIUS
- (Type 2) 3 * (DEG+1) position coefficient sets
- (Type 3) 6 * (DEG+1) position+velocity coefficient sets

Thus: `RSIZE = 2 + K*(DEG+1)` where K = 3 (Type 2) or 6 (Type 3)
And total segment double count = `N*RSIZE + 4` (directory trailer). Initial / final descriptor integer addresses cover this whole block.

### What Our Parser Currently Does
- Treats `totalDoubles = final - initial + 1` as pure coefficient area
- Attempts `totalDoubles % (2 + K*(deg+1)) == 0` ? fails because of the `+4` trailer
- Falls through returning `RecordCount = 0`

### Correct Approach
```text
payloadDoubles = totalDoubles - 4
if (payloadDoubles % RSIZE != 0) -> invalid segment
recordCount = payloadDoubles / RSIZE
Extract records from first recordCount*RSIZE doubles
Read trailer: INIT, INTLEN, RSIZE(trailer), N(trailer)
Cross?validate:
  RSIZE(trailer) == computed RSIZE
  N(trailer) == recordCount
  start/stop span consistent with INIT + (N*INTLEN)
```

---
## Detailed Defects & Fix Plan
### 1. DAF Summary Record Control Area Parsing
**Defect:** Reads 32-bit ints at offsets 0,8,16 instead of reading 8?byte words #0,#1,#2 (bytes 0..7,8..15,16..23) as doubles representing integer values.

**Fix:**
- Read three 8-byte words; convert each to double then cast to int (after range check). Example: `int next = (int)BitConverter.Int64BitsToDouble(ReadInt64(wordSpan));` (guard for fractional or out-of-range values).
- Remove assumption of 32-bit alignment inside record.

### 2. Big-Endian Support
**Defect:** Element reads & doubles decoding ignore big-endian path.

**Fix:**
- Centralize double read: `ReadDouble(wordBytesSpan, isLittle)`
- Apply everywhere (file record, summary, elements, coefficient fetch).

### 3. Segment Name Deduplication
**Defect:** `seenNames` filters later segments with same name, violating SPK precedence semantics.

**Fix:** Remove de-dup filtering; yield all summaries in sequence.

### 4. Type 2/3 Record Inference
**Defect:** Ignores 4-double trailer.

**Fix:**
- Adjust inference: `payload = total - 4`; if `< 0` invalid.
- Iterate degree; compute `rs = 2 + k*(deg+1)`; if `payload % rs == 0` candidate.
- After loop, read trailer to verify `RSIZE` and `N` fields; fallback invalid if mismatch.
- Store trailer values (INIT, INTLEN) in `SpkSegment` for future interval mapping (will be needed for precise coverage / interpolation, caching).

### 5. Lazy Record Access Offsets
**Defect:** Offsets not excluding trailer; potential off-by-4*8 bytes.

**Fix:** Limit record header reads to `initial .. initial + recordCount*RSIZE - 1` address span.

### 6. Validation Additions
Add explicit checks:
- `(final - initial + 1) == recordCount*RSIZE + 4`
- `N (trailer) == recordCount`
- `RSIZE (trailer) == RSIZE computed`
- `start <= stop` and (optionally) `stop - start` consistent with `INTLEN * recordCount` within numeric tolerances (not strictly required—some official kernels have slight extension at boundary).

### 7. Diagnostic Enhancements (Short Term)
Extend the temporary CLI tool to emit for each segment:
```
Segment: target=center= frame= type=
  DAF addresses: initial..final  (count doubles)
  Raw start/stop ET: start stop
  Trailer: INIT INTLEN RSIZE N
  Computed: recordCount degree componentsPerSet
```
So misalignment visibly surfaces.

---
## Proposed Stepwise Remediation
| Step | Change | Rationale | Risk |
|------|--------|-----------|------|
| 1 | Refactor DAF control word parsing to 8-byte words | Correct spec compliance | Low |
| 2 | Implement unified endian-aware double/int readers | Future-proofs real kernels | Low |
| 3 | Remove segment name de-duplication | Preserve precedence | Low |
| 4 | Rework Type 2/3 inference (subtract trailer; parse & validate trailer) | Enables non-zero records | Medium |
| 5 | Add INIT/INTLEN/RSIZE/N storage to `SpkSegment` | Needed for validation & future interval search | Low |
| 6 | Fix lazy path address math (exclude trailer) | Ensure MID/RADIUS accurate | Medium |
| 7 | Add diagnostics to CLI | Aids validation | Low |
| 8 | Add unit tests using a small real SPK slice verifying: recordCount, degree, sample state vs CSPICE | Prevent regressions | Medium |

---
## Unit Test Additions (Outline)
1. **Segment Metadata Test**
   - Load truncated known DE/SPK excerpt
   - Assert each segment `RecordCount > 0`, `Degree > 0`, `Trailer.N == RecordCount`
2. **Trailer Consistency Test**
   - Check `(final - initial + 1) == RecordCount * RSIZE + 4`
3. **State Evaluation Sanity**
   - Pick mid epoch of first record; evaluate state; ensure position magnitude within expected planetary distance range (e.g., Earth ~1 AU ± 0.2 AU) to catch byte-order swaps.
4. **Cross Reference vs CSPICE (if available)**
   - For a handful of epochs across two records compare to precomputed vectors (tolerance ~1e-12 km for first pass if using same kernel).

---
## Data Source Interface Consideration
`IEphemerisDataSource.ReadDoubles(startAddress, buffer)` semantics must be **explicitly documented** re: 1-based vs 0-based addresses. Current parser passes `initial + r*recordSize` directly; if underlying implementation assumes 1-based indexing this is fine; if 0-based it is shifted by +1 word. Confirm & standardize (prefer 1-based to match NAIF docs internally; convert at boundary).

---
## Longer-Term Improvements (Post-Fix)
- Support additional SPK data types (5, 14, 20) once core fidelity validated.
- Introduce segment interval index (Prompt 18) using INIT/INTLEN for O(log N) lookup without linear scan.
- Implement streaming / windowed coefficient access to avoid materializing large multi-GB kernels.
- Provide optional checksum / integrity validation (compare directory math) during load.

---
## Immediate Action Checklist
- [ ] Patch `FullDafReader` (control words, endianness, remove name de-dup)
- [ ] Patch `RealSpkKernelParser` (trailer handling, record inference, lazy offsets, trailer capture)
- [ ] Extend `SpkSegment` to store: `Init`, `IntLen`, `RSize`, `TrailerN`
- [ ] Add CLI diagnostic printing segment structural info
- [ ] Add unit tests for trailer math + non-zero records
- [ ] Verify initial real kernel slice passes tests
- [ ] Implement CSPICE comparison test (optional initial; mandatory before precision claims)

---
## Conclusion
The zero-record outcome is a deterministic consequence of treating the final 4-double Type 2/3 trailer as part of the repeating record structure—making the total coefficient count non-factorable by the per-record formula—combined with summary parsing shortcuts that could silently misalign data. Correcting record boundary math, validating trailer metadata, and aligning with the DAF spec will surface real segment structure and enable state evaluation.

Once fixed, subsequent work (indexing, performance, broader type support) can proceed on a solid, spec-faithful foundation.

---
*Prepared for internal remediation planning — aligns with Phase 2 Prompt 14 objectives.*
