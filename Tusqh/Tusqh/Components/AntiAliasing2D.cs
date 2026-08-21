using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Sculpt2D.Sculpt3D;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Sculpt2D.Components
{
    public class AntiAliasing2D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public AntiAliasing2D()
          : base("AntiAliasing 2D", "aliasing2d",
              "Antialiasing algorithm to handle background mesh",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background mesh", "back", "Background mesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Winding Numbers", "wind", "Winding numbers of background mesh", GH_ParamAccess.list);
            pManager.AddPointParameter("Sample Points", "pts", "Sample points of winding numbers", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cutoff", "cut", "Volume fraction threshold", GH_ParamAccess.item);
            pManager.AddIntegerParameter("X Points", "xpts", "Sample points in x", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Y Points", "ypts", "Sample points in y", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Components for Removal", "rm1", "Consider components less than x faces for removal", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Remove Components", "rm2", "Remove components with less than x subcells", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Vertices", "verts", "Vertices that are 'inside'", GH_ParamAccess.list);
            pManager.AddGenericParameter("Edges", "edges", "Edges that are 'inside'", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Faces", "faces", "Faces that are 'inside'", GH_ParamAccess.list);
            pManager.AddPointParameter("Vertex Visualization", "v_viz", "Vertex visualization", GH_ParamAccess.list);
            pManager.AddLineParameter("Edge Visualization", "e_viz", "Edge visualization", GH_ParamAccess.list);
            pManager.AddMeshParameter("Face Visualization", "f_viz", "Face visualization", GH_ParamAccess.item);
            pManager.AddMeshParameter("Final Mesh", "mesh", "Final anti-aliasing", GH_ParamAccess.item);
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

            try
            {
                SolveInstanceCore(DA, timings, total_timer, phase_timer);
            }
            catch (Exception ex)
            {
                // Same blind spot MakeManifold had: Timings is only ever
                // published at the very end of a normal run, so a failure
                // partway through previously left zero visibility into
                // which phase it reached or why. This publishes whatever
                // phases DID complete, plus the actual exception detail,
                // instead of Grasshopper's generic "Solution exception"
                // message being the only thing visible.
                timings.Add($"EXCEPTION after {total_timer.Elapsed.TotalMilliseconds:F3} ms: "
                    + $"{ex.GetType().Name}: {ex.Message}");
                timings.Add($"STACK TRACE: {ex.StackTrace}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{ex.GetType().Name}: {ex.Message}");
                DA.SetDataList(7, timings);
            }
        }

        private void SolveInstanceCore(IGH_DataAccess DA, List<string> timings, Stopwatch total_timer, Stopwatch phase_timer)
        {
            Mesh background = new Mesh();
            List<double> winding = new List<double>();
            List<Point3d> sample_points = new List<Point3d>();
            double volume_cutoff = new double();
            int x_pts = new int();
            int y_pts = new int();
            int consider_components = new int();
            int remove_components = new int();

            DA.GetData(0, ref background);
            DA.GetDataList(1, winding);
            DA.GetDataList(2, sample_points);
            DA.GetData(3, ref volume_cutoff);
            DA.GetData(4, ref x_pts);
            DA.GetData(5, ref y_pts);
            DA.GetData(6, ref consider_components);
            DA.GetData(7, ref remove_components);

            // Grid spacing: NOT diagnostic-only -- this is the actual
            // geometric input ConnectBridgeGraph uses to position every
            // template vertex, so it's derived from an actual background
            // quad's own corners (face[0].A -> B for the X vector, A -> D
            // for the Y vector) instead of assuming background vertex
            // index 1 is the +X neighbor of index 0. That assumption is
            // what Mesh.CreateFromPlane's raster-scan ordering happens to
            // produce, but it isn't a documented guarantee; a specific
            // face's own corner structure (A/B/C/D going around the quad)
            // is the same convention already relied on throughout this
            // file (e.g. the interior_edges A-B/B-C/D-C/A-D pairing a few
            // lines up), so this doesn't add a new assumption, just a more
            // direct source for one that was already being made.
            MeshFace grid_face = background.Faces[0];
            Point3d grid_a = background.Vertices[grid_face.A];
            Point3d grid_b = background.Vertices[grid_face.B];
            Point3d grid_d = background.Vertices[grid_face.D];
            Vector3d grid_x_vector = grid_b - grid_a;
            Vector3d grid_y_vector = grid_d - grid_a;

            double diag_x_dist = grid_x_vector.X;
            double diag_y_dist = grid_y_vector.Y;

            bool grid_axis_aligned = Math.Abs(grid_x_vector.Y) < 1e-5 && Math.Abs(grid_y_vector.X) < 1e-5;
            double diag_dist_delta = Math.Abs(Math.Abs(diag_x_dist) - Math.Abs(diag_y_dist));
            timings.Add($"00 STAGE-0 DIAGNOSTIC grid spacing: x_dist={diag_x_dist:G17}, y_dist={diag_y_dist:G17}, "
                + $"abs(abs(x_dist)-abs(y_dist))={diag_dist_delta:G17}, "
                + $"grid_x_vector={grid_x_vector}, grid_y_vector={grid_y_vector}, dot={Vector3d.Multiply(grid_x_vector, grid_y_vector):G17}"
                + (grid_axis_aligned ? "" : ", WARNING: background face[0] is not axis-aligned -- straight/turn classification assumes axis-aligned grid")
                + (diag_dist_delta < 1e-5 ? " -- WITHIN 1e-5: square-cell orientation bug would have been ACTIVE on this grid under the old classifier" : " -- outside 1e-5 tolerance")
                + ": 0.000 ms");

            List<double> face_volume_fractions = new List<double>();
            double divisor = (double)x_pts * (double)y_pts;
            double volume_fraction = 0;
            int n_pts = x_pts * y_pts;
            int counter = 0;
            double cur_wind;
            foreach (MeshFace face in background.Faces)
            {
                volume_fraction = 0;
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    cur_wind = winding[counter + pt_idx];
                    volume_fraction += cur_wind;
                }
                volume_fraction /= divisor;
                face_volume_fractions.Add(volume_fraction);
                counter += n_pts;
            }

            timings.Add($"01 inputs + face volume fractions ({background.Faces.Count:N0} faces): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            MeshTopologyVertexList verts = background.TopologyVertices;

            Dictionary<Tuple<int, int>, List<double>> edge_winding_numbers = new Dictionary<Tuple<int, int>, List<double>>();
            Dictionary<int, List<double>> vert_winding_numbers = new Dictionary<int, List<double>>();
            counter = 0;
            foreach(MeshFace face in background.Faces)
            {
                Point3d a = verts[face.A];
                Point3d b = verts[face.B];
                Point3d c = verts[face.C];
                Point3d d = verts[face.D];
                List<double> cur_winding_list = new List<double>();
                List<Point3d> points = new List<Point3d>();
                Point3d centroid = AlephSupport.GetCentroid(face, background);
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    points.Add(sample_points[pt_idx + counter]);
                    cur_winding_list.Add(winding[pt_idx + counter]);
                }
                counter += n_pts;

                List<double> A = new List<double>();
                List<double> B = new List<double>();
                List<double> C = new List<double>();
                List<double> D = new List<double>();

                for(int i = 0; i < points.Count; ++i)
                {
                    bool x_pos = points[i].X > centroid.X;
                    bool y_pos = points[i].Y > centroid.Y;

                    if (!x_pos && !y_pos)
                        A.Add(cur_winding_list[i]);
                    else if (x_pos && !y_pos)
                        B.Add(cur_winding_list[i]);
                    else if (x_pos && y_pos)
                        C.Add(cur_winding_list[i]);
                    else if (!x_pos && y_pos)
                        D.Add(cur_winding_list[i]);
                }

                List<List<double>> quadrants = new List<List<double>> { A, B, C, D };
                if (!vert_winding_numbers.TryAdd(face.A, A))
                    vert_winding_numbers[face.A].AddRange(A);

                if (!vert_winding_numbers.TryAdd(face.B, B))
                    vert_winding_numbers[face.B].AddRange(B);

                if (!vert_winding_numbers.TryAdd(face.C, C))
                    vert_winding_numbers[face.C].AddRange(C);

                if (!vert_winding_numbers.TryAdd(face.D, D))
                    vert_winding_numbers[face.D].AddRange(D);

                // Edge winding number assignment
                List<int> e1 = new List<int> { face.A, face.B };
                e1.Sort();
                Tuple<int, int> t1 = new Tuple<int, int>(e1[0], e1[1]);
                if (!edge_winding_numbers.TryAdd(t1, A))
                    edge_winding_numbers[t1].AddRange(A);
                edge_winding_numbers[t1].AddRange(B);

                List<int> e2 = new List<int> { face.B, face.C };
                e2.Sort();
                Tuple<int, int> t2 = new Tuple<int, int>(e2[0], e2[1]);
                if (!edge_winding_numbers.TryAdd(t2, B))
                    edge_winding_numbers[t2].AddRange(B);
                edge_winding_numbers[t2].AddRange(C);

                List<int> e3 = new List<int> { face.C, face.D };
                e3.Sort();
                Tuple<int, int> t3 = new Tuple<int, int>(e3[0], e3[1]);
                if (!edge_winding_numbers.TryAdd(t3, C))
                    edge_winding_numbers[t3].AddRange(C);
                edge_winding_numbers[t3].AddRange(D);

                List<int> e4 = new List<int> { face.A, face.D };
                e4.Sort();
                Tuple<int, int> t4 = new Tuple<int, int>(e4[0], e4[1]);
                if (!edge_winding_numbers.TryAdd(t4, A))
                    edge_winding_numbers[t4].AddRange(A);
                edge_winding_numbers[t4].AddRange(D);
            }

            timings.Add($"02 vertex/edge winding aggregation: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Get vertex volume fractions
            List<double> vertex_volume_fractions = new List<double>();
            for (int i = 0; i < verts.Count; i++)
            {
                List<double> winding_list = vert_winding_numbers[i];
                volume_fraction = 0;
                foreach (var wind in winding_list)
                    volume_fraction += wind;
                volume_fraction /= winding_list.Count;
                vertex_volume_fractions.Add(volume_fraction);
            }

            // Get edge volume fractions
            Dictionary<Tuple<int, int>, double> edge_volume_fractions = new Dictionary<Tuple<int, int>, double>();
            foreach (var kvp in edge_winding_numbers)
            {
                volume_fraction = 0;
                foreach (var wind in kvp.Value)
                    volume_fraction += wind;
                volume_fraction /= kvp.Value.Count;
                edge_volume_fractions.Add(kvp.Key, volume_fraction);
            }

            timings.Add($"03 vertex/edge volume fraction computation: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Output vertex list and vertex visualization
            List<int> verts_to_keep = new List<int>();
            HashSet<int> verts_to_keep_set = new HashSet<int>();
            List<Point3d> vert_viz = new List<Point3d>();
            int init_count = verts.Count;
            for (int i = init_count - 1; i >= 0; i--)
            {
                if (vertex_volume_fractions[i] >= volume_cutoff)
                {
                    verts_to_keep.Add(i);
                    verts_to_keep_set.Add(i);
                    vert_viz.Add(verts[i]);
                }
            }


            // Output edge list and edge visualization
            List<Tuple<int, int>> edges_to_keep = new List<Tuple<int, int>>();
            HashSet<Tuple<int, int>> edges_to_keep_set = new HashSet<Tuple<int, int>>();
            List<Line> edge_viz = new List<Line>();
            foreach (var kvp in edge_volume_fractions)
            {
                if (kvp.Value >= volume_cutoff)
                {
                    edges_to_keep.Add(kvp.Key);
                    edges_to_keep_set.Add(kvp.Key);
                    edge_viz.Add(new Line(verts[kvp.Key.Item1], verts[kvp.Key.Item2]));
                }
            }

            timings.Add($"04 verts/edges output construction: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            init_count = background.Faces.Count;
            List<int> faces_to_keep = new List<int>();
            HashSet<int> faces_to_keep_set = new HashSet<int>();
            List<int> faces_to_remove = new List<int>();
            Mesh sculpted_mesh = background.DuplicateMesh();
            for (int i = init_count - 1; i >= 0; --i)
            {
                if (face_volume_fractions[i] < volume_cutoff)
                    faces_to_remove.Add(i);
                else
                {
                    faces_to_keep.Add(i);
                    faces_to_keep_set.Add(i);
                }
            }
            // Batch removal: RemoveAt(i, true) recompacts the face array on
            // every call, so removing faces one at a time here cost
            // O(faces^2). DeleteFaces does it in a single pass.
            //
            // compact = false is required here, not optional: the single-
            // argument overload defaults to compact = true, which silently
            // deletes any vertex that loses all its incident faces and
            // renumbers the survivors. ConnectBridgeGraph (below, in phase
            // 09) relies on background vertex index i still being
            // sculpted_mesh vertex index i -- true right after
            // DuplicateMesh(), but false the moment a compacting delete
            // runs. Compacting here caused "Index was outside the bounds
            // of the array" inside AddBridgeEdgeTemplate on every tested
            // resolution. The deferred sculpted_mesh.Compact() later
            // (after all bridge/template geometry has been added, once
            // background-index alignment is no longer needed) is the
            // correct place for that cleanup, not here.
            sculpted_mesh.Faces.DeleteFaces(faces_to_remove, false);

            if (sculpted_mesh.Vertices.Count != background.Vertices.Count)
            {
                timings.Add($"WARNING: sculpted_mesh.Vertices.Count ({sculpted_mesh.Vertices.Count:N0}) != "
                    + $"background.Vertices.Count ({background.Vertices.Count:N0}) after phase 05 -- "
                    + "ConnectBridgeGraph's background-index assumption will be violated");
            }

            Mesh face_viz = sculpted_mesh.DuplicateMesh();
            DA.SetData(5, face_viz);

            timings.Add($"05 initial face removal ({faces_to_remove.Count:N0} removed, "
                + $"{sculpted_mesh.Vertices.Count:N0} vertices preserved): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            List<HashSet<int>> components = new List<HashSet<int>>();
            HashSet<int> visited = new HashSet<int>();
            foreach (int f in faces_to_keep)
            {
                if (visited.Contains(f))
                    continue;

                HashSet<int> component = new HashSet<int>();
                component.Add(f);
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(f);

                while(queue.Count > 0)
                {
                    int face = queue.Dequeue();
                    visited.Add(face);
                    List<int> connected_faces = Functions.GetAdjacentFaces(face, background);
                    foreach(int i in connected_faces)
                    {
                        if (!component.Contains(i) && faces_to_keep_set.Contains(i))
                        {
                            queue.Enqueue(i);
                            component.Add(i);
                        }
                    }
                }

                components.Add(component);
            }

            timings.Add($"06 first BFS: connected components ({components.Count:N0} found): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Identify exterior edges
            HashSet<Tuple<int, int>> interior_edges = new HashSet<Tuple<int, int>>();
            HashSet<Tuple<int, int>> exterior_edges = new HashSet<Tuple<int, int>>();
            HashSet<int> components_verts = new HashSet<int>();
            foreach (var component in components)
            {
                foreach (var face in component)
                {
                    MeshFace f = background.Faces[face];
                    components_verts.Add(f.A);
                    components_verts.Add(f.B);
                    components_verts.Add(f.C);
                    components_verts.Add(f.D);

                    interior_edges.Add(new Tuple<int, int>(f.A, f.B));
                    interior_edges.Add(new Tuple<int, int>(f.B, f.C));
                    interior_edges.Add(new Tuple<int, int>(f.D, f.C));
                    interior_edges.Add(new Tuple<int, int>(f.A, f.D));
                }
            }

            foreach(var edge in edges_to_keep)
            {
                if(!interior_edges.Contains(edge))
                    exterior_edges.Add(edge);
            }

            timings.Add($"07 exterior edge identification ({exterior_edges.Count:N0} edges): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Get Paths between components
            //
            // FindAllPaths used to rebuild its adjacency list from
            // exterior_edges on every single (start,end) call; exterior_edges
            // doesn't change across this whole loop, so it's built once here
            // instead. path_cache memoizes exact (start,end) pairs: a given
            // vertex can only be a "start" candidate for the one component it
            // belongs to, so within one (i,j) iteration a pair can't repeat --
            // but two components that are face-disjoint can still share a
            // vertex at a shared corner, so the same ordered pair can
            // legitimately recur across different (i,j) iterations. This is
            // exact memoization of a pure function (same edges, same
            // endpoints -> same paths), not an approximation, so it can't
            // change which paths get found or returned.
            Dictionary<int, List<int>> exterior_adjacency = Functions.BuildAdjacencyList(exterior_edges);
            Dictionary<Tuple<int, int>, List<List<int>>> path_cache = new Dictionary<Tuple<int, int>, List<List<int>>>();
            List<List<List<List<int>>>> vertex_paths = new List<List<List<List<int>>>>();
            int find_all_paths_calls = 0;
            int find_all_paths_cache_hits = 0;
            int total_paths_returned = 0;
            int max_paths_single_call = 0;
            for (int i = 0; i < components.Count; i++)
            {
                HashSet<int> component1 = components[i];
                HashSet<int> verts1 = new HashSet<int>();
                foreach (int face in component1)
                {
                    List<int> list = new List<int> { background.Faces[face].A, background.Faces[face].B, background.Faces[face].C, background.Faces[face].D };
                    foreach (int v in list)
                        verts1.Add(v);
                }    
                for (int j = i + 1; j < components.Count; j++)
                {
                    HashSet<int> component2 = components[j];
                    HashSet<int> verts2 = new HashSet<int>();
                    foreach (int face in component2)
                    {
                        List<int> list = new List<int> { background.Faces[face].A, background.Faces[face].B, background.Faces[face].C, background.Faces[face].D };
                        foreach (int v in list)
                            verts2.Add(v);
                    }

                    // Initialize possible start locations
                    List<Tuple<int, int>> start_edges = new List<Tuple<int, int>>();
                    List<int> start_idxs = new List<int>();
                    foreach (var edge in exterior_edges)
                    {
                        if (verts1.Contains(edge.Item1))
                        {
                            start_edges.Add(edge);
                            start_idxs.Add(edge.Item1);
                        }
                        else if (verts1.Contains(edge.Item2))
                        {
                            start_edges.Add(edge);
                            start_idxs.Add(edge.Item2);
                        }
                    }

                    List<Tuple<int, int>> end_edges = new List<Tuple<int, int>>();
                    List<int> end_idxs = new List<int>();
                    foreach (var edge in exterior_edges)
                    {
                        if (verts2.Contains(edge.Item1))
                        {
                            end_edges.Add(edge);
                            end_idxs.Add(edge.Item1);
                        }
                        else if (verts2.Contains(edge.Item2))
                        {
                            end_edges.Add(edge);
                            end_idxs.Add(edge.Item2);
                        }
                    }

                    // NOTE: start_idxs/end_idxs are intentionally left
                    // un-deduplicated -- a vertex touched by two exterior
                    // edges into the same component is processed once per
                    // occurrence below, and each occurrence's paths feed
                    // ConnectByVertexPath downstream, so removing duplicates
                    // here would change the resulting mesh (fewer connect
                    // operations), not just the run time. path_cache below
                    // skips the repeated DFS search itself for a duplicate
                    // (start,end) pair, but the cached paths are still
                    // re-iterated and re-processed downstream for each
                    // occurrence -- not free, just far cheaper than
                    // re-searching.

                    // Now the edges are initialized I would have to start at each initialized edge and see if it creates a path to component2
                    List<List<List<int>>> components_paths = new List<List<List<int>>>();
                    for (int k = 0; k < start_idxs.Count; k++)
                    {
                        for (int l = 0; l < end_idxs.Count; l++)
                        {
                            int start = start_idxs[k];
                            int end = end_idxs[l];
                            find_all_paths_calls++;

                            Tuple<int, int> cache_key = new Tuple<int, int>(start, end);
                            List<List<int>> all_paths;
                            if (path_cache.TryGetValue(cache_key, out all_paths))
                            {
                                find_all_paths_cache_hits++;
                            }
                            else
                            {
                                all_paths = Functions.FindAllPaths(exterior_adjacency, start, end);
                                path_cache[cache_key] = all_paths;
                            }

                            total_paths_returned += all_paths.Count;
                            if (all_paths.Count > max_paths_single_call)
                                max_paths_single_call = all_paths.Count;

                            List<List<int>> paths = new List<List<int>>();
                            foreach (var path in all_paths)
                            {
                                if (path.Last() == end)
                                    paths.Add(path);
                            }

                            if (paths.Count > 0)
                                components_paths.Add(paths);
                        }
                    }

                    vertex_paths.Add(components_paths);
                }
            }

            timings.Add($"08 inter-component path finding ({components.Count:N0} components, "
                + $"{find_all_paths_calls:N0} (start,end) pairs evaluated, "
                + $"{path_cache.Count:N0} unique pairs actually searched, "
                + $"{find_all_paths_cache_hits:N0} cache hits, "
                + $"{total_paths_returned:N0} total paths returned, "
                + $"{max_paths_single_call:N0} max paths from one pair, "
                + $"{exterior_edges.Count:N0} exterior edges): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Flatten every accepted path (across every component pair)
            // into one list before meshing anything: ConnectBridgeGraph
            // consolidates them into a single set of unique bridge edges
            // internally, so a duplicate or repeatedly-cached path can no
            // longer stack duplicate trapezoids the way calling
            // ConnectByVertexPath once per path used to. No RoundPoint-based
            // location_to_index is built any more -- every function
            // ConnectBridgeGraph calls (AddBridgeEdgeTemplate,
            // MeshComponentFace, MeshPinchVertexX/Y) identifies vertices
            // through the structural TemplateVertexKey registry instead.
            List<List<int>> all_accepted_paths = new List<List<int>>();
            foreach (List<List<List<int>>> paths in vertex_paths)
                foreach (var start in paths)
                    foreach (List<int> path in start)
                        all_accepted_paths.Add(path);

            List<string> bridge_diagnostics = new List<string>();
            Functions.ConnectBridgeGraph(background, sculpted_mesh, faces_to_keep, all_accepted_paths,
                diag_x_dist, diag_y_dist, bridge_diagnostics);

            sculpted_mesh.Compact();

            timings.Add($"09 trapezoid connection (ConnectBridgeGraph, {all_accepted_paths.Count:N0} paths flattened): "
                + $"{phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            foreach (string diag in bridge_diagnostics)
                timings.Add($"09b {diag}");
            phase_timer.Restart();

            components = new List<HashSet<int>>();
            visited = new HashSet<int>();
            for (int i = 0; i < sculpted_mesh.Faces.Count; i++)
            {
                if (visited.Contains(i))
                    continue;

                HashSet<int> component = new HashSet<int>();
                component.Add(i);
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);

                while(queue.Count != 0)
                {
                    int face = queue.Dequeue();
                    visited.Add(face);
                    // NOTE: this used to also call
                    // sculpted_mesh.Faces.GetConnectedFaces(face) here, but its
                    // result was never used -- see GetConnectedFacesUnused()
                    // below for why that call alone accounted for effectively
                    // all of this component's runtime at fine grid resolution.
                    List<int> faces = Functions.GetAdjacentFaces(face, sculpted_mesh);
                    foreach(var f in faces)
                    {
                        if (!component.Contains(f))
                        {
                            component.Add(f);
                            queue.Enqueue(f);
                        }
                    }
                }

                components.Add(component);
            }

            timings.Add($"10 second BFS: connected components ({components.Count:N0} found, {sculpted_mesh.Faces.Count:N0} faces): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            MeshTopologyVertexList topology_verts = sculpted_mesh.TopologyVertices;

            // Match each surviving sculpted face back to its original
            // background face index. A face that survived unmodified has
            // vertex coordinates bit-identical to its background source
            // (DuplicateMesh + face-list-only removal never moves a
            // vertex), so its centroid is bit-identical too -- rounding
            // it to the same precision FacesAreEqual already matches at
            // (0.001) buckets each sculpted face with its true match
            // deterministically, regardless of the grid being uniform,
            // non-uniform-but-rectangular, or general (even concave)
            // structured quads. The bucket is only ever a candidate
            // prefilter: FacesAreEqual still does the exact match, so a
            // rounding collision between unrelated cells (rare, and only
            // possible for degenerate/adjacent cells) just means checking
            // a couple of candidates instead of one -- it can't produce a
            // wrong match. This turns an O(sculpted x background)
            // comparison into O(sculpted + background).
            Dictionary<Point3d, List<int>> background_centroid_to_idx = new Dictionary<Point3d, List<int>>();
            for (int j = 0; j < background.Faces.Count; j++)
            {
                Point3d key = Functions.RoundPoint(AlephSupport.GetCentroid(background.Faces[j], background));
                if (!background_centroid_to_idx.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>();
                    background_centroid_to_idx[key] = bucket;
                }
                bucket.Add(j);
            }

            Dictionary<int, int> face_idx_to_background_idx = new Dictionary<int, int>();
            for (int i = 0; i < sculpted_mesh.Faces.Count; i++)
            {
                MeshFace face1 = sculpted_mesh.Faces[i];
                Point3d key = Functions.RoundPoint(AlephSupport.GetCentroid(face1, sculpted_mesh));
                if (background_centroid_to_idx.TryGetValue(key, out List<int> candidates))
                {
                    foreach (int j in candidates)
                    {
                        MeshFace face2 = background.Faces[j];
                        if (AlephSupport.FacesAreEqual(face1, face2, sculpted_mesh, background))
                            face_idx_to_background_idx.Add(i, j);
                    }
                }
            }

            timings.Add($"11 face-to-background matching ({sculpted_mesh.Faces.Count:N0} x {background.Faces.Count:N0}): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            HashSet<int> remove_faces = new HashSet<int>();
            foreach(var component in components)
            {
                if (component.Count <= consider_components)
                {
                    List<int> back_comp = new List<int>();
                    foreach (int face in component)
                    {
                        back_comp.Add(face_idx_to_background_idx[face]);
                    }

                    int subcells_counter = 0;
                    foreach (int face in back_comp)
                    {
                        MeshFace f = background.Faces[face];
                        List<int> face_verts = new List<int> { f.A, f.B, f.C, f.D };
                        List<Tuple<int, int>> face_edges = new List<Tuple<int, int>>
                        { new Tuple<int, int>(f.A, f.B), new Tuple<int, int>(f.B, f.C), 
                          new Tuple<int, int>(f.D, f.C), new Tuple<int, int>(f.A, f.D) };

                        foreach(int v in face_verts)
                        {
                            if (verts_to_keep_set.Contains(v))
                                subcells_counter++;
                        }
                        foreach(var edge in face_edges)
                        {
                            if (edges_to_keep_set.Contains(edge))
                                subcells_counter++;
                        }
                    }

                    if (subcells_counter <= remove_components)
                    {
                        foreach(var face in component)
                            remove_faces.Add(face);
                    }
                }
            }

            // Same batch-removal fix as above: DeleteFaces in one pass
            // instead of RemoveAt(i, true) recompacting per call.
            sculpted_mesh.Faces.DeleteFaces(remove_faces.ToList());

            timings.Add($"12 small-component removal ({remove_faces.Count:N0} faces): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Stage 0 diagnostic (no behavior change): report face-type
            // and manifoldness statistics on the final published mesh, so
            // "it looks like triangles in the viewport" can be checked
            // against the actual face data (overlapping quads vs. genuine
            // triangles) rather than inferred from appearance alone.
            {
                int diag_triangle_faces = 0;
                int diag_quad_faces = 0;
                int diag_degenerate_faces = 0;
                Dictionary<Tuple<int, int, int, int>, int> diag_face_key_counts =
                    new Dictionary<Tuple<int, int, int, int>, int>();

                // Scale-relative area tolerance: a face is only "degenerate
                // by area" if it's near-zero relative to an actual cell,
                // not relative to an arbitrary fixed constant that might be
                // wrong for this grid's units.
                double diag_area_tolerance = 1e-6 * Math.Abs(diag_x_dist * diag_y_dist);

                foreach (MeshFace f in sculpted_mesh.Faces)
                {
                    if (f.IsTriangle)
                        diag_triangle_faces++;
                    else
                        diag_quad_faces++;

                    int[] idxs = f.IsTriangle
                        ? new int[] { f.A, f.B, f.C }
                        : new int[] { f.A, f.B, f.C, f.D };

                    bool repeated_index = idxs.Distinct().Count() != idxs.Length;

                    // Shoelace formula on the flat XY projection (valid --
                    // this whole system lives on a flat background plane):
                    // catches distinct-but-collinear/near-zero-area faces
                    // that a repeated-index check alone would miss.
                    double signed_area2 = 0;
                    for (int pi = 0; pi < idxs.Length; pi++)
                    {
                        Point3d pcur = sculpted_mesh.Vertices[idxs[pi]];
                        Point3d pnext = sculpted_mesh.Vertices[idxs[(pi + 1) % idxs.Length]];
                        signed_area2 += pcur.X * pnext.Y - pnext.X * pcur.Y;
                    }
                    bool near_zero_area = Math.Abs(signed_area2) * 0.5 < diag_area_tolerance;

                    if (repeated_index || near_zero_area)
                        diag_degenerate_faces++;

                    int[] sorted_idxs = (int[])idxs.Clone();
                    Array.Sort(sorted_idxs);
                    Tuple<int, int, int, int> key = sorted_idxs.Length == 3
                        ? new Tuple<int, int, int, int>(sorted_idxs[0], sorted_idxs[1], sorted_idxs[2], -1)
                        : new Tuple<int, int, int, int>(sorted_idxs[0], sorted_idxs[1], sorted_idxs[2], sorted_idxs[3]);

                    diag_face_key_counts.TryGetValue(key, out int diag_existing);
                    diag_face_key_counts[key] = diag_existing + 1;
                }

                int diag_duplicate_faces = diag_face_key_counts.Values.Where(c => c > 1).Sum(c => c - 1);

                int diag_non_manifold_edges = 0;
                MeshTopologyEdgeList diag_edges = sculpted_mesh.TopologyEdges;
                for (int de = 0; de < diag_edges.Count; de++)
                {
                    if (diag_edges.GetConnectedFaces(de).Length > 2)
                        diag_non_manifold_edges++;
                }

                // Non-manifold VERTEX check (distinct from non-manifold
                // edges): a "bowtie" -- two face fans touching at only one
                // shared vertex -- can have every incident edge at exactly
                // 2 faces (or fewer at a boundary) while the vertex itself
                // is still non-manifold, because its incident faces don't
                // form one connected fan. For each topology vertex, build
                // the adjacency graph among its own connected faces (two
                // faces adjacent if they share one of the vertex's own
                // connected edges) and check it's a single component.
                int diag_non_manifold_vertices = 0;
                MeshTopologyVertexList diag_verts = sculpted_mesh.TopologyVertices;
                for (int dv = 0; dv < diag_verts.Count; dv++)
                {
                    int[] vfaces = diag_verts.ConnectedFaces(dv);
                    if (vfaces.Length <= 1)
                        continue;

                    int[] vedges = diag_verts.ConnectedEdges(dv);
                    Dictionary<int, int> face_to_local = new Dictionary<int, int>();
                    for (int fi = 0; fi < vfaces.Length; fi++)
                        face_to_local[vfaces[fi]] = fi;

                    // Union-find over the vertex's own incident faces.
                    int[] parent = new int[vfaces.Length];
                    for (int pi = 0; pi < parent.Length; pi++) parent[pi] = pi;
                    Func<int, int> find = null;
                    find = (x) => parent[x] == x ? x : (parent[x] = find(parent[x]));

                    foreach (int ve in vedges)
                    {
                        int[] edge_faces = diag_edges.GetConnectedFaces(ve);
                        if (edge_faces.Length != 2)
                            continue;
                        if (face_to_local.TryGetValue(edge_faces[0], out int f0) && face_to_local.TryGetValue(edge_faces[1], out int f1))
                        {
                            int r0 = find(f0), r1 = find(f1);
                            if (r0 != r1) parent[r0] = r1;
                        }
                    }

                    HashSet<int> roots = new HashSet<int>();
                    for (int pi = 0; pi < parent.Length; pi++)
                        roots.Add(find(pi));

                    if (roots.Count > 1)
                        diag_non_manifold_vertices++;
                }

                timings.Add($"12b STAGE-0 DIAGNOSTIC final mesh ({sculpted_mesh.Faces.Count:N0} faces): "
                    + $"triangle_faces={diag_triangle_faces:N0}, quad_faces={diag_quad_faces:N0}, "
                    + $"degenerate_faces={diag_degenerate_faces:N0}, duplicate_faces={diag_duplicate_faces:N0}, "
                    + $"non_manifold_edges={diag_non_manifold_edges:N0}, non_manifold_vertices={diag_non_manifold_vertices:N0}"
                    + $": {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                phase_timer.Restart();
            }

            DA.SetDataList(0, verts_to_keep);
            DA.SetDataList(1, edges_to_keep);
            DA.SetDataList(2, faces_to_keep);
            DA.SetDataList(3, vert_viz);
            DA.SetDataList(4, edge_viz);
            DA.SetData(6, sculpted_mesh);

            timings.Add($"13 publish outputs: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            total_timer.Stop();
            timings.Add($"TOTAL: {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(7, timings);
        }

        /// <summary>
        /// NEVER CALLED -- kept only for reference, not part of the
        /// component's execution path.
        ///
        /// This is exactly the call that used to sit inside the "10 second
        /// BFS: connected components" loop in SolveInstance (in the while
        /// loop iterating the BFS queue, right after
        /// `Functions.GetAdjacentFaces(face, sculpted_mesh)` is computed).
        /// Its result (`connected_faces`) was computed but never read --
        /// pure dead code. At x=240,y=315 that phase alone took ~150.5s
        /// (99% of the component's total runtime) with ~28,490 faces
        /// processed, while the equivalent first BFS (which never called
        /// this) finished in ~84ms. Since GetConnectedFaces isn't backed by
        /// a precomputed/cached adjacency structure the way
        /// TopologyVertices/TopologyEdges are, calling it once per
        /// dequeued face is consistent with effectively quadratic
        /// behavior over the mesh's face count. Left here, unused, as a
        /// record of what was removed and why -- do not call this from
        /// SolveInstance again without first confirming the underlying
        /// RhinoCommon call has been made cheap for repeated per-face use.
        /// </summary>
        private int[] GetConnectedFacesUnused(int face, Mesh sculpted_mesh)
        {
            return sculpted_mesh.Faces.GetConnectedFaces(face);
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
            get { return new Guid("060ACAF9-C861-4DB4-8615-950DE6E2D6BF"); }
        }
    }
}