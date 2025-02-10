using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Security;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Rhino.Render.ChangeQueue;
using Sculpt2D;
using Sculpt2D.Dijkstra;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Sculpt2D.Components
{
    public class Dijkstra : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Dijkstra()
          : base("Dijkstra", "dijk",
              "Find the lowest cost path from one face to another using Dijkstra's algorithm",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Start Face Centroid", "start", "Centroid start face to connect to end face", GH_ParamAccess.item);
            pManager.AddPointParameter("End Face Centroid", "end", "Centroid of end face to connect to start face", GH_ParamAccess.item);
            pManager.AddMeshParameter("Regular Mesh", "mesh", "Regular mesh", GH_ParamAccess.item);
            pManager.AddMeshParameter("Tri Dual Mesh", "dual", "Tri Dual mesh of the faces mesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Volume Fractions", "vols", "List of volume fractions corresponding to the regular mesh faces", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fraction", "vol", "Current volume fraction cutoff", GH_ParamAccess.item);
            pManager.AddMeshParameter("Background Mesh", "back", "Background mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Levels", "level", "Determines the number of levels from the shortest path for adding faces", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Add", "add", "Force mesh faces to be added no matter the cost", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Precise", "prec", "Use precise centroids or not", GH_ParamAccess.item);
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Sculpted Mesh", "mesh", "Sculpted mesh to include or exclude mesh faces", GH_ParamAccess.item);
            pManager.AddNumberParameter("Add Cost", "add", "Cost to add faces to mesh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Remove Cost", "remove", "Cost to remove start faces", GH_ParamAccess.item);
            pManager.AddCurveParameter("Distance Path", "dist", "Shortest path from start to end based on distance", GH_ParamAccess.item);
            pManager.AddCurveParameter("Volume Fraction Path", "vol", "Shortest path based on volume fraction", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d start_point = new Point3d();
            Point3d end_point = new Point3d();
            Rhino.Geometry.Mesh regular_mesh = new Rhino.Geometry.Mesh();
            Rhino.Geometry.Mesh trimesh = new Rhino.Geometry.Mesh();
            Rhino.Geometry.Mesh background = new Rhino.Geometry.Mesh();
            List<double> volume_fractions = new List<double>();
            double vol = 0.0;
            List<Point3d> start_points = new List<Point3d>();
            int level = 0;
            bool force_add = false;
            bool precise = true;

            DA.GetData(0, ref start_point);
            DA.GetData(1, ref end_point);
            DA.GetData(2, ref regular_mesh);
            DA.GetData(3, ref trimesh);
            DA.GetDataList(4, volume_fractions);
            DA.GetData(5, ref vol);
            DA.GetData(6, ref background);
            DA.GetData(7, ref level);
            DA.GetData(8, ref force_add);
            DA.GetData(9, ref precise);

            MeshFaceList regular_faces = regular_mesh.Faces;

            int start_face_idx = -1;
            int regular_mesh_start_face_idx = -1;
            int end_face_idx = -1;
            int regular_mesh_end_face_idx = -1;
            for (int i = 0; i < regular_faces.Count; i++)
            {
                Point3d centroid = AlephSupport.GetCentroid(regular_faces[i], regular_mesh);
                if (AlephSupport.PointsAlmostEqual(centroid, start_point, 0.001))
                    start_face_idx = i;
                else if (AlephSupport.PointsAlmostEqual(centroid, end_point, 0.001))
                    end_face_idx = i;
            }

            regular_mesh_start_face_idx = start_face_idx;
            regular_mesh_end_face_idx = end_face_idx;
            MeshFace start = regular_faces[start_face_idx];
            MeshFace end = regular_faces[end_face_idx];
            bool start_found = false;
            bool end_found = false;
            for (int i = 0; i < background.Faces.Count; i++)
            {
                if (AlephSupport.FacesAreEqual(start, background.Faces[i], regular_mesh, background))
                {
                    start_face_idx = i;
                    start_found = true;
                }
                else if (AlephSupport.FacesAreEqual(end, background.Faces[i], regular_mesh, background))
                {
                    end_face_idx = i;
                    end_found = true;
                }
                else if (end_found == true && start_found == true)
                    break;
            }

            Point3d start_centroid = AlephSupport.GetCentroid(start, regular_mesh);
            Point3d end_centroid = AlephSupport.GetCentroid(end, regular_mesh);
            start_centroid.Z = Math.Abs(volume_fractions[start_face_idx]);
            end_centroid.Z = Math.Abs(volume_fractions[end_face_idx]);
            Point3d start_vertex = trimesh.ClosestPoint(start_centroid);
            Point3d end_vertex = trimesh.ClosestPoint(end_centroid);

            bool start_vertex_is_vertex = false;
            bool end_vertex_is_vertex = false;
            int start_idx = -1;
            int end_idx = -1;
            MeshTopologyVertexList verts = trimesh.TopologyVertices;

            for (int i = 0; i < verts.Count; i++)
            {
                Point3d point = verts[i];
                if (AlephSupport.PointsAlmostEqual(start_vertex, point, 0.00001))
                {
                    start_vertex_is_vertex = true;
                    start_idx = i;
                    start_vertex = point;
                }
                else if (AlephSupport.PointsAlmostEqual(end_vertex, point, 0.00001))
                {
                    end_vertex_is_vertex = true;
                    end_idx = i;
                    end_vertex = point;
                }
                else if (end_vertex_is_vertex && start_vertex_is_vertex)
                    break;
            }

            // create nodes for use in dijkstra's algorithm 
            List<Node> nodes = new List<Node>();

            for (int i = 0; i < verts.Count; ++i)
            {
                Node temp_node = new Node(i);
                nodes.Add(temp_node);
            }

            foreach (Node node in nodes)
            {
                int n = node.getIndex();
                var connected_verts = verts.ConnectedTopologyVertices(n);
                foreach (int vert_idx in connected_verts)
                {
                    double cost = verts[vert_idx].DistanceTo(verts[n]);
                    // add the cost to travel from one node to another as the distance between nodes
                    if (cost > 0)
                        nodes[n].AddNeighbour(nodes[vert_idx], cost);
                }
            }

            // Create a "graph" to be used in dijkstra's algorithm 
            Graph trimesh_graph = new Graph();
            foreach (Node node in nodes)
                trimesh_graph.Add(node);

            // Use Dijkstra's algorithm to calculate the shortest route
            DistanceCalculator cal = new DistanceCalculator(trimesh_graph);
            List<int> route = cal.Calculate(nodes[start_idx], nodes[end_idx]);

            // Redo everything using cost based on nodes that are spaced x levels from this path
            List<int> verts_within_levels = new List<int>();
            Polyline distance_path = new Polyline();
            foreach (int idx in route)
            {
                distance_path.Add(verts[idx]);
                verts_within_levels.Add(idx);
            }

            /* 
            Breadth First Search
            */
            for (int i = 0; i < level; i++)
            {
                List<int> add_vertices = new List<int>();
                foreach (int vertex in verts_within_levels)
                {
                    var connected_verts = verts.ConnectedTopologyVertices(vertex);
                    foreach (int idx in connected_verts)
                    {
                        if (!verts_within_levels.Contains(idx) && !add_vertices.Contains(idx))
                        {
                            add_vertices.Add(idx);
                        }
                    }
                }

                foreach (int idx in add_vertices)
                    verts_within_levels.Add(idx);
            }

            int index = 0;
            Dictionary<int, int> vert_idx_to_node_idx = new Dictionary<int, int>();
            nodes.Clear();
            foreach (int idx in verts_within_levels)
            {
                Node temp_node = new Node(idx);
                nodes.Add(temp_node);
                vert_idx_to_node_idx.Add(idx, index);
                index++;
            }

            foreach (Node node in nodes)
            {
                int n = node.getIndex();
                var connected_verts = verts.ConnectedTopologyVertices(n); 
                n = vert_idx_to_node_idx[n];
                foreach (int vert_idx in connected_verts)
                {
                    if (vert_idx_to_node_idx.ContainsKey(vert_idx))
                    {
                        double x = 1 - Math.Abs(vol);
                        Point3d point = verts[vert_idx];
                        double cost = verts[vert_idx].Z - Math.Abs(vol);
                        int node_idx = vert_idx_to_node_idx[vert_idx];
                        // add the cost to travel to a non existing face as:
                        // current volume fraction - face volume fraction
                        if (cost > 0)
                        {
                            nodes[n].AddNeighbour(nodes[node_idx], cost);
                            //nodes[n].AddEdgeWeights(node_idx, cost);
                        }
                        // if the face is already in the mesh at the current volume fraction
                        // add the cost to travel along there as zero
                        else
                        {
                            nodes[n].AddNeighbour(nodes[node_idx], 0);
                            //nodes[n].AddEdgeWeights(node_idx, 0);
                        }
                        // also store the overall height, used to calculate cost to add faces later
                        nodes[n].AddEdgeWeights(node_idx, verts[vert_idx].Z);
                    }
                }
            }

            // Create graph to be used for dijkstra's algorithm
            Graph vol_graph = new Graph();
            foreach (Node node in nodes)
                vol_graph.Add(node);

            // Use Dijkstra's algorithm to calculate the shortest route
            DistanceCalculator calculated = new DistanceCalculator(vol_graph);
            List<int> vol_route = calculated.Calculate(nodes[vert_idx_to_node_idx[start_idx]], nodes[vert_idx_to_node_idx[end_idx]]);

            Polyline vol_path = new Polyline();
            List<Point3d> centroids = new List<Point3d>();
            foreach (int idx in vol_route)
            {
                vol_path.Add(verts[idx]);
                Point3d point = AlephSupport.RoundPoint(verts[idx], 4);
                point.Z = 0;
                centroids.Add(point);
            }

            List<int> path_faces = new List<int>();
            foreach(Point3d point in centroids)
            {
                // For just the centroids
                // More precise, but is prone to jaggies
                if (precise)
                {
                    for (int i = 0; i < background.Faces.Count; i++)
                    {
                        Point3d centroid = AlephSupport.GetCentroid(background.Faces[i], background);
                        if (AlephSupport.PointsAlmostEqual(point, centroid, 0.01))
                        {
                            path_faces.Add(i);
                            break;
                        }
                    }
                }
                // This works, but is not precise
                // it will add some faces that are not necessarily the lowest cost, but avoids jaggies
                else
                {
                    MeshPoint mesh_point = background.ClosestMeshPoint(point, 1);
                    path_faces.Add(mesh_point.FaceIndex);
                }
            }

            List<double> adding_weight_list = new List<double>();
            List<double> adding_cost_list = new List<double>();
            List<double> removing_cost_list = new List<double>();
            for (int i = 0; i < vol_route.Count - 1; i++)
            {
                start_idx = vol_route[i];
                end_idx = vol_route[i + 1];
                Dictionary<int, double> edge_weights = nodes[vert_idx_to_node_idx[start_idx]].getEdgeWeights();
                foreach (var edge_weight in edge_weights)
                {
                    if (edge_weight.Key == vert_idx_to_node_idx[end_idx])
                    {
                        //adding_weight_list.Add(edge_weight.Value);
                        if (edge_weight.Value > Math.Abs(vol))
                        {
                            adding_cost_list.Add(edge_weight.Value);
                        }
                    }
                }
            }

            double adding_cost = 0;
            if (adding_cost_list.Count != 0)
            {
                adding_cost = adding_cost_list.Max();
                adding_cost = adding_cost - Math.Abs(vol);
            }
            else
                adding_cost = 0;

            //adding_cost = adding_weight_list.Max();

            // Loop through each face connected to the start and end face
            // Check the cost to remove all of those connected faces
            Dictionary<int, MeshFace> connected_faces_start = new Dictionary<int, MeshFace>();
            Dictionary<int, MeshFace> connected_faces_end = new Dictionary<int, MeshFace>();
            connected_faces_start.Add(regular_mesh_start_face_idx, regular_faces[regular_mesh_start_face_idx]);
            connected_faces_end.Add(regular_mesh_end_face_idx, regular_faces[regular_mesh_end_face_idx]);

            List<int> start_list = new List<int>();
            List<int> end_list = new List<int>();

            int[] connected_face_idxs = regular_faces.GetConnectedFaces(regular_mesh_start_face_idx);
            foreach (var idx in connected_face_idxs)
            {
                if (!connected_faces_start.ContainsKey(idx))
                {
                    start_list.Add(idx);
                }
            }

            connected_face_idxs = regular_faces.GetConnectedFaces(regular_mesh_end_face_idx);
            foreach (var idx in connected_face_idxs)
            {
                if (!connected_faces_end.ContainsKey(idx))
                {
                    end_list.Add(idx);
                }
            }

            foreach (int idx in start_list)
                connected_faces_start.Add(idx, regular_faces[idx]);

            foreach (int idx in end_list)
                connected_faces_end.Add(idx, regular_faces[idx]);

            // Connected_faces only contains those that were manifold to the origin face
            // This will loop through and get all of the nonmanifold faces that are also connected
            bool keep_going = true;
            while (keep_going)
            {
                int last_connected_start_face = connected_faces_start.Keys.Last();
                int last_connected_end_face = connected_faces_end.Keys.Last();
                var regular_vertices = regular_mesh.TopologyVertices;
                List<int> nonmanifold_faces_start = new List<int>();
                List<int> nonmanifold_faces_end = new List<int>();
                foreach (MeshFace face in connected_faces_start.Values)
                {
                    List<int> face_vert_idxs = new List<int>();
                    face_vert_idxs.Add(face.A);
                    face_vert_idxs.Add(face.B);
                    face_vert_idxs.Add(face.C);
                    face_vert_idxs.Add(face.D);
                    foreach (int idx in face_vert_idxs)
                    {
                        if (regular_vertices.ConnectedTopologyVertices(idx).Count() == 4)
                        {
                            var faces = regular_vertices.ConnectedFaces(idx);
                            foreach (int new_face in faces)
                            {
                                if (!connected_faces_start.ContainsKey(new_face))
                                    nonmanifold_faces_start.Add(new_face);
                            }
                        }
                    }
                }

                foreach (MeshFace face in connected_faces_end.Values)
                {
                    List<int> face_vert_idxs = new List<int>();
                    face_vert_idxs.Add(face.A);
                    face_vert_idxs.Add(face.B);
                    face_vert_idxs.Add(face.C);
                    face_vert_idxs.Add(face.D);
                    foreach (int idx in face_vert_idxs)
                    {
                        if (regular_vertices.ConnectedTopologyVertices(idx).Count() == 4)
                        {
                            var faces = regular_vertices.ConnectedFaces(idx);
                            foreach (int new_face in faces)
                            {
                                if (!connected_faces_end.ContainsKey(new_face))
                                    nonmanifold_faces_end.Add(new_face);
                            }
                        }
                    }
                }

                foreach (int idx in nonmanifold_faces_start)
                {
                    connected_faces_start.Add(idx, regular_faces[idx]);
                    connected_face_idxs = regular_faces.GetConnectedFaces(idx);
                    foreach (int connected_idx in connected_face_idxs)
                    {
                        if (!connected_faces_start.ContainsKey(connected_idx))
                            connected_faces_start.Add(connected_idx, regular_faces[connected_idx]);
                    }
                }

                foreach (int idx in nonmanifold_faces_end)
                {
                    connected_faces_end.Add(idx, regular_faces[idx]);
                    connected_face_idxs = regular_faces.GetConnectedFaces(idx);
                    foreach (int connected_idx in connected_face_idxs)
                    {
                        if (!connected_faces_end.ContainsKey(connected_idx))
                            connected_faces_end.Add(connected_idx, regular_faces[connected_idx]);
                    }
                }

                if (last_connected_end_face == connected_faces_end.Keys.Last() && last_connected_start_face == connected_faces_start.Keys.Last())
                    keep_going = false;
            }

            // Loop through to check cost to remove the faces
            List<double> vol_fracs_start = new List<double>();
            List<double> vol_fracs_end = new List<double>();
            foreach (MeshFace start_face in connected_faces_start.Values)
            {
                for (int i = 0; i < background.Faces.Count; i++)
                {
                    if (AlephSupport.FacesAreEqual(background.Faces[i], start_face, background, regular_mesh))
                    {
                        vol_fracs_start.Add(volume_fractions[i]);
                        break;
                    }
                }
            }

            foreach (MeshFace end_face in connected_faces_end.Values)
            {
                for (int i = 0; i < background.Faces.Count; i++)
                {
                    if (AlephSupport.FacesAreEqual(background.Faces[i], end_face, background, regular_mesh))
                    {
                        vol_fracs_end.Add(volume_fractions[i]);
                        break;
                    }
                }
            }

            List<double> min_vol_fracs = new List<double>();
            min_vol_fracs.Add(vol_fracs_start.Max());
            min_vol_fracs.Add(vol_fracs_end.Max());
            double removing_cost = min_vol_fracs.Min() - vol;

            double minimal_cost = -1;
            string add_remove = string.Empty;
            Rhino.Geometry.Mesh sculpted_mesh = new Rhino.Geometry.Mesh();
            if(adding_cost <= removing_cost || force_add)
            {
                minimal_cost = adding_cost;
                int num_faces = background.Faces.Count;
                sculpted_mesh = background.DuplicateMesh();
                for (int i = num_faces - 1; i >= 0; --i)
                {
                    if (volume_fractions[i] < vol && !path_faces.Contains(i))
                        sculpted_mesh.Faces.RemoveAt(i, true);
                }
            }
            else if(adding_cost > removing_cost)
            {
                minimal_cost = removing_cost;
                if (min_vol_fracs[0] > min_vol_fracs[1])
                    add_remove = "remove end";
                else
                {
                    add_remove = "remove start";
                    // add code to remove the faces here
                    int num_faces = regular_mesh.Faces.Count;
                    sculpted_mesh = regular_mesh.DuplicateMesh();
                    for (int i = num_faces - 1; i >= 0; --i)
                    {
                        if (connected_faces_start.ContainsKey(i))
                            sculpted_mesh.Faces.RemoveAt(i, true);
                    }
                }
            }

            DA.SetData(0, sculpted_mesh);
            DA.SetData(1, adding_cost);
            DA.SetData(2, removing_cost);
            DA.SetData(3, distance_path);
            DA.SetData(4, vol_path);
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
            get { return new Guid("59FAA4C0-FB7A-46CB-800E-82877B4B9432"); }
        }
    }
}