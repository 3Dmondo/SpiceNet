# Spice.IO

Low-level kernel access layer (internal implementation for the `Spice` library).

Responsibilities
- DAF Binary Reader (`FullDafReader`) per NAIF spec.
- Endianness detection and coefficient byte-swapping.
- Segment enumeration and address handling.
- Ephemeris data sources (stream / memory-mapped).

Notes
- Internal implementation detail; not published.
