using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EigenWrapper.Eigen;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Sculpt2D.Components
{
    public class BackVol3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BackVol3D()
          : base("Background and Volumes", "backvol",
              "Builds a background mesh and calculates volume fractions",
              "Sculpt3D", "sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Bounding Box", "box", "Bounding box of object to be sculpted", GH_ParamAccess.item);                                   // 0
            pManager.AddIntegerParameter("X", "x", "number of x divisions", GH_ParamAccess.item);                                                            // 1
            pManager.AddIntegerParameter("Y", "y", "number of y divisions", GH_ParamAccess.item);                                                            // 2
            pManager.AddIntegerParameter("Z", "z", "number of z divisions", GH_ParamAccess.item);                                                            // 3
            pManager.AddIntegerParameter("x points", "x", "Number of points in x-direction", GH_ParamAccess.item);                                           // 4
            pManager.AddIntegerParameter("y points", "y", "Number of points in y-direction", GH_ParamAccess.item);                                           // 5
            pManager.AddIntegerParameter("z points", "z", "Number of points in z-direction", GH_ParamAccess.item);                                           // 6
            pManager.AddMeshParameter("Surface Mesh", "mesh", "Surface mesh to be sculpted", GH_ParamAccess.item);                                           // 7
            pManager.AddBooleanParameter("Reverse Orientation", "rev", "Use reverse orientation of the curves", GH_ParamAccess.item);                        // 8
            pManager.AddIntegerParameter("Method of Sample", "avg", "0 - average, 1 - average of positives, 2 - average of negatives", GH_ParamAccess.item); // 9
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Background Mesh", "mesh", "Background Mesh within bounding box", GH_ParamAccess.item);                       // 0
            pManager.AddNumberParameter("Volume Fractions", "vol", "Volume fraction list corresponding to background cells", GH_ParamAccess.list);  // 1
            pManager.AddPointParameter("Hex Centroids", "cent", "Centroid of hexahedral cells", GH_ParamAccess.list);                               // 2
            //pManager.AddCurveParameter("Boundary Curves", "boud", "Boundary curves of input curves", GH_ParamAccess.list);                          
            pManager.AddPointParameter("Sample Points", "pts", "List of sample points", GH_ParamAccess.list);                                       // 3
            pManager.AddNumberParameter("Winding Numbers", "wind", "List of winding numbers", GH_ParamAccess.list);                                 // 4
            pManager.AddIntegerParameter("Divisions", "divs", "List of x_div, y_div, and z_div", GH_ParamAccess.list);                              // 5
        }

        enum MethodOfAverage : uint
        {
            Average = 0,
            AveragePositive = 1,
            AverageNegative = 2
        }

        private List<double> SubdivideDualIntervalList(List<double> pts)
        {
            List<double> dual = new List<double>();
            dual.Add(pts[0]);
            for (int i = 1; i < pts.Count; ++i)
                dual.Add(pts[i - 1] + (pts[i] - pts[i - 1]) / 2.0);
            dual.Add(pts.Last());

            return dual;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rhino.Geometry.Box box = new Rhino.Geometry.Box();
            int x_div = 0;
            int y_div = 0;
            int z_div = 0;
            Mesh surface_mesh = new Mesh();
            int x_pts = new int();
            int y_pts = new int();
            int z_pts = new int();
            bool reverse_boundary_orient = false;
            int avg_int = 0;
            MethodOfAverage avg = MethodOfAverage.Average;
            bool compute_vols = true;

            DA.GetData(0, ref box);
            DA.GetData(1, ref x_div);
            DA.GetData(2, ref y_div);
            DA.GetData(3, ref z_div);
            DA.GetData(4, ref x_pts);
            DA.GetData(5, ref y_pts);
            DA.GetData(6, ref z_pts);
            if (!DA.GetData(7, ref surface_mesh))
                compute_vols = false;
            if (!DA.GetData(8, ref reverse_boundary_orient))
                reverse_boundary_orient = false;
            if (!DA.GetData(9, ref avg_int))
                avg_int = 0;

            List<Tuple<double, double, double>> vert_array = new List<Tuple<double, double, double>>();
            List<Tuple<int, int>> edge_array = new List<Tuple<int, int>>();
            List<Tuple<int, int, int>> triangle_array = new List<Tuple<int, int, int>>();

            MeshVertexList vertices = surface_mesh.Vertices;
            AlephSupport.ProcessMesh(surface_mesh, out vert_array, out triangle_array);

            Mesh background_mesh = Rhino.Geometry.Mesh.CreateFromBox(box, x_div, y_div, z_div);
            var x_interval = box.X;
            var y_interval = box.Y;
            var z_interval = box.Z;
            var corners = box.GetCorners();
            double x_length = x_interval.Length;
            double y_length = y_interval.Length;
            double z_length = z_interval.Length;
            double x_dist = x_length / (double)x_div;
            double y_dist = y_length / (double)y_div;
            double z_dist = z_length / (double)z_div;
            double x_segments = x_dist / (double)x_pts;
            double y_segments = y_dist / (double)y_pts;
            double z_segments = z_dist / (double)z_pts;
            List<double> volume_fractions = new List<double>();
            List<Point3d> point_grid = new List<Point3d>();
            List<Point3d> centroids = new List<Point3d>();
            List<Tuple<double, double, double>> querry_pts = new List<Tuple<double, double, double>>(x_div * y_div * z_div * x_pts * y_pts * z_pts);
            List<double> pt_winding = new List<double>(querry_pts.Capacity);
            for (int x = 0; x < x_div; x++)
            { 
                for (int y = 0; y < y_div; y++)
                {
                    for (int z = 0; z < z_div; z++)
                    {
                        Point3d point = new Point3d();
                        Point3d point_a = new Point3d();
                        double double_x = (double)x;
                        double double_y = (double)y;
                        double double_z = (double)z;
                        point.X = x_interval.T0 + (x_dist * (0.5 + double_x));
                        point.Y = y_interval.T0 + (y_dist * (0.5 + double_y));
                        point.Z = z_interval.T0 + (z_dist * (0.5 + double_z));
                        centroids.Add(point);

                        point_a.X = x_interval.T0 + (x_dist * double_x);
                        point_a.Y = y_interval.T0 + (y_dist * double_y);
                        point_a.Z = z_interval.T0 + (z_dist * double_z);

                        for (int i = 0; i < x_pts; i++)
                        {
                            for (int j = 0; j < y_pts; j++)
                            {
                                for (int k = 0; k < z_pts; k++)
                                {
                                    double x_querry = point_a.X + x_segments / 2 + x_segments * i;
                                    double y_querry = point_a.Y + y_segments / 2 + y_segments * j;
                                    double z_querry = point_a.Z + z_segments / 2 + z_segments * k;

                                    querry_pts.Add(new Tuple<double, double, double>(x_querry, y_querry, z_querry));
                                    point_grid.Add(new Point3d(x_querry, y_querry, z_querry));
                                }
                            }
                        }
                    }
                }
            }

            // reindex to put into LibIGL---this code shouldn't ever change
            List<double> vert_list = new List<double>();
            List<int> triangle_list = new List<int>();
            List<double> querry_list = new List<double>(3 * querry_pts.Count);
            bool col_major = true; // all input should be in column-major format, so this should be true

            // Column major population
            AlephSupport.ColumnMajorConstruction(vert_array, triangle_array, querry_pts, out vert_list, out triangle_list, out querry_list);

            // output to C++ code
            if (compute_vols)
            {
                List<double> winding = new List<double>(querry_pts.Count);
                for (int i = 0; i < querry_pts.Count; ++i)
                    winding.Add(0);
                EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 3,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(triangle_list), triangle_array.Count, 3,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 3,
                                                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));


                double divisor = (double)x_pts * (double)y_pts * (double)z_pts;

                int n_pts = x_pts * y_pts * z_pts;
                int counter = 0;
                double cur_wind;
                double volume_fraction = 0;
                foreach (Point3d centroid in centroids)
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
            }

            List<int> divs = new List<int> { x_div, y_div, z_div };

            DA.SetData(0, background_mesh);
            DA.SetDataList(2, centroids);
            DA.SetDataList(3, point_grid);
            DA.SetDataList(5, divs);
            if (compute_vols)
            {
                DA.SetDataList(1, volume_fractions);
                DA.SetDataList(4, pt_winding);
            }
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
            get { return new Guid("360E927D-7693-4574-9B36-F9811880B3B6"); }
        }
    }
}