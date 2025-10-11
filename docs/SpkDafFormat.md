# SPK / DAF Binary Layout (Concise Reference)

This document summarizes the subset of the NAIF DAF + SPK specification implemented by the current SpiceNet codebase (Types 2 & 3 only). It complements the authoritative NAIF Required Reading files (`daf.req`, `spk.req`). Non?implemented details are intentionally omitted.

> Barycentric Chaining Note (Prompt 26 D3)
> The service resolves relative states generically via Solar System Barycenter (SSB id 0) composition:`state(target,center)=state(target,SSB)-state(center,SSB)`. No legacy Earth/Moon (EMB/EMRAT) special-case path remains; all chaining uses the same recursion with a cycle guard.

## DAF Concepts
- Record size: 1024 bytes (128 double precision words)
- Addressing: 1-based double word index over entire file (word 1 is first 8 bytes of record 1)
- File structure (physical records):
  1. File record (record 1)
  2. Reserved / comment records (0+; NOT parsed currently)
  3. Summary / Name record pairs (doubly linked list)
  4. Element (data) records (Chebyshev coefficients etc.)

### File Record (record 1)
Offsets (bytes) relative to start of file record:
- 0  (8 bytes)  IDWORD e.g. DAF/SPK?
- 8  (4)        ND (# double components in summary)
- 12 (4)        NI (# integer components in summary)
- 16 (60)       Internal file name (space padded)
- 76 (4)        FWARD (record number of first summary record) 0 => none
- 80 (4)        BWARD (record number of last summary record) 0 => none
- 84 (4)        FREE  (first free address; currently ignored)
Remaining fields (LOC* / FTPSTR / padding) ignored in current implementation.

### Summary Record
A summary record holds:
- Control area (first 3 double words): NEXT, PREV, NSUM stored as IEEE 754 double values whose integer parts encode the integers. Synthetic little-endian 32-bit encoding (low half only) also accepted (unit tests).
- Packed summaries begin at word index 4.
- Each packed summary consumes: `SS = ND + ((NI + 1) / 2)` double words.
- Maximum summaries per record: `floor(125 / SS)` (since 3 words for control).

### Packed Summary Layout
Given ND, NI:
- First ND double words: DC components (double precision)
- Following ceil(NI / 2) double words: each stores two 32-bit signed integers (IC components) packed high/low (endianness-dependent). If NI is odd final 32-bit slot unused.

Current assumptions (real planetary SPK typical): ND=2, NI=6 so:
- DC[0] = segment start ET (seconds past J2000 TDB)
- DC[1] = segment stop ET
- IC[0] = target body ID
- IC[1] = center body ID
- IC[2] = reference frame ID
- IC[3] = data type (2 or 3 supported)
- IC[4] = initial data address (1-based) of coefficient payload
- IC[5] = final data address (1-based) inclusive

A paired name record immediately follows each summary record; it contains contiguous fixed-length (NC) character names for each summary. For ND=2, NI=6 => NC = 40.

## SPK Segment (Types 2 & 3)
Within address range [INITIAL .. FINAL] (inclusive) the segment data are laid out as:
- N uniform-size logical records (Chebyshev sets)
- 4-double trailer: `[INIT, INTLEN, RSIZE, N]`

Where:
- INIT: Initial epoch of first record coverage (seconds past J2000)
- INTLEN: Interval length (seconds) covered by each record (uniform)
- RSIZE: Number of double words per logical record
- N: Number of logical records

### Logical Record Structure
```
[ MID, RADIUS, coeffs... ]
```
- MID: midpoint epoch of record coverage (seconds past J2000)
- RADIUS: half-length of coverage interval (seconds). Mapping from epoch t to normalized Chebyshev domain: tau = (t - MID) / RADIUS.

Coefficients:
- Type 2: 3 component sets (X,Y,Z) position only
- Type 3: 6 component sets (X,Y,Z, dX/dt, dY/dt, dZ/dt) position + velocity

Degree determination:
For K = 3 (type 2) or 6 (type 3)
```
RSIZE = 2 + K * (DEG + 1)
=> DEG = (RSIZE - 2) / K - 1
```
Validate `(RSIZE - 2) % K == 0` and DEG >= 0.

### Evaluation
Type 2:
1. Locate record where t ? [MID - RADIUS, MID + RADIUS]. Uniform spacing allows index ? floor((t - INIT)/INTLEN) (code still validates by interval test).
2. Compute tau and evaluate Chebyshev polynomials for position components.
3. Velocity derived by differentiating polynomial: d/dt = (1/RADIUS) * d/dtau.

Type 3:
Same as Type 2 but velocity polynomials provided directly; evaluate without differentiation.

## Endianness Handling
- ND/NI read in both little & big forms; plausible range 0 < value < 256 selects active byte order.
- All subsequent 64-bit words are byte-swapped if file endianness differs from host little-endian.
- Packed integer pairs extracted from each 8-byte word by reading two 32-bit values under active endianness.

## Control Word Fallback Logic
1. Interpret 8 bytes as double; if integral within 1e-12 use that.
2. If upper 32 bits zero and lower non-zero treat lower 32 bits as synthetic int.
3. Otherwise fallback to lower 32 bits.

## Address Math Helper
For 1-based word address A:
```
recordIndex = (A - 1) / 128
wordInRecord = (A - 1) % 128
byteOffset = recordIndex * 1024 + wordInRecord * 8
```

## Not Implemented Yet
- Additional SPK segment types (5, 13, 20, 21)
- Comment area parsing
- Free address validation / integrity checks
- Backward traversal using PREV control word
- Chained segment precedence heuristics beyond current latest-start selection

## Validation Heuristics in Code
- Reject segments where `(final - initial + 1) != RSIZE * N + 4`
- Ensure `Degree >= 0`
- Ensure NSUM within reasonable cap (? 10000)

## References
- NAIF DAF Required Reading (daf.req)
- NAIF SPK Required Reading (spk.req)

Authoritative semantics remain with NAIF documentation.
