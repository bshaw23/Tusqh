using System;
using System.Collections.Generic;
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
            pManager[4].Optional = true;
            pManager[5].Optional = true;
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
            Mesh background_grid = new Mesh();
            int u_pts = new int();
            int v_pts = new int();
            List<Curve> ori_curves = new List<Curve>();

            bool reverse_boundary_orient = false;
            int avg_int = 0;
            MethodOfAverage avg = MethodOfAverage.Average;

            DA.GetData(0, ref background_grid);
            DA.GetData(1, ref u_pts);
            DA.GetData(2, ref v_pts);
            DA.GetDataList(3, ori_curves);
            if (!DA.GetData(4, ref reverse_boundary_orient))
                reverse_boundary_orient = false;
            if (!DA.GetData(5, ref avg_int))
                avg_int = 0;

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

            int A;
            int B;
            int C;
            int D;
            Point3d point_a = new Point3d();
            Point3d point_b = new Point3d();
            Point3d point_c = new Point3d();
            Point3d point_d = new Point3d();
            double u_dist;//for now x distance
            double v_dist;//for now y distance
            var u_segments = 0.0;
            var v_segments = 0.0;
            string u_string = u_pts.ToString();
            string v_string = v_pts.ToString();
            List<Point3d> point_grid = new List<Point3d>();
            List<Point3d> centroid = new List<Point3d>();
            double x;
            double y;
            double z;

            double volume_fraction = new double();

            List<Point3d> pt_grid = new List<Point3d>(background_grid.Faces.Count * u_pts * v_pts);
            List<double> pt_winding = new List<double>(pt_grid.Capacity);


            List<Tuple<double, double>> vert_array = new List<Tuple<double, double>>();
            List<Tuple<uint, uint>> edge_array = new List<Tuple<uint, uint>>();
            uint vertex_count = 0;
            AlephSupport.ProcessPolylines(ori_curves, reverse_boundary_orient, out vert_array, out edge_array);

            // points to querry in the background mesh
            List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>(background_grid.Faces.Count * u_pts * v_pts);
            AlephSupport.GetQuerryPoints(background_grid, u_pts, v_pts, out centroid, out pt_grid, out querry_pts);

            // reindex to put into LibIGL---this code shouldn't ever change
            List<double> vert_list = new List<double>();
            List<int> edge_list = new List<int>();
            List<double> querry_list = new List<double>(2 * querry_pts.Count);
            bool col_major = true; // all input should be in column-major format, so this should be true

            // Column major population
            if (col_major)
            {
                foreach (var item in vert_array)
                {
                    vert_list.Add(item.Item1);
                }
                foreach (var item in vert_array)
                {
                    vert_list.Add(item.Item2);
                }


                foreach (var item in edge_array)
                {
                    edge_list.Add((int)item.Item1);
                }
                foreach (var item in edge_array)
                {
                    edge_list.Add((int)item.Item2);
                }

                foreach (var item in querry_pts)
                {
                    querry_list.Add(item.Item1);
                }
                foreach (var item in querry_pts)
                {
                    querry_list.Add(item.Item2);
                }
            }
            else // row major---this should never be used
            {
                foreach (var item in vert_array)
                {
                    vert_list.Add(item.Item1);
                    vert_list.Add(item.Item2);
                }
                foreach (var item in edge_array)
                {
                    edge_list.Add((int)item.Item1);
                    edge_list.Add((int)item.Item2);
                }
                foreach (var item in querry_pts)
                {
                    querry_list.Add(item.Item1);
                    querry_list.Add(item.Item2);
                }
            }

            // output to C++ code
            List<double> winding = new List<double>(querry_pts.Count);
            for (int i = 0; i < querry_pts.Count; ++i)
                winding.Add(0);
            EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edge_list), edge_array.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));

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


            DA.SetDataList(2, ori_curves);


            DA.SetDataList(0, volume_fractions);
            DA.SetDataList(1, centroid);
            DA.SetDataList(3, pt_grid);
            DA.SetDataList(4, pt_winding);



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
