using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.UI.Controls;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class AntiAliasing3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public AntiAliasing3D()
          : base("AntiAliasing 3D", "aliasing3d",
              "Antialiasing algorithm to handle background mesh",
              "Sculpt3d", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Bouding Box", "box", "Bounding Box", GH_ParamAccess.item);
            pManager.AddNumberParameter("Winding Numbers", "wind", "List of winding numbers", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cutoff", "cut", "Minimum Volume fraction cutoff", GH_ParamAccess.item);
            pManager.AddPointParameter("Centroids", "cent", "Centroids of hexes", GH_ParamAccess.list);
            pManager.AddPointParameter("Sample Points", "smpl", "Sample points of winding numbers", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Divisions", "divs", "List of x_div, y_div, z_div", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Sample Points", "pts", "List of x_pts, y_pts, and z_pts", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Visualize Interior", "inter", "Visualize interior faces of mesh", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Reverse Visualization", "rev", "Reverse the visualization", GH_ParamAccess.item);
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("All Vertices", "all_v", "Vertices of background mesh", GH_ParamAccess.list);                // 0
            pManager.AddGenericParameter("All Hexes", "all_h", "Full background hex mesh", GH_ParamAccess.list);                    // 1
            pManager.AddIntegerParameter("Vertices", "v", "Vertices that are 'inside'", GH_ParamAccess.list);                       // 2
            pManager.AddGenericParameter("Edges", "e", "Edges that are 'inside'", GH_ParamAccess.list);                             // 3
            pManager.AddGenericParameter("Faces", "f", "Faces that are 'inside'", GH_ParamAccess.list);                             // 4
            pManager.AddGenericParameter("Hexes", "h", "Hexes that are 'inside'", GH_ParamAccess.list);                             // 5
            pManager.AddPointParameter("Vertex Visualization", "v_viz", "Visualization of 'inside' vertices", GH_ParamAccess.list); // 6
            pManager.AddLineParameter("Edge Visualization", "e_viz", "Visualization of 'inside' edges", GH_ParamAccess.list);       // 7
            pManager.AddMeshParameter("Face Visualization", "f_viz", "Visualization of 'inside' faces", GH_ParamAccess.list);
            pManager.AddMeshParameter("Hex Visualization", "h_viz", "Visualization of 'inside' hexes", GH_ParamAccess.list);        // 9
        } 

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            List<int> divs = new List<int>();
            List<Point3d> sample_points = new List<Point3d>();
            List<Point3d> centroids = new List<Point3d>();
            List<double> winding_numbers = new List<double>();
            double cutoff = new double();
            List<int> pts = new List<int>();
            bool interior = false;
            bool reverse = false;

            DA.GetData(0, ref box);
            DA.GetDataList(1, winding_numbers);
            DA.GetData(2, ref cutoff);
            DA.GetDataList(3, centroids);
            DA.GetDataList(4, sample_points);
            DA.GetDataList(5, divs);
            DA.GetDataList(6, pts);
            DA.GetData(7, ref interior);
            DA.GetData(8, ref reverse);

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

            List<double> hex_volume_fractions = new List<double>();
            int x_pts = pts[0];
            int y_pts = pts[1];
            int z_pts = pts[2];
            double divisor = (double)x_pts * (double)y_pts * (double)z_pts;
            int n_pts = x_pts * y_pts * z_pts;
            int counter = 0;
            double cur_wind;
            double volume_fraction = 0;
            for(int i = 0; i < hexes.Count; i++)
            {
                volume_fraction = 0;
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    cur_wind = winding_numbers[counter + pt_idx];
                    volume_fraction += cur_wind;
                    //pt_winding.Add(cur_wind);
                }
                volume_fraction /= divisor;
                hex_volume_fractions.Add(volume_fraction);
                counter += n_pts;
            }

            List<List<int>> background_hexes = new List<List<int>>(hexes);
            int init_count = hexes.Count;
            HashSet<int> sculpt_hash = new HashSet<int>();
            List<int> hexes_to_keep = new List<int>();
            if (reverse)
            {
                for (int i = init_count - 1; i >= 0; i--)
                {
                    if (hex_volume_fractions[i] < cutoff)
                    {
                        sculpt_hash.Add(i);
                        hexes_to_keep.Add(i);
                    }
                }
            }
            else
            {
                for (int i = init_count - 1; i >= 0; i--)
                {
                    if (hex_volume_fractions[i] >= cutoff)
                    {
                        sculpt_hash.Add(i);
                        hexes_to_keep.Add(i);
                    }
                }
            }

            Dictionary<int, List<double>> vertex_winding_numbers = new Dictionary<int, List<double>>();
            Dictionary<Tuple<int, int>, List<double>> edge_winding_numbers = new Dictionary<Tuple<int, int>, List<double>>();
            Dictionary<Tuple<int, int, int, int>, List<double>> face_winding_numbers = new Dictionary<Tuple<int, int, int, int>, List<double>>();
            counter = 0;

            // loop through each hex and find the winding point numbers that coorespond to each vertex and edge
            for (int i = 0; i < background_hexes.Count; i++)
            {
                List<int> hex = background_hexes[i];
                Point3d centroid = Functions.GetCentroid(verts, hex);
                List<double> cur_winding_list = new List<double>();
                List<Point3d> points = new List<Point3d>();
                for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                {
                    points.Add(sample_points[pt_idx + counter]);
                    cur_winding_list.Add(winding_numbers[pt_idx + counter]);
                }
                counter += n_pts;

                List<double> A = new List<double>();
                List<double> B = new List<double>();
                List<double> C = new List<double>();
                List<double> D = new List<double>();
                List<double> E = new List<double>();
                List<double> F = new List<double>();
                List<double> G = new List<double>();
                List<double> H = new List<double>();

                for(int j = 0; j < points.Count; j++)
                {
                    bool x_pos = points[j].X > centroid.X;
                    bool y_pos = points[j].Y > centroid.Y;
                    bool z_pos = points[j].Z > centroid.Z;

                    if (!x_pos && !y_pos && !z_pos)
                        A.Add(cur_winding_list[j]);
                    else if (!x_pos && !y_pos && z_pos)
                        B.Add(cur_winding_list[j]);
                    else if (!x_pos && y_pos && !z_pos)
                        C.Add(cur_winding_list[j]);
                    else if (!x_pos && y_pos && z_pos)
                        D.Add(cur_winding_list[j]);
                    else if (x_pos && !y_pos && !z_pos)
                        E.Add(cur_winding_list[j]);
                    else if (x_pos && !y_pos && z_pos)
                        F.Add(cur_winding_list[j]);
                    else if (x_pos && y_pos && !z_pos)
                        G.Add(cur_winding_list[j]);
                    else if (x_pos && y_pos && z_pos)
                        H.Add(cur_winding_list[j]);
                }

                List<List<double>> quadrants = new List<List<double>> { A, B, C, D, E, F, G, H };
                List<int> sorted_hex = new List<int>(hex);
                sorted_hex.Sort();
                // Vertex winding number assignment
                for (int j = 0; j < hex.Count; j++)
                {
                    if (!vertex_winding_numbers.TryAdd(sorted_hex[j], quadrants[j]))
                        vertex_winding_numbers[sorted_hex[j]].AddRange(quadrants[j]);
                }

                // Edge winding number assignment
                Tuple<int, int> t1 = new Tuple<int, int>(hex[0], hex[1]);
                if (!edge_winding_numbers.TryAdd(t1, A))
                    edge_winding_numbers[t1].AddRange(A);
                edge_winding_numbers[t1].AddRange(E);

                Tuple<int, int> t2 = new Tuple<int, int>(hex[0], hex[3]);
                if (!edge_winding_numbers.TryAdd(t2, A))
                    edge_winding_numbers[t2].AddRange(A);
                edge_winding_numbers[t2].AddRange(C);

                Tuple<int, int> t3 = new Tuple<int, int>(hex[0], hex[4]);
                if (!edge_winding_numbers.TryAdd(t3, A))
                    edge_winding_numbers[t3].AddRange(A);
                edge_winding_numbers[t3].AddRange(B);

                Tuple<int, int> t4 = new Tuple<int, int>(hex[1], hex[2]);
                if (!edge_winding_numbers.TryAdd(t4, E))
                    edge_winding_numbers[t4].AddRange(E);
                edge_winding_numbers[t4].AddRange(G);

                Tuple<int, int> t5 = new Tuple<int, int>(hex[1], hex[5]);
                if (!edge_winding_numbers.TryAdd(t5, E))
                    edge_winding_numbers[t5].AddRange(E);
                edge_winding_numbers[t5].AddRange(F);

                Tuple<int, int> t6 = new Tuple<int, int>(hex[3], hex[2]);
                if (!edge_winding_numbers.TryAdd(t6, G))
                    edge_winding_numbers[t6].AddRange(G);
                edge_winding_numbers[t6].AddRange(C);

                Tuple<int, int> t7 = new Tuple<int, int>(hex[2], hex[6]);
                if (!edge_winding_numbers.TryAdd(t7, G))
                    edge_winding_numbers[t7].AddRange(G);
                edge_winding_numbers[t7].AddRange(H);

                Tuple<int, int> t8 = new Tuple<int, int>(hex[3], hex[7]);
                if (!edge_winding_numbers.TryAdd(t8, C))
                    edge_winding_numbers[t8].AddRange(C);
                edge_winding_numbers[t8].AddRange(D);

                Tuple<int, int> t9 = new Tuple<int, int>(hex[4], hex[5]);
                if (!edge_winding_numbers.TryAdd(t9, B))
                    edge_winding_numbers[t9].AddRange(B);
                edge_winding_numbers[t9].AddRange(F);

                Tuple<int, int> t10 = new Tuple<int, int>(hex[5], hex[6]);
                if (!edge_winding_numbers.TryAdd(t10, F))
                    edge_winding_numbers[t10].AddRange(F);
                edge_winding_numbers[t10].AddRange(H);

                Tuple<int, int> t11 = new Tuple<int, int>(hex[7], hex[6]);
                if (!edge_winding_numbers.TryAdd(t11, H))
                    edge_winding_numbers[t11].AddRange(H);
                edge_winding_numbers[t11].AddRange(D);

                Tuple<int, int> t12 = new Tuple<int, int>(hex[4], hex[7]);
                if (!edge_winding_numbers.TryAdd(t12, B))
                    edge_winding_numbers[t12].AddRange(B);
                edge_winding_numbers[t12].AddRange(D);

                // face #1      1, 2, 6, 5
                Tuple<int, int, int, int> f1 = new Tuple<int, int, int, int>(hex[0], hex[4], hex[1], hex[5]);
                if (!face_winding_numbers.TryAdd(f1, A))
                    face_winding_numbers[f1].AddRange(A);
                face_winding_numbers[f1].AddRange(B);
                face_winding_numbers[f1].AddRange(E);
                face_winding_numbers[f1].AddRange(F);

                // face #2      2, 3, 7, 6
                Tuple<int, int, int, int> f2 = new Tuple<int, int, int, int>(hex[1], hex[5], hex[2], hex[6]);
                if (!face_winding_numbers.TryAdd(f2, E))
                    face_winding_numbers[f2].AddRange(E);
                face_winding_numbers[f2].AddRange(F);
                face_winding_numbers[f2].AddRange(G);
                face_winding_numbers[f2].AddRange(H);

                // face #3      3, 4, 8, 7
                Tuple<int, int, int, int> f3 = new Tuple<int, int, int, int>(hex[3], hex[7], hex[2], hex[6]);
                if (!face_winding_numbers.TryAdd(f3, G))
                    face_winding_numbers[f3].AddRange(G);
                face_winding_numbers[f3].AddRange(H);
                face_winding_numbers[f3].AddRange(C);
                face_winding_numbers[f3].AddRange(D);

                // face #4      1, 5, 8, 4
                Tuple<int, int, int, int> f4 = new Tuple<int, int, int, int>(hex[0], hex[4], hex[3], hex[7]);
                if (!face_winding_numbers.TryAdd(f4, A))
                    face_winding_numbers[f4].AddRange(A);
                face_winding_numbers[f4].AddRange(B);
                face_winding_numbers[f4].AddRange(C);
                face_winding_numbers[f4].AddRange(D);

                // face #5      1, 4, 3, 2
                Tuple<int, int, int, int> f5 = new Tuple<int, int, int, int>(hex[0], hex[3], hex[1], hex[2]);
                if (!face_winding_numbers.TryAdd(f5, A))
                    face_winding_numbers[f5].AddRange(A);
                face_winding_numbers[f5].AddRange(C);
                face_winding_numbers[f5].AddRange(E);
                face_winding_numbers[f5].AddRange(G);

                // face #6      5, 6, 7, 8
                Tuple<int, int, int, int> f6 = new Tuple<int, int, int, int>(hex[4], hex[7], hex[5], hex[6]);
                if (!face_winding_numbers.TryAdd(f6, B))
                    face_winding_numbers[f6].AddRange(B);
                face_winding_numbers[f6].AddRange(D);
                face_winding_numbers[f6].AddRange(F);
                face_winding_numbers[f6].AddRange(H);
            }

            // Get vertex volume fractions
            List<double> vertex_volume_fractions = new List<double>();
            for(int i = 0; i < verts.Count; i++)
            {
                List<double> winding_list = vertex_winding_numbers[i];
                volume_fraction = 0;
                foreach (var wind in winding_list)
                    volume_fraction += wind;
                volume_fraction /= winding_list.Count;
                vertex_volume_fractions.Add(volume_fraction);
            }

            // Get edge volume fractions
            Dictionary<Tuple<int, int>, double> edge_volume_fractions = new Dictionary<Tuple<int, int>, double>();
            foreach(var kvp in edge_winding_numbers)
            {
                volume_fraction = 0;
                foreach (var wind in kvp.Value)
                    volume_fraction += wind;
                volume_fraction /= kvp.Value.Count;
                edge_volume_fractions.Add(kvp.Key, volume_fraction);
            }

            // Get face volume fractions
            Dictionary<Tuple<int, int, int, int>, double> face_volume_fractions = new Dictionary<Tuple<int, int, int, int>, double>();
            foreach(var kvp in face_winding_numbers)
            {
                volume_fraction = 0;
                foreach (var wind in kvp.Value)
                    volume_fraction += wind;
                volume_fraction /= kvp.Value.Count;
                face_volume_fractions.Add(kvp.Key, volume_fraction);
            }

            // Output vertex list and vertex visualization
            List<int> verts_to_keep = new List<int>();
            List<Point3d> vert_viz = new List<Point3d>();
            init_count = verts.Count;
            if (reverse)
            {
                for (int i = init_count - 1; i >= 0; i--)
                {
                    if (vertex_volume_fractions[i] < cutoff)
                    {
                        verts_to_keep.Add(i);
                        vert_viz.Add(verts[i]);
                    }
                }
            }
            else
            {
                for (int i = init_count - 1; i >= 0; i--)
                {
                    if (vertex_volume_fractions[i] >= cutoff)
                    {
                        verts_to_keep.Add(i);
                        vert_viz.Add(verts[i]);
                    }
                }
            }

            // Output edge list and edge visualization
            List<Tuple<int, int>> edges_to_keep = new List<Tuple<int, int>>();
            List<Line> edge_viz = new List<Line>();
            if (reverse)
            {
                foreach (var kvp in edge_volume_fractions)
                {
                    if (kvp.Value < cutoff)
                    {
                        edges_to_keep.Add(kvp.Key);
                        edge_viz.Add(new Line(verts[kvp.Key.Item1], verts[kvp.Key.Item2]));
                    }
                }
            }
            else
            {
                foreach (var kvp in edge_volume_fractions)
                {
                    if (kvp.Value >= cutoff)
                    {
                        edges_to_keep.Add(kvp.Key);
                        edge_viz.Add(new Line(verts[kvp.Key.Item1], verts[kvp.Key.Item2]));
                    }
                }
            }

            // Output face list and face visualization
            Dictionary<Tuple<int, int, int, int>, List<int>> face_dict = Functions.CreateQuadFaceDictionary(background_hexes);
            List<List<int>> faces_to_keep = new List<List<int>>();
            List<Mesh> face_viz = new List<Mesh>();
            if (reverse)
            {
                foreach (var kvp in face_volume_fractions)
                {
                    if (kvp.Value < cutoff)
                    {
                        List<int> face = face_dict[kvp.Key];
                        faces_to_keep.Add(face);
                        Mesh mesh1 = new Mesh();
                        mesh1.Vertices.Add(verts[face[0]]);
                        mesh1.Vertices.Add(verts[face[1]]);
                        mesh1.Vertices.Add(verts[face[2]]);
                        mesh1.Vertices.Add(verts[face[3]]);
                        mesh1.Faces.AddFace(0, 1, 2, 3);
                        face_viz.Add(mesh1);
                    }
                }
            }
            else
            {
                foreach (var kvp in face_volume_fractions)
                {
                    if (kvp.Value >= cutoff)
                    {
                        List<int> face = face_dict[kvp.Key];
                        faces_to_keep.Add(face);
                        Mesh mesh1 = new Mesh();
                        mesh1.Vertices.Add(verts[face[0]]);
                        mesh1.Vertices.Add(verts[face[1]]);
                        mesh1.Vertices.Add(verts[face[2]]);
                        mesh1.Vertices.Add(verts[face[3]]);
                        mesh1.Faces.AddFace(0, 1, 2, 3);
                        face_viz.Add(mesh1);
                    }
                }
            }


            // Output hex visualization
            List<Mesh> hex_viz = new List<Mesh>();
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
                    hex_viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(z2) || interior || ((i + 1) % z_div) == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    hex_viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(y1) || (i % (z_div * y_div)) < z_div)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    hex_viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(y2) || interior || (i % (z_div * y_div)) >= ((y_div * z_div) - z_div))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    hex_viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(x1))
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][0]]);
                    mesh.Vertices.Add(verts[hexes[i][4]]);
                    mesh.Vertices.Add(verts[hexes[i][7]]);
                    mesh.Vertices.Add(verts[hexes[i][3]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    hex_viz.Add(mesh);
                }
                if (!sculpt_hash.Contains(x2) || interior)
                {
                    Mesh mesh = new Mesh();
                    mesh.Vertices.Add(verts[hexes[i][1]]);
                    mesh.Vertices.Add(verts[hexes[i][2]]);
                    mesh.Vertices.Add(verts[hexes[i][6]]);
                    mesh.Vertices.Add(verts[hexes[i][5]]);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    hex_viz.Add(mesh);
                }
            }

            List<List<int>> hexes_to_keep_real = new List<List<int>>();
            for (int i = 0; i < hexes.Count; i++)
            {
                if (sculpt_hash.Contains(i))
                    hexes_to_keep_real.Add(hexes[i]);
            }


            DA.SetDataList(0, verts);
            DA.SetDataList(1, hexes);
            DA.SetDataList(2, verts_to_keep);
            DA.SetDataList(3, edges_to_keep);
            DA.SetDataList(4, faces_to_keep);
            DA.SetDataList(5, hexes_to_keep_real);
            DA.SetDataList(6, vert_viz);
            DA.SetDataList(7, edge_viz);
            DA.SetDataList(8, face_viz);
            DA.SetDataList(9, hex_viz);
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
            get { return new Guid("994139C8-F514-4398-BA8A-305CD6E79D55"); }
        }
    }
}