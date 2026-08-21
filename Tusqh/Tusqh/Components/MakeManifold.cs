using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using EigenWrapper.Eigen;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Rhino.UI;
using Rhino.UI.Controls;
using Sculpt2D;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class MakeManifold : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MakeManifold()
          : base("Make Manifold", "manifold",
              "Makes a 2D Manifold Mesh",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Non-manifold Mesh", "mesh", "Non-manifold mesh to be made manifold", GH_ParamAccess.item);
            pManager.AddMeshParameter("Background Mesh", "back", "Background mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Inside Vertices", "in_vs", "Vertices that are 'inside'", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Background Vertex Map", "bg_map",
                "Optional: AntiAliasing2D's Background Vertex Map output. When supplied and its length matches the "
                + "Non-manifold Mesh's vertex count, pinch vertices are classified directly from this provenance data "
                + "instead of by spatial matching against Background Mesh.", GH_ParamAccess.list);
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Manifold Mesh", "mesh", "Manifold mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stopwatch total_timer = Stopwatch.StartNew();
            Stopwatch phase_timer = Stopwatch.StartNew();
            List<string> timings = new List<string>();

            Rhino.Geometry.Mesh input_mesh = new Rhino.Geometry.Mesh();
            Mesh background = new Mesh();
            List<int> verts = new List<int>();
            List<int> bg_map = new List<int>();

            DA.GetData(0, ref input_mesh);
            DA.GetData(1, ref background);
            DA.GetDataList(2, verts);
            DA.GetDataList(3, bg_map);

            // Stage 2: use AntiAliasing2D's provenance data directly when
            // it's actually wired in and matches this mesh, instead of
            // reconstructing background identity by spatial guessing.
            // Falls back to the original in_to_back matching below when
            // it's not supplied (e.g. an older Grasshopper definition that
            // predates this input) -- fully backward compatible.
            bool has_provenance = bg_map.Count == input_mesh.Vertices.Count;

            Mesh output_mesh = input_mesh.DuplicateMesh();
            int init_face_count = output_mesh.Faces.Count;

            HashSet<int> in_verts = new HashSet<int>();
            foreach (int vert in verts)
                in_verts.Add(vert);

            timings.Add($"01 inputs ({input_mesh.Vertices.Count:N0} verts, {init_face_count:N0} faces): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            Dictionary<Point3d, int> location_to_index = new Dictionary<Point3d, int>();
            for (int v = 0; v < input_mesh.Vertices.Count; v++)
            {
                Point3d vert = input_mesh.Vertices[v];
                Point3d rounded_vert = Functions.RoundPoint(vert);

                location_to_index.Add(rounded_vert, v);
            }

            timings.Add($"02 location_to_index construction: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            MeshTopologyVertexList vert_list = input_mesh.TopologyVertices;
            MeshTopologyEdgeList edge_list = input_mesh.TopologyEdges;
            //List<int> nonmanifold_verts = new List<int>();
            HashSet<int> nonmanifold_verts = new HashSet<int>();

            int two_faces_counter = 0;

            for(int i = 0; i < vert_list.Count; i++)
            {
                var vert_faces = vert_list.ConnectedFaces(i);

                if (vert_faces.Length == 2)
                {
                    var vert_edges = vert_list.ConnectedEdges(i);
                    two_faces_counter++;
                    if (vert_edges.Length == 4)
                    {
                        nonmanifold_verts.Add(i);
                    }
                }
            }

            timings.Add($"03 non-manifold vertex detection ({nonmanifold_verts.Count:N0} found): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // input_mesh.Vertices.Count vs input_mesh.TopologyVertices.Count
            // differing would mean nonmanifold_verts (built from
            // TopologyVertices indices, above) and in_to_back (keyed by raw
            // Vertices indices, below) are indexing into different spaces --
            // a pre-existing possible correctness issue independent of this
            // lookup's speed, flagged here rather than silently "fixed" so
            // it doesn't change behavior.
            bool topology_index_mismatch = input_mesh.Vertices.Count != vert_list.Count;

            // EpsilonEquals compares each of X/Y/Z independently (a box
            // tolerance), not Euclidean distance, so an RTree search radius
            // of exactly epsilon would miss points up to epsilon*sqrt(3)
            // away that the box test would still accept (e.g. differing by
            // epsilon on all three axes at once). Searching that wider,
            // exact bound first guarantees every candidate EpsilonEquals
            // could accept is found, then EpsilonEquals itself -- unchanged
            // -- makes the final call, so the match set is identical to the
            // brute-force loop.
            const double epsilon = 1e-5;
            double search_radius = epsilon * Math.Sqrt(3);

            Point3d[] input_pts = input_mesh.Vertices.ToPoint3dArray();
            Point3d[] background_pts = background.Vertices.ToPoint3dArray();

            Dictionary<int, int> in_to_back = new Dictionary<int, int>(); // Dictionary of input vertex indexes to background vertex indexes

            if (has_provenance)
            {
                timings.Add("04 in_to_back matching: skipped -- using Background Vertex Map provenance instead: "
                    + $"{phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                phase_timer.Restart();
            }
            else
            {
                // Point3dClosestPoints returns only the single nearest
                // background point per query (subject to the distance limit),
                // not every candidate within that radius -- so it can silently
                // omit the lowest-index EpsilonEquals match the original loop
                // would have picked. RTree.Search(Sphere, ...) against a tree
                // built once over the background points instead collects every
                // candidate within the radius, matching the original loop's
                // candidate set exactly.
                // RTree owns unmanaged resources (IDisposable) -- "using" ensures
                // it's released deterministically at the end of this scope
                // rather than left for the finalizer, which matters here since
                // MakeManifold's SolveInstance runs repeatedly per Grasshopper
                // solution.
                using Rhino.Geometry.RTree background_tree = Rhino.Geometry.RTree.CreateFromPointArray(background_pts);

                int multiply_matched_verts = 0;
                for (int v = 0; v < input_pts.Length; v++)
                {
                    Point3d vert = input_pts[v];

                    List<int> candidates = new List<int>();
                    background_tree.Search(new Rhino.Geometry.Sphere(vert, search_radius),
                        (sender, e) => candidates.Add(e.Id));

                    // Sort by index so ties resolve to the lowest-index match,
                    // exactly matching the original loop's "first w in index
                    // order" behavior.
                    candidates.Sort();

                    int matches_found = 0;
                    foreach (int w in candidates)
                    {
                        Point3d back_vert = background_pts[w];
                        if (vert.EpsilonEquals(back_vert, epsilon))
                        {
                            matches_found++;
                            if (matches_found == 1)
                                in_to_back.Add(v, w);
                        }
                    }
                    if (matches_found > 1)
                        multiply_matched_verts++;
                }

                timings.Add($"04 in_to_back matching ({input_pts.Length:N0} x {background_pts.Length:N0}, "
                    + $"{in_to_back.Count:N0} mapped, {input_pts.Length - in_to_back.Count:N0} unmatched, "
                    + $"{multiply_matched_verts:N0} with multiple candidates within tolerance"
                    + (topology_index_mismatch ? ", WARNING: input_mesh.Vertices.Count != TopologyVertices.Count -- nonmanifold_verts/in_to_back index spaces may not match" : "")
                    + $"): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                phase_timer.Restart();
            }

            // Breadth first search to find connected nonmanifold_verts
            List<HashSet<int>> connected_pinches = new List<HashSet<int>>();
            HashSet<int> verts_in_series = new HashSet<int>();
            foreach (int vert in nonmanifold_verts)
            {
                HashSet<int> list = new HashSet<int>();
                list.Add(vert);
                if (verts_in_series.Contains(vert))
                    continue;

                Queue<int> queue = new Queue<int>();
                HashSet<int> visited = new HashSet<int>();

                visited.Add(vert);
                queue.Enqueue(vert);

                while (queue.Count != 0)
                {
                    int current_vert = queue.Dequeue();
                    visited.Add(current_vert);
                    int[] connected_faces = input_mesh.TopologyVertices.ConnectedFaces(current_vert);
                    MeshFace f1 = input_mesh.Faces[connected_faces[0]];
                    MeshFace f2 = input_mesh.Faces[connected_faces[1]];
                    List<int> adj_verts_raw = new List<int> { f1.A, f1.B, f1.C, f1.D, f2.A, f2.B, f2.C, f2.D };

                    // face.A/B/C/D are raw mesh-vertex indices; nonmanifold_verts,
                    // list/queue/verts_in_series are all TopologyVertices indices
                    // (built from vert_list.Count above). Converting explicitly
                    // here keeps this BFS internally consistent instead of relying
                    // on raw == topology, which only happens to hold when the mesh
                    // has no coincident vertices.
                    foreach (int adj_raw in adj_verts_raw)
                    {
                        int adj = input_mesh.TopologyVertices.TopologyVertexIndex(adj_raw);
                        if (nonmanifold_verts.Contains(adj) && !visited.Contains(adj) && !list.Contains(adj))
                        {
                            queue.Enqueue(adj);
                            list.Add(adj);
                            verts_in_series.Add(adj);
                            verts_in_series.Add(vert);
                        }
                    }
                }

                connected_pinches.Add(list);
            }

            timings.Add($"05 connected-pinch BFS ({connected_pinches.Count:N0} groups): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Performance: the removal scan below used to re-scan every
            // face in output_mesh from scratch for every "separate" pinch
            // group -- O(groups x total_faces), which came to dominate
            // this component's runtime once the upstream crash was fixed
            // (93 groups x 28,470 faces measured here, ~4.4s of a ~4.5s
            // total). Precomputing a topology-vertex -> incident-face-index
            // lookup once, from output_mesh's state right here (identical
            // to input_mesh's, since nothing has mutated it yet), turns
            // that into an O(1) lookup per set member instead.
            //
            // output_mesh only ever grows during the loop below
            // (ConnectPinch/SeparatePinch append new vertices/faces;
            // nothing is deleted until phase 07), so face indices below
            // original_face_count stay valid for the rest of this method,
            // and this lookup never goes stale for them. Faces added by an
            // earlier group in this same loop still get checked against a
            // later group's set -- exactly like the original full rescan
            // did -- by separately re-scanning just the small, growing
            // tail of newly-added faces each time, not the whole mesh.
            int original_face_count = output_mesh.Faces.Count;
            Dictionary<int, List<int>> topo_vertex_to_faces = new Dictionary<int, List<int>>();
            for (int fi = 0; fi < original_face_count; fi++)
            {
                MeshFace f = output_mesh.Faces[fi];
                foreach (int raw in new int[] { f.A, f.B, f.C, f.D })
                {
                    int topo = input_mesh.TopologyVertices.TopologyVertexIndex(raw);
                    if (!topo_vertex_to_faces.TryGetValue(topo, out List<int> flist))
                    {
                        flist = new List<int>();
                        topo_vertex_to_faces[topo] = flist;
                    }
                    flist.Add(fi);
                }
            }

            timings.Add($"05b topo_vertex_to_faces lookup build ({original_face_count:N0} faces): "
                + $"{phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            List<int> remove_face_at = new List<int>();
            List<int> added_faces = new List<int>();

            // Stage 2 classification: resolves a nonmanifold_verts entry
            // (a TopologyVertices index) to a background-mesh vertex
            // index, going through the topology vertex's actual
            // constituent raw indices explicitly rather than assuming
            // topology index == raw index. Returns -1 for a confirmed
            // synthetic (template-generated) vertex, -2 if it can't be
            // classified at all (missing/inconsistent provenance, or no
            // spatial match in the legacy fallback). Errors are reported
            // via runtime_errors rather than thrown, so one unresolvable
            // pinch group doesn't crash the whole component -- its group
            // is skipped instead of guessed at.
            List<string> runtime_errors = new List<string>();
            int ClassifyBackgroundIndex(int topology_vert)
            {
                int[] raw_indices = vert_list.MeshVertexIndices(topology_vert);
                if (raw_indices.Length == 0)
                {
                    runtime_errors.Add($"Topology vertex {topology_vert} has no constituent raw mesh vertices.");
                    return -2;
                }

                int? result = null;
                foreach (int raw in raw_indices)
                {
                    int candidate;
                    if (has_provenance)
                        candidate = (raw >= 0 && raw < bg_map.Count) ? bg_map[raw] : -2;
                    else
                        candidate = in_to_back.TryGetValue(raw, out int back_idx) ? back_idx : -2;

                    if (result == null)
                        result = candidate;
                    else if (result != candidate)
                    {
                        runtime_errors.Add($"Topology vertex {topology_vert} maps to raw vertices with inconsistent "
                            + $"provenance ({result}, {candidate}) -- collision, cannot classify.");
                        return -2;
                    }
                }

                if (result == -1)
                {
                    runtime_errors.Add($"Unexpected non-manifold synthetic (template-generated) vertex at topology "
                        + $"index {topology_vert} -- AntiAliasing2D's seam generation should have prevented this.");
                }
                else if (result == -2)
                {
                    runtime_errors.Add($"Topology vertex {topology_vert} has no background match.");
                }

                return result.Value;
            }

            Stopwatch connect_pinch_timer = new Stopwatch();
            Stopwatch separate_pinch_timer = new Stopwatch();
            Stopwatch removal_scan_timer = new Stopwatch();
            int connect_pinch_calls = 0;
            int separate_pinch_calls = 0;

            foreach (var set in connected_pinches)
            {
                int separate_counter = 0;
                int connect_counter = 0;
                bool group_unclassifiable = false;
                foreach (var vert in set)
                {
                    int bg_idx = ClassifyBackgroundIndex(vert);
                    if (bg_idx < 0)
                    {
                        group_unclassifiable = true;
                        continue;
                    }

                    if (in_verts.Contains(bg_idx))
                        connect_counter++;
                    else
                        separate_counter++;
                }

                if (group_unclassifiable)
                {
                    // Skip resolving this pinch group entirely rather than
                    // guess with incomplete classification -- reported via
                    // runtime_errors below, not silently dropped.
                    continue;
                }

                bool separate = true;
                if (separate_counter > connect_counter)
                    separate = true;
                else if (separate_counter < connect_counter)
                    separate = false;
                else
                    separate = true;

                // Add faces
                if (!separate)
                {
                    foreach (int vert in set)
                    {
                        connect_pinch_timer.Start();
                        Functions.ConnectPinch(output_mesh, input_mesh, vert, location_to_index);
                        connect_pinch_timer.Stop();
                        connect_pinch_calls++;
                    }
                }
                // Remove faces
                else
                {
                    foreach (int vert in set)
                    {
                        separate_pinch_timer.Start();
                        Functions.SeparatePinch(output_mesh, input_mesh, vert, remove_face_at, location_to_index);
                        separate_pinch_timer.Stop();
                        separate_pinch_calls++;
                    }

                    removal_scan_timer.Start();

                    // Original faces: O(1) lookup per set member via the
                    // precomputed index instead of an O(faces) rescan.
                    // `set` is TopologyVertices-indexed (see the BFS above),
                    // matching how the lookup was built.
                    foreach (int topo_vert in set)
                    {
                        if (topo_vertex_to_faces.TryGetValue(topo_vert, out List<int> faces_touching))
                        {
                            foreach (int fi in faces_touching)
                                remove_face_at.Add(fi);
                        }
                    }

                    // Faces added by ConnectPinch/SeparatePinch so far in
                    // this loop (this group's own calls above, or any
                    // earlier group's) -- a small, bounded, growing range,
                    // not the whole mesh -- rescanned exactly like the
                    // original code did, since the precomputed lookup only
                    // covers the mesh's original faces.
                    for (int i = original_face_count; i < output_mesh.Faces.Count; i++)
                    {
                        var face = output_mesh.Faces[i];
                        List<int> face_verts_raw = new List<int> { face.A, face.B, face.C, face.D };
                        foreach (var v_raw in face_verts_raw)
                        {
                            // A brand-new vertex (added by an earlier
                            // ConnectPinch/SeparatePinch call this loop)
                            // has no input_mesh topology index and can
                            // never be a member of `set`, which only ever
                            // contains original-mesh topology indices.
                            int v_topo = (v_raw >= 0 && v_raw < input_mesh.Vertices.Count)
                                ? input_mesh.TopologyVertices.TopologyVertexIndex(v_raw)
                                : -1;
                            if (v_topo >= 0 && set.Contains(v_topo))
                                remove_face_at.Add(i);
                        }
                    }

                    removal_scan_timer.Stop();
                }
            }

            timings.Add($"06a ConnectPinch ({connect_pinch_calls:N0} calls): "
                + $"{connect_pinch_timer.Elapsed.TotalMilliseconds:F3} ms");
            timings.Add($"06b SeparatePinch ({separate_pinch_calls:N0} calls): "
                + $"{separate_pinch_timer.Elapsed.TotalMilliseconds:F3} ms");
            timings.Add($"06c removal scan (lookup + tail rescan): "
                + $"{removal_scan_timer.Elapsed.TotalMilliseconds:F3} ms");

            if (runtime_errors.Count > 0)
            {
                foreach (string err in runtime_errors.Distinct())
                    timings.Add($"WARNING: {err}");

                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{runtime_errors.Count:N0} pinch-vertex classification issue(s) found -- affected pinch groups "
                    + "were left unresolved. See Timings for detail.");
            }

            timings.Add($"06 pinch resolution ({runtime_errors.Count:N0} classification issues, "
                + $"connect/separate): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            int removed_faces = output_mesh.Faces.DeleteFaces(remove_face_at);

            output_mesh.Faces.ExtractDuplicateFaces();
            output_mesh.UnifyNormals();
            output_mesh.Compact();

            MeshFaceNormalList normals = output_mesh.FaceNormals;

            timings.Add($"07 face removal + mesh cleanup ({removed_faces:N0} removed): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            DA.SetData(0, output_mesh);

            timings.Add($"08 publish output: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            total_timer.Stop();
            timings.Add($"TOTAL: {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(1, timings);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E052DBC4-2ADC-4AF3-80A7-15E1F280AA71"); }
        }
    }
}