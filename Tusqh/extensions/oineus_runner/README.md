# Tusqh Oineus runner

This is the Oineus counterpart to `extensions/aleph_runner`: same input files,
same output layout, different (parallel) reduction backend. It reads either
the compact binary format produced by the Grasshopper `Export Mesh to Aleph
Compact` component or the legacy text format produced by `Export Mesh to
Aleph Opt` -- the exact same files `aleph_runner` reads, no export-side
changes needed -- and computes persistence diagrams with Oineus instead of
Aleph.

## Why this exists, and what's different from Aleph

Aleph's `SimplicialComplex` has `createMissingFaces()` and
`recalculateWeights()`, which take a set of tetrahedra plus per-vertex
values and derive every edge/triangle, each with a filtration value computed
from its vertices. Oineus's `Filtration` has no equivalent -- it expects an
already face-closed list of cells, each with its value already assigned (see
`docs/topics/filtrations.md` in the Oineus checkout). So `oineus_runner`
does that face-closure itself: for every tetrahedron it enumerates its 6
edges, 4 triangles, and the tetrahedron itself, computes each cell's value
from its vertices' values, and deduplicates the (numerous, since faces are
shared between neighboring tetrahedra) repeats by combinatorial uid before
handing the finished cell list to Oineus.

The per-cell value convention matches Aleph's exactly, verified directly
against `include/oineus/grid.h`'s `simplex_value_and_vertex` (the
Freudenthal-grid code path this port's own smoke test and Catch2 suite
already validated end to end), not assumed from either library's
terminology: `negate=false` (Aleph's *sublevel*) takes the **max** of a
cell's vertex values with ascending filtration order; `negate=true` (Aleph's
*superlevel*) takes the **min** with descending order. So `oineus_runner`'s
internal `negate` flag is exactly `aleph_runner`'s `superlevel` flag, not
its opposite, despite the different name.

## Build on Windows

Prerequisite: the vcpkg install at `C:/vcpkg` needs the Boost ports Oineus's
bundled `hera` library and its own 128-bit uid encoding need. As set up for
this port:

```powershell
C:\vcpkg\vcpkg.exe install --triplet x64-windows `
  boost-multiprecision boost-iterator boost-array boost-range `
  boost-serialization boost-random boost-foreach boost-heap `
  boost-smart-ptr boost-static-assert boost-type-traits
```

From the Tusqh repository root:

```powershell
cmake -S Tusqh/extensions/oineus_runner `
      -B Tusqh/extensions/_build/oineus_runner `
      -G "Visual Studio 18 2026"

cmake --build Tusqh/extensions/_build/oineus_runner `
      --config Release --parallel
```

The executable is written to:

```text
Tusqh/extensions/_build/oineus_runner/Release/tusqh_oineus_runner.exe
```

`OINEUS_SOURCE_DIR` and `VCPKG_INSTALLED_DIR` are CMake cache variables if
the local dependency directories differ from the defaults.

## Run

```powershell
Tusqh/extensions/_build/oineus_runner/Release/tusqh_oineus_runner.exe `
  C:\path\to\grasshopper_export.txt `
  C:\path\to\results\dragon `
  --threads 8
```

For a compact input, the runner uses the superlevel/sublevel setting stored
in the file header, same as `aleph_runner`. `--superlevel` or `--sublevel`
explicitly overrides it. Legacy text input defaults to superlevel, also
matching `aleph_runner`.

Outputs are the same shape as `aleph_runner`'s:

```text
dragon_d0.txt
dragon_d1.txt
dragon_d2.txt
dragon_d3.txt
dragon_timings.tsv
```

(`_d3.txt` is new relative to Aleph's output: tetrahedra are dimension-3
cells, and Aleph's dualized reduction happened to only ever emit dimensions
0-2 for tetrahedral input in `aleph_runner`'s existing usage.)

`--dualize` mirrors Aleph's dualized boundary matrix, but is opt-in here
(default off) rather than opt-out: this port's own validation (a standalone
smoke test and Oineus's own Catch2 `tests_reduction.cpp` suite, both run
against the patched headers) only ever exercised `dualize=false`, so that's
the default; `--dualize` is available but unverified. `--keep-diagonal`
matches Aleph's flag exactly. `--threads N` controls the reduction's actual
thread count (default: hardware concurrency) -- unlike Aleph, whose
`reduce_and_pair` stage is serial regardless of thread count, this is the
whole point of using Oineus.

The timing file separates input parsing, cell construction (face
enumeration + dedup), filtration construction (sort + uid index), reduction,
diagram construction, output, and total wall time, plus the actual thread
count used.

## Validating against Aleph

Since both runners read the same input file, the direct way to check a
result is to run both on the same file and compare the birth/death pairs in
each `_d{N}.txt` (allowing for the fact that `oineus_runner` also emits a
`_d3.txt`, and that floating-point values may differ in the last few digits
between the two independent reduction implementations). This has not yet
been done on real Tusqh mesh data as of the initial port -- only on
`aleph_runner`'s own `example_single_tetra.txt` / `example_single_tetra_compact.hex`
fixtures and Oineus's own Catch2 test suite.
