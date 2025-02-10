using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using EigenWrapper.Eigen;
using Eto.Drawing;
using Eto.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Rhino.DocObjects;
using Rhino.Geometry;
using Sculpt2D.Sculpt3D;
using Sculpt2D.Sculpt3D.Collections;

namespace Sculpt2D.Components
{
    public class PinchPoints3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public PinchPoints3D()
          : base("Pinch Points 3D", "Pinch3D",
              "Removes pinch points from a 3D mesh",
              "Sculpt3D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of background mesh", GH_ParamAccess.list);            // 0
            pManager.AddGenericParameter("Hexes", "hexes", "Hexes of the sculpted mesh", GH_ParamAccess.list);              // 1
            pManager.AddIntegerParameter("Divisions", "divs", "Number of divisions in x, y, and z", GH_ParamAccess.list);   // 2
            pManager.AddIntegerParameter("Inside Vertices", "in_verts", "Vertices 'inside'", GH_ParamAccess.list);          // 3
            pManager.AddGenericParameter("Inisde Edges", "in_edges", "Edges 'inside'", GH_ParamAccess.list);                // 4
            pManager.AddGenericParameter("Inside Faces", "in_faces", "Faces 'inside'", GH_ParamAccess.list);                // 5

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of mended mesh", GH_ParamAccess.list);
            pManager.AddGenericParameter("Hexes", "hexes", "Sculpted hexes without pinch points", GH_ParamAccess.list);
            pManager.AddMeshParameter("Visualization", "viz", "Visualize the mesh without pinch points", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Point3d> vertices = new List<Point3d>();
            List<List<int>> hexes = new List<List<int>>();
            List<int> divs = new List<int>();
            List<int> in_verts = new List<int>();
            List<Tuple<int, int>> in_edges = new List<Tuple<int, int>>();
            List<List<int>> in_faces = new List<List<int>>();

            List<List<int>> new_faces = new List<List<int>>();
            List<int> hex_idxs_to_remove = new List<int>();

            double divisor = 3;

            DA.GetDataList(0, vertices);
            DA.GetDataList(1, hexes);
            DA.GetDataList(2, divs);
            DA.GetDataList(3, in_verts);
            DA.GetDataList(4, in_edges);
            DA.GetDataList(5, in_faces);

            HashSet<int> in_verts_set = new HashSet<int>();
            foreach (int vert in in_verts)
                in_verts_set.Add(vert);

            HashSet<Tuple<int, int>> in_edges_set = new HashSet<Tuple<int, int>>();
            foreach (var edge in in_edges)
                in_edges_set.Add(edge);

            HashSet<Tuple<int, int>> all_edges = new HashSet<Tuple<int, int>>();
            foreach(var hex in hexes)
            {
                List<Tuple<int, int>> hex_edges = new List<Tuple<int, int>>();
                hex_edges.Add(new Tuple<int, int>(hex[0], hex[1]));
                hex_edges.Add(new Tuple<int, int>(hex[0], hex[3]));
                hex_edges.Add(new Tuple<int, int>(hex[0], hex[4]));
                hex_edges.Add(new Tuple<int, int>(hex[1], hex[2]));
                hex_edges.Add(new Tuple<int, int>(hex[1], hex[5]));
                hex_edges.Add(new Tuple<int, int>(hex[3], hex[2]));
                hex_edges.Add(new Tuple<int, int>(hex[2], hex[6]));
                hex_edges.Add(new Tuple<int, int>(hex[3], hex[7]));
                hex_edges.Add(new Tuple<int, int>(hex[4], hex[5]));
                hex_edges.Add(new Tuple<int, int>(hex[4], hex[7]));
                hex_edges.Add(new Tuple<int, int>(hex[5], hex[6]));
                hex_edges.Add(new Tuple<int, int>(hex[7], hex[6]));
            }

            HashSet<Tuple<int, int, int, int>> in_faces_set = new HashSet<Tuple<int, int, int, int>>();
            foreach (var face in in_faces)
            {
                face.Sort();
                in_faces_set.Add(new Tuple<int, int, int, int>(face[0], face[1], face[2], face[3]));
            }

            int init_vert_count = vertices.Count;
            Dictionary<int, List<int>> connected_verts_dict = Functions.MakeConnectedVertexDict(hexes);

            Dictionary<int, List<int>> vert_to_hexes = new Dictionary<int, List<int>>();
            for (int i = 0; i < hexes.Count; i++)
            {
                List<int> hex = hexes[i];
                foreach (int vert in hex)
                {
                    if (vert_to_hexes.ContainsKey(vert))
                        vert_to_hexes[vert].Add(i);
                    else
                        vert_to_hexes.Add(vert, new List<int> { i });
                }
            }

            
            List<List<int>> faces = new List<List<int>>();
            HashSet<Tuple<int, int, int, int>> tuples = new HashSet<Tuple<int, int, int, int>>();
            foreach(List<int> hex in hexes)
            {
                List<int> v = new List<int>(hex);
                List<List<int>> f_list = new List<List<int>>();
                f_list.Add(new List<int> { v[0], v[1], v[5], v[4] });
                f_list.Add(new List<int> { v[1], v[2], v[6], v[5] });
                f_list.Add(new List<int> { v[2], v[3], v[7], v[6] });
                f_list.Add(new List<int> { v[0], v[4], v[7], v[3] });
                f_list.Add(new List<int> { v[0], v[3], v[2], v[1] });
                f_list.Add(new List<int> { v[4], v[5], v[6], v[7] });

                foreach (var face in f_list)
                {
                    List<int> verts = new List<int> (face);
                    verts.Sort();
                    Tuple<int, int, int, int> tuple = new Tuple<int, int, int, int>(verts[0], verts[1], verts[2], verts[3]);
                    if(!tuples.Contains(tuple))
                    {
                        tuples.Add(tuple);
                        faces.Add(face);
                    }
                }
            }

            Dictionary<Tuple<int, int, int, int>, List<int>> face_dict = Functions.CreateQuadFaceDictionary(hexes);

            double x_dist = 0;
            double y_dist = 0;
            double z_dist = 0;
            List<int> init_hex = hexes[0];
            Point3d hex_A = vertices[init_hex[0]];
            Point3d hex_G = vertices[init_hex[6]];
            Vector3d vec = hex_G - hex_A;
            x_dist = Math.Abs(vec.X);
            y_dist = Math.Abs(vec.Y);
            z_dist = Math.Abs(vec.Z);
            //double x_segments = x_dist / (double)x_pts / 2.0;
            //double y_segments = y_dist / (double)y_pts / 2.0;
            //double z_segments = z_dist / (double)z_pts / 2.0;

            HashSet<int> kissing_points_fullset = new HashSet<int>();
            HashSet<int> pinch_points_fullset = new HashSet<int>();
            HashSet<Tuple<int, int>> pinch_edges_fullset = new HashSet<Tuple<int, int>>();
            HashSet<int> remove_pts_fullset = new HashSet<int>();
            HashSet<int> bad_pts_fullset = new HashSet<int>();
            HashSet<int> nonmanifold_points_set = new HashSet<int>();
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vert_to_hexes.ContainsKey(i))
                {
                    List<int> hex_idxs = vert_to_hexes[i];
                    int num_hexes = hex_idxs.Count;
                    List<int> verts = new List<int>();
                    switch(num_hexes)
                    {
                        case 1:
                            break;
                        case 2:
                            {
                                List<int> hex1_verts = new List<int>(hexes[hex_idxs[0]]);
                                List<int> hex2_verts = new List<int>(hexes[hex_idxs[1]]);
                                verts.AddRange(hex1_verts);
                                verts.AddRange(hex2_verts);

                                List<int> shared_verts = verts.GroupBy(x => x)
                                                         .Where(g => g.Count() > 1)
                                                         .Select(g => g.Key)
                                                         .ToList();
                                if (shared_verts.Count == 0 || shared_verts.Count == 3)
                                    throw new Exception(string.Format("I was not expecting to have {0} shared vertices", shared_verts.Count));
                                if (shared_verts.Count == 1)
                                {
                                    //kissing_points.Add(i);
                                    //remove_pts.Add(i);

                                    kissing_points_fullset.Add(i);
                                    remove_pts_fullset.Add(i);
                                    nonmanifold_points_set.Add(i);
                                }
                                else if (shared_verts.Count == 2)
                                {
                                    shared_verts.Sort();
                                    Tuple<int, int> edge = new Tuple<int, int>(shared_verts[0], shared_verts[1]);
                                    //if (!pinch_edges.Contains(edge))
                                    //    pinch_edges.Add(edge);
                                    //remove_pts.Add(i);
                                    pinch_edges_fullset.Add(edge);
                                    remove_pts_fullset.Add(i);
                                    nonmanifold_points_set.Add(i);
                                }
                                else if (shared_verts.Count == 4)
                                    break;
                                
                                break;
                            }
                        case 3:
                            {
                                List<int> hex1_verts = new List<int>(hexes[hex_idxs[0]]);
                                List<int> hex2_verts = new List<int>(hexes[hex_idxs[1]]);
                                List<int> hex3_verts = new List<int>(hexes[hex_idxs[2]]);
                                verts.AddRange(hex1_verts);
                                verts.AddRange(hex2_verts);
                                verts.AddRange(hex3_verts);

                                List<int> three_hexes = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() > 2)
                                                        .Select(g => g.Key)
                                                        .ToList();
                                List<int> two_hexes = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 2)
                                                        .Select(g => g.Key)
                                                        .ToList();

                                if (three_hexes.Count == 2)
                                    break;
                                else if (two_hexes.Count == 3 && three_hexes.Count == 1)
                                // three pinched edges
                                {
                                    foreach (int idx in two_hexes)
                                    {
                                        List<int> e = new List<int> { idx, three_hexes[0] };
                                        e.Sort();
                                        Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                        //if (!pinch_edges.Contains(edge))
                                        //    pinch_edges.Add(edge);
                                        pinch_edges_fullset.Add(edge);
                                    }

                                    nonmanifold_points_set.Add(i);
                                }
                                else if (two_hexes.Count == 4)
                                // one pinched edge
                                {
                                    foreach (int idx in two_hexes)
                                    {
                                        List<int> connected_verts = new List<int>(connected_verts_dict[idx]);
                                        List<int> all = verts.GroupBy(x => x).Select(x => x.Key).ToList();
                                        connected_verts.AddRange(all);
                                        List<int> shared = connected_verts.GroupBy(x => x)
                                                                    .Where(g => g.Count() > 1)
                                                                    .Select(g => g.Key)
                                                                    .ToList();
                                        if (shared.Count == 5)
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //{
                                            //    pinch_edges.Add(edge);
                                            //    break;
                                            //}
                                            pinch_edges_fullset.Add(edge);
                                        }
                                    }
                                    //bad_pts.Add(i);
                                    bad_pts_fullset.Add(i);
                                    nonmanifold_points_set.Add(i);
                                }
                                else
                                    throw new Exception(string.Format("I was not expecting this: two_hexes = {0} and three_hexes = {1}", two_hexes.Count, three_hexes.Count));

                                break;
                            }
                        case 4:
                            {
                                verts.AddRange(hexes[hex_idxs[0]]);
                                verts.AddRange(hexes[hex_idxs[1]]);
                                verts.AddRange(hexes[hex_idxs[2]]);
                                verts.AddRange(hexes[hex_idxs[3]]);

                                List<int> one_hex = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 1)
                                                        .Select(g => g.Key)
                                                        .ToList();
                                List<int> two_hexes = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 2)
                                                        .Select(g => g.Key)
                                                        .ToList();
                                List<int> three_hexes = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 3)
                                                        .Select(g => g.Key)
                                                        .ToList();

                                if (three_hexes.Count == 2)
                                    break;
                                else if (one_hex.Count == 12)
                                // 2 colinear pinched edges
                                {
                                    //pinch_points.Add(i);
                                    pinch_points_fullset.Add(i);

                                    foreach (int idx in two_hexes)
                                    {
                                        List<int> all_verts = verts.GroupBy(x => x)
                                                                .Where(g => g.Count() > 0)
                                                                .Select(g => g.Key)
                                                                .ToList();
                                        List<int> all_connections = new List<int>(connected_verts_dict[idx]);
                                        all_verts.AddRange(all_connections);

                                        List<int> connected_verts = all_verts.GroupBy(x => x)
                                                                        .Where(g => g.Count() > 1)
                                                                        .Select(g => g.Key)
                                                                        .ToList();

                                        if (connected_verts.Count == 5)
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //    pinch_edges.Add(edge);
                                            pinch_edges_fullset.Add(edge);
                                            nonmanifold_points_set.Add(i);
                                        }
                                    }
                                }
                                else if (three_hexes.Count == 1) // This one is a problem, it adds an extrapinched edge
                                                                 // 2 pinched edges
                                {
                                    foreach (int idx in two_hexes)
                                    {
                                        List<int> all_verts = verts.GroupBy(x => x)
                                                                .Where(g => g.Count() > 0)
                                                                .Select(g => g.Key)
                                                                .ToList();
                                        List<int> all_connections = new List<int>(connected_verts_dict[idx]);
                                        all_verts.AddRange(all_connections);

                                        List<int> connected_verts = all_verts.GroupBy(x => x)
                                                                        .Where(g => g.Count() > 1)
                                                                        .Select(g => g.Key)
                                                                        .ToList();

                                        if (connected_verts.Count == 5)
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //    pinch_edges.Add(edge);
                                            pinch_edges_fullset.Add(edge);
                                            nonmanifold_points_set.Add(i);
                                        }
                                    }
                                }
                                else if (one_hex.Count == 16)
                                // 6 pinched edges
                                {
                                    List<int> connected_verts = new List<int>(connected_verts_dict[i]);

                                    foreach (int idx in connected_verts)
                                    {
                                        List<int> e = new List<int> { idx, i };
                                        e.Sort();
                                        Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                        //if (!pinch_edges.Contains(edge))
                                        //    pinch_edges.Add(edge);
                                        pinch_edges_fullset.Add(edge);
                                    }
                                    nonmanifold_points_set.Add(i);
                                }

                                break;
                            }
                        case 5:
                            {
                                verts.AddRange(hexes[hex_idxs[0]]);
                                verts.AddRange(hexes[hex_idxs[1]]);
                                verts.AddRange(hexes[hex_idxs[2]]);
                                verts.AddRange(hexes[hex_idxs[3]]);
                                verts.AddRange(hexes[hex_idxs[4]]);

                                List<int> one_hex = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 1)
                                                        .Select(g => g.Key)
                                                        .ToList();

                                if (one_hex.Count == 11)
                                    break;
                                else if (one_hex.Count == 12)
                                // 1 pinched edge
                                {
                                    List<int> two_hexes = verts.GroupBy(x => x)
                                                            .Where(g => g.Count() == 2)
                                                            .Select(g => g.Key)
                                                            .ToList();

                                    //Point3d center = vertices[i].getLocation();
                                    foreach (int idx in two_hexes)
                                    {
                                        List<int> all_verts = verts.GroupBy(x => x)
                                                                .Where(g => g.Count() > 0)
                                                                .Select(g => g.Key)
                                                                .ToList();
                                        List<int> all_connections = new List<int>(connected_verts_dict[idx]);
                                        all_verts.AddRange(all_connections);

                                        List<int> connected_verts = all_verts.GroupBy(x => x)
                                                                        .Where(g => g.Count() > 1)
                                                                        .Select(g => g.Key)
                                                                        .ToList();

                                        if (connected_verts.Count == 5)
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //    pinch_edges.Add(edge);
                                            pinch_edges_fullset.Add(edge);
                                            nonmanifold_points_set.Add(i);
                                        }
                                    }
                                }
                                else if (one_hex.Count == 14)
                                // 3 pinched edges
                                {
                                    List<int> two_hexes = verts.GroupBy(x => x)
                                                            .Where(g => g.Count() == 2)
                                                            .Select(g => g.Key)
                                                            .ToList();
                                    Point3d center = vertices[i];
                                    foreach (int idx in two_hexes)
                                    {
                                        Point3d point = vertices[idx];
                                        double x = point.X;
                                        double y = point.Y;
                                        double z = point.Z;
                                        if ((x == center.X && y == center.Y)
                                            || (x == center.X && z == center.Z)
                                            || (y == center.Y && z == center.Z))
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //    pinch_edges.Add(edge);
                                            pinch_edges_fullset.Add(edge);
                                            nonmanifold_points_set.Add(i);
                                        }
                                    }
                                }

                                break;
                            }
                        case 6:
                            {
                                verts.AddRange(hexes[hex_idxs[0]]);
                                verts.AddRange(hexes[hex_idxs[1]]);
                                verts.AddRange(hexes[hex_idxs[2]]);
                                verts.AddRange(hexes[hex_idxs[3]]);
                                verts.AddRange(hexes[hex_idxs[4]]);
                                verts.AddRange(hexes[hex_idxs[5]]);

                                List<int> four_hexes = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() == 4)
                                                        .Select(g => g.Key)
                                                        .ToList();

                                if (four_hexes.Count == 2)
                                    break;
                                else if (four_hexes.Count == 1)
                                // 1 pinched edge
                                {
                                    List<int> two_hexes = verts.GroupBy(x => x)
                                                            .Where(g => g.Count() == 2)
                                                            .Select(g => g.Key)
                                                            .ToList();
                                    Point3d center = vertices[i];
                                    foreach (int idx in two_hexes)
                                    {
                                        Point3d point = vertices[idx];
                                        double x = point.X;
                                        double y = point.Y;
                                        double z = point.Z;
                                        if ((x == center.X && y == center.Y)
                                            || (x == center.X && z == center.Z)
                                            || (y == center.Y && z == center.Z))
                                        {
                                            List<int> e = new List<int> { idx, i };
                                            e.Sort();
                                            Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                            //if (!pinch_edges.Contains(edge))
                                            //    pinch_edges.Add(edge);
                                            pinch_edges_fullset.Add(edge);
                                            nonmanifold_points_set.Add(i);
                                        }
                                    }
                                }
                                else if (four_hexes.Count == 0)
                                // 1 pinch point
                                // This is solved by adding truncated hexes 
                                {
                                    List<int> three_hexes = verts.GroupBy(x => x)
                                                            .Where(g => g.Count() == 3)
                                                            .Select(g => g.Key)
                                                            .ToList();

                                    foreach (int idx in three_hexes)
                                    {
                                        List<int> e = new List<int> { idx, i };
                                        e.Sort();
                                        Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                                        //if (!pinch_edges.Contains(edge))
                                        //    pinch_edges.Add(edge);
                                        pinch_edges_fullset.Add(edge);
                                        nonmanifold_points_set.Add(i);
                                    }
                                }

                                break;
                            }
                        case 7:
                            break;
                        case 8:
                            break;
                    }
                }
            }

            // Breadth First Search for connected points
            HashSet<int> verts_in_series = new HashSet<int>();
            List<HashSet<int>> connected_nonmanifolds = new List<HashSet<int>>();
            foreach (int vert in nonmanifold_points_set)
            {
                HashSet<int> list = new HashSet<int>();
                list.Add(vert);
                if (verts_in_series.Contains(vert))
                    continue;

                Queue<int> queue = new Queue<int>();
                HashSet<int> visited = new HashSet<int>();

                visited.Add(vert);
                queue.Enqueue(vert);

                while (queue.Count != 0)
                {
                    int current_vert = queue.Dequeue();
                    visited.Add(current_vert);
                    List<int> adj_verts = Functions.GetAdjacentVertices(current_vert, divs[0], divs[1], divs[2]);
                    foreach (int adj in adj_verts)
                    {
                        if (nonmanifold_points_set.Contains(adj) && !visited.Contains(adj) && !list.Contains(adj))
                        {
                            queue.Enqueue(adj);
                            list.Add(adj);
                            verts_in_series.Add(adj);
                            verts_in_series.Add(vert);
                        }
                    }
                }

                connected_nonmanifolds.Add(list);
            }

            for (int w = 0; w < connected_nonmanifolds.Count; w++)
            {
                int separate_counter = 0;
                int connect_counter = 0;
                var series = connected_nonmanifolds[w];
                foreach (var idx in series)
                {
                    if (in_verts_set.Contains(idx))
                        connect_counter++;
                    else
                        separate_counter++;
                }

                for (int i = 0; i < series.Count; i++)
                {
                    for (int j = i; j < series.Count; j++)
                    {
                        List<int> e = new List<int> { i, j };
                        e.Sort();
                        Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                        if (all_edges.Contains(edge))
                        {
                            if (in_edges_set.Contains(edge))
                                connect_counter++;
                            else
                                separate_counter++;
                        }
                    }
                }

                HashSet<int> kissing_points_set = new HashSet<int>();
                HashSet<int> pinch_points_set = new HashSet<int>();
                HashSet<Tuple<int, int>> pinch_edges_set = new HashSet<Tuple<int, int>>();
                HashSet<int> remove_pts_set = new HashSet<int>();
                HashSet<int> bad_pts_set = new HashSet<int>();

                bool separate = false;
                if (separate_counter > connect_counter)
                    separate = true;
                else if (separate_counter == connect_counter)
                    separate = false;

                foreach (int idx in series)
                {
                    if (kissing_points_fullset.Contains(idx))
                        kissing_points_set.Add(idx);
                    if (pinch_points_fullset.Contains(idx))
                        pinch_points_set.Add(idx);
                    if (remove_pts_fullset.Contains(idx))
                        remove_pts_set.Add(idx);
                    if (bad_pts_fullset.Contains(idx))
                        bad_pts_set.Add(idx);

                    List<int> adjs = Functions.GetAdjacentVertices(idx, divs[0], divs[1], divs[2]);
                    List<int> connected_verts = new List<int> { adjs[0], adjs[2], adjs[11], adjs[13], adjs[4], adjs[3] };
                    foreach (int vertex in connected_verts)
                    {
                        List<int> e = new List<int> { vertex, idx };
                        e.Sort();
                        Tuple<int, int> edge = new Tuple<int, int>(e[0], e[1]);
                        if (pinch_edges_fullset.Contains(edge))
                            pinch_edges_set.Add(edge);
                    }
                }

                // Add hexes to make mesh manifold
                if (!separate)
                {
                    Dictionary<int, int> faces_needing_pyramids = new Dictionary<int, int>(); // key = face index value = 0(pinched edge only) or 1(pinch point and pinched edge) or 2(kissing point)
                    foreach (int kissing_point in kissing_points_set)
                    {
                        List<int> verts = new List<int>();
                        List<int> hex_idxs = vert_to_hexes[kissing_point];
                        List<int> hex1 = hexes[hex_idxs[0]];
                        List<int> hex2 = hexes[hex_idxs[1]];

                        if (vertices[hex1[4]].Z < vertices[hex2[4]].Z)
                        {
                            hex1 = hexes[hex_idxs[1]];
                            hex2 = hexes[hex_idxs[0]];
                        }

                        List<int> h1_f1 = new List<int>();
                        List<int> h1_f2 = new List<int>();
                        List<int> h1_f3 = new List<int>();
                        List<int> h2_f1 = new List<int>();
                        List<int> h2_f2 = new List<int>();
                        List<int> h2_f3 = new List<int>();

                        if (hex1[0] == kissing_point)
                        {

                            h1_f1 = new List<int> { hex1[0], hex1[1], hex1[2], hex1[3] };
                            h1_f2 = new List<int> { hex1[0], hex1[4], hex1[7], hex1[3] };
                            h1_f3 = new List<int> { hex1[0], hex1[4], hex1[5], hex1[1] };
                            if (hex2[4] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[4], hex2[5], hex2[6], hex2[7] };
                                h2_f2 = new List<int> { hex2[4], hex2[5], hex2[1], hex2[0] };
                                h2_f3 = new List<int> { hex2[4], hex2[7], hex2[3], hex2[0] };
                            }
                            else if (hex2[5] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[5], hex2[4], hex2[7], hex2[6] };
                                h2_f2 = new List<int> { hex2[5], hex2[4], hex2[0], hex2[1] };
                                h2_f3 = new List<int> { hex2[5], hex2[6], hex2[2], hex2[1] };
                            }
                            else if (hex2[6] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[6], hex2[7], hex2[4], hex2[5] };
                                h2_f2 = new List<int> { hex2[6], hex2[5], hex2[1], hex2[2] };
                                h2_f3 = new List<int> { hex2[6], hex2[7], hex2[3], hex2[2] };
                            }
                            else if (hex2[7] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[7], hex2[4], hex2[5], hex2[6] };
                                h2_f2 = new List<int> { hex2[7], hex2[4], hex2[0], hex2[3] };
                                h2_f3 = new List<int> { hex2[7], hex2[6], hex2[2], hex2[3] };
                            }
                        }
                        else if (hex1[1] == kissing_point)
                        {
                            h1_f1 = new List<int> { hex1[1], hex1[2], hex1[3], hex1[0] };
                            h1_f2 = new List<int> { hex1[1], hex1[5], hex1[4], hex1[0] };
                            h1_f3 = new List<int> { hex1[1], hex1[5], hex1[6], hex1[2] };
                            if (hex2[4] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[4], hex2[5], hex2[6], hex2[7] };
                                h2_f2 = new List<int> { hex2[4], hex2[5], hex2[1], hex2[0] };
                                h2_f3 = new List<int> { hex2[4], hex2[7], hex2[3], hex2[0] };
                            }
                            else if (hex2[5] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[5], hex2[4], hex2[7], hex2[6] };
                                h2_f2 = new List<int> { hex2[5], hex2[4], hex2[0], hex2[1] };
                                h2_f3 = new List<int> { hex2[5], hex2[6], hex2[2], hex2[1] };
                            }
                            else if (hex2[6] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[6], hex2[7], hex2[4], hex2[5] };
                                h2_f2 = new List<int> { hex2[6], hex2[5], hex2[1], hex2[2] };
                                h2_f3 = new List<int> { hex2[6], hex2[7], hex2[3], hex2[2] };
                            }
                            else if (hex2[7] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[7], hex2[4], hex2[5], hex2[6] };
                                h2_f2 = new List<int> { hex2[7], hex2[4], hex2[0], hex2[3] };
                                h2_f3 = new List<int> { hex2[7], hex2[6], hex2[2], hex2[3] };
                            }
                        }
                        else if (hex1[2] == kissing_point)
                        {
                            h1_f1 = new List<int> { hex1[2], hex1[3], hex1[0], hex1[1] };
                            h1_f2 = new List<int> { hex1[2], hex1[6], hex1[5], hex1[1] };
                            h1_f3 = new List<int> { hex1[2], hex1[6], hex1[7], hex1[3] };
                            if (hex2[4] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[4], hex2[5], hex2[6], hex2[7] };
                                h2_f2 = new List<int> { hex2[4], hex2[5], hex2[1], hex2[0] };
                                h2_f3 = new List<int> { hex2[4], hex2[7], hex2[3], hex2[0] };
                            }
                            else if (hex2[5] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[5], hex2[4], hex2[7], hex2[6] };
                                h2_f2 = new List<int> { hex2[5], hex2[4], hex2[0], hex2[1] };
                                h2_f3 = new List<int> { hex2[5], hex2[6], hex2[2], hex2[1] };
                            }
                            else if (hex2[6] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[6], hex2[7], hex2[4], hex2[5] };
                                h2_f2 = new List<int> { hex2[6], hex2[5], hex2[1], hex2[2] };
                                h2_f3 = new List<int> { hex2[6], hex2[7], hex2[3], hex2[2] };
                            }
                            else if (hex2[7] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[7], hex2[4], hex2[5], hex2[6] };
                                h2_f2 = new List<int> { hex2[7], hex2[4], hex2[0], hex2[3] };
                                h2_f3 = new List<int> { hex2[7], hex2[6], hex2[2], hex2[3] };
                            }
                        }
                        else if (hex1[3] == kissing_point)
                        {
                            h1_f1 = new List<int> { hex1[3], hex1[0], hex1[1], hex1[2] };
                            h1_f2 = new List<int> { hex1[3], hex1[7], hex1[4], hex1[0] };
                            h1_f3 = new List<int> { hex1[3], hex1[7], hex1[6], hex1[2] };
                            if (hex2[4] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[4], hex2[5], hex2[6], hex2[7] };
                                h2_f2 = new List<int> { hex2[4], hex2[5], hex2[1], hex2[0] };
                                h2_f3 = new List<int> { hex2[4], hex2[7], hex2[3], hex2[0] };
                            }
                            else if (hex2[5] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[5], hex2[4], hex2[7], hex2[6] };
                                h2_f2 = new List<int> { hex2[5], hex2[4], hex2[0], hex2[1] };
                                h2_f3 = new List<int> { hex2[5], hex2[6], hex2[2], hex2[1] };
                            }
                            else if (hex2[6] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[6], hex2[7], hex2[4], hex2[5] };
                                h2_f2 = new List<int> { hex2[6], hex2[5], hex2[1], hex2[2] };
                                h2_f3 = new List<int> { hex2[6], hex2[7], hex2[3], hex2[2] };
                            }
                            else if (hex2[7] == kissing_point)
                            {
                                h2_f1 = new List<int> { hex2[7], hex2[4], hex2[5], hex2[6] };
                                h2_f2 = new List<int> { hex2[7], hex2[4], hex2[0], hex2[3] };
                                h2_f3 = new List<int> { hex2[7], hex2[6], hex2[2], hex2[3] };
                            }
                        }

                        h1_f1.Sort();
                        h1_f2.Sort();
                        h1_f3.Sort();
                        h2_f1.Sort();
                        h2_f2.Sort();
                        h2_f3.Sort();

                        List<int> f1 = face_dict[new Tuple<int, int, int, int>(h1_f1[0], h1_f1[1], h1_f1[2], h1_f1[3])];
                        List<int> f2 = face_dict[new Tuple<int, int, int, int>(h1_f2[0], h1_f2[1], h1_f2[2], h1_f2[3])];
                        List<int> f3 = face_dict[new Tuple<int, int, int, int>(h1_f3[0], h1_f3[1], h1_f3[2], h1_f3[3])];
                        List<int> f4 = face_dict[new Tuple<int, int, int, int>(h2_f1[0], h2_f1[1], h2_f1[2], h2_f1[3])];
                        List<int> f5 = face_dict[new Tuple<int, int, int, int>(h2_f2[0], h2_f2[1], h2_f2[2], h2_f2[3])];
                        List<int> f6 = face_dict[new Tuple<int, int, int, int>(h2_f3[0], h2_f3[1], h2_f3[2], h2_f3[3])];

                        List<List<int>> p_faces = new List<List<int>> { f1, f2, f3, f4, f5, f6 };
                        List<int> build_faces = new List<int>();

                        for (int i = 0; i < faces.Count; i++)
                        {
                            List<int> face = faces[i];
                            if (p_faces.Contains(face))
                                build_faces.Add(i);
                        }


                        foreach (int idx in build_faces)
                        {
                            if (!faces_needing_pyramids.ContainsKey(idx))
                                faces_needing_pyramids.Add(idx, 1);
                        }
                    }

                    List<Tuple<int, int, int, int>> possible_faces = new List<Tuple<int, int, int, int>>();
                    foreach (List<int> h in hexes)
                    {
                        List<List<int>> h_faces = Functions.GetHexFaces(h, false);

                        foreach (var hex_face in h_faces)
                        {
                            List<int> list_v = new List<int>(hex_face);
                            List<int> v = new List<int> { list_v[0], list_v[1] };
                            v.Sort();
                            Tuple<int, int> e1 = new Tuple<int, int>(v[0], v[1]);
                            v = new List<int> { list_v[1], list_v[2] };
                            v.Sort();
                            Tuple<int, int> e2 = new Tuple<int, int>(v[0], v[1]);
                            v = new List<int> { list_v[2], list_v[3] };
                            v.Sort();
                            Tuple<int, int> e3 = new Tuple<int, int>(v[0], v[1]);
                            v = new List<int> { list_v[3], list_v[0] };
                            v.Sort();
                            Tuple<int, int> e4 = new Tuple<int, int>(v[0], v[1]);
                            if (pinch_edges_set.Contains(e1) || pinch_edges_set.Contains(e2) || pinch_edges_set.Contains(e3) || pinch_edges_set.Contains(e4))
                            {
                                list_v.Sort();
                                possible_faces.Add(new Tuple<int, int, int, int>(list_v[0], list_v[1], list_v[2], list_v[3]));
                            }
                        }
                    }

                    List<Tuple<int, int, int, int>> true_faces = possible_faces.GroupBy(x => x)
                                                                    .Where(g => g.Count() == 1)
                                                                    .Select(g => g.Key)
                                                                    .ToList();

                    for (int i = 0; i < faces.Count; i++)
                    {
                        List<int> face_verts = new List<int>(faces[i]);
                        face_verts.Sort();
                        Tuple<int, int, int, int> face_tuple = new Tuple<int, int, int, int>
                            (face_verts[0], face_verts[1], face_verts[2], face_verts[3]);
                        bool pinched_point = false;
                        int point_idx = -1;

                        if (true_faces.Contains(face_tuple))
                        {
                            foreach (int idx in face_verts)
                            {
                                if (pinch_points_set.Contains(idx))
                                {
                                    pinched_point = true;
                                    point_idx = idx;
                                    break;
                                }
                            }

                            if (!faces_needing_pyramids.ContainsKey(i) && !pinched_point)
                                faces_needing_pyramids.Add(i, -1);
                            else if (!faces_needing_pyramids.ContainsKey(i) && pinched_point)
                                faces_needing_pyramids.Add(i, 0);
                            else if (pinched_point && faces_needing_pyramids[i] == 1)
                                faces_needing_pyramids[i] = 2;
                            else if (pinched_point)
                                faces_needing_pyramids[i] = 0;
                        }
                    }


                    double dist = Math.Sqrt(Math.Pow(x_dist / divisor, 2) + Math.Pow(y_dist / divisor, 2) + Math.Pow(z_dist / divisor, 2));
                    double x_factor = -1;
                    double y_factor = -1;
                    double z_factor = -1;
                    List<List<int>> pinch_point_faces = new List<List<int>>();
                    List<List<int>> pinch_point_hexes = new List<List<int>>();
                    List<List<int>> kissing_point_faces = new List<List<int>>();
                    List<List<int>> kissing_point_hexes = new List<List<int>>();
                    foreach (int face_idx in faces_needing_pyramids.Keys)
                    {
                        List<int> list = new List<int>(faces[face_idx]);
                        list.Sort();
                        List<int> h_face = new List<int> { list[0], list[1], list[2], list[3] };

                        List<int> hex = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
                        foreach (List<int> h in hexes)
                        {
                            List<List<int>> h_faces = Functions.GetHexFaces(h, true);
                            if (h_faces.Contains(h_face))
                            {
                                hex = h;
                                break;
                            }
                        }

                        List<int> face = faces[face_idx];
                        List<int> face_vertices = new List<int>(face);

                        Point3d tip = Functions.GetCentroid(face, vertices);

                        Vector3d normal = Functions.Normal(face, vertices);

                        if (Math.Abs(normal.X) > 1e-5)
                        {
                            normal *= x_dist / 2;
                            tip.X += normal.X;
                            x_factor = 1;
                            y_factor = -1;
                            z_factor = -1;
                        }
                        else if (Math.Abs(normal.Y) > 1e-5)
                        {
                            normal *= y_dist / 2;
                            tip.Y += normal.Y;
                            x_factor = -1;
                            y_factor = 1;
                            z_factor = -1;
                        }
                        else if (Math.Abs(normal.Z) > 1e-5)
                        {
                            normal *= z_dist / 2;
                            tip.Z += normal.Z;
                            x_factor = -1;
                            y_factor = -1;
                            z_factor = 1;
                        }

                        List<Vector3d> vectors = new List<Vector3d>();
                        foreach (int vert_idx in face_vertices)
                        {
                            Point3d vertex = vertices[vert_idx];
                            Vector3d temp_vector = vertex - tip;
                            temp_vector.Unitize();
                            vectors.Add(temp_vector);
                        }


                        List<Point3d> new_vertices = new List<Point3d>();
                        List<int> new_idxs = new List<int>();

                        bool kissing_point = false;
                        bool pinched_point = false;
                        if (faces_needing_pyramids[face_idx] == 1 || faces_needing_pyramids[face_idx] == 2)
                        {
                            kissing_point = true;
                        }

                        if (faces_needing_pyramids[face_idx] == 0 || faces_needing_pyramids[face_idx] == 2)
                        {
                            pinched_point = true;
                        }

                        for (int i = 0; i < vectors.Count; i++)
                        {
                            Point3d temp_point = new Point3d();
                            Point3d point = vertices[face_vertices[i]];
                            temp_point.X = point.X + x_factor * (dist * vectors[i].X);
                            temp_point.Y = point.Y + y_factor * (dist * vectors[i].Y);
                            temp_point.Z = point.Z + z_factor * (dist * vectors[i].Z);
                            new_vertices.Add(temp_point);
                            int new_idx = vertices.Count();
                            bool add_vert = true;

                            for (int j = vertices.Count - 1; j >= init_vert_count; --j)
                            {
                                Point3d vert = vertices[j];
                                if (vert.EpsilonEquals(temp_point, 1e-2))
                                {
                                    new_idx = j;
                                    add_vert = false;
                                }
                            }

                            new_idxs.Add(new_idx);
                            if (add_vert)
                            {
                                //Vertex temp_vertex = new Vertex(new_idx, temp_point);
                                vertices.Add(temp_point);
                            }
                        }

                        List<int> face1 = new List<int> { face_vertices[0], new_idxs[0], new_idxs[1], face_vertices[1] };
                        List<int> face2 = new List<int> { face_vertices[1], new_idxs[1], new_idxs[2], face_vertices[2] };
                        List<int> face3 = new List<int> { face_vertices[2], new_idxs[2], new_idxs[3], face_vertices[3] };
                        List<int> face4 = new List<int> { face_vertices[3], new_idxs[3], new_idxs[0], face_vertices[0] };
                        List<int> face5 = new List<int> { new_idxs[0], new_idxs[1], new_idxs[2], new_idxs[3] };

                        List<int> hex_verts = new List<int>();
                        hex_verts.AddRange(face_vertices);
                        hex_verts.AddRange(new_idxs);

                        hexes.Add(new List<int> { hex_verts[0], hex_verts[1], hex_verts[2], hex_verts[3],
                                    hex_verts[4], hex_verts[5], hex_verts[6], hex_verts[7] });


                        if (pinched_point)
                        {
                            pinch_point_faces.Add(face1);
                            pinch_point_faces.Add(face2);
                            pinch_point_faces.Add(face3);
                            pinch_point_faces.Add(face4);
                            pinch_point_faces.Add(face5);
                            pinch_point_hexes.Add(hexes.Last());
                        }
                        if (kissing_point)
                        {
                            kissing_point_faces.Add(face1);
                            kissing_point_faces.Add(face2);
                            kissing_point_faces.Add(face3);
                            kissing_point_faces.Add(face4);
                            kissing_point_faces.Add(face5);
                            kissing_point_hexes.Add(hexes.Last());
                        }

                    }

                    foreach (int pinch_point in pinch_points_set)
                    {
                        List<List<int>> connected_faces = new List<List<int>>();
                        foreach (List<int> face in pinch_point_faces)
                        {
                            if (face.Contains(pinch_point))
                                connected_faces.Add(face);
                        }

                        List<List<QuadFace>> faces_to_connect = new List<List<QuadFace>>();
                        List<Tuple<int, int>> pairs = new List<Tuple<int, int>>();
                        for (int i = 0; i < connected_faces.Count; i++)
                        {
                            List<int> face1 = connected_faces[i];
                            for (int j = 0; j < connected_faces.Count; j++)
                            {
                                List<int> face2 = connected_faces[j];
                                //List<int> f1_verts = new List<int>(face1.getQaudFace());
                                //List<int> f2_verts = new List<int>(face2.getQaudFace());
                                List<int> verts = new List<int>();
                                verts.AddRange(face1);
                                verts.AddRange(face2);
                                List<int> shared_verts = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() > 1)
                                                        .Select(g => g.Key)
                                                        .ToList();
                                if (shared_verts.Count == 2) // This means they share two vertices 
                                {
                                    Vector3d f1_normal = Functions.Normal(face1, vertices);
                                    Vector3d f2_normal = Functions.Normal(face2, vertices);
                                    List<int> hex = new List<int>();
                                    List<int> pair = new List<int> { i, j };
                                    pair.Sort();
                                    Tuple<int, int> tuple = new Tuple<int, int>(pair[0], pair[1]);
                                    double value = f1_normal.IsParallelTo(f2_normal, 1 * Math.PI / 180);
                                    Vector3d shared_side = vertices[shared_verts[0]] - vertices[shared_verts[1]];
                                    double length = shared_side.Length;

                                    if (value == 0 && !pairs.Contains(tuple) &&
                                        (Math.Abs(length - x_dist) < 1e-5
                                        || Math.Abs(length - y_dist) < 1e-5
                                        || Math.Abs(length - z_dist) < 1e-5))
                                    {
                                        List<List<int>> crystal_faces = Functions.ConnectFacesWithOneHex
                                            (face1, face2, pinch_point, x_dist, y_dist, z_dist, vertices, out hex);
                                        hexes.Add(hex);
                                        pairs.Add(tuple);
                                    }

                                }
                            }
                        }
                    }

                    foreach (int kissing_point in kissing_points_set)
                    {

                        List<List<int>> connected_faces = new List<List<int>>();
                        foreach (List<int> face in kissing_point_faces)
                        {
                            if (face.Contains(kissing_point))
                                connected_faces.Add(face);
                        }

                        List<List<QuadFace>> faces_to_connect = new List<List<QuadFace>>();
                        List<Tuple<int, int>> pairs = new List<Tuple<int, int>>();
                        for (int i = 0; i < connected_faces.Count; i++)
                        {
                            List<int> face1 = connected_faces[i];
                            for (int j = 0; j < connected_faces.Count; j++)
                            {
                                List<int> face2 = connected_faces[j];

                                List<int> verts = new List<int>();
                                verts.AddRange(face1);
                                verts.AddRange(face2);

                                List<int> shared_verts = verts.GroupBy(x => x)
                                                        .Where(g => g.Count() > 1)
                                                        .Select(g => g.Key)
                                                        .ToList();

                                if (shared_verts.Count == 2)
                                {
                                    Vector3d f1_normal = Functions.Normal(face1, vertices);
                                    Vector3d f2_normal = Functions.Normal(face2, vertices);

                                    if (f1_normal.IsParallelTo(f2_normal) != 0)
                                    {
                                        List<int> list = new List<int> { i, j };
                                        list.Sort();
                                        Tuple<int, int> tuple = new Tuple<int, int>(list[0], list[1]);
                                        if (!pairs.Contains(tuple))
                                            pairs.Add(tuple);
                                    }
                                }
                            }
                        }

                        foreach (var pair in pairs)
                        {
                            List<int> face1 = connected_faces[pair.Item1];
                            List<int> face2 = connected_faces[pair.Item2];
                            List<int> hex1 = new List<int> { 0, 0, 0, 0, 0, 0, 0, 0 };
                            List<int> hex2 = new List<int> { 0, 0, 0, 0, 0, 0, 0, 0 };

                            List<List<int>> quad_faces = Functions.ConnectFacesWithTwoHexes(face1, face2, kissing_point, x_dist, y_dist, z_dist, vertices, out hex1, out hex2);
                            hexes.Add(hex1);
                            hexes.Add(hex2);
                            faces.AddRange(quad_faces);
                        }
                    }
                }
                // Remove hexes to make mesh manifold
                else
                {

                    HashSet<int> all_pts = new HashSet<int>();
                    foreach (Tuple<int, int> e in pinch_edges_set)
                    {
                        int e1 = e.Item1;
                        int e2 = e.Item2;

                        if (!all_pts.Contains(e1))
                            all_pts.Add(e1);
                        if (!all_pts.Contains(e2))
                            all_pts.Add(e2);
                    }


                    // 1 to 7 splits
                    List<List<int>> new_hexes = new List<List<int>>();
                    int hex_count = hexes.Count;
                    for (int i = 0; i < hex_count; i++)
                    {
                        List<int> hex = hexes[i];
                        List<int> hex_verts = hex;
                        bool split = false;
                        foreach (int vert in hex_verts)
                        {
                            if (kissing_points_set.Contains(vert) || remove_pts_set.Contains(vert) || pinch_points_set.Contains(vert))
                            {
                                Point3d centroid = Functions.GetCentroid(vertices, hex);
                                List<int> new_idxs = new List<int>();
                                List<Point3d> new_points = new List<Point3d>();
                                //List<Vertex> new_verts = new List<Vertex>();
                                foreach (int v in hex_verts)
                                {
                                    Vector3d vector = vertices[v] - centroid;

                                    Point3d point = centroid + (vector / 2);
                                    int idx = vertices.Count;
                                    //Vertex vertex = new Vertex(idx, point);

                                    new_idxs.Add(idx);
                                    new_points.Add(point);
                                    //new_verts.Add(vertex);
                                    vertices.Add(point);
                                }


                                List<int> cube = new List<int> { new_idxs[0], new_idxs[1], new_idxs[2], new_idxs[3], new_idxs[4], new_idxs[5], new_idxs[6], new_idxs[7] };
                                List<int> hex1 = new List<int> { hex_verts[0], hex_verts[1], hex_verts[2], hex_verts[3], new_idxs[0], new_idxs[1], new_idxs[2], new_idxs[3] };
                                List<int> hex2 = new List<int> { hex_verts[4], hex_verts[5], hex_verts[6], hex_verts[7], new_idxs[4], new_idxs[5], new_idxs[6], new_idxs[7] };
                                List<int> hex3 = new List<int> { hex_verts[2], hex_verts[3], hex_verts[7], hex_verts[6], new_idxs[2], new_idxs[3], new_idxs[7], new_idxs[6] };
                                List<int> hex4 = new List<int> { hex_verts[0], hex_verts[1], hex_verts[5], hex_verts[4], new_idxs[0], new_idxs[1], new_idxs[5], new_idxs[4] };
                                List<int> hex5 = new List<int> { hex_verts[3], hex_verts[0], hex_verts[4], hex_verts[7], new_idxs[3], new_idxs[0], new_idxs[4], new_idxs[7] };
                                List<int> hex6 = new List<int> { hex_verts[1], hex_verts[2], hex_verts[6], hex_verts[5], new_idxs[1], new_idxs[2], new_idxs[6], new_idxs[5] };

                                hexes.Add(cube); new_hexes.Add(cube);
                                hexes.Add(hex1); new_hexes.Add(hex1);
                                hexes.Add(hex2); new_hexes.Add(hex2);
                                hexes.Add(hex3); new_hexes.Add(hex3);
                                hexes.Add(hex4); new_hexes.Add(hex4);
                                hexes.Add(hex5); new_hexes.Add(hex5);
                                hexes.Add(hex6); new_hexes.Add(hex6);
                                split = true;
                                break;
                            }
                        }
                        if (split)
                            continue;

                        List<int> e1 = new List<int> { hex[0], hex[1] };
                        List<int> e2 = new List<int> { hex[1], hex[2] };
                        List<int> e3 = new List<int> { hex[2], hex[3] };
                        List<int> e4 = new List<int> { hex[3], hex[0] };
                        List<int> e5 = new List<int> { hex[0], hex[4] };
                        List<int> e6 = new List<int> { hex[1], hex[5] };
                        List<int> e7 = new List<int> { hex[2], hex[6] };
                        List<int> e8 = new List<int> { hex[3], hex[7] };
                        List<int> e9 = new List<int> { hex[4], hex[5] };
                        List<int> e10 = new List<int> { hex[5], hex[6] };
                        List<int> e11 = new List<int> { hex[6], hex[7] };
                        List<int> e12 = new List<int> { hex[7], hex[4] };
                        List<List<int>> edges = new List<List<int>> { e1, e2, e3, e4, e5, e6, e7, e8, e9, e10, e11, e12 };

                        foreach (var edge in edges)
                        {
                            edge.Sort();
                            Tuple<int, int> tuple = new Tuple<int, int>(edge[0], edge[1]);
                            if (pinch_edges_set.Contains(tuple))
                            {
                                Point3d centroid = Functions.GetCentroid(vertices, hex);
                                List<int> new_idxs = new List<int>();
                                List<Point3d> new_points = new List<Point3d>();
                                //List<Vertex> new_verts = new List<Vertex>();
                                foreach (int v in hex_verts)
                                {
                                    Vector3d vector = vertices[v] - centroid;

                                    Point3d point = centroid + (vector / 2);
                                    int idx = vertices.Count;
                                    //Vertex vertex = new Vertex(idx, point);

                                    new_idxs.Add(idx);
                                    new_points.Add(point);
                                    //new_verts.Add(vertex);
                                    vertices.Add(point);
                                }


                                List<int> cube = new List<int> { new_idxs[0], new_idxs[1], new_idxs[2], new_idxs[3], new_idxs[4], new_idxs[5], new_idxs[6], new_idxs[7] };
                                List<int> hex1 = new List<int> { hex_verts[0], hex_verts[1], hex_verts[2], hex_verts[3], new_idxs[0], new_idxs[1], new_idxs[2], new_idxs[3] };
                                List<int> hex2 = new List<int> { hex_verts[4], hex_verts[5], hex_verts[6], hex_verts[7], new_idxs[4], new_idxs[5], new_idxs[6], new_idxs[7] };
                                List<int> hex3 = new List<int> { hex_verts[2], hex_verts[3], hex_verts[7], hex_verts[6], new_idxs[2], new_idxs[3], new_idxs[7], new_idxs[6] };
                                List<int> hex4 = new List<int> { hex_verts[0], hex_verts[1], hex_verts[5], hex_verts[4], new_idxs[0], new_idxs[1], new_idxs[5], new_idxs[4] };
                                List<int> hex5 = new List<int> { hex_verts[3], hex_verts[0], hex_verts[4], hex_verts[7], new_idxs[3], new_idxs[0], new_idxs[4], new_idxs[7] };
                                List<int> hex6 = new List<int> { hex_verts[1], hex_verts[2], hex_verts[6], hex_verts[5], new_idxs[1], new_idxs[2], new_idxs[6], new_idxs[5] };

                                hexes.Add(cube); new_hexes.Add(cube);
                                hexes.Add(hex1); new_hexes.Add(hex1);
                                hexes.Add(hex2); new_hexes.Add(hex2);
                                hexes.Add(hex3); new_hexes.Add(hex3);
                                hexes.Add(hex4); new_hexes.Add(hex4);
                                hexes.Add(hex5); new_hexes.Add(hex5);
                                hexes.Add(hex6); new_hexes.Add(hex6);
                                split = true;
                                break;
                            }
                        }
                    }

                    // 2 to 6 splits
                    List<Tuple<int, int, int, int>> face_tuples = new List<Tuple<int, int, int, int>>();
                    foreach (List<int> hex1 in new_hexes)
                    {
                        foreach (List<int> hex2 in new_hexes)
                        {
                            if (hex1.Equals(hex2))
                                continue;

                            List<int> f1 = new List<int> { hex1[0], hex1[1], hex1[2], hex1[3] };
                            List<int> f2 = new List<int> { hex2[0], hex2[1], hex2[2], hex2[3] };
                            f1.Sort();
                            f2.Sort();
                            Tuple<int, int, int, int> hex1_face = new Tuple<int, int, int, int>(f1[0], f1[1], f1[2], f1[3]);
                            Tuple<int, int, int, int> hex2_face = new Tuple<int, int, int, int>(f1[0], f2[1], f2[2], f2[3]);
                            if (hex1_face.Equals(hex2_face) && !face_tuples.Contains(hex1_face))
                            // Do the two to six split
                            {
                                List<int> verts1 = hex1;
                                List<int> unordered_verts2 = hex2;
                                List<int> verts2 = new List<int> { -1, -1, -1, -1, -1, -1, -1, -1 };

                                // Order unordered_verts2 to be in the same order as verts1 
                                for (int i = 0; i < 4; i++)
                                {
                                    int idx1 = verts1[i];
                                    int position = unordered_verts2.IndexOf(idx1);
                                    verts2[i] = unordered_verts2[position];
                                    verts2[i + 4] = unordered_verts2[position + 4];
                                }

                                Vector3d vector1 = vertices[verts1[2]] - vertices[verts1[0]];
                                Vector3d vector2 = vertices[verts1[3]] - vertices[verts1[1]];

                                Point3d point1 = new Point3d(vertices[verts1[0]]);
                                point1.X += vector1.X * 3 / 8;
                                point1.Y += vector1.Y * 3 / 8;
                                point1.Z += vector1.Z * 3 / 8;

                                Point3d point2 = new Point3d(vertices[verts1[1]]);
                                point2.X += vector2.X * 3 / 8;
                                point2.Y += vector2.Y * 3 / 8;
                                point2.Z += vector2.Z * 3 / 8;

                                Point3d point3 = new Point3d(vertices[verts1[0]]);
                                point3.X += vector1.X * 5 / 8;
                                point3.Y += vector1.Y * 5 / 8;
                                point3.Z += vector1.Z * 5 / 8;

                                Point3d point4 = new Point3d(vertices[verts1[1]]);
                                point4.X += vector2.X * 5 / 8;
                                point4.Y += vector2.Y * 5 / 8;
                                point4.Z += vector2.Z * 5 / 8;

                                List<Point3d> points = new List<Point3d> { point1, point2, point3, point4 };
                                List<int> idxs = new List<int>();
                                foreach (Point3d point in points)
                                {
                                    idxs.Add(vertices.Count);
                                    //Vertex temp_vert = new Vertex(vertices.Count, point);
                                    vertices.Add(point);
                                }

                                List<List<int>> add_hexes = new List<List<int>>();
                                add_hexes.Add(new List<int> { verts1[0], verts1[4], idxs[0], verts2[4], verts1[1], verts1[5], idxs[1], verts2[5] });
                                add_hexes.Add(new List<int> { verts1[1], verts1[5], idxs[1], verts2[5], verts1[2], verts1[6], idxs[2], verts2[6] });
                                add_hexes.Add(new List<int> { verts1[2], verts1[6], idxs[2], verts2[6], verts1[3], verts1[7], idxs[3], verts2[7] });
                                add_hexes.Add(new List<int> { verts1[3], verts1[7], idxs[3], verts2[7], verts1[0], verts1[4], idxs[0], verts2[4] });
                                add_hexes.Add(new List<int> { verts1[4], verts1[5], verts1[6], verts1[7], idxs[0], idxs[1], idxs[2], idxs[3] });
                                add_hexes.Add(new List<int> { verts2[4], verts2[5], verts2[6], verts2[7], idxs[0], idxs[1], idxs[2], idxs[3] });

                                foreach (List<int> hex in add_hexes)
                                {
                                    hexes.Add(hex);
                                    // new_hexes.Add(hex);
                                }
                            }
                        }
                    }

                    // Right now this is causing us problems
                    // Because we are removing hexes it is messing up the ordering of the hexes
                    // I need to do something different, like make a list of all the hexes that need removed
                    // and remove them all at once at the end of the solving
                    // Before this was not a problem becasue this was the end of the looping, but now it is not
                    hex_count = hexes.Count;
                    
                    for (int i = hex_count - 1; i >= 0; i--)
                    {
                        List<int> verts = hexes[i];
                        List<int> hex = hexes[i];
                        bool removed = false;
                        foreach (int vert in verts)
                        {
                            int counter = 0;
                            if (kissing_points_set.Contains(vert) || pinch_points_set.Contains(vert) || remove_pts_set.Contains(vert))
                            {
                                hex_idxs_to_remove.Add(i);
                                removed = true;
                                break;
                            }
                            else if (bad_pts_set.Contains(vert))
                            {
                                if (Functions.IsSquare(hex, vertices))
                                    continue;
                                foreach (List<int> h in hexes)
                                {
                                    if (Functions.IsSquare(h, vertices))
                                    {
                                        if (h.Contains(hex[0])
                                        && h.Contains(hex[1])
                                        && h.Contains(hex[2])
                                        && h.Contains(hex[3]))
                                        {
                                            counter++;
                                        }
                                    }
                                }

                                if (counter == 2)
                                    continue;
                                else
                                {
                                    hex_idxs_to_remove.Add(i);
                                    removed = true;
                                    break;
                                }
                            }
                        }
                        if (removed)
                            continue;

                        List<int> e1 = new List<int> { hex[0], hex[1] };
                        List<int> e2 = new List<int> { hex[1], hex[2] };
                        List<int> e3 = new List<int> { hex[2], hex[3] };
                        List<int> e4 = new List<int> { hex[3], hex[0] };
                        List<int> e5 = new List<int> { hex[0], hex[4] };
                        List<int> e6 = new List<int> { hex[1], hex[5] };
                        List<int> e7 = new List<int> { hex[2], hex[6] };
                        List<int> e8 = new List<int> { hex[3], hex[7] };
                        List<int> e9 = new List<int> { hex[4], hex[5] };
                        List<int> e10 = new List<int> { hex[5], hex[6] };
                        List<int> e11 = new List<int> { hex[6], hex[7] };
                        List<int> e12 = new List<int> { hex[7], hex[4] };
                        List<List<int>> edges = new List<List<int>> { e1, e2, e3, e4, e5, e6, e7, e8, e9, e10, e11, e12 };
                        foreach (var edge in edges)
                        {
                            edge.Sort();
                            Tuple<int, int> t = new Tuple<int, int>(edge[0], edge[1]);
                            if (pinch_edges_set.Contains(t))
                            {
                                hex_idxs_to_remove.Add(i);
                                removed = true;
                                break;
                            }
                        }
                    }
                }
            }

            hex_idxs_to_remove.Sort();
            hex_idxs_to_remove.Reverse();
            foreach (int idx in hex_idxs_to_remove)
                hexes.RemoveAt(idx);

            foreach (List<int> v in hexes)
            {
                new_faces.Add(new List<int> { v[0], v[1], v[5], v[4] });
                new_faces.Add(new List<int> { v[1], v[2], v[6], v[5] });
                new_faces.Add(new List<int> { v[2], v[3], v[7], v[6] });
                new_faces.Add(new List<int> { v[0], v[4], v[7], v[3] });
                new_faces.Add(new List<int> { v[0], v[3], v[2], v[1] });
                new_faces.Add(new List<int> { v[4], v[5], v[6], v[7] });
            }

            List<Mesh> viz = new List<Mesh>();
            //List<Tuple<int, int, int, int>> meshed_faces = new List<Tuple<int, int, int, int>>();
            HashSet<HashSet<int>> meshed_faces = new HashSet<HashSet<int>>();
            foreach (List<int> face in new_faces)
            {
                List<int> f = new List<int> { face[0], face[1], face[2], face[3] };
                //f.Sort();
                //Tuple<int, int, int, int> tuple = new Tuple<int, int, int, int>(f[0], f[1], f[2], f[3]);
                HashSet<int> face_set = new HashSet<int>(f);
                if (!meshed_faces.Contains(face_set))
                {
                    Mesh mesh = new Mesh();
                    Point3d A = vertices[face[0]];
                    Point3d B = vertices[face[1]];
                    Point3d C = vertices[face[2]];
                    Point3d D = vertices[face[3]];

                    mesh.Vertices.Add(A);
                    mesh.Vertices.Add(B);
                    mesh.Vertices.Add(C);
                    mesh.Vertices.Add(D);

                    mesh.Faces.AddFace(0, 1, 2, 3);

                    viz.Add(mesh);
                    meshed_faces.Add(face_set);
                }
            }

            DA.SetDataList(0, vertices);
            DA.SetDataList(1, hexes);
            DA.SetDataList(2, viz);
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
            get { return new Guid("A9EB7C9A-7028-48A5-B6CA-570389B60B80"); }
        }
    }
}