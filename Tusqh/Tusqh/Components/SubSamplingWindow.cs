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
    public class SubSamplingWindow : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public SubSamplingWindow()
          : base("SubSamplingWindow", "window",
              "Subsample a grid of points within a window of specified size",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background grid", "back", "Background grid", GH_ParamAccess.item);                                                       // 0
            pManager.AddCurveParameter("Polylines", "pl", "Oriented polylines", GH_ParamAccess.list);                                                           // 1
            pManager.AddIntegerParameter("Window Size-x", "w_x", "Window size in x-direction", GH_ParamAccess.item);                                            // 2
            pManager.AddIntegerParameter("Window Size-y", "w_y", "Window size in y-direction", GH_ParamAccess.item);                                            // 3
            pManager.AddIntegerParameter("Sample Points-x", "n_x", "Number of sample points in x-direction", GH_ParamAccess.item);                              // 4
            pManager.AddIntegerParameter("Sample Pionts-y", "n_y", "Number of sample points in y-direction", GH_ParamAccess.item);                              // 5
            //pManager.AddBooleanParameter("Reverse Orientation", "rev", "Reverse orientation of the boundary curve", GH_ParamAccess.item);                       // 6
            //pManager.AddIntegerParameter("Method of Sample", "mos", "0 if average, 1 if average all positive, 2 if average all negative", GH_ParamAccess.item); // 7
            //pManager[6].Optional = true;
            //pManager[7].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Window", "w", "window as mesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Volume Fractions", "w_vols", "Volume fractions for the window", GH_ParamAccess.list);
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
            Mesh background = new Mesh();
            List<Curve> ori_curves = new List<Curve>();
            bool reverse_boundary_orient = false;
            int w_x = new int();
            int w_y = new int();
            int n_x = new int();
            int n_y = new int();
            int avg_int = new int();
            MethodOfAverage avg = MethodOfAverage.Average;

            DA.GetData(0, ref background);
            DA.GetDataList(1, ori_curves);
            DA.GetData(2, ref w_x);
            DA.GetData(3, ref w_y);
            DA.GetData(4, ref n_x);
            DA.GetData(5, ref n_y);
            //DA.GetData(6, ref reverse_boundary_orient);
            //DA.GetData(7, ref avg_int);

            switch (avg_int)
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

            double x_dist = 0;
            double y_dist = 0;
            Point3d A = background.Vertices[background.Faces[0].A];
            Point3d B = background.Vertices[background.Faces[0].B];
            Point3d C = background.Vertices[background.Faces[0].C];
            Vector3d vec = C - A;
            x_dist = Math.Abs(vec.X);
            y_dist = Math.Abs(vec.Y);

            BoundingBox box = background.GetBoundingBox(false);
            Point3d cent = box.Center;

            Point3d origin = new Point3d(cent.X + (x_dist * w_x / 2), cent.Y - (y_dist * w_y / 2), 0);
            Vector3d normal = Vector3d.CrossProduct(vec, B - A);
            Plane plane = new Plane(origin, normal);
            Rectangle3d rectangle = new Rectangle3d(plane, x_dist * w_x, y_dist * w_y);
            Mesh window = Mesh.CreateFromPlane(plane, rectangle.X, rectangle.Y, 2 * w_x, 2 * w_y);

            List<Tuple<double, double>> vert_array = new List<Tuple<double, double>>();
            List<Tuple<uint, uint>> edge_array = new List<Tuple<uint, uint>>();
            AlephSupport.ProcessPolylines(ori_curves, reverse_boundary_orient, out vert_array, out edge_array);

            List<Point3d> centroids = new List<Point3d>();
            List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>();
            List<Point3d> sample_pts = new List<Point3d>();
            AlephSupport.GetQuerryPoints(window, n_x, n_y, out centroids, out sample_pts, out querry_pts);

            // reindex to put into LibIGL---this code shouldn't ever change
            List<double> vert_list = new List<double>();
            List<int> edge_list = new List<int>();
            List<double> querry_list = new List<double>(2 * querry_pts.Count);
            AlephSupport.ColumnMajorConstruction(vert_array, edge_array, querry_pts, 
                out vert_list, out edge_list, out querry_list);

            // output to C++ code
            List<double> winding = new List<double>(querry_pts.Count);
            for (int i = 0; i < querry_pts.Count; ++i)
                winding.Add(0);
            EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edge_list), edge_array.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 2,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));

            double divisor = (double)n_x * (double)n_y;

            List<double> pt_winding = new List<double>(window.Faces.Count * n_x * n_y);
            int n_pts = n_x * n_y;
            int counter = 0;
            double cur_wind;
            foreach (MeshFace face in window.Faces)
            {
                double volume_fraction = 0;
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

            DA.SetData(0, window);
            DA.SetDataList(1, volume_fractions);
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
            get { return new Guid("5805F703-BC3A-4D07-A6FD-969C653A4308"); }
        }
    }
}