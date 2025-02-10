using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Security.Principal;
using EigenWrapper.Eigen;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Runtime;
using Rhino.UI.Controls;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class JaggiesResolution3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public JaggiesResolution3D()
          : base("Resolve Jaggies 3D", "jaggies3D",
              "Resolve jaggies in a 3D mesh",
              "Sculpt3D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of background mesh", GH_ParamAccess.list);                            // 0
            pManager.AddGenericParameter("Hexes", "hexes", "Hexes of background mesh", GH_ParamAccess.list);                                // 1
            pManager.AddNumberParameter("Volume Fractions", "vols", "List of volume fractions", GH_ParamAccess.list);                       // 2
            pManager.AddNumberParameter("Volume Fraction Threshold", "cut", "Volume Fraction Threshold", GH_ParamAccess.item);              // 3
            pManager.AddIntegerParameter("Maximum resolution", "max", "Maximum distance of components for resolution", GH_ParamAccess.item);// 4
            pManager.AddMeshParameter("Surface mesh", "mesh", "Surface mesh (object of interst)", GH_ParamAccess.item);                     // 5
            pManager.AddIntegerParameter("Sample Points-x", "x_pts", "Number of sample points in x-direction", GH_ParamAccess.item);        // 6
            pManager.AddIntegerParameter("Sample Pionts-y", "y_pts", "Number of sample points in y-direction", GH_ParamAccess.item);        // 7
            pManager.AddIntegerParameter("Sample Points-z", "z_pts", "Number of sample points in z-direction", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Divs", "divs", "Number of divisions in x, y, and z respectively", GH_ParamAccess.list);           // 9
            pManager.AddBooleanParameter("Reverse", "rev", "Reverse volume fraction", GH_ParamAccess.item);
            pManager[10].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of mended mesh", GH_ParamAccess.list);
            pManager.AddGenericParameter("Hexes", "hexes", "Sculpted hexes without pinch points", GH_ParamAccess.list);
            pManager.AddMeshParameter("Visualization", "viz", "Visualize the mesh without pinch points", GH_ParamAccess.list);
            pManager.AddPointParameter("Corners", "corners", "Visualize corners of boxes", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Point3d> vertices = new List<Point3d>();
            List<List<int>> hexes = new List<List<int>>();
            List<double> original_volume_fractions = new List<double>();
            double vol_cutoff = new double();
            int max_window = new int();
            Mesh surface_mesh = new Mesh();
            int x_pts = new int();
            int y_pts = new int();
            int z_pts = new int();
            List<int> divs = new List<int>();
            bool reverse = false;

            DA.GetDataList(0, vertices);
            DA.GetDataList(1, hexes);
            DA.GetDataList(2, original_volume_fractions);
            DA.GetData(3, ref vol_cutoff);
            DA.GetData(4, ref max_window);
            DA.GetData(5, ref surface_mesh);
            DA.GetData(6, ref x_pts);
            DA.GetData(7, ref y_pts);
            DA.GetData(8, ref z_pts);
            DA.GetDataList(9, divs);
            DA.GetData(10, ref reverse);

            int x_div = divs[0];
            int y_div = divs[1];
            int z_div = divs[2];

            List<int> hex = hexes[0];
            Point3d point1 = vertices[hex[0]];
            Point3d point2 = vertices[hex[6]];
            Vector3d vector = point2 - point1;

            double x_dist = vector.X;
            double y_dist = vector.Y;
            double z_dist = vector.Z;

            double x_segments = x_dist / (double)x_pts / 2;
            double y_segments = y_dist / (double)y_pts / 2;
            double z_segments = z_dist / (double)z_pts / 2;

            Point3d point = new Point3d(vertices[0]);
            Vector3d normal = new Vector3d(0, 0, 1);
            Plane plane = new Plane(new Point3d(0, 0, 0), normal);            

            List<Box> boxes = new List<Box>(); // Store windows as Hashsets of hexes that would be within a window
            for (int w = max_window; w > 2; w--)
            {
                for(int x = 0; x < x_div - w + 1; x++)
                {
                    for (int y = 0; y < y_div - w + 1; y++)
                    {
                        for (int z = 0; z < z_div - w + 1; z++)
                        {
                            Point3d pt = new Point3d(point.X + x * x_dist, point.Y + y * y_dist, point.Z + z * z_dist);
                            Interval x_size = new Interval(pt.X, pt.X + w * x_dist);
                            Interval y_size = new Interval(pt.Y, pt.Y + w * y_dist);
                            Interval z_size = new Interval(pt.Z, pt.Z + w * z_dist);
                            Box box = new Box(plane, x_size, y_size, z_size);

                            boxes.Add(box);
                        }
                    }
                }
            }

            List<HashSet<int>> windows = new List<HashSet<int>>();
            for (int i = 0; i < boxes.Count; i++)
                windows.Add(new HashSet<int>());

            for (int i = 0; i < boxes.Count; i++)
            {
                Box box = boxes[i];
                Point3d[] points = box.GetCorners();
                for (int h = 0; h < hexes.Count; h++)
                {
                    if (reverse && original_volume_fractions[h] >= vol_cutoff)
                        continue;
                    else if (!reverse && original_volume_fractions[h] < vol_cutoff)
                        continue;

                    List<int> hex1 = hexes[h];
                    Point3d centroid = Functions.GetCentroid(vertices, hex1);
                    if (box.Contains(centroid))
                        windows[i].Add(h);
                }
            }
            

            // Breadth first search
            List<int> windows_to_subdivide = new List<int>();
            for(int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                if (window.Count == 0)
                    continue;

                int a = window.First();
                List<int> adjacent_hexes = Functions.GetAdjacentHexes(a, x_div, y_div, z_div);

                HashSet<int> list = new HashSet<int>();
                list.Add(a);

                Queue<int> queue = new Queue<int>();
                HashSet<int> visited = new HashSet<int>();
                queue.Enqueue(a);

                while (queue.Count != 0)
                {
                    int current_hex = queue.Dequeue();
                    visited.Add(current_hex);
                    List<int> adj_verts = Functions.GetAdjacentHexes(current_hex, divs[0], divs[1], divs[2]);
                    foreach (int adj in adj_verts)
                    {
                        if (window.Contains(adj) && !list.Contains(adj))
                        {
                            queue.Enqueue(adj);
                            list.Add(adj);
                        }
                    }
                }

                if (list.Count < window.Count)
                    windows_to_subdivide.Add(i);
            }

            List<Tuple<double, double, double>> querry_pts = new List<Tuple<double, double, double>>();
            int volume_fraction_counter = 0;
            foreach (var w in windows_to_subdivide)
            {
                Box box = boxes[w];
                Interval x_interval = box.X;
                Interval y_interval = box.Y;
                Interval z_interval = box.Z;

                int x_len = (int)(x_interval.Length / x_dist);
                int y_len = (int)(y_interval.Length / y_dist);
                int z_len = (int)(z_interval.Length / z_dist);

                for (int x = 0; x < x_len * 2; x++)
                {
                    for (int y = 0; y < y_len * 2; y++)
                    {
                        for (int z = 0; z < z_len * 2; z++)
                        {
                            point = box.GetCorners()[0];
                            point.X += x_dist / 2 * (double)x;
                            point.Y += y_dist / 2 * (double)y;
                            point.Z += z_dist / 2 * (double)z;
                            volume_fraction_counter++;

                            for (int i = 0; i < x_pts; i++)
                            {
                                for (int j = 0; j < y_pts; j++)
                                {
                                    for (int k = 0; k < z_pts; k++)
                                    {
                                        double x_querry = point.X + x_segments / 2 + x_segments * i;
                                        double y_querry = point.Y + y_segments / 2 + y_segments * j;
                                        double z_querry = point.Z + z_segments / 2 + z_segments * k;

                                        querry_pts.Add(new Tuple<double, double, double>(x_querry, y_querry, z_querry));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            List<Tuple<double, double, double>> vert_array = new List<Tuple<double, double, double>>();
            List<Tuple<int, int, int>> triangle_array = new List<Tuple<int, int, int>>();
            AlephSupport.ProcessMesh(surface_mesh, out vert_array, out triangle_array);

            List<double> vert_list = new List<double>();
            List<int> triangle_list = new List<int>();
            List<double> querry_list = new List<double>();

            AlephSupport.ColumnMajorConstruction(vert_array, triangle_array, querry_pts, 
                out vert_list, out triangle_list, out querry_list);

            List<double> winding = new List<double>(querry_pts.Count);
            for (int i = 0; i < querry_pts.Count; ++i)
                winding.Add(0);
            EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vert_list), vert_array.Count, 3,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(triangle_list), triangle_array.Count, 3,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(querry_list), querry_pts.Count, 3,
                                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(winding));

            // Translate winding numbers to volume fractions
            List<double> volume_fractions = new List<double>();
            double divisor = (double)x_pts * (double)y_pts * (double)z_pts;
            int n_pts = x_pts * y_pts * z_pts;
            int counter = 0;
            double cur_wind;
            double volume_fraction = 0;
            for(int i = 0; i < volume_fraction_counter; i++)
            {
                volume_fraction = 0;
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    cur_wind = winding[counter + pt_idx];
                    volume_fraction += cur_wind;
                }
                volume_fraction /= divisor;
                volume_fractions.Add(volume_fraction);
                counter += n_pts;
            }

            // i loop through all the windows_to_subdivide again
            // i have an integer that tracks where i'm at in the volume fractions
            

            int vol_tracker = 0;
            HashSet<int> hexes_to_keep = new HashSet<int>();
            //List<Mesh> viz = new List<Mesh>();
            //List<Point3d> corners = new List<Point3d>();
            foreach (var window in windows_to_subdivide)
            {
                Box box = boxes[window];
                point = box.GetCorners()[0];
                
                //corners.Add(point);

                Interval x_interval = box.X;
                Interval y_interval = box.Y;
                Interval z_interval = box.Z;

                int x_len = (int)(x_interval.Length / x_dist);
                int y_len = (int)(y_interval.Length / y_dist);
                int z_len = (int)(z_interval.Length / z_dist);

                //viz.Add(Mesh.CreateFromBox(box, x_len * 2, y_len * 2, z_len * 2));

                List<double> box_vols = new List<double>();
                List<List<List<double>>> box_vol_matrix = new List<List<List<double>>>();
                for (int x = 0; x < x_len * 2; x++)
                {
                    List<List<double>> vector1 = new List<List<double>>();
                    for (int y = 0; y < y_len * 2; y++)
                    {
                        List<double> vector2 = new List<double>();
                        for (int z = 0; z < z_len * 2; z++)
                        {
                            vector2.Add(volume_fractions[vol_tracker]);
                            box_vols.Add(volume_fractions[vol_tracker]);
                            vol_tracker++;
                        }
                        vector1.Add(vector2);
                    }
                    box_vol_matrix.Add(vector1);
                }

                List<int> new_hexes = new List<int>();
                for (int x = 0; x < x_len; x++)
                {
                    for (int y = 0; y < y_len; y++)
                    {
                        for (int z = 0; z < z_len; z++)
                        {
                            List<double> hex_vols = new List<double>();
                            hex_vols.Add(box_vol_matrix[x * 2][y * 2][z * 2]);
                            hex_vols.Add(box_vol_matrix[x * 2][y * 2][z * 2 + 1]);
                            hex_vols.Add(box_vol_matrix[x * 2][y * 2 + 1][z * 2]);
                            hex_vols.Add(box_vol_matrix[x * 2][y * 2 + 1][z * 2 + 1]);
                            hex_vols.Add(box_vol_matrix[x * 2 + 1][y * 2][z * 2]);
                            hex_vols.Add(box_vol_matrix[x * 2 + 1][y * 2][z * 2 + 1]);
                            hex_vols.Add(box_vol_matrix[x * 2 + 1][y * 2 + 1][z * 2]);
                            hex_vols.Add(box_vol_matrix[x * 2 + 1][y * 2 + 1][z * 2 + 1]);

                            int vol_counter = 0;
                            foreach (var v in hex_vols)
                            {
                                if (v >= vol_cutoff)
                                    vol_counter++;
                            }

                            if (vol_counter >= 4)
                                new_hexes.Add((x * (y_len * z_len)) + (y * (z_len)) + z);
                        }
                    }
                }

                // Now I need to figure out how to get the correct hex indexes for the background mesh 
                // with respect to the new_hexes
                // To do that I need to figure out how to at least find the correct index for
                // one of the hexes. 
                // One way to do that would be to loop through all the hexes until I find one 
                // that has the same centroid
                // From there I can just use the new face index and add it to the actual
                // face index I find.

                Point3d centroid = point;
                centroid.X += x_dist / 2;
                centroid.Y += y_dist / 2;
                centroid.Z += z_dist / 2;
                int hex_idx = 0;
                for (int h = 0; h < hexes.Count; h++)
                {
                    Point3d c = Functions.GetCentroid(vertices, hexes[h]);
                    if (c.EpsilonEquals(centroid, 0.001))
                    {
                        hex_idx = h;
                        break;
                    }
                }

                foreach (int new_h in new_hexes)
                {
                    hexes_to_keep.Add(new_h + hex_idx);
                }

                if (new_hexes.Count == 0)
                    continue;

                //int a = new_hexes[0];
                //List<int> adjacent_hexes = Functions.GetAdjacentHexes(a, x_div, y_div, z_div);

                //HashSet<int> list = new HashSet<int>();
                //list.Add(a);

                //Queue<int> queue = new Queue<int>();
                //HashSet<int> visited = new HashSet<int>();
                //queue.Enqueue(a);

                //while (queue.Count != 0)
                //{
                //    int current_hex = queue.Dequeue();
                //    visited.Add(current_hex);
                //    List<int> adj_verts = Functions.GetAdjacentHexes(current_hex, divs[0], divs[1], divs[2]);
                //    foreach (int adj in adj_verts)
                //    {
                //        if (new_hexes.Contains(adj) && !list.Contains(adj))
                //        {
                //            queue.Enqueue(adj);
                //            list.Add(adj);
                //        }
                //    }
                //}
            }

            for (int h = 0; h < hexes.Count; h++)
            {
                if (!reverse && original_volume_fractions[h] >= vol_cutoff)
                    hexes_to_keep.Add(h);
                else if (reverse && original_volume_fractions[h] < vol_cutoff)
                    hexes_to_keep.Add(h);
            }

            List<Mesh> viz = new List<Mesh>();
            foreach (int i in hexes_to_keep)
            {
                int z1 = i - 1;
                int z2 = i + 1;
                int y1 = i - z_div;
                int y2 = i + z_div;
                int x1 = i - (y_div * z_div);
                int x2 = i + (y_div * z_div);
                if (!hexes_to_keep.Contains(z1) || (i % z_div) == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][0]]);
                    mesh.Vertices.Add(vertices[hexes[i][3]]);
                    mesh.Vertices.Add(vertices[hexes[i][2]]);
                    mesh.Vertices.Add(vertices[hexes[i][1]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!hexes_to_keep.Contains(z2) || ((i + 1) % z_div) == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][4]]);
                    mesh.Vertices.Add(vertices[hexes[i][5]]);
                    mesh.Vertices.Add(vertices[hexes[i][6]]);
                    mesh.Vertices.Add(vertices[hexes[i][7]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!hexes_to_keep.Contains(y1) || (i % (z_div * y_div)) < z_div)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][0]]);
                    mesh.Vertices.Add(vertices[hexes[i][1]]);
                    mesh.Vertices.Add(vertices[hexes[i][5]]);
                    mesh.Vertices.Add(vertices[hexes[i][4]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!hexes_to_keep.Contains(y2) || (i % (z_div * y_div)) >= ((y_div * z_div) - z_div))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][2]]);
                    mesh.Vertices.Add(vertices[hexes[i][3]]);
                    mesh.Vertices.Add(vertices[hexes[i][7]]);
                    mesh.Vertices.Add(vertices[hexes[i][6]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!hexes_to_keep.Contains(x1))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][0]]);
                    mesh.Vertices.Add(vertices[hexes[i][4]]);
                    mesh.Vertices.Add(vertices[hexes[i][7]]);
                    mesh.Vertices.Add(vertices[hexes[i][3]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
                if (!hexes_to_keep.Contains(x2))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(vertices[hexes[i][1]]);
                    mesh.Vertices.Add(vertices[hexes[i][2]]);
                    mesh.Vertices.Add(vertices[hexes[i][6]]);
                    mesh.Vertices.Add(vertices[hexes[i][5]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    viz.Add(mesh);
                }
            }

            for (int i = hexes.Count - 1; i >= 0; i--)
            {
                if (!hexes_to_keep.Contains(i))
                    hexes.RemoveAt(i);
            }

            DA.SetDataList(0, vertices);
            DA.SetDataList(1, hexes);
            DA.SetDataList(2, viz);
            //DA.SetDataList(3, corners);
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
            get { return new Guid("9EA31490-8464-4409-B96C-A725B5E39EAC"); }
        }
    }
}