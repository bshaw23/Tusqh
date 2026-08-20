using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eto.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Rhino.Geometry;

using System.Linq;

using EigenWrapper.Eigen;
using System.Collections;

namespace Sculpt2D.Components
{
    public class VolumeFractionsEdges : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public VolumeFractionsEdges()
          : base("PointsInEdges", "edgepts",
              "For each face a grid of points are checked if they are in the set of edges or not",
              "Sculpt2D", "Volume")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background Grid", "grid", "Background Grid from bounding box", GH_ParamAccess.item);
            pManager.AddIntegerParameter("u points", "upts", "number of points to check in u direction", GH_ParamAccess.item);
            pManager.AddIntegerParameter("v points", "vpts", "number of points to check in v direction", GH_ParamAccess.item);
            pManager.AddCurveParameter("Polylines", "pl", "Oriented polylines", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Reverse Orientation", "rev", "Reverse orientation of the boundary curve", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Method of Sample", "mos", "0 if average, 1 if average all positive, 2 if average all negative", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Accelerate", "accel", "True (default) uses the hierarchical WindingNumberFast2D acceleration; false falls back to the brute-force WindingNumber, for comparison", GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Volume Fraction", "vol", "Returns a list of volume fractions", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centroid", "cent", "Center of the face whose volume fraction was just computed", GH_ParamAccess.list);
            pManager.AddCurveParameter("Boundary curves", "bc", "Boundary curves of the mesh used", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Sample Points", "sample", "sample points of the face", GH_ParamAccess.list);
            pManager.AddNumberParameter("Point Winding Number", "wpt", "Computed winding number of the point", GH_ParamAccess.list);
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
        }

        enum MethodOfAverage : uint
        {
            Average = 0,
            AveragePositive = 1,
            AverageNegative = 2
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

            Mesh background_grid = new Mesh();
            int u_pts = new int();
            int v_pts = new int();
            List<Curve> ori_curves = new List<Curve>();

            bool reverse_boundary_orient = false;
            int avg_int = 0;
            MethodOfAverage avg = MethodOfAverage.Average;
            bool accelerate = true;

            DA.GetData(0, ref background_grid);
            DA.GetData(1, ref u_pts);
            DA.GetData(2, ref v_pts);
            DA.GetDataList(3, ori_curves);
            if (!DA.GetData(4, ref reverse_boundary_orient))
                reverse_boundary_orient = false;
            if (!DA.GetData(5, ref avg_int))
                avg_int = 0;
            if (!DA.GetData(6, ref accelerate))
                accelerate = true;

            switch(avg_int)
            {
                case 0:
                    avg = MethodOfAverage.Average;
                    break;
                case 1:
                    avg = MethodOfAverage.AveragePositive;
                    break;
                case 2:
                    avg = MethodOfAverage.AverageNegative;
                    break;
                default:
                    avg = MethodOfAverage.Average;
                    break;
            }

            List<double> volume_fractions = new List<double>();
            double u_pts_double = (double)u_pts;
            double v_pts_double = (double)v_pts;
            double volume_fraction;

            List<Point3d> centroid;
            List<Point3d> pt_grid;
            List<double> pt_winding = new List<double>(background_grid.Faces.Count * u_pts * v_pts);

            timings.Add($"01 inputs: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            List<Tuple<double, double>> vert_array;
            List<Tuple<uint, uint>> edge_array;
            AlephSupport.ProcessPolylines(ori_curves, reverse_boundary_orient, out vert_array, out edge_array);

            timings.Add($"02 polyline processing ({vert_array.Count:N0} verts, {edge_array.Count:N0} edges): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // points to querry in the background mesh
            List<Tuple<double, double>> querry_pts;
            AlephSupport.GetQuerryPoints(background_grid, u_pts, v_pts, out centroid, out pt_grid, out querry_pts);

            timings.Add($"03 sample point generation ({querry_pts.Count:N0} points): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // reindex to put into LibIGL---this code shouldn't ever change
            List<double> vert_list;
            List<int> edge_list;
            List<double> querry_list;
            AlephSupport.ColumnMajorConstruction(vert_array, edge_array, querry_pts, out vert_list, out edge_list, out querry_list);

            timings.Add($"04 column-major packing: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            // output to C++ code
            List<double> winding = new List<double>(querry_pts.Count);
            for (int i = 0; i < querry_pts.Count; ++i)
                winding.Add(0);
            if (accelerate)
            {
                EigenDenseUtilities.WindingNumberFast2D(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edge_list), edge_array.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));
            }
            else
            {
                EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edge_list), edge_array.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 2,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));
            }

            timings.Add($"05 native winding numbers, {(accelerate ? "accelerated" : "brute force")} ({querry_pts.Count:N0} points): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            double divisor = u_pts_double * v_pts_double;

            int n_pts = u_pts * v_pts;
            int counter = 0;
            double cur_wind;
            foreach (MeshFace face in background_grid.Faces)
            {
                volume_fraction = 0;
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    cur_wind = winding[counter + pt_idx];
                    if (cur_wind >= 0 && avg == MethodOfAverage.AverageNegative)
                        cur_wind = 0;
                    else if (cur_wind <= 0 && avg == MethodOfAverage.AveragePositive)
                        cur_wind = 0;
                    volume_fraction += cur_wind;
                    pt_winding.Add(cur_wind);
                }
                volume_fraction /= divisor;
                volume_fractions.Add(volume_fraction);
                counter += n_pts;
            }

            timings.Add($"06 volume-fraction aggregation: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            DA.SetDataList(0, volume_fractions);
            DA.SetDataList(1, centroid);
            DA.SetDataList(2, ori_curves);
            DA.SetDataList(3, pt_grid);
            DA.SetDataList(4, pt_winding);

            timings.Add($"07 publish outputs: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            total_timer.Stop();
            timings.Add($"TOTAL (excluding timing output): {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(5, timings);
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
            get { return new Guid("06b59a23-f56f-4ede-a073-71155c915b29"); }
    }
  }
}
