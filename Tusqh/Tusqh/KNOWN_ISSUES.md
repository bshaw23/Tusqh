# Known Issues

## AntiAliasing2D: non-retained vertex with two retained incident edges can cause premature small-component removal

**Status:** deferred -- diagnosed, not yet fixed. Logged 2026-08-20.

**Where:** `Components/AntiAliasing2D.cs`, phases 06/10 (`Functions.BuildFaceComponentsEdgeFirst` in `AlephSupport.cs`), which feeds phase 12's small-component removal.

**The bug:** when two faces A and B touch only diagonally at a background vertex V, and V itself is NOT in `verts_to_keep_set` (fails the vertex volume-fraction cutoff), but the two edges bordering that diagonal split (each between an "in" face and an "out" face) ARE in `edges_to_keep_set`, the current component builder does not merge A and B. Its retained-vertex gate only checks `verts_to_keep_set.Contains(bg_idx)` -- it never consults the edges through which each face-group actually touches V.

**Why this happens:** `face_volume_fractions`, `vertex_volume_fractions`, and `edge_volume_fractions` are three *independently sampled* winding-number classifications (see `AntiAliasing2D.cs` around lines 143-158 for faces; separate subgrid samples for vertices and edges elsewhere in the same phase). It's a structural consequence of independent sampling -- not a rare fluke -- for a vertex's own sample to miss cutoff while its incident edges' independent samples clear it.

**Consequence:** A and B can each remain small, isolated phase-10 components and get deleted by phase-12 small-component removal, even though the two retained edges are real evidence that material continues through V. Real material can get silently deleted as if it were debris.

**Recommended fix direction (not yet implemented, needs sign-off before starting):** broaden the phase-06/10 vertex gate so that if V itself isn't retained, but every distinct face-group meeting at V has at least one of its own edges into V that's in `edges_to_keep_set`, treat V as connectivity-justified for the removal decision.

This is scoped entirely to AntiAliasing2D's phase-12 removal decision -- it should **not** need to touch MakeManifold. MakeManifold's own connect-vs-separate choice (`in_verts.Contains(...)`, vertex-only) is a separate, later question about final geometry, not about whether the material survives to be considered at all.

**Tradeoff to weigh:** broadening the gate could occasionally protect a genuinely-spurious speck that happens to touch a retained edge by coincidence, trading "real material gets deleted" for a small risk of "some noise survives." The phase-12 `12a` per-component audit trail (added alongside the Stage 3 edge-first rewrite) makes this observable if it happens -- look for `reason=synthetic-only component` or an unexpectedly low subcell score on something that got kept.

## RefineDualForVolumeFraction (2D): RTree KNN could likely be replaced with direct structured-grid indexing

**Status:** deferred, performance-only -- current behavior is correct. Logged 2026-08-21.

**Where:** `Components/RefineDualForVolumeFraction.cs`, phase 02 ("nearest-centroid assignment"), which maps each dual-mesh vertex to its nearest sculpted-face centroid via `Rhino.Geometry.RTree.Point3dKNeighbors(centroids, dual_vertex_pts, k)`.

**The observation:** this component already replaced an earlier brute-force O(vertices x centroids) loop with an R-tree-accelerated K=8 nearest-neighbor query, but that step still scales worse than expected at large grids -- growing ~46.7x for a ~16x increase in cell count (measured on Chesapeake x60y79 -> x240y315), and is the single largest GH-side stage at the highest resolution tested (1292.8 ms of the pipeline's ~4.3s GH-side total). The RTree's own native cost is opaque (no vendored source, unlike Aleph/libigl), so the exact mechanism isn't confirmed.

**Why this is likely fixable the same way `3DTriangulateDualOpt.cs` already is:** the 3D sibling component (`Components/3DTriangulateDualOpt.cs`, phase 02 "direct volume mapping") solves the exact same problem -- assign each mesh vertex the volume fraction of its enclosing structured-grid cell -- without any spatial search at all. Because its centroids are a validated regular structured grid (`nx x ny x nz`, checked against the actual centroid count), it computes each vertex's grid cell directly via `(vertex - min) / dist` rounded to the nearest integer, then a flat array index -- O(1) per vertex instead of O(log n) (RTree) or O(n) (brute force). Confirmed fast in practice: 0.31ms for 11,040 vertices in the dragon dataset.

**Recommended fix direction (not yet implemented, needs sign-off before starting):** `RefineDualForVolumeFraction.cs`'s `centroids` almost certainly come from the same kind of regular background grid `3DTriangulateDualOpt.cs` already assumes (one centroid per background cell, laid out in a structured x/y grid). If so, the same direct-index approach should apply directly in 2D: validate the structured-grid shape once, then replace the RTree KNN call with `(vertex - min) / (x_dist, y_dist)` rounded to an index -- eliminating the K-nearest-neighbor search (and its still-not-fully-understood superlinear cost) entirely, the same way the 3D "Opt" sibling already does.

## 3DDual.cs: Vertices/Hexes outputs could be bundled into one compact item instead of two lists

**Status:** deferred, performance-only -- current behavior is correct and already substantially improved. Logged 2026-08-21.

**Where:** `Components/3DDual.cs`, phase 06 (`06a publish verts`, `06b publish hexes`), publishing `List<Point3d>` and the now-flattened `List<int>` via `GH_ParamAccess.list`.

**Current state:** after flattening Hexes (see the Hexes-list-of-lists fix earlier this session), `06b`'s per-scalar-value publish cost (~0.161 us/int) is now within ~10% of `06a`'s (~0.146 us/double) -- the earlier ~9x generic-goo tax is gone. The phase is still the largest in the component (737.564 ms of a 1351.207 ms total, measured on dragon x120y54z85) purely because it carries 7.7x more individual list entries (4,578,640 flat ints vs 594,384 points), not because of any remaining inefficiency in how it's published.

**Why this is likely fixable the same way `3DTriangulateDualOpt.cs` already is:** that component's own outputs (`Vertices`, `Tets`) aren't published as `.list` at all -- they're bundled into one `CompactVertices3D`/`CompactTets3D` object per output via `GH_ParamAccess.item`, so Grasshopper marshals one item instead of millions. Applying the same pattern to `3DDual.cs`'s `Vertices`/`Hexes` outputs would collapse both `06a` and `06b` well below their current cost.

## 3DBackVol.cs: Sample Points / Winding Numbers outputs publish one entry per query point, and may be unused

**Status:** resolved 2026-08-21 (same day as logged). See "Resolution" below.

**Resolution:** added a new optional boolean input, `Output Winding Diagnostics` (`diag`), defaulting to `false` when unconnected. `Sample Points`/`Winding Numbers` are now only built and published when it's `true`; `Volume Fractions`, `Hex Centroids`, the background mesh, and the winding-number computation itself (phase 06) are unaffected -- only the two large per-query-point diagnostic outputs are gated. Confirmed with the toggle off at the same x120y54z85 config profiled below: phase 08 dropped from 15,526.995 ms to 237.033 ms, and the component's total dropped from 73,010.716 ms to 56,939.564 ms. The remaining total is now ~93% phase 06 (the native winding-number call itself, already using all available hardware threads via libigl's own default), which is expected and out of scope for this fix. Existing saved `.gh` files using this component will get empty `Sample Points`/`Winding Numbers` lists by default until that input is explicitly wired and set to `true` -- worth checking any definitions that relied on them being populated automatically.

The original observation/analysis is kept below for reference.

**Where:** `Components/3DBackVol.cs`, phase 08 ("publish outputs"), specifically the `Sample Points` (output 3, `point_grid`) and `Winding Numbers` (output 4, `pt_winding`) parameters.

**The observation:** at the largest dragon config tested (x120y54z85, 35,251,200 query points), phase 08 costs 15,526.995 ms of the component's 73,010.716 ms total -- the largest non-native phase by far (bigger than everything else combined except the native winding-number call itself), and 73-80% of all non-native C# time at every resolution tested. This is not the same class of bug as the earlier Hexes/DualBackgroundMesh fixes -- `Sample Points`/`Winding Numbers` are already properly-typed (`AddPointParameter`/`AddNumberParameter`, not `AddGenericParameter`), so there's no hidden goo-wrapping tax. The cost is simply the honest price of marshaling ~141 million scalar values (35.25M `Point3d` + 35.25M `double`, one pair per individual query point) through Grasshopper's list output pipe, confirmed via the ~0.15 us/value rate established fixing `3DDual.cs`.

**Why this may be pure waste:** the rest of the pipeline only consumes `Volume Fractions` (one per background cell -- 550,800 at this scale, cheap to publish). `Sample Points` and `Winding Numbers` publish the full per-query-point field instead of the aggregated result, which looks like a diagnostic/visualization capability (inspecting the raw winding-number field) rather than something the downstream pipeline needs. Whether this 15.5s is being spent for nothing depends on whether anything in the actual Grasshopper document is wired to those two outputs at production resolutions -- not yet confirmed.

**Fix direction taken:** made population (`pt_winding.Add(...)` in the phase-07 loop, `point_grid.Add(...)` in phase 04) and publish (`DA.SetDataList(3, point_grid)` / `DA.SetDataList(4, pt_winding)`) conditional on the new `Output Winding Diagnostics` boolean input, gated by explicit user choice rather than by whether the outputs happen to be wired downstream (both options were considered; see conversation history for the tradeoffs). This has nothing to do with the winding-number computation itself (phase 06 is unchanged); it's purely about not building and moving 141M discarded values. Types/wire contract are unchanged -- still `List<Point3d>`/`List<double>` -- so no downstream component needed updating, unlike the Hexes-flattening fix.
