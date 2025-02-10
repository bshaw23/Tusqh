using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

using EigenWrapper.Eigen;
using System.Collections;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Sculpt2D.Components
{
    public class OverlayGrid : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public OverlayGrid()
          : base("Overlay Grid", "overlay",
              "Overlay grid to check simulate quadtree",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Bounding Box", "bb", "Bounding box of curves to be sculpted", GH_ParamAccess.item);
            pManager.AddIntegerParameter("X", "x", "number of x divisions in original mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Y", "y", "number of y divisions in original mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("x points", "x", "Number of points in x-direction", GH_ParamAccess.item);
            pManager.AddIntegerParameter("y points", "y", "Number of points in y-direction", GH_ParamAccess.item);
            pManager.AddCurveParameter("Polylines", "pl", "Polylines to be sculpted to", GH_ParamAccess.list);    
            pManager.AddNumberParameter("Cutoff", "cut", "Volume fraction cuttoff", GH_ParamAccess.item);         
            pManager.AddNumberParameter("Delta", "delta", "Difference from volume fraction", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Overlay grid", "overlay", "Overlay grid", GH_ParamAccess.item);
            pManager.AddMeshParameter("Quad tree", "quad", "Simulation of quadtree", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rhino.Geometry.Rectangle3d bounding_box = new Rectangle3d();
            int x_div = new int();
            int y_div = new int();
            int x_pts = new int();
            int y_pts = new int();
            List<Curve> polylines = new List<Curve>();
            double cut = new double();
            double delta = new double();

            DA.GetData(0, ref bounding_box);
            DA.GetData(1, ref x_div);
            DA.GetData(2, ref y_div);
            DA.GetData(3, ref x_pts);
            DA.GetData(4, ref y_pts);
            DA.GetDataList(5, polylines);
            DA.GetData(6, ref cut);
            DA.GetData(7, ref delta);

            Rhino.Geometry.Plane rectangle = new Rhino.Geometry.Plane(bounding_box.Plane);
            var corner = bounding_box.Corner(0);
            var x_corner = bounding_box.Corner(1);
            var y_corner = bounding_box.Corner(3);
            Rhino.Geometry.Interval X = new Interval(corner.X, x_corner.X);
            Rhino.Geometry.Interval Y = new Interval(corner.Y, y_corner.Y);
            x_div *= 2;
            y_div *= 2;
            var overlay_mesh = Rhino.Geometry.Mesh.CreateFromPlane(rectangle, X, Y, x_div, y_div);
            Mesh background_grid = overlay_mesh;
            DA.SetData(0, overlay_mesh);

            double x_pts_double = (double)x_pts;
            double y_pts_double = (double)y_pts;
            int A = new int();
            int B = new int();
            int C = new int();
            int D = new int();
            Point3d point_a = new Point3d();
            Point3d point_b = new Point3d();
            Point3d point_c = new Point3d();
            Point3d point_d = new Point3d();
            List<Point3d> centroid = new List<Point3d>();
            List<Point3d> point_grid = new List<Point3d>();
            double x_dist; //for now x distance
            double y_dist; //for now y distance
            var x_segments = 0.0;
            var y_segments = 0.0;
            List<Point3d> pt_grid = new List<Point3d>();

            // points to querry in the background mesh
            List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>(background_grid.Faces.Count * x_pts * y_pts);
            foreach (MeshFace face in background_grid.Faces)
            {
                // indeces for corners of face starting at bottom left and goin counter clockwise
                A = face.A;
                B = face.B;
                C = face.C;
                D = face.D;

                point_a = background_grid.Vertices.Point3dAt(A);
                point_b = background_grid.Vertices.Point3dAt(B);
                point_c = background_grid.Vertices.Point3dAt(C);
                point_d = background_grid.Vertices.Point3dAt(D);

                centroid.Add(new Point3d((point_a + point_b + point_c + point_d) / 4));

                x_dist = point_b.X - point_a.X;
                y_dist = point_d.Y - point_a.Y;

                x_segments = x_dist / x_pts_double;
                y_segments = y_dist / y_pts_double;

                point_grid.Clear();

                for (int i = 0; i < x_pts; i++)
                {
                    double x = 0.0;
                    double y = 0.0;
                    double z = 0.0;

                    for (int j = 0; j < y_pts; j++)
                    {
                        x = point_a.X + x_segments / 2 + x_segments * i;
                        y = point_a.Y + y_segments / 2 + y_segments * j;
                        z = point_a.Z;

                        querry_pts.Add(new Tuple<double, double>(x, y));
                        pt_grid.Add(new Point3d(x, y, 0));
                    }
                }
            }

            List<Tuple<double, double>> vert_array = new List<Tuple<double, double>>();
            List<Tuple<uint, uint>> edge_array = new List<Tuple<uint, uint>>();
            uint vertex_count = 0;
            foreach (var polylineiter in polylines)
            {
                var polylinecurve = polylineiter.ToNurbsCurve();
                var polyline = polylinecurve.Points;

                var polyline_pts = polyline.Select(p => p);
                uint pt_count = 0;
                // store all vertices
                foreach (var pt in polyline_pts)
                {
                    vert_array.Add(new Tuple<double, double>(pt.X, pt.Y));
                    // store the associated edges with this particular boundary
                    if (pt_count != 0)
                    {
                        uint prev_idx = vertex_count + pt_count - 1;
                        edge_array.Add(new Tuple<uint, uint>(prev_idx, prev_idx + 1));
                    }
                    pt_count += 1;
                }

                // if the polyline is a topological circle (true for every boundary
                // of a mesh, get the last edge as well
                if (polylinecurve.IsClosed)
                    edge_array.Add(new Tuple<uint, uint>(vertex_count + pt_count - 1, vertex_count));

                // iterate to the next boundary
                vertex_count += pt_count;
            }

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

            double divisor = x_pts_double * y_pts_double;
            List<double> volume_fractions = new List<double>();
            List<double> pt_winding = new List<double>();

            int n_pts = x_pts * y_pts;
            int counter = 0;
            double cur_wind;
            foreach (MeshFace face in background_grid.Faces)
            {
                double volume_fraction = 0;
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    cur_wind = winding[counter + pt_idx];
                    volume_fraction += cur_wind;
                    pt_winding.Add(cur_wind);
                }
                volume_fraction /= divisor;
                volume_fractions.Add(volume_fraction);
                counter += n_pts;
            }

            double top = cut - delta;
            double bottom = cut + delta;
            Rhino.Geometry.Mesh quad_tree_mesh = (Rhino.Geometry.Mesh)overlay_mesh.Duplicate();
            int num_faces = background_grid.Faces.Count;
            for (int i = num_faces - 1; i >= 0; --i)
            {
                if (volume_fractions[i] < bottom && volume_fractions[i] > top) 
                    quad_tree_mesh.Faces.RemoveAt(i, true);
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
            get { return new Guid("B126DAF4-36DE-450C-AABD-3398A34B5773"); }
        }
    }
}