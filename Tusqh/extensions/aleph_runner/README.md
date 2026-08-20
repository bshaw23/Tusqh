# Tusqh Aleph runner

This command-line program reads either the compact binary format produced by
the Grasshopper `Export Mesh to Aleph Compact` component or the legacy text
format produced by `Export Mesh to Aleph Opt`, then computes persistence
diagrams with Aleph. Input format detection is automatic.

The existing file contains explicit `verts`, `edges`, `faces`, and `tets`
sections. The runner reads the vertex values and tetrahedra, skips the redundant
edge/face records, reconstructs all missing faces with Aleph, and recomputes
their filtration values from the vertices. Tetrahedron indices in this format
are one-based.

The compact format stores only the data Aleph actually needs: one float32
filtration value per vertex followed by four zero-based uint32 indices per
tetrahedron. It does not contain redundant edges, faces, or tetrahedron values.
The Grasshopper component rounds values to four decimal places before storage,
matching the existing optimized text exporter's filtration semantics.

## Compact binary format v1

All integer and floating-point fields are little-endian:

```text
8 bytes   magic: TQALPH1 followed by a zero byte
uint32    format version (1)
uint32    flags: bit 0 = superlevel; bits 8-9 = filtration coordinate
uint32    vertex count
uint64    tetrahedron count
float32[] vertex filtration values
uint32[]  four zero-based indices per tetrahedron
```

The fixed header is 28 bytes. The exact file size is therefore
`28 + 4 * vertices + 16 * tetrahedra` bytes. The runner validates the header,
exact file length, finite values, index ranges, and degenerate tetrahedra.

## Build on Windows

From the Tusqh repository root:

```powershell
cmake -S Tusqh/extensions/aleph_runner `
      -B Tusqh/extensions/_build/aleph_runner `
      -G "Visual Studio 18 2026"

cmake --build Tusqh/extensions/_build/aleph_runner `
      --config Release --parallel
```

The executable is written to:

```text
Tusqh/extensions/_build/aleph_runner/Release/tusqh_aleph_runner.exe
```

`ALEPH_SOURCE_DIR` and `BOOST_ROOT` are CMake cache variables if the local
dependency directories differ from the defaults.

## Run

```powershell
Tusqh/extensions/_build/aleph_runner/Release/tusqh_aleph_runner.exe `
  C:\path\to\grasshopper_export.txt `
  C:\path\to\results\dragon
```

For a compact input, the runner uses the superlevel/sublevel setting stored in
the file header. `--superlevel` or `--sublevel` explicitly overrides it. Legacy
text input defaults to superlevel filtration. The remaining defaults are a
dualized boundary matrix and removal of zero-persistence points.

Outputs are:

```text
dragon_d0.txt
dragon_d1.txt
dragon_d2.txt
dragon_timings.tsv
```

Use `--help` to see optional sublevel, non-dualized, top-dimensional, and
keep-diagonal modes.

The timing file separates input parsing, complex creation, missing-face and
weight reconstruction, filtration sorting, boundary-matrix construction,
dualization, reduction/pairing, diagram construction, output, and total wall
time. The runner inserts input simplices into Aleph in bounded batches and
releases the raw vertex/tetrahedron arrays before missing faces are generated,
reducing peak transient memory. Aleph's boundary-matrix reduction is serial;
OpenMP support elsewhere in Aleph does not parallelize the `reduce_and_pair`
stage.
