using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Linq;
using GH_IO.Serialization;
//using System.Numerics;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Rhino.Geometry;
using Rhino.Runtime;
using Rhino.UI;
using Sculpt2D.Sculpt3D;
using Sculpt2D.Sculpt3D.Collections;

namespace Sculpt2D.Components
{
    public class Dual3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Dual3D()
          : base("Dual 3D", "Dual 3D",
              "Creates a dual mesh of the background mesh",
              "Sculpt3D", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Box", "box", "Bounding box of object", GH_ParamAccess.item);       // 0
            pManager.AddIntegerParameter("X", "x", "number of x divisions", GH_ParamAccess.item);        // 1
            pManager.AddIntegerParameter("Y", "y", "number of y divisions", GH_ParamAccess.item);        // 2
            pManager.AddIntegerParameter("Z", "z", "number of z divisions", GH_ParamAccess.item);        // 3
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of hexahedral dual mesh", GH_ParamAccess.list);             // 0
            pManager.AddGenericParameter("Hexes", "hexes", "Hexes of dual mesh as list of vertex indicies", GH_ParamAccess.list);   // 1
            pManager.AddNumberParameter("x distance", "xdist", "length of hexes in x", GH_ParamAccess.item);                        // 2
            pManager.AddNumberParameter("y distance", "ydist", "length of hexes in y", GH_ParamAccess.item);                        // 3
            pManager.AddNumberParameter("z distance", "zdist", "length of hexes in z", GH_ParamAccess.item);                        // 4
            pManager.AddMeshParameter("Visualization", "viz", "Way to visualize the mesh", GH_ParamAccess.list);                    // 5
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
        
        private bool EdgeSpansEdges(Tuple<int, int> edge1, Tuple<int, int> edge3, Tuple<int, int> edge4)
        {
            bool spans = false;
            if(edge1.Item1 == edge4.Item1)
            {
                if (edge3.Item2 == edge4.Item2)
                    spans = true;
            }
            else if(edge1.Item1 == edge4.Item2)
            {
                if (edge3.Item2 == edge4.Item1)
                    spans = true;
            }

            return spans;
        }
        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            int x_div = new int();
            int y_div = new int();
            int z_div = new int();
            List<Point3d> centroids = new List<Point3d>();

            DA.GetData(0, ref box);
            DA.GetData(1, ref x_div);
            DA.GetData(2, ref y_div);
            DA.GetData(3, ref z_div);

            var x_interval = box.X;
            var y_interval = box.Y;
            var z_interval = box.Z;
            double x_length = x_interval.Length;
            double y_length = y_interval.Length;
            double z_length = z_interval.Length;
            double x_dist = x_length / (double)x_div;
            double y_dist = y_length / (double)y_div;
            double z_dist = z_length / (double)z_div;

            if (x_div == 1)
                x_dist /= 2;
            if (y_div == 1)
                y_dist /= 2;
            if (z_div == 1)
                z_dist /= 2;

            DA.SetData(2, x_dist);
            DA.SetData(3, y_dist);
            DA.SetData(4, z_dist);

            for (int x = 0; x < x_div; x++)
            {
                for (int y = 0; y < y_div; y++)
                {
                    for (int z = 0; z < z_div; z++)
                    {
                        Point3d point = new Point3d();
                        double double_x = (double)x;
                        double double_y = (double)y;
                        double double_z = (double)z;
                        point.X = x_interval.T0 + (x_dist * (0.5 + double_x));
                        point.Y = y_interval.T0 + (y_dist * (0.5 + double_y));
                        point.Z = z_interval.T0 + (z_dist * (0.5 + double_z));
                        centroids.Add(point);
                    }
                }
            }

            Point3d[] corners = box.GetCorners();
            Plane top_plane = new Plane(corners[4], corners[5], corners[7]);
            Plane bottom_plane = new Plane(corners[0], corners[1], corners[3]);
            Plane north_plane = new Plane(corners[3], corners[2], corners[7]);
            Plane south_plane = new Plane(corners[0], corners[1], corners[4]);
            Plane east_plane = new Plane(corners[0], corners[3], corners[4]);
            Plane west_plane = new Plane(corners[1], corners[2], corners[5]);
            Rectangle3d top_rec = new Rectangle3d(top_plane, corners[5], corners[7]);
            Rectangle3d bottom_rec = new Rectangle3d(bottom_plane, corners[1], corners[3]);
            Rectangle3d north_rec = new Rectangle3d(north_plane, corners[2], corners[7]);
            Rectangle3d south_rec = new Rectangle3d(south_plane, corners[1], corners[4]);
            Rectangle3d east_rec = new Rectangle3d(east_plane, corners[3], corners[4]);
            Rectangle3d west_rec = new Rectangle3d(west_plane, corners[2], corners[5]);

            List<Rectangle3d> rectangles = new List<Rectangle3d>();
            rectangles.Add(top_rec);
            rectangles.Add(bottom_rec);
            rectangles.Add(north_rec);
            rectangles.Add(south_rec);
            rectangles.Add(east_rec);
            rectangles.Add(west_rec);


            var regular_mesh = Rhino.Geometry.Mesh.CreateFromBox(box, x_div, y_div, z_div);
            List<double> x_pts = new List<double>();
            List<double> y_pts = new List<double>();
            List<double> z_pts = new List<double>();

            var vertices = regular_mesh.TopologyVertices;
            Point3d corner = box.GetCorners()[0];
            
            foreach(Point3d vertex in vertices)
            {
                if (Rhino.RhinoMath.EpsilonEquals(corner.X, vertex.X, 1e-2))
                { 
                    if (Rhino.RhinoMath.EpsilonEquals(corner.Y, vertex.Y, 1e-2))
                        z_pts.Add(vertex.Z);
                }
                if (Rhino.RhinoMath.EpsilonEquals(corner.X, vertex.X, 1e-2))
                { 
                    if (Rhino.RhinoMath.EpsilonEquals(corner.Z, vertex.Z, 1e-2))
                        y_pts.Add(vertex.Y);
                }
                if (Rhino.RhinoMath.EpsilonEquals(corner.Y, vertex.Y, 1e-2))
                { 
                    if (Rhino.RhinoMath.EpsilonEquals(corner.Z, vertex.Z, 1e-2))
                        x_pts.Add(vertex.X);
                }
            }

            x_pts.Sort();
            y_pts.Sort();
            z_pts.Sort();

            List<double> dual_xvals = SubdivideDualIntervalList(x_pts);
            List<double> dual_yvals = SubdivideDualIntervalList(y_pts);
            List<double> dual_zvals = SubdivideDualIntervalList(z_pts);

            // Create Dual Surface Mesh
            List<Line> mesh_grid = new List<Line>();
            for (int i = 0; i < dual_xvals.Count; ++i)
            {
                for (int j = 0; j < dual_yvals.Count; ++j)
                {
                    for (int k = 1; k < dual_zvals.Count; ++k)
                        mesh_grid.Add(new Line(dual_xvals[i], dual_yvals[j], dual_zvals[k-1], dual_xvals[i], dual_yvals[j], dual_zvals[k]));
                }
            }
            for (int i = 0; i < dual_xvals.Count; ++i)
            {
                for (int k = 0; k < dual_zvals.Count; ++k)
                {
                    for(int j = 1; j < dual_yvals.Count; ++j)
                        mesh_grid.Add(new Line(dual_xvals[i], dual_yvals[j-1], dual_zvals[k], dual_xvals[i], dual_yvals[j], dual_zvals[k]));
                }
            }

            NurbsCurve[] linecurves = new NurbsCurve[mesh_grid.Count];
            for (int i = 0; i < mesh_grid.Count; ++i)
                linecurves[i] = mesh_grid[i].ToNurbsCurve();

            var dual_mesh = Mesh.CreateFromLines(linecurves, 4, 1e-6);
            var topology_vertices = dual_mesh.TopologyVertices;

            // Build hexes as list of vertex indicies
            List<List<int>> hexes = new List<List<int>>();
            HashSet<int> z1 = new HashSet<int>();
            HashSet<int> z2 = new HashSet<int>();
            HashSet<int> y1 = new HashSet<int>();
            HashSet<int> y2 = new HashSet<int>();
            HashSet<int> x1 = new HashSet<int>();
            HashSet<int> x2 = new HashSet<int>();
            int hex_counter = 0;
            for (int x = 0; x < x_div + 1; x++)
            {
                for (int y = 0; y < y_div + 1; y++)
                {
                    for (int z = 0; z < z_div + 1; z++)
                    {
                        int A = z + y * (z_div + 2) + x * ((z_div + 2) * (y_div +  2));
                        int B = A + ((z_div + 2) * (y_div + 2));
                        int C = A + (z_div + 2) + ((z_div + 2) * (y_div + 2));
                        int D = A + (z_div + 2);
                        int E = A + 1;
                        int F = B + 1;
                        int G = C + 1;
                        int H = D + 1;

                        hexes.Add(new List<int> { A, B, C, D, E, F, G, H });

                        if (z == 0)
                            z1.Add(hex_counter);
                        else if (z == z_div)
                            z2.Add(hex_counter);
                        if (y == 0)
                            y1.Add(hex_counter);
                        else if (y == y_div)
                            y2.Add(hex_counter);
                        if (x == 0)
                            x1.Add(hex_counter);
                        else if (x == x_div)
                            x2.Add(hex_counter);

                        hex_counter++;
                    }
                }
            }

            List<Point3d> verts = new List<Point3d>();
            foreach (Point3d vert in topology_vertices)
                verts.Add(vert);

            List<Mesh> viz = new List<Mesh>();
            x_div++;
            y_div++;
            z_div++;
            for (int i = 0; i < hexes.Count; i++)
            {
                if (z1.Contains(i)/*(i % z_div) == 0*/) // z1
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                else if (z2.Contains(i)/*((i + 1) % z_div) == 0*/) // z2
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (y1.Contains(i)/*(i % (z_div * y_div)) < z_div*/) // y1
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                else if (y2.Contains(i)/*(i % (z_div * y_div)) >= ((y_div * z_div) - y_div)*/) // y2
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (x1.Contains(i)/*(i % (z_div * y_div * x_div)) < (z_div * y_div)*/) // x1
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                else if (x2.Contains(i)/*(i % (z_div * y_div * x_div)) >= ((x_div * y_div * z_div) - (y_div* z_div))*/) // x2
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
            }

            DA.SetDataList(0, verts);
            DA.SetDataList(1, hexes);
            DA.SetDataList(5, viz);
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
            get { return new Guid("536B7BB0-764F-48EA-9CD6-73027BF2D07F"); }
        }
    }
}