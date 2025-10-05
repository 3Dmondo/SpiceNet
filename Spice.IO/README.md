# Spice.IO
Low-level kernel access layer.

## Responsibilities
- **DAF Binary Reader** (`FullDafReader`): traverses doubly-linked summary/name record chain (NEXT, PREV, NSUM) per NAIF spec.
- **Endianness Detection**: Heuristic via ND / NI plausibility; supports little & big endian numeric payloads (byte swapping for coefficients when required).
- **Segment Enumeration**: Exposes raw segment (array) descriptor components (DC, IC) + initial/final word addresses for higher layers.
- **Ephemeris Data Sources**: `IEphemerisDataSource` abstraction with implementations:
  - Stream (sequential / on-demand)
  - Memory-mapped (fast random access)
- **Addressing Model**: 1-based double word addressing; record size = 128 8-byte words (1024 bytes). Word ? byte: `(addr-1)/128 * 1024 + ((addr-1)%128)*8`.
- **Control Word Decoding**: Supports spec-compliant double precision control words and legacy synthetic 32-bit test encoding fallback.

## Non-Goals
- No interpolation or time math
- No meta-kernel logic
- No frame / body metadata resolution

## Upcoming Enhancements
- Buffered batched range reads (vectorized) for large span evaluation
- Optional pooled scratch buffers for bulk coefficient extraction
- Validation utilities (dump summaries / coverage to diagnostics)

See `../docs/SpkDafFormat.md` for a concise layout reference.
