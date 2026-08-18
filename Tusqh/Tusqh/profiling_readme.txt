Tusqh Profiling Guide
=====================

1. Build and load the Release plugin
------------------------------------

Close Rhino before rebuilding because Rhino locks loaded plugin DLLs.

From this directory, run:

    dotnet build Tusqh.sln -c Release --no-restore

The normal plugin output is:

    bin\Release\net7.0\Tusqh.gha

The managed Tusqh plugin and wrapper should report Configuration=Release and
Optimize=true. The native libigl wrapper is built separately. If its C++ source
has changed, run this first:

    ..\libigl_wrapper\build_native.bat

Then rebuild Tusqh. Restart Rhino so Grasshopper loads the new files.


2. Display component timings in Grasshopper
--------------------------------------------

The following components have a final output named "time":

    Background and Volumes
    Dual 3D
    Tri3D
    Export Mesh to Aleph
    Export to SPN

Connect a Grasshopper Panel to each "time" output. If a component loaded from
an older saved definition does not show the new output, delete it and place a
new instance from the toolbar.

Also enable Grasshopper's whole-component profiler:

    Display -> Canvas Widgets -> Profiler

Use Solution -> Recompute to execute the definition again.

The Panel reports internal phases. The Grasshopper profiler reports the whole
component, including some Grasshopper overhead. Small differences between the
internal TOTAL and Grasshopper's value are expected.


3. Use a repeatable measurement procedure
-----------------------------------------

For each test case:

    1. Keep the mesh, grid, sampling resolution, filtration, and optional
       visualization settings fixed.
    2. Run 2 or 3 warm-up computations and discard their results. This avoids
       counting JIT compilation and cold-cache effects.
    3. Run another 10 to 20 computations.
    4. Record the median time. Also record the minimum and maximum, or the
       95th percentile when possible.
    5. Keep Rhino otherwise idle and avoid moving the viewport during a run.
    6. Use the same computer, power settings, and output drive for comparisons.
    7. Set Tri3D visualization to false unless visualization is intentionally
       part of the measurement.

For every result, record enough input information to reproduce it:

    Surface mesh vertex and triangle counts
    Grid divisions: nx, ny, nz
    Sample counts: sx, sy, sz
    Total winding-number query points
    Number of dual hexes
    Number of Tri3D tetrahedra
    Filtration setting
    CPU model/core count and Sculpt thread count

The winding-number query count is:

    nx * ny * nz * sx * sy * sz

Suggested result columns:

    Case
    Mesh vertices/faces
    Grid divisions
    Samples per cell
    Query points
    Background and Volumes TOTAL
    Native winding-number time
    Dual 3D TOTAL
    Tri3D TOTAL
    Aleph export TOTAL
    SPN export TOTAL
    Sculpt process time
    Aleph solver time


4. Interpret the winding-number timing
---------------------------------------

The managed sample-point generation, column-major packing, volume-fraction
aggregation, and native data copies are serial.

libigl evaluates winding numbers across query points in parallel when there are
at least 10,000 query points and the machine provides more than one hardware
thread. Its hierarchy construction remains serial. libigl creates and joins
native threads for each invocation, so 10,000 is a conservative threshold and
not an experimentally established optimum for every mesh and computer.

For large production cases containing hundreds of thousands or millions of
query points, the exact threshold should have little effect. If the threshold
needs validation, compare forced-serial and forced-parallel native timings at
approximately 500, 1,000, 2,500, 5,000, 10,000, 25,000, and 50,000 points.


5. Keep exporter and external-solver times separate
---------------------------------------------------

"Export Mesh to Aleph" measures construction and writing of the Aleph input
file. It does not run or time the Aleph solver.

"Export to SPN" measures construction and writing of the .spn file. It does
not run or time Sculpt.

Report these as separate stages:

    Tusqh computation
    SPN export
    Sculpt process
    Aleph export
    Aleph solver process

Measure Sculpt and Aleph using wall-clock time from process launch until process
exit. Record the exact command line, exit code, executable version, thread
count, and input file size. The generated Sculpt command currently requests
eight workers with "-j 8"; keep that value fixed when comparing cases.


6. Important cautions
---------------------

File-writing measurements are affected by filesystem caching and storage speed.
Decide whether the study represents warm repeated runs or cold first runs, and
use the same policy throughout.

Export components may overwrite their output on every recomputation. Confirm
the output path before beginning a repeated benchmark.

Do not combine Tusqh export time with Sculpt or Aleph solver time. Reporting
the stages separately makes performance changes much easier to understand.


7. Comparing the optimized 24-tet path
--------------------------------------

"Tri3D Opt" preserves the 24-tetrahedron subdivision but replaces the
brute-force nearest-centroid search with structured-grid indexing. It stores
weighted vertices and tetrahedra as two compact objects rather than millions
of individual Grasshopper items.

Connect the same inputs used by Tri3D to Tri3D Opt. Keep viz=false for large
tests. The compact verts and tets outputs must be connected to "Export Mesh to
Aleph Opt"; they are intentionally not compatible with the original exporter.

For a fair comparison, use identical inputs and filtration settings, write to
different output file names, discard warm-up runs, and compare the TOTAL and
phase timings from Panels connected to each time output.
