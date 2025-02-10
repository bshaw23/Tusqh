using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class Sculpt3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Sculpt3D()
          : base("Sculpt3D", "Sculpt3D",
              "Represents a sculpted volumetric mesh",
              "Sculpt3D", "sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Bouding Box", "box", "Bounding Box", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Divisions", "divs", "List of x_div, y_div, z_div", GH_ParamAccess.list);
            pManager.AddPointParameter("Centroids", "cent", "Centroids of background mesh hexes", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fractions", "vol", "List of volume fractions", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cutoff", "cut", "Minimum Volume fraction cutoff", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Visualize Interior", "inter", "Visualize interior faces of mesh", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Reverse Visualization", "rev", "Reverse the visualization", GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of background mesh", GH_ParamAccess.list);                      // 0
            pManager.AddGenericParameter("Hexes", "hexes", "Hexes of sculpted mesh", GH_ParamAccess.list);                              // 1
            pManager.AddGenericParameter("Background Hexes", "back", "Full background hex mesh", GH_ParamAccess.list);                  // 2
            pManager.AddMeshParameter("Visualization", "viz", "Visualization of hexes to be kept in the mesh", GH_ParamAccess.list);    // 3
        }
        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            List<int> divs = new List<int>();
            List<Point3d> centroids = new List<Point3d>();
            List<double> volume_fractions = new List<double>();
            double cutoff = new double();
            bool interior = false;
            bool reverse = false;

            DA.GetData(0, ref box);
            DA.GetDataList(1, divs);
            DA.GetDataList(2, centroids);
            DA.GetDataList(3, volume_fractions);
            DA.GetData(4, ref cutoff);
            DA.GetData(5, ref interior);
            DA.GetData(6, ref reverse);

            var x_div = divs[0];
            var y_div = divs[1];
            var z_div = divs[2];
            var x_interval = box.X;
            var y_interval = box.Y;
            var z_interval = box.Z;
            double x_length = x_interval.Length;
            double y_length = y_interval.Length;
            double z_length = z_interval.Length;
            double x_dist = x_length / (double)x_div;
            double y_dist = y_length / (double)y_div;
            double z_dist = z_length / (double)z_div;

            // Create vertices
            List<Point3d> verts = new List<Point3d>();
            Dictionary<Point3d, int> point_to_vert = new Dictionary<Point3d, int>();
            int index = 0;
            for (int x = 0; x <= x_div; x++)
            {
                for (int y = 0; y <= y_div; y++)
                {
                    for (int z = 0; z <= z_div; z++)
                    {
                        Point3d point = new Point3d();
                        point.X = x_interval.T0 + (double)x * x_dist;
                        point.Y = y_interval.T0 + (double)y * y_dist;
                        point.Z = z_interval.T0 + (double)z * z_dist;
                        verts.Add(point);

                        point.X = Math.Round(point.X, 4);
                        point.Y = Math.Round(point.Y, 4);
                        point.Z = Math.Round(point.Z, 4);
                        point_to_vert.Add(point, index);
                        index++;
                    }
                }
            }

            // Create Hexes
            List<List<int>> hexes = new List<List<int>>();
            foreach (Point3d centroid in centroids)
            {
                Point3d vert_location = new Point3d(centroid);
                vert_location.X -= x_dist / 2;
                vert_location.Y -= y_dist / 2;
                vert_location.Z -= z_dist / 2;
                vert_location.X = Math.Round(vert_location.X, 4);
                vert_location.Y = Math.Round(vert_location.Y, 4);
                vert_location.Z = Math.Round(vert_location.Z, 4);
                int idx = point_to_vert[vert_location];
                int A = idx;
                int B = A + ((z_div + 1) * (y_div + 1));
                int C = A + (z_div + 1) + ((z_div + 1) * (y_div + 1));
                int D = A + (z_div + 1);
                int E = A + 1;
                int F = B + 1;
                int G = C + 1;
                int H = D + 1;

                List<int> hex = new List<int> { A, B, C, D, E, F, G, H };
                hexes.Add(hex);
            }

            List<List<int>> background_hexes = new List<List<int>>(hexes);

            int init_count = hexes.Count;
            HashSet<int> sculpt_hash = new HashSet<int>();
            for (int i = init_count - 1; i >= 0; i--)
            {
                if (volume_fractions[i] >= cutoff && !reverse)
                    sculpt_hash.Add(i);
                else if (volume_fractions[i] < cutoff && reverse)
                    sculpt_hash.Add(i);
            }

            List<Mesh> viz = new List<Mesh>();
            foreach (int i in sculpt_hash)
            {
                int z1 = i - 1;
                int z2 = i + 1;
                int y1 = i - z_div;
                int y2 = i + z_div;
                int x1 = i - (y_div * z_div);
                int x2 = i + (y_div * z_div);
                if (!sculpt_hash.Contains(z1) || (i % z_div) == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(z2) || interior || ((i + 1) % z_div) == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(y1) || (i % (z_div * y_div)) < z_div)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(y2) || interior || (i % (z_div * y_div)) >= ((y_div * z_div) - z_div))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(x1))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(x2) || interior)
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

            for(int i = init_count - 1; i > -1; i--)
            {
                if (!sculpt_hash.Contains(i))
                    hexes.RemoveAt(i);
            }
            
            DA.SetDataList(0, verts);
            DA.SetDataList(1, hexes);
            DA.SetDataList(2, background_hexes);
            DA.SetDataList(3, viz);
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
            get { return new Guid("BA10663B-4162-47DD-898D-289E022E4957"); }
        }
    }
}