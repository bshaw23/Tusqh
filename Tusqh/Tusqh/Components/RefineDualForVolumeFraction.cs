using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
  public class RefineDualForVolumeFraction : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public RefineDualForVolumeFraction()
      : base("RefineDualForVolumeFraction", "DualVF",
        "Refine Dual For Volume Fraction output to Aleph",
        "Sculpt2D", "Aleph")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
            pManager.AddMeshParameter("Dual Grid", "dual", "2D dual grid", GH_ParamAccess.item);
            pManager.AddPointParameter("Face Centroid", "cent", "Center of the face whose volume fraction was just computed", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fraction", "vol", "List of volume fractions", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Use Minimum for Centroid", "cm", "Use the minimum coordinate value for the centroid", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Negate Z Values", "neg", "Use the negative of the mesh's Z values", GH_ParamAccess.item);
            pManager.AddNumberParameter("Shift Z Values", "sft", "Shift the mesh's Z values", GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager.AddBooleanParameter("Verify Nearest-Centroid Fix", "verify", "Also run the original brute-force nearest-centroid search and report any mismatches against the accelerated K-NN result", GH_ParamAccess.item);
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
            pManager.AddMeshParameter("Triangulation", "tri", "Triangulation with volume fraction data ready for Aleph", GH_ParamAccess.item);
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
    /// to store data in output parameters.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
            Stopwatch total_timer = Stopwatch.StartNew();
            Stopwatch phase_timer = Stopwatch.StartNew();
            List<string> timings = new List<string>();

            Mesh dual_mesh_init = null;
            List<Point3d> centroids = new List<Point3d>();
            List<double> volume_fracs = new List<double>();
            bool use_min = false;
            bool negate_z = false;
            double shift_d = 0;
            bool verify = false;

            DA.GetData<Mesh>(0, ref dual_mesh_init);
            DA.GetDataList<Point3d>(1, centroids);
            DA.GetDataList<double>(2, volume_fracs);
            DA.GetData(3, ref use_min);
            DA.GetData(4, ref negate_z);
            if (!DA.GetData(5, ref shift_d))
                shift_d = 0;
            if (!DA.GetData(6, ref verify))
                verify = false;

            float shift = (float)shift_d;

            Mesh dual_mesh = dual_mesh_init.DuplicateMesh();

            timings.Add($"01 inputs ({dual_mesh.Vertices.Count:N0} verts, {centroids.Count:N0} centroids): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // move the volume fraction data to appropriate face
            // for boundary ones, move them to the closest face centroid
            //
            // Dual-grid vertices routinely sit at *exact* ties between
            // several centroids (e.g. a vertex shared by 4 structured-grid
            // cells at equal spacing), and the loop below breaks those ties
            // by lowest index (strict "<" only overwrites on a real
            // improvement, so the first index to reach the minimum keeps
            // it). To preserve that exactly while still avoiding an
            // O(verts x centroids) brute-force scan: batch-query each
            // vertex's K nearest centroids by tree metric (K chosen well
            // above the largest tie group a structured 2D dual grid can
            // produce -- an interior vertex touches at most 4 cells, so 4
            // is the natural bound; 8 leaves comfortable headroom), then
            // run the *exact same* tempdist/mindist comparison as before,
            // just scoped to those K candidates instead of every centroid.
            // If more than K centroids were ever exactly tied for nearest
            // -- not possible for this component's structured-grid inputs
            // -- this would fall back to whichever of the tied candidates
            // the tree ranked within the top K, rather than guaranteed
            // lowest index; flagged in the timing output so that
            // assumption stays checkable rather than silent.
            const int nearest_centroid_k = 8;
            Point3d[] dual_vertex_pts = dual_mesh.Vertices.ToPoint3dArray();
            int k = Math.Min(nearest_centroid_k, centroids.Count);
            List<int[]> nearest_candidates =
                Rhino.Geometry.RTree.Point3dKNeighbors(centroids, dual_vertex_pts, k).ToList();

            int exact_tie_at_k_boundary = 0;
            int[] fast_minidx = new int[dual_mesh.Vertices.Count];

            for (int j = 0; j < dual_mesh.Vertices.Count; ++j)
            {
                Point3d vert = dual_vertex_pts[j];

                // Point3dKNeighbors documents only that these are the K
                // closest indices, not that ties are broken by index order.
                // Sorting ascending before applying the original strict "<"
                // comparison reproduces the brute-force loop's lowest-index
                // tie-break exactly, as long as the whole tie group is
                // within the K candidates returned.
                int[] candidates = nearest_candidates[j];
                Array.Sort(candidates);

                double mindist = double.PositiveInfinity;
                int minidx = -1;
                double maxcandidatedist = double.NegativeInfinity;
                double tempdist;
                foreach (int i in candidates)
                {
                    var pt = centroids[i];
                    tempdist = pt.DistanceToSquared(vert);
                    if (tempdist < mindist)
                    {
                        mindist = tempdist;
                        minidx = i;
                    }
                    if (tempdist > maxcandidatedist)
                        maxcandidatedist = tempdist;
                }

                // Diagnostic only: if the farthest returned candidate is
                // exactly as close as the chosen one, the true tie group
                // might extend beyond K and this vertex's result is worth
                // spot-checking against the brute-force loop. Computed from
                // the actual max distance among candidates rather than
                // assuming the array is distance-sorted. Requires k <
                // centroids.Count -- when K already covers every centroid
                // there's nothing beyond K to miss, so a tie at the
                // boundary isn't a truncation risk.
                if (candidates.Length == k && k < centroids.Count && maxcandidatedist == mindist)
                    exact_tie_at_k_boundary++;

                fast_minidx[j] = minidx;
                vert.Z = volume_fracs[minidx];
                dual_mesh.Vertices[j] = (Point3f)vert;
            }

            timings.Add($"02 nearest-centroid assignment ({dual_mesh.Vertices.Count:N0} verts, {centroids.Count:N0} centroids, K={k}"
                + (exact_tie_at_k_boundary > 0 ? $", WARNING: {exact_tie_at_k_boundary:N0} vertices had a tie reaching the K-th candidate -- verify against brute force" : "")
                + $"): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // Opt-in correctness check: re-run the original brute-force
            // nearest-centroid search (unchanged algorithm) and diff its
            // result against the K-NN result above, index for index. Only
            // runs when the "verify" input is explicitly set true, so
            // default behavior/speed is untouched; meant to be flipped on
            // once against real data to confirm the K-NN fix is exact,
            // then flipped back off.
            if (verify)
            {
                int mismatches = 0;
                int first_mismatch = -1;
                for (int j = 0; j < dual_vertex_pts.Length; ++j)
                {
                    Point3d vert = dual_vertex_pts[j];
                    double mindist = double.PositiveInfinity;
                    int minidx = -1;
                    double tempdist;
                    for (int i = 0; i < centroids.Count; ++i)
                    {
                        var pt = centroids[i];
                        tempdist = pt.DistanceToSquared(vert);
                        if (tempdist < mindist)
                        {
                            mindist = tempdist;
                            minidx = i;
                        }
                    }

                    // Compare the selected centroid index directly, not the
                    // resulting volume-fraction value: the fast path writes
                    // its result through a Point3f (float), so comparing
                    // floats-vs-doubles produces false-positive mismatches
                    // from rounding alone, and would equally hide a true
                    // index mismatch between two centroids that happen to
                    // share the same (or very close) volume fraction.
                    if (fast_minidx[j] != minidx)
                    {
                        mismatches++;
                        if (first_mismatch == -1) first_mismatch = j;
                    }
                }

                timings.Add($"02b VERIFY brute-force cross-check: {mismatches:N0} mismatches out of {dual_vertex_pts.Length:N0}"
                    + (first_mismatch >= 0 ? $" (first at vert {first_mismatch}, pos={dual_vertex_pts[first_mismatch]})" : "")
                    + $": {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                phase_timer.Restart();
            }

            // next, modify each quadrilateral face to be subdivided into 4 triangles
            //
            // Rebuilt as a single forward pass into a brand-new mesh instead
            // of repeatedly removing the original quad from, and re-adding 4
            // triangles to, the SAME growing mesh: MeshFaceList.RemoveAt
            // shifts every face already appended after the removal point
            // down by one, so each of these ~76k removals (at x=240,y=315)
            // was re-shifting an ever-larger tail of already-added
            // triangles -- an O(quads^2) cost that measured at ~226s, 99.4%
            // of this component's total runtime at that resolution.
            //
            // This is output-preserving: quad_face.A/B/C/D always index the
            // ORIGINAL (pre-subdivision) vertices, whose Z values were fixed
            // by phase 02 and never touched again by this loop, so the
            // per-corner min/max Z selection below depends only on those 4
            // corner values -- never on what's happened to any other face or
            // on iteration order. Iterating original face index from
            // init_face_count-1 down to 0 (same order as before) and always
            // appending the new centroid vertex/4 triangles to the new mesh
            // reproduces the exact same vertex list, the exact same centroid
            // indices (both start at the original vertex count and
            // increment by one per face, in the same order), and the exact
            // same face list and order as the original in-place loop --
            // just without ever mutating a mesh's face list in place.
            int init_face_count = dual_mesh.Faces.Count;
            Mesh subdivided_mesh = new Mesh();
            for (int j = 0; j < dual_mesh.Vertices.Count; ++j)
                subdivided_mesh.Vertices.Add(dual_mesh.Vertices[j]);

            for (int i = init_face_count - 1; i > -1; --i)
            {
                var quad_face = dual_mesh.Faces[i];
                var centroid = dual_mesh.Faces.GetFaceCenter(i);
                // extract the z coordinates of all adjacent vertices and use the lowest one
                List<int> iter_list = new List<int>(4) { quad_face.A, quad_face.B, quad_face.C, quad_face.D };
                foreach (int idx in iter_list)
                {
                    // Oddly the homology does not appear to change when I change which one I use
                    // What is up with that?
                    // Should I change the negate z from multiplying by -1 to just shift them?

                    if (dual_mesh.Vertices[idx].Z < centroid.Z && use_min)
                        centroid.Z = dual_mesh.Vertices[idx].Z;
                    else if (dual_mesh.Vertices[idx].Z > centroid.Z && !use_min)
                        centroid.Z = dual_mesh.Vertices[idx].Z;

                }

                int new_idx = subdivided_mesh.Vertices.Add(centroid);
                subdivided_mesh.Faces.AddFace(quad_face.A, quad_face.B, new_idx);
                subdivided_mesh.Faces.AddFace(quad_face.B, quad_face.C, new_idx);
                subdivided_mesh.Faces.AddFace(quad_face.C, quad_face.D, new_idx);
                subdivided_mesh.Faces.AddFace(quad_face.D, quad_face.A, new_idx);
            }

            dual_mesh = subdivided_mesh;

            timings.Add($"03 quad-to-triangle subdivision ({init_face_count:N0} quads): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            if (negate_z)
            {
                Point3f temp_pt;
                for (int i = 0; i < dual_mesh.Vertices.Count; ++i)
                {
                    temp_pt = dual_mesh.Vertices[i];
                    temp_pt.Z *= -1;
                    dual_mesh.Vertices[i] = temp_pt;
                }
            }

            if (shift != 0)
            {
                Point3f temp_pt;
                for (int i = 0; i < dual_mesh.Vertices.Count; ++i)
                {
                    temp_pt = dual_mesh.Vertices[i];
                    temp_pt.Z += shift;
                    dual_mesh.Vertices[i] = temp_pt;
                }
            }

            timings.Add($"04 z negate/shift: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            DA.SetData(0, dual_mesh);

            timings.Add($"05 publish output: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            total_timer.Stop();
            timings.Add($"TOTAL: {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(1, timings);
    }

    /// <summary>
    /// Provides an Icon for every component that will be visible in the User Interface.
    /// Icons need to be 24x24 pixels.
    /// </summary>
    protected override System.Drawing.Bitmap Icon
    {
      get
      { 
        // You can add image files to your project resources and access them like this:
        //return Resources.IconForThisComponent;
        return null;
      }
    }

    /// <summary>
    /// Each component must have a unique Guid to identify it. 
    /// It is vital this Guid doesn't change otherwise old ghx files 
    /// that use the old ID will partially fail during loading.
    /// </summary>
    public override Guid ComponentGuid
    {
      get { return new Guid("60b9f393-dc44-4781-ad23-30fa2b036dae"); }
    }
  }
}
