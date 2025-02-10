using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Sculpt2D.Dijkstra;

using EigenWrapper.Eigen;
using Rhino.Geometry.Collections;
using Sculpt2D.Sculpt3D;
using Ed.Eto;

namespace Sculpt2D.Components
{
    public class JaggiesRosolution : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public JaggiesRosolution()
          : base("Resolve Jaggies 2D", "jaggies",
              "Resolve jaggies up to a given size",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background mesh", "back", "Background mesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Volume Fractions", "vols", "List of volume fractions", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fraction Threshold", "cut", "Volume Fraction Threshold", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Maximum resolution", "max", "Maximum distance of components for resolution", GH_ParamAccess.item);
            pManager.AddCurveParameter("Polylines", "pl", "Oriented polylines", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Sample Points-x", "n_x", "Number of sample points in x-direction", GH_ParamAccess.item);                              // 4
            pManager.AddIntegerParameter("Sample Pionts-y", "n_y", "Number of sample points in y-direction", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Resolved Mesh", "mesh", "Mesh (hopefully) resolved of jaggies", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh background = new Mesh();
            List<double> volume_fractions = new List<double>();
            double vol = new double();
            int max = new int();
            List<Curve> ori_curves = new List<Curve>();
            int n_x = new int();
            int n_y = new int();

            DA.GetData(0, ref background);
            DA.GetDataList(1, volume_fractions);
            DA.GetData(2, ref vol);
            DA.GetData(3, ref max);
            DA.GetDataList(4, ori_curves);
            DA.GetData(5, ref n_x);
            DA.GetData(6, ref n_y);

            double x_dist = 0;
            double y_dist = 0;
            Point3d A = background.Vertices[background.Faces[0].A];
            Point3d B = background.Vertices[background.Faces[0].B];
            Point3d C = background.Vertices[background.Faces[0].C];
            Vector3d vec = C - A;
            x_dist = Math.Abs(vec.X);
            y_dist = Math.Abs(vec.Y);

            BoundingBox bounding_box = background.GetBoundingBox(false);
            Box box = new Box(bounding_box);

            Interval x_interval = box.X;
            Interval y_interval = box.Y;

            int x_div = (int)Math.Round(x_interval.Length / x_dist, 0);
            int y_div = (int)Math.Round(y_interval.Length / y_dist, 0);

            Vector3d normal = new Vector3d(0, 0, 1);

            HashSet<int> faces_to_include = new HashSet<int>();
            for (int w = max; w > 2; w--)
            {
                List<List<int>> windows = new List<List<int>>();
                // Loop through enough faces to get every possible window of faces
                for (int i = 0; i < (background.Faces.Count - (x_div * (w - 1))); i++)
                {
                    if ((i % x_div) < (x_div - w + 1)) // Don't make a window that will spill into the other side of mesh
                    {
                        List<int> window = new List<int>();
                        for (int x = 0; x < w; x++) 
                        {
                            for (int y = 0; y < w; y++)
                            {
                                window.Add(i + (y * x_div) + x);
                            }
                        }
                        windows.Add(window);
                    }
                }

                foreach (List<int> window in windows)
                // Check if each window contains disconnected components
                {
                    // Loop through each face in the window
                    // Do the Breadth first search for each face
                    // Store the face indexes that are connected in a List<int> 
                    // after the BFS is done, store the list of face indexs in a List<List<int>>
                    List<List<int>> connected_face_idxs = new List<List<int>>();
                    foreach (int idx in window)
                    {
                        if (window.Count == 0)
                            continue;

                        int a = window[0];
                        List<int> list = new List<int>();
                        list.Add(a);

                        Queue<int> queue = new Queue<int>();
                        HashSet<int> visited = new HashSet<int>();
                        queue.Enqueue(a);

                        while (queue.Count != 0)
                        {
                            int current_face = queue.Dequeue();
                            visited.Add(current_face);
                            HashSet<int> connected_faces = new HashSet<int>();
                            foreach (var face in background.TopologyVertices.ConnectedFaces(background.Faces[current_face].A))
                                connected_faces.Add(face);
                            foreach(var face in background.TopologyVertices.ConnectedFaces(background.Faces[current_face].B))
                                connected_faces.Add(face);
                            foreach (var face in background.TopologyVertices.ConnectedFaces(background.Faces[current_face].C))
                                connected_faces.Add(face);
                            foreach (var face in background.TopologyVertices.ConnectedFaces(background.Faces[current_face].D))
                                connected_faces.Add(face);

                            foreach (int face in connected_faces)
                            {
                                if (window.Contains(face) && !list.Contains(face))
                                {
                                    queue.Enqueue(face);
                                    list.Add(face);
                                }
                            }
                        }

                        connected_face_idxs.Add(list);
                    }

                    for (int i = 0; i < connected_face_idxs.Count; i++)
                    {
                        List<int> list1 = connected_face_idxs[i];
                        for (int j = 0; j < connected_face_idxs.Count; j++)
                        {

                            List<int> list2 = connected_face_idxs[j];
                            if (i == j || list1.Equals(list2))
                                continue;

                            // find the two closest quad faces


                        }
                    }

                    // Create the nodes
                    List<Node> nodes = new List<Node>();
                    //Dictionary<int, int> node_idx_to_position = new Dictionary<int, int>();
                    //int index = 0;
                    //foreach (int a in window)
                    //{
                    //    Node node = new Node(a);
                    //    nodes.Add(node);
                    //    node_idx_to_position.Add(a, index);
                    //    index++;
                    //}

                    //foreach(Node node in nodes)
                    //// Add neighbors and cost
                    //{
                    //    int n = node.getIndex();
                    //    List<int> connected_faces = new List<int>();
                    //    // This needs to work for edge cases as well as middle stuff
                    //    // the problem is the function gets all the faces that are connected
                    //    //int[] function_connected_faces = background.Faces.GetConnectedFaces(n);
                    //    //List<int> connected_vertices = new List<int>();
                    //    //connected_vertices.AddRange(background.TopologyVertices.ConnectedTopologyVertices(background.Faces[n].A));
                    //    //connected_vertices.AddRange(background.TopologyVertices.ConnectedTopologyVertices(background.Faces[n].B));
                    //    //connected_vertices.AddRange(background.TopologyVertices.ConnectedTopologyVertices(background.Faces[n].C));
                    //    //connected_vertices.AddRange(background.TopologyVertices.ConnectedTopologyVertices(background.Faces[n].D));
                    //    //connected_vertices.GroupBy(x => x)
                    //    //        .Where(g => g.Count() > 0)
                    //    //        .Select(g => g.Key)
                    //    //        .ToList();

                    //    connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].A));
                    //    connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].B));
                    //    connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].C));
                    //    connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].D));
                    //    connected_faces = connected_faces.GroupBy(x => x)
                    //                            .Where(g => g.Count() > 0)
                    //                            .Select(g => g.Key)
                    //                            .ToList();
                    //    foreach(int a in connected_faces)
                    //    {
                    //        if (a == n || !node_idx_to_position.ContainsKey(a))
                    //            continue;
                    //        double cost = vol - volume_fractions[a];
                    //        if (cost > 0)
                    //            nodes[node_idx_to_position[n]].AddNeighbour(nodes[node_idx_to_position[a]], cost);
                    //        else
                    //            nodes[node_idx_to_position[n]].AddNeighbour(nodes[node_idx_to_position[a]], 0);
                    //    }
                    //}

                    //// Create graph
                    //Graph graph = new Graph();
                    //foreach (Node node in nodes)
                    //    graph.Add(node);

                    List<Tuple<int, int>> disconnected_components = new List<Tuple<int, int>>();
                    //foreach (int a in window)
                    //{
                    //    if (volume_fractions[a] < vol)
                    //        continue;
                    //    else
                    //        faces_to_include.Add(a);
                    //    foreach(int b in window)
                    //    {
                    //        if (volume_fractions[b] < vol || a == b)
                    //            continue;
                    //        DistanceCalculator calculated = new DistanceCalculator(graph);
                    //        List<int> path = calculated.Calculate(
                    //            nodes[node_idx_to_position[a]], 
                    //            nodes[node_idx_to_position[b]]);
                    //        double max_cost = 0;
                    //        for(int i = 1; i < path.Count; i++)
                    //        {
                    //            Node node1 = nodes[node_idx_to_position[path[i - 1]]];
                    //            Dictionary<Node, double> neighbors = node1.getNeighbors();
                    //            Node node2 = nodes[node_idx_to_position[path[i]]];
                    //            double cost = neighbors[node2];
                    //            if (max_cost < cost)
                    //                max_cost = cost;
                    //            //if (node1.getIndex() == 135 || node2.getIndex() == 135)
                    //            //    max_cost = cost;
                    //        }
                    //        if (max_cost > 0)
                    //            disconnected_components.Add(new Tuple<int, int>(a, b));
                    //    }
                    //}

                    List<double> sub_volumes = new List<double>();
                    if (disconnected_components.Count > 0)
                    {
                        int origin_face_idx = window[0] + w - 1;
                        Point3d origin = background.Vertices[background.Faces[origin_face_idx].B];
                        Plane plane = new Plane(origin, normal);
                        Rectangle3d rectangle = new Rectangle3d(plane, x_dist * w, y_dist * w);
                        Mesh mesh = Mesh.CreateFromPlane(plane, rectangle.X, rectangle.Y, w * 2, w * 2);

                        List<Tuple<double, double>> vert_array = new List<Tuple<double, double>>();
                        List<Tuple<uint, uint>> edge_array = new List<Tuple<uint, uint>>();
                        AlephSupport.ProcessPolylines(ori_curves, false, out vert_array, out edge_array);

                        List<Point3d> centroids = new List<Point3d>();
                        List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>();
                        List<Point3d> sample_pts = new List<Point3d>();
                        AlephSupport.GetQuerryPoints(mesh, n_x, n_y, out centroids, out sample_pts, out querry_pts);

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

                        List<double> pt_winding = new List<double>(mesh.Faces.Count * n_x * n_y);
                        int n_pts = n_x * n_y;
                        int counter = 0;
                        double cur_wind;
                        foreach (MeshFace face in mesh.Faces)
                        {
                            double volume_fraction = 0;
                            for (int pt_idx = 0; pt_idx < n_pts; ++pt_idx)
                            {
                                cur_wind = winding[counter + pt_idx];
                                volume_fraction += cur_wind;
                                pt_winding.Add(cur_wind);
                            }
                            volume_fraction /= divisor;
                            sub_volumes.Add(volume_fraction);
                            counter += n_pts;
                        }

                        List<int> new_faces = new List<int>();
                        for (int y = 0; y < w; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                int i = x + y * w;
                                if (vol > volume_fractions[window[i]])
                                {
                                    List<int> sub_faces = new List<int>
                                  { x * 2 + y * w * 4,
                                    x * 2 + 1 + y * w * 4, 
                                    w * 2 + x * 2 + y * w * 4, 
                                    w * 2 + x * 2 + 1 + y * w * 4 };
                                    int sub_counter = 0;
                                    foreach (int j in sub_faces)
                                    {
                                        if (sub_volumes[j] >= vol)
                                            sub_counter++;
                                    }

                                    if (sub_counter > 2)
                                        new_faces.Add(window[i]);
                                }
                                else
                                    new_faces.Add(window[i]);
                            }
                        }

                        // Create the nodes
                        List<Node> new_nodes = new List<Node>();
                        Dictionary<int, int> new_idx_to_position = new Dictionary<int, int>();
                        int index = 0;
                        foreach (int a in window)
                        {
                            Node temp_node = new Node(a);
                            new_nodes.Add(temp_node);
                            new_idx_to_position.Add(a, index);
                            index++;
                        }

                        foreach (Node node in new_nodes)
                        // Add neighbors and cost
                        {
                            int n = node.getIndex();
                            List<int> connected_faces = new List<int>();
                            connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].A));
                            connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].B));
                            connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].C));
                            connected_faces.AddRange(background.TopologyVertices.ConnectedFaces(background.Faces[n].D));
                            connected_faces = connected_faces.GroupBy(x => x)
                                                    .Where(g => g.Count() > 0)
                                                    .Select(g => g.Key)
                                                    .ToList();

                            foreach (int a in connected_faces)
                            {
                                if (a == n || !new_idx_to_position.ContainsKey(a))
                                    continue;
                                double cost = vol - volume_fractions[a];
                                if (cost > 0)
                                    new_nodes[new_idx_to_position[n]].AddNeighbour(nodes[new_idx_to_position[a]], cost);
                                else
                                    new_nodes[new_idx_to_position[n]].AddNeighbour(nodes[new_idx_to_position[a]], 0);
                            }
                        }

                        // Create graph
                        Graph new_graph = new Graph();
                        foreach (Node node in new_nodes)
                            new_graph.Add(node);

                        // Use Dijkstra's algorithm to find all disconnected components
                        List<Tuple<int, int>> new_disconnected_components = new List<Tuple<int, int>>();
                        foreach (int a in window)
                        {
                            if (volume_fractions[a] < vol)
                                continue;
                            foreach (int b in window)
                            {
                                if (volume_fractions[b] < vol || a == b)
                                    continue;
                                DistanceCalculator calculated = new DistanceCalculator(new_graph);
                                 List<int> path = calculated.Calculate(
                                     new_nodes[new_idx_to_position[a]], 
                                     new_nodes[new_idx_to_position[b]]);
                                double max_cost = 0;
                                for (int i = 1; i < path.Count; i++)
                                {
                                    Node node1 = nodes[new_idx_to_position[path[i - 1]]];
                                    Dictionary<Node, double> neighbors = node1.getNeighbors();
                                    Node node2 = nodes[new_idx_to_position[path[i]]];
                                    double cost = neighbors[node2];
                                    if (max_cost < cost)
                                        max_cost = cost;
                                }
                                if (max_cost > 0)
                                    new_disconnected_components.Add(new Tuple<int, int>(a, b));
                            }
                        }

                        if (new_disconnected_components.Count < disconnected_components.Count)
                        {
                            foreach (int idx in new_faces)
                                faces_to_include.Add(idx);
                        }
                    }
                }
            }

            Mesh dejaggied_mesh = background.DuplicateMesh();
            for (int i = background.Faces.Count - 1; i >= 0; i--)
            {
                if (!faces_to_include.Contains(i))
                    dejaggied_mesh.Faces.RemoveAt(i);
            }

            dejaggied_mesh.Compact();

            DA.SetData(0, dejaggied_mesh);
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
            get { return new Guid("4CB285B0-3288-491D-94EF-64AFEB7AD819"); }
        }
    }
}