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
