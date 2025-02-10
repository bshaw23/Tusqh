using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using EigenWrapper.Eigen;
using Eto.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Grasshopper.Rhinoceros.Annotations;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Rhino.Geometry.MeshRefinements;
using Rhino.Input.Custom;
using Rhino.Render;
using Rhino.UI;
using Sculpt2D.Sculpt3D;
using Sculpt2D.Sculpt3D.Collections;

namespace Sculpt2D
{
    public static partial class AlephSupport
    {
        public static void ExportToOBJ(string path, string name, Rhino.Geometry.Mesh mesh)
        {
            // initialize output file
            string directory = path + name;
            System.IO.Directory.CreateDirectory(directory);
            FileStream fs = new FileStream(directory + "/" + name + ".obj", FileMode.Create);
            StreamWriter sw = new StreamWriter(fs);

            // access mesh vertices and faces
            MeshVertexList vertices = mesh.Vertices;
            MeshFaceList faces = mesh.Faces;

            // write vertices into *.obj file
            WriteVerticesOBJ(vertices, sw);

            // write faces into *.obj file
            WriteFacesOBJ(faces, sw);

            sw.Close();
            fs.Close();
        }

        public static void ExportToPLY(Rhino.Geometry.Mesh mesh, string name, string path)
        {
            // initialize output file
            string directory = path + name;
            System.IO.Directory.CreateDirectory(directory);
            FileStream fs = new FileStream(directory + "/" + name + ".ply", FileMode.Create);
            StreamWriter sw = new StreamWriter(fs);

            // access mesh vertices and faces
            MeshVertexList vertices = mesh.Vertices;
            MeshFaceList faces = mesh.Faces;

            // write header
            string end = "\n";
            sw.Write("ply" + end);
            sw.Write("format ascii 1.0" + end);
            sw.Write("element vertex " + vertices.Count.ToString() + end);
            sw.Write("property float x" + end + "property float y" + end + "property float z" + end);
            sw.Write("element face " + faces.Count.ToString() + end);
            sw.Write("property list uchar int vertex_index" + end);
            sw.Write("end_header" + end);

            // write vertices into .ply file
            WriteVerticesPLY(vertices, sw);

            //write faces into .ply file
            WriteFacesPLY(faces, sw);

            sw.Close();
            fs.Close();
        }

        public static void WriteVerticesPLY(MeshVertexList vertices, StreamWriter sw)
        {
            string s = " ";
            string end = "\n";
            for(int i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                string x = Math.Round(vertex.X, 5).ToString();
                string y = Math.Round(vertex.Y, 5).ToString();
                string z = Math.Round(vertex.Z, 5).ToString();

                sw.Write(x + s + y + s + z + end);
            }
        }

        public static void WriteFacesPLY(MeshFaceList faces, StreamWriter sw)
        {
            string s = " ";
            string end = "\n";
            for (int i = 0;i < faces.Count; i++)
            {
                var face = faces[i];
                string A = face.A.ToString();
                string B = face.B.ToString();
                string C = face.C.ToString();
                string D = face.D.ToString();

                if (C == D)
                    sw.Write("3" + s + A + s + B + s + C + end);
                else
                    sw.Write("4" + s + A + s + B + s + C + s + D + end);
            }
        }

        public static void WriteVerticesOBJ(MeshVertexList vertices, StreamWriter sw)
        {
            Dictionary<Point3d, int> point2idx = new Dictionary<Point3d, int>();
            for (int i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                string x = Math.Round(vertex.X, 3).ToString();
                string y = Math.Round(vertex.Y, 3).ToString();
                string z = Math.Round(vertex.Z, 3).ToString();

                sw.Write("v " + x.PadRight(9, '0') + " " + y.PadRight(9, '0') + " " + z + "\n");
            }
        }

        public static void WriteFacesOBJ(MeshFaceList faces, StreamWriter sw)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                int A = face.A + 1;
                int B = face.B + 1;
                int C = face.C + 1;
                int D = face.D + 1;

                sw.Write("f " + A.ToString() + " " + B.ToString() + " " +
                    C.ToString() + " " + D.ToString() + "\n");
            }
        }

        public static Matrix BackgroundGridPts(Mesh grid, int pts_in_x, int pts_in_y, out List<Point3d> centroid, out int pts_per_face, out ReadOnlySpan<double> point_grids_rospan)
        {
            int A;
            int B;
            int C;
            int D;
            Point3d point_a = new Point3d();
            Point3d point_b = new Point3d();
            Point3d point_c = new Point3d();
            Point3d point_d = new Point3d();

            double x_dist;
            double y_dist;
            var x_segments = 0.0;
            var y_segments = 0.0;

            //List<Point3d> point_grid = new List<Point3d>();
            centroid = new List<Point3d>();
            List<double> pts_x = new List<double>();
            List<double> pts_y = new List<double>(); 
            double x;
            double y;

            foreach (var face in grid.Faces)
            {
                A = face.A;
                B = face.B;
                C = face.C;
                D = face.D;

                point_a = grid.Vertices.Point3dAt(A);
                point_b = grid.Vertices.Point3dAt(B);
                point_c = grid.Vertices.Point3dAt(C);
                point_d = grid.Vertices.Point3dAt(D);

                centroid.Add(new Point3d((point_a + point_b + point_c + point_d) / 4));

                x_dist = point_b.X - point_a.X;
                y_dist = point_d.Y = point_a.Y;

                x_segments = x_dist / pts_in_x;
                y_segments = y_dist / pts_in_y;

                for (int i = 1; i <= pts_in_x; i++)
                {
                    x = 0.0;
                    y = 0.0;
                    for (int j = 1; j <= pts_in_y; j++)
                    {
                        x = point_a.X + (x_segments / 2) + (x_segments * i);
                        y = point_a.Y + (y_segments / 2) + (y_segments * j);

                        pts_x.Add(x);
                        pts_y.Add(y);
                    }
                }
            }

            double[] points = new double[pts_x.Count * 2];
            Matrix point_grid = new Matrix(pts_x.Count, 2);
            Span<double> point_grids_span = new Span<double>(points);

            for (int i = 0; i < pts_x.Count; i++)
            {
                point_grid[i, 0] = pts_x[i];
                point_grid[i, 1] = pts_y[i];

                point_grids_span[i * 2] = pts_x[i];
                point_grids_span[i * 2 + 1] = pts_y[i];
            }

            point_grids_rospan = point_grids_span;

            pts_per_face = pts_in_x * pts_in_y;

            return point_grid;
        }

        public static Matrix EdgeListToBoundaries(Mesh mesh, out ReadOnlySpan<int> edges_rospan)
        {
            MeshTopologyVertexList vertex_list = mesh.TopologyVertices;
            MeshTopologyEdgeList edge_list = mesh.TopologyEdges;
            Rhino.IndexPair edge_verts = new Rhino.IndexPair();
            List<IndexPair> boundary_verts = new List<IndexPair>();
            Dictionary<int, bool> edge_is_exterior = new Dictionary<int, bool>();
            int v1 = 0, v2 = 0;
            int edge_idx = 0;
            
            for(int i = 0; i < edge_list.Count; i++)
            {
                if (edge_list.GetConnectedFaces(i).Length == 1)
                {
                    edge_verts = edge_list.GetTopologyVertices(i);
                    boundary_verts.Add(edge_verts);
                }
            }


            Matrix boundaries = new Matrix(boundary_verts.Count, 2);
            for(int i = 0; i < boundary_verts.Count; i++)
            {
                bool is_exterior = false;
                v1 = boundary_verts[i].I;
                v2 = boundary_verts[i].J;

                boundaries[i, 0] = v1;
                boundaries[i, 1] = v2;

                edge_idx = edge_list.GetEdgeIndex(v1, v2);

                if (edge_list.IsNgonInterior(edge_idx) == false)
                    is_exterior = true;

                edge_is_exterior.Add(edge_idx, is_exterior);
            }

            int[] edges = new int[(boundaries.RowCount) * 2];
            Span<int>edges_span = new Span<int>(edges);

            for (int i = 0; i < boundaries.RowCount; i++)
            {
                edges_span[i * 2] = (int)boundaries[i, 0];
                edges_span[i * 2 + 1] = (int)boundaries[i, 1];
            }

            edges_rospan = edges_span;

            return boundaries;
        }

        public static bool PointsAlmostEqual(Point3d point1, Point3d point2, double  tolerance)
        {
            double x_dif = Math.Abs(point2.X - point1.X);
            double y_dif = Math.Abs(point2.Y - point1.Y);
            double z_dif = Math.Abs(point2.Z - point1.Z);
            bool almost_equal = false;

            if(x_dif < tolerance && y_dif < tolerance && z_dif < tolerance)
                almost_equal = true;

            return almost_equal;
        }

        public static bool DoublesAlmostEqual(double x, double y, double tolerance)
        {
            double dif = Math.Abs(y - x);
            bool almost_equal = false;
            if (dif < tolerance)
                almost_equal = true;

            return almost_equal;
        }

        public static Point3d GetCentroid(MeshFace quad_face, Mesh quad_mesh, bool quad = true)
        {
            Point3d centroid = new Point3d();
            if (quad)
            {
                int A = quad_face.A;
                int B = quad_face.B;
                int C = quad_face.C;
                int D = quad_face.D;

                Point3d point_a = quad_mesh.Vertices[A];
                Point3d point_b = quad_mesh.Vertices[B];
                Point3d point_c = quad_mesh.Vertices[C];
                Point3d point_d = quad_mesh.Vertices[D];

                centroid = (point_a + point_b + point_c + point_d) / 4;
            }
            else
            {
                int A = quad_face.A;
                int B = quad_face.B;
                int C = quad_face.C;

                Point3d point_a = quad_mesh.Vertices[A];
                Point3d point_b = quad_mesh.Vertices[B];
                Point3d point_c = quad_mesh.Vertices[C];

                centroid = (point_a + point_b + point_c) / 3;
            }

            return centroid;
        }

        public static bool FacesAreEqual(MeshFace face1, MeshFace face2, Mesh mesh1, Mesh mesh2)
        {
            bool are_equal = false;
            
            int A1 = face1.A;
            int A2 = face2.A;
            int B1 = face1.B;
            int B2 = face2.B;
            int C1 = face1.C;
            int C2 = face2.C;
            int D1 = face1.D;
            int D2 = face2.D;

            Point3d a1 = mesh1.Vertices[A1];
            Point3d a2 = mesh2.Vertices[A2];
            Point3d b1 = mesh1.Vertices[B1];
            Point3d b2 = mesh2.Vertices[B2];
            Point3d c1 = mesh1.Vertices[C1];
            Point3d c2 = mesh2.Vertices[C2];
            Point3d d1 = mesh1.Vertices[D1];
            Point3d d2 = mesh2.Vertices[D2];

            if ((PointsAlmostEqual(a1,a2,0.001) && PointsAlmostEqual(b1, b2, 0.001)) && (PointsAlmostEqual(c1, c2, 0.001) && PointsAlmostEqual(d1, d2, 0.001)))
                are_equal = true;


            return are_equal;
        }

        public static Point3d RoundPoint(Point3d point, int decimals)
        {
            double x = point.X;
            double y = point.Y; 
            double z = point.Z;

            x = Math.Round(x, decimals);
            y = Math.Round(y, decimals);
            z = Math.Round(z, decimals);

            Point3d new_point = new Point3d(x, y, z);

            return new_point;
        }

        public static void ProcessPolylines(List<Curve> ori_curves, bool reverse_boundary_orient, 
            out List<Tuple<double, double>> vert_array, out List<Tuple<uint, uint>> edge_array)
        {
            vert_array = new List<Tuple<double, double>>();
            edge_array = new List<Tuple<uint, uint>>();
            uint vertex_count = 0;
            foreach (var polylineiter in ori_curves)
            {
                // potentially reverse the orientation of the mesh boundary
                // if the inverse mesh is desired
                if (!polylineiter.IsPolyline())
                    throw new Exception("Input must be polylines for now");
                var polylinecurve = polylineiter.ToNurbsCurve();
                var polyline = polylinecurve.Points;

                var polyline_pts = polyline.Select(p => p);
                if (reverse_boundary_orient)
                    polyline_pts = polyline.Reverse();
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
        }

        public static void GetQuerryPoints(Mesh grid, int x_pts, int y_pts, 
            out List<Point3d> centroids, out List<Point3d> sample_points, out List<Tuple<double, double>> querry_pts)
        {
            List<Point3d> point_grid = new List<Point3d>();
            List<Point3d> pt_grid = new List<Point3d>();
            centroids = new List<Point3d>();
            sample_points = new List<Point3d>();

            // points to querry in the background mesh
            querry_pts = new List<Tuple<double, double>>(grid.Faces.Count * x_pts * y_pts);
            foreach (MeshFace face in grid.Faces)
            {
                // indeces for corners of face starting at bottom left and goin counter clockwise
                int A = face.A;
                int B = face.B;
                int C = face.C;
                int D = face.D;

                Point3d point_a = grid.Vertices.Point3dAt(A);
                Point3d point_b = grid.Vertices.Point3dAt(B);
                Point3d point_c = grid.Vertices.Point3dAt(C);
                Point3d point_d = grid.Vertices.Point3dAt(D);

                centroids.Add(new Point3d((point_a + point_b + point_c + point_d) / 4));

                double u_dist = point_b.X - point_a.X;
                double v_dist = point_d.Y - point_a.Y;

                double u_segments = u_dist / (double)x_pts;
                double v_segments = v_dist / (double)y_pts;

                point_grid.Clear();

                for (int i = 0; i < x_pts; i++)
                {
                    double x = 0.0;
                    double y = 0.0;
                    double z = 0.0;

                    for (int j = 0; j < y_pts; j++)
                    {
                        x = point_a.X + u_segments / 2 + u_segments * i;
                        y = point_a.Y + v_segments / 2 + v_segments * j;
                        z = point_a.Z;

                        querry_pts.Add(new Tuple<double, double>(x, y));
                        pt_grid.Add(new Point3d(x, y, 0));
                    }
                }

                sample_points = new List<Point3d>(pt_grid);
            }


            
        }

        public static void ColumnMajorConstruction(List<Tuple<double, double>> vert_array, 
            List<Tuple<uint, uint>> edge_array, List<Tuple<double, double>> querry_pts, 
            out List<double> vert_list, out List<int> edge_list, out List<double> querry_list)
        {
            // reindex to put into LibIGL---this code shouldn't ever change
            vert_list = new List<double>();
            edge_list = new List<int>();
            querry_list = new List<double>(2 * querry_pts.Count);

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

        public static void ProcessMesh(Mesh surface_mesh, out List<Tuple<double, double, double>> vert_array, 
            out List<Tuple<int, int, int>> triangle_array)
        {
            vert_array = new List<Tuple<double, double, double>>();
            foreach (var vert in surface_mesh.Vertices)
                vert_array.Add(new Tuple<double, double, double>(vert.X, vert.Y, vert.Z));
            
            triangle_array = new List<Tuple<int, int, int>>();
            foreach (var face in surface_mesh.Faces)
                triangle_array.Add(new Tuple<int, int, int>(face.A, face.B, face.C));
        }

        public static void ColumnMajorConstruction(List<Tuple<double, double, double>> vert_array,
            List<Tuple<int, int, int>> triangle_array, List<Tuple<double, double, double>> querry_pts,
            out List<double> vert_list, out List<int> triangle_list, out List<double> querry_list)
        {
            vert_list = new List<double>();
            triangle_list = new List<int>();
            querry_list = new List<double>();

            foreach (var item in vert_array)
            {
                vert_list.Add(item.Item1);
            }
            foreach (var item in vert_array)
            {
                vert_list.Add(item.Item2);
            }
            foreach (var item in vert_array)
            {
                vert_list.Add(item.Item3);
            }

            foreach (var item in triangle_array)
            {
                triangle_list.Add(item.Item1);
            }
            foreach (var item in triangle_array)
            {
                triangle_list.Add(item.Item2);
            }
            foreach (var item in triangle_array)
            {
                triangle_list.Add(item.Item3);
            }

            foreach (var item in querry_pts)
            {
                querry_list.Add(item.Item1);
            }
            foreach (var item in querry_pts)
            {
                querry_list.Add(item.Item2);
            }
            foreach (var item in querry_pts)
            {
                querry_list.Add(item.Item3);
            }
        }
    }

    namespace Dijkstra
    {
        //class Program
        //{
        //    static void Main(string[] args)
        //    {
        //        Graph Cities = new Graph();

        //        Node NewYork = new Node("New York");
        //        Node Miami = new Node("Miami");
        //        Node Chicago = new Node("Chicago");
        //        Node Dallas = new Node("Dallas");
        //        Node Denver = new Node("Denver");
        //        Node SanFrancisco = new Node("San Francisco");
        //        Node LA = new Node("Los Angeles");
        //        Node SanDiego = new Node("San Diego");

        //        Cities.Add(NewYork);
        //        Cities.Add(Miami);
        //        Cities.Add(Chicago);
        //        Cities.Add(Dallas);
        //        Cities.Add(Denver);
        //        Cities.Add(SanFrancisco);
        //        Cities.Add(LA);
        //        Cities.Add(SanDiego);

        //        NewYork.AddNeighbour(Chicago, 75);
        //        NewYork.AddNeighbour(Miami, 90);
        //        NewYork.AddNeighbour(Dallas, 125);
        //        NewYork.AddNeighbour(Denver, 100);

        //        Miami.AddNeighbour(Dallas, 50);

        //        Dallas.AddNeighbour(SanDiego, 90);
        //        Dallas.AddNeighbour(LA, 80);

        //        SanDiego.AddNeighbour(LA, 45);

        //        Chicago.AddNeighbour(SanFrancisco, 25);
        //        Chicago.AddNeighbour(Denver, 20);

        //        SanFrancisco.AddNeighbour(LA, 45);

        //        Denver.AddNeighbour(SanFrancisco, 75);
        //        Denver.AddNeighbour(LA, 100);

        //        DistanceCalculator c = new DistanceCalculator(Cities);
        //        c.Calculate(NewYork, LA);
        //    }


        //}

        class DistanceCalculator
        {
            Dictionary<int, double> Distances;
            Dictionary<Node, Node> Routes;
            Graph graph;
            List<Node> AllNodes;
            List<int> OptimalRoute;
            int leg_counter = 0;

            public DistanceCalculator(Graph g)
            {
                this.graph = g;
                this.AllNodes = g.GetNodes();
                Distances = SetDistances();
                Routes = SetRoutes();
                OptimalRoute = SetOptimalRoute();
            }

            public List<int> Calculate(Node Source, Node Destination)
            {
                Distances[Source.getIndex()] = 0;

                while (AllNodes.ToList().Count != 0)
                {
                    Node LeastExpensiveNode = getLeastExpensiveNode();
                    ExamineConnections(LeastExpensiveNode);
                    AllNodes.Remove(LeastExpensiveNode);
                }
                Print(Source, Destination);
                return OptimalRoute;
            }

            private void Print(Node Source, Node Destination)
            {
                //Console.WriteLine(string.Format("The least possible cost for flying from {0} to {1} is: {2} $", Source.getIndex(), Destination.getIndex(), Distances[Destination]));
                OptimalRoute[leg_counter] = Destination.getIndex();
                PrintLeg(Destination);
                OptimalRoute.RemoveRange(leg_counter, OptimalRoute.Count - leg_counter);
                //Console.ReadKey();
            }

            private void PrintLeg(Node d)
            {
                leg_counter++;
                if (Routes[d] == null)
                    return;
                //Console.WriteLine(string.Format("{0} <-- {1}", d.getIndex(), Routes[d].getIndex()));
                OptimalRoute[leg_counter] = Routes[d].getIndex();
                PrintLeg(Routes[d]);
            }

            // Or maybe I should just change stuff in here...
            private void ExamineConnections(Node n)
            {
                foreach (var neighbor in n.getNeighbors())
                {
                    // I could add more conditions that take into account the sign of the distace
                    // later I could return two lists of vertices to look at removing or adding as well as the cost
                    double x = Distances[n.getIndex()];
                    double y = neighbor.Value;
                    double z = Distances[neighbor.Key.getIndex()];
                    if (Distances[n.getIndex()] + neighbor.Value < Distances[neighbor.Key.getIndex()])
                    {
                        Distances[neighbor.Key.getIndex()] = neighbor.Value + Distances[n.getIndex()];
                        Routes[neighbor.Key] = n;
                    }
                    //if (Distances[n] > 0)
                    //{
                    //    if (Math.Abs(Distances[n]) + Math.Abs(neighbor.Value) < Math.Abs(Distances[neighbor.Key]))
                    //    {
                    //        Distances[neighbor.Key] = neighbor.Value + Distances[n];
                    //        RemovingRoutes[neighbor.Key] = n;
                    //    }
                    //}
                    //else if (Distances[n] < 0)
                    //{
                    //    if (Math.Abs(Distances[n]) + Math.Abs(neighbor.Value) < Math.Abs(Distances[neighbor.Key]))
                    //    {
                    //        Distances[neighbor.Key] = neighbor.Value + Distances[n];
                    //        AddingRoutes[neighbor.Key] = n;
                    //    }
                    //}
                    //else
                    //{
                    //    if (Math.Abs(Distances[n]) + Math.Abs(neighbor.Value) < Math.Abs(Distances[neighbor.Key]))
                    //    {
                    //        Distances[neighbor.Key] = neighbor.Value + Distances[n];
                    //        RemovingRoutes[neighbor.Key] = n;
                    //        AddingRoutes[neighbor.Key] = n;
                    //    }
                    //}
                }
            }

            private Node getLeastExpensiveNode()
            {
                Node LeastExpensive = AllNodes.FirstOrDefault();

                foreach (var n in AllNodes)
                {
                    if (Distances[n.getIndex()] < Distances[LeastExpensive.getIndex()])
                        LeastExpensive = n;
                }

                return LeastExpensive;
            }

            //private Dictionary<Node, double> SetDistances()
            //{
            //    Dictionary<Node, double> Distances = new Dictionary<Node, double>();

            //    foreach (Node n in graph.GetNodes())
            //    {
            //        Distances.Add(n, double.MaxValue);
            //    }
            //    return Distances;
            //}

            private Dictionary<int, double> SetDistances()
            {
                Dictionary<int, double> Distances = new Dictionary<int, double>();

                foreach (Node n in graph.GetNodes())
                {
                    Distances.Add(n.getIndex(), double.MaxValue);
                }
                return Distances;
            }

            private Dictionary<Node, Node> SetRoutes()
            {
                Dictionary<Node, Node> Routes = new Dictionary<Node, Node>();

                foreach (Node n in graph.GetNodes())
                {
                    Routes.Add(n, null);
                }
                return Routes;
            }

            private List<int> SetOptimalRoute()
            {
                List<int> OptimalRoute = new List<int>();

                for(int i = 0; i < graph.GetNodes().Count; i++)
                {
                    OptimalRoute.Add(-1);
                }
                return OptimalRoute;
            }
        }

        // Changed "Node" class to use an index instead of Names
        class Node
        {
            private int Index;
            private Dictionary<Node, double> Neighbors;
            private Dictionary<int, double> EdgeWeights;

            public Node(int NodeIndex)
            {
                this.Index = NodeIndex;
                Neighbors = new Dictionary<Node, double>();
                EdgeWeights = new Dictionary<int, double>();
            }

            public void AddNeighbour(Node n, double cost)
            {
                Neighbors.Add(n, cost);
            }

            public void AddEdgeWeights(int n, double weight)
            {
                EdgeWeights.Add(n, weight);
            }
            public int getIndex()
            {
                return Index;
            }

            public Dictionary<Node, double> getNeighbors()
            {
                return Neighbors;
            }

            public Dictionary<int, double> getEdgeWeights()
            {
                return EdgeWeights;
            }
        }

        class Graph
        {
            private List<Node> Nodes;

            public Graph()
            {
                Nodes = new List<Node>();
            }

            public void Add(Node n)
            {
                Nodes.Add(n);
            }

            public void Remove(Node n)
            {
                Nodes.Remove(n);
            }

            public List<Node> GetNodes()
            {
                return Nodes.ToList();
            }

            public int getCount()
            {
                return Nodes.Count;
            }
        }
    }

    namespace Sculpt3D
    {
        public class Vertex
        {
            private int Index;
            private Point3d Location;
            private double VolumeFraction;
            private List<int> ConnectedFaces;
            private List<int> ConnectedEdges; // Indicies of connected edges (I should just do connected vertices. It would be faster, but I already did it this way. Maybe I'll change it later)
            private List<int> ConnectedVertices;

            public Vertex(int Index, Point3d Location)
            {
                this.Index = Index;
                this.Location = Location;
                VolumeFraction = new double();
                ConnectedEdges = new List<int>();
                ConnectedFaces = new List<int>();
                ConnectedVertices = new List<int>();
            }

            public void SetVolumeFraction(double VolumeFraction)
            {
                this.VolumeFraction = VolumeFraction;
            }

            public void AddConnectedFace(int face_idx)
            {
                List<int> connected_faces = this.ConnectedFaces;
                if (!connected_faces.Contains(face_idx))
                    ConnectedFaces.Add(face_idx);
            }

            public void AddConnectedEdge(int edge_idx)
            {
                ConnectedEdges.Add(edge_idx);
            }

            public void AddConnectedVertex(int vertex_idx)
            {
                if(!ConnectedVertices.Contains(vertex_idx))
                    ConnectedVertices.Add(vertex_idx);
            }

            public int getIndex()
            {
                return Index;
            }

            public Point3d getLocation()
            {
                return Location;
            }

            public double getVolumeFraction()
            {
                return VolumeFraction;
            }

            public List<int> getConnectedEdges()
            {
                return ConnectedEdges;
            }

            public List<int> getConnectedVertices()
            {
                return ConnectedVertices;
            }

            public void ClearConnectedVertices()
            {
                ConnectedVertices.Clear();
            }

            public List<int> getConnectedFaces()
            {
                return ConnectedFaces;
            }

            public double DistanceTo(Vertex vertex)
            {
                Point3d vert1 = this.getLocation();
                Point3d vert2 = vertex.getLocation();

                double dist = vert1.DistanceTo(vert2);

                return dist;
            }
        }

        public class Edge
        {
            private int Index;
            private int Start;
            private int End;

            public Edge(int Index)
            {
                this.Index = Index;
                Start = new int();
                End = new int();
            }

            public Edge(int Index, int Start, int End)
            {
                this.Index = Index; // Edge index
                this.Start = Start; // Start point index in vertex list
                this.End = End;     // End point index in vertex list
            }

            public void AddStart(int start_idx)
            {
                Start = start_idx;
            }

            public void AddEnd(int end_idx)
            {
                End = end_idx;
            }

            public int getIndex()
            {
                return Index;
            }

            public int getStart()
            {
                return Start;
            }

            public int getEnd()
            {
                return End;
            }

            // dot product between this and the input edge
            public double dot(Edge edge, List<Vertex> vertex_list)
            {
                Point3d e1_start = vertex_list[this.Start].getLocation();
                Point3d e1_end = vertex_list[this.End].getLocation();
                Point3d e2_start = vertex_list[edge.Start].getLocation();
                Point3d e2_end = vertex_list[edge.End].getLocation();

                double x1 = e1_start.X - e1_end.X;
                double y1 = e1_start.Y - e1_end.Y;
                double z1 = e1_start.Z - e1_end.Z;

                double x2 = e2_start.X - e2_end.X;
                double y2 = e2_start.Y - e2_end.Y;
                double z2 = e2_start.Z - e2_end.Z;

                double dot = (x1 * x2) + (y1 * y2) + (z1 * z2);

                return dot;
            }

            // Creates a plane between this and the given edge
            public Plane Plane(Edge edge, List<Vertex> vertex_list)
            {
                Point3d e1_start = vertex_list[this.Start].getLocation();
                Point3d e1_end = vertex_list[this.End].getLocation();
                Point3d e2_start = vertex_list[edge.Start].getLocation();
                Point3d e2_end = vertex_list[edge.End].getLocation();

                double x1 = e1_start.X - e1_end.X;
                double y1 = e1_start.Y - e1_end.Y;
                double z1 = e1_start.Z - e1_end.Z;

                double x2 = e2_start.X - e2_end.X;
                double y2 = e2_start.Y - e2_end.Y;
                double z2 = e2_start.Z - e2_end.Z;

                Vector3d vector1 = new Vector3d(x1, y1, z1);
                Vector3d vector2 = new Vector3d(x2, y2, z2);

                Rhino.Geometry.Plane plane = new Rhino.Geometry.Plane(e1_start, vector1, vector2);

                return plane;
            }

            // Returns true if this is on the given plane
            public bool OnPlane(Plane plane, List<Vertex> vertex_list)
            {
                Point3d start = vertex_list[this.Start].getLocation();
                Point3d end = vertex_list[this.End].getLocation();

                Vector3d vector = start - end;

                bool on_plane = false;
                var normal = plane.Normal;
                double dot = Vector3d.Multiply(vector, normal);
                double distance = plane.DistanceTo(start);
                if (dot < 1e-2 && distance < 1e-2)
                    on_plane = true;

                return on_plane;
            }

            // If the given vertex is on the edge then the other vertex is returned
            public Vertex UnsharedVertex(int shared_vertex, List<Vertex> vertex_list)
            {
                int unshared_idx;
                int start = this.Start;
                int end = this.End;

                if (start == shared_vertex)
                {
                    unshared_idx = end;
                }
                else if (end == shared_vertex)
                {
                    unshared_idx = start;
                }
                else
                {
                    throw new Exception("The given vertex is not shared with the edge");
                }

                return vertex_list[unshared_idx];
            }
        }

        public class QuadFace
        {
            private int a;
            private int b;
            private int c;
            private int d;

            public QuadFace(int A, int B, int C, int D) // Where A, B, C, and D are indicies of the vertices 
            {
                this.a = A;
                this.b = B;
                this.c = C;
                this.d = D;
            }

            public List<int> getQaudFace()
            {
                return (new List<int> { a, b, c, d });
            }

            public int A
            {
                get
                {
                    return a;
                }
            }

            public int B
            {
                get
                {
                    return b;
                }
            }

            public int C
            {
                get
                {
                    return c;
                }
            }

            public int D
            {
                get
                {
                    return d;
                }
            }

            public Point3d getCentroid(VertexList vertices)
            {
                Point3d centroid = new Point3d();
                int A = this.a;
                int B = this.b;
                int C = this.c;
                int D = this.d;

                Point3d point_a = vertices[A].getLocation();
                Point3d point_b = vertices[B].getLocation();
                Point3d point_c = vertices[C].getLocation();
                Point3d point_d = vertices[D].getLocation();

                centroid = (point_a + point_b + point_c + point_d) / 4;

                return centroid;
            }

            public bool ContainsPoint(List<Vertex> vertices, Point3d point)
            {
                bool contains = false;

                int A = this.a;
                int B = this.b;
                int C = this.c;
                int D = this.d;

                Point3d point_a = vertices[A].getLocation();
                Point3d point_b = vertices[B].getLocation();
                Point3d point_c = vertices[C].getLocation();
                Point3d point_d = vertices[D].getLocation();

                Mesh mesh = new Mesh();
                mesh.Vertices.Add(point_a);
                mesh.Vertices.Add(point_b);
                mesh.Vertices.Add(point_c);
                mesh.Vertices.Add(point_d);
                mesh.Faces.AddFace(0, 1, 2, 3);

                Point3d close_point = mesh.ClosestPoint(point);

                if (close_point.EpsilonEquals(point, 1e-2))
                    contains = true;


                return contains;
            }

            public Vector3d Normal(VertexList vertex_list)
            {
                int A = this.A;
                int B = this.B;
                int D = this.D;

                Point3d a = vertex_list[A].getLocation();
                Point3d b = vertex_list[B].getLocation();
                Point3d d = vertex_list[D].getLocation();

                Vector3d AB = a - b;
                Vector3d AD = a - d;

                Vector3d normal = Vector3d.CrossProduct(AB, AD);

                return normal;
            }
        }

        public class TriFace
        {
            private int a;
            private int b;
            private int c;

            public TriFace(int A, int B, int C) // Where A, B, and C refer to vertex indicies in the list of vertices
            {
                this.a = A;
                this.b = B;
                this.c = C;
            }

            public Tuple<int, int, int> getTriFace()
            {
                return (new Tuple<int, int, int>(a, b, c));
            }

            public int A
            {
                get
                {
                    return a;
                }
            }

            public int B
            {
                get
                {
                    return b;
                }
            }

            public int C
            {
                get
                {
                    return c;
                }
            }
        }

        public class Hex
        {
            private int a;
            private int b;
            private int c;
            private int d;
            private int e;
            private int f;
            private int g;
            private int h;

            public Hex(int A, int B, int C, int D, int E, int F, int G, int H)
            {
                this.a = A;
                this.b = B;
                this.c = C;
                this.d = D;
                this.e = E;
                this.f = F;
                this.g = G;
                this.h = H;
            }

            public Hex(List<int> h)
            {
                this.a = h[0];
                this.b = h[1];
                this.c = h[2];
                this.d = h[3];
                this.e = h[4];
                this.f = h[5];
                this.g = h[6];
                this.h = h[7];
            }

            public int A
            {
                get
                {
                    return a;
                }
            }

            public int B
            {
                get
                {
                    return b;
                }
            }

            public int C
            {
                get
                {
                    return c;
                }
            }

            public int D
            {
                get
                {
                    return d;
                }
            }

            public int E
            {
                get
                {
                    return e;
                }
            }

            public int F
            {
                get
                {
                    return f;
                }
            }

            public int G
            {
                get
                {
                    return g;
                }
            }

            public int H
            {
                get
                {
                    return h;
                }
            }

            public List<int> GetVertices()
            {
                int A = this.a;
                int B = this.b;
                int C = this.c;
                int D = this.d;
                int E = this.e;
                int F = this.f;
                int G = this.g;
                int H = this.h;

                List<int> vertices = new List<int>{ A, B, C, D, E, F, G, H };

                return vertices;
            }

            public Point3d GetCentroid(VertexList vertices)
            {
                Point3d A = vertices[this.A].getLocation();
                Point3d B = vertices[this.B].getLocation();
                Point3d C = vertices[this.C].getLocation();
                Point3d D = vertices[this.D].getLocation();
                Point3d E = vertices[this.E].getLocation();
                Point3d F = vertices[this.F].getLocation();
                Point3d G = vertices[this.G].getLocation();
                Point3d H = vertices[this.H].getLocation();

                Point3d centroid = (A + B + C + D + E + F + G + H) / 8;

                return centroid;
            }

            public List<Tuple<int, int>> GetEdges(VertexList vertex_list)
            {
                List<int> vertices = this.GetVertices();
                List<Tuple<int, int>> edges = new List<Tuple<int, int>>();
                
                foreach(int vertex in vertices)
                {
                    List<int> connectedverts = vertex_list[vertex].getConnectedVertices();
                    List<int> connected_verts = new List<int>();
                    connected_verts.AddRange(connectedverts);
                    List<int> vertices_to_remove = new List<int>();
                    foreach(int v in connected_verts)
                    {
                        if(!vertices.Contains(v))
                            vertices_to_remove.Add(v);
                    }

                    foreach(int v in vertices_to_remove)
                    {
                        connected_verts.Remove(v);
                    }

                    foreach(int v in connected_verts)
                    {
                        List<int> list = new List<int> { v, vertex };
                        list.Sort();
                        Tuple<int, int> tuple = new Tuple<int, int>(list[0], list[1]);
                        if (!edges.Contains(tuple) && tuple.Item1 != tuple.Item2)
                            edges.Add(tuple);
                    }
                }

                return edges;
            }

            public List<Tuple<int, int, int, int>> GetFaces(VertexList vertex_list)
            {
                List<int> vertices = this.GetVertices();
                List<Tuple<int, int, int, int>> faces = new List<Tuple<int, int, int, int>>();
                if (this.IsSquare(vertex_list))
                { 
                    List<Point3d> points = new List<Point3d>();
                    List<int> face1 = new List<int>(4);
                    List<int> face2 = new List<int>(4);
                    List<int> face3 = new List<int>(4);
                    List<int> face4 = new List<int>(4);
                    List<int> face5 = new List<int>(4);
                    List<int> face6 = new List<int>(4);
                    double x1 = new double();
                    double x2 = new double();
                    double y1 = new double();
                    double y2 = new double();
                    double z1 = new double();
                    double z2 = new double();
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        int vert = vertices[i];
                        double x = vertex_list[vert].getLocation().X;
                        if (i == 0)
                        {
                            face1.Add(vert);
                            x1 = x;
                        }
                        else if (x == x1)
                        {
                            face1.Add(vert);
                        }
                        else if (face2.Count == 0)
                        {
                            face2.Add(vert);
                            x2 = x;
                        }
                        else if (x == x2)
                        {
                            face2.Add(vert);
                        }

                        double y = vertex_list[vert].getLocation().Y;
                        if (i == 0)
                        {
                            face3.Add(vert);
                            y1 = y;
                        }
                        else if (y == y1)
                        {
                            face3.Add(vert);
                        }
                        else if (face4.Count == 0)
                        {
                            face4.Add(vert);
                            y2 = y;
                        }
                        else if (y == y2)
                        {
                            face4.Add(vert);
                        }

                        double z = vertex_list[vert].getLocation().Z;
                        if (i == 0)
                        {
                            face5.Add(vert);
                            z1 = z;
                        }
                        else if (z == z1)
                        {
                            face5.Add(vert);
                        }
                        else if (face6.Count == 0)
                        {
                            face6.Add(vert);
                            z2 = z;
                        }
                        else if (z == z2)
                        {
                            face6.Add(vert);
                        }
                    }

                    List<List<int>> face_list = new List<List<int>> { face1, face2, face3, face4, face5, face6 };
                    foreach (List<int> face in face_list)
                    {
                        face.Sort();
                        faces.Add(new Tuple<int, int, int, int>(face[0], face[1], face[2], face[3]));
                    }

                    return faces;
                }
                else
                {
                    faces.Add(new Tuple<int, int, int, int>(vertices[0], vertices[1], vertices[5], vertices[4]));
                    faces.Add(new Tuple<int, int, int, int>(vertices[1], vertices[2], vertices[6], vertices[5]));
                    faces.Add(new Tuple<int, int, int, int>(vertices[2], vertices[3], vertices[7], vertices[6]));
                    faces.Add(new Tuple<int, int, int, int>(vertices[0], vertices[4], vertices[7], vertices[3]));
                    faces.Add(new Tuple<int, int, int, int>(vertices[0], vertices[4], vertices[2], vertices[1]));
                    faces.Add(new Tuple<int, int, int, int>(vertices[4], vertices[5], vertices[6], vertices[7]));

                    return faces;
                }
            }

            public Vector3d FaceNormal(QuadFace face, VertexList vertex_list)
            {
                Point3d hex_centroid = this.GetCentroid(vertex_list);
                Point3d face_centroid = face.getCentroid(vertex_list);
                Point3d A = vertex_list[face.A].getLocation();
                Point3d B = vertex_list[face.B].getLocation();
                Point3d D = vertex_list[face.D].getLocation();

                Vector3d inward = face_centroid - hex_centroid;
                Vector3d AB = B - A;
                Vector3d AD = D - A;

                Vector3d normal = Vector3d.CrossProduct(AB, AD);

                double theta = Math.Acos((inward * normal) / (inward.Length * normal.Length));
                double degrees = Math.Abs(theta) * 180 / Math.PI;

                if (degrees < 90)
                    normal *= -1;
                normal.Unitize();

                return normal;
            }

            public bool IsSquare(VertexList vertex_list)
            {
                List<int> vertices = this.GetVertices();
                List<Point3d> points = new List<Point3d>();

                for (int i = 0; i < vertices.Count; i++)
                {
                    points.Add(vertex_list[vertices[i]].getLocation());
                }

                List<double> x = new List<double>();
                List<double> y = new List<double>();
                List<double> z = new List<double>();
                foreach (Point3d point in points)
                {
                    x.Add(Math.Round(point.X, 5));
                    y.Add(Math.Round(point.Y, 5));
                    z.Add(Math.Round(point.Z, 5));
                }

                x = x.GroupBy(x => x).Select(x => x.Key).ToList();
                y = y.GroupBy(y => y).Select(y => y.Key).ToList();
                z = z.GroupBy(z => z).Select(z => z.Key).ToList();

                if (x.Count == 2 && y.Count == 2 && z.Count == 2)
                    return true;
                else
                    return false;
            }
        }

        public class Tetrahedral
        {
            private int a;
            private int b;
            private int c;
            private int d;

            public Tetrahedral(int A, int B, int C, int D)
            {
                this.a = A;
                this.b = B;
                this.c = C;
                this.d = D;
            }

            public int A
            {
                get
                { 
                    return a;
                }
            }

            public int B
            {
                get
                {
                    return b;
                }
            }

            public int C
            {
                get
                {
                    return c;
                }
            }

            public int D
            {
                get
                {
                    return d;
                }
            }
        }

        public class QuadMesh
        {
            private List<Vertex> Vertices;
            private Sculpt3D.Collections.EdgeList Edges;
            private List<QuadFace> Faces;

            public QuadMesh(List<Vertex> vertices, Sculpt3D.Collections.EdgeList edges, List<QuadFace> faces)
            {
                Vertices = vertices;
                Edges = edges;
                Faces = faces;
            }
        }

        public class TriMesh
        {
            private List<Vertex> Vertices;
            private List<Edge> Edges;
            private List<TriFace> Faces;

            public TriMesh(List<Vertex> vertices, List<Edge> edges, List<TriFace> faces)
            {
                Vertices = vertices;
                Edges = edges;
                Faces = faces;
            }

            public List<Vertex> GetVertices()
            {
                return Vertices;
            }

            public List<Edge> GetEdges()
            {
                return Edges;
            }

            public List<TriFace> GetFaces()
            {
                return Faces;
            }
        }

        namespace Collections
        {
            public class VertexList
            {
                private List<Vertex> vertices;

                public VertexList(List<Vertex> Vertices)
                {
                    this.vertices = Vertices;
                }

                public void Add(Vertex v)
                {
                    vertices.Add(v);
                }

                public int Count
                {
                    get
                    {
                        return vertices.Count;
                    }
                }

                public Vertex this[int i]
                {
                    get
                    {
                        return vertices[i];
                    }
                    set
                    {
                        vertices[i] = value;
                    }
                }

                public void AddConnectedQuadFaces(QuadFaceList faces)
                {
                    for(int i = 0; i < faces.Count; ++i)
                    {
                        QuadFace face = faces[i];
                        
                        int a = face.A;
                        int b = face.B;
                        int c = face.C;
                        int d = face.D;

                        vertices[a].AddConnectedFace(i);
                        vertices[b].AddConnectedFace(i);
                        vertices[c].AddConnectedFace(i);
                        vertices[d].AddConnectedFace(i);
                    }
                }

                public void AddConnectedTriFaces(TriFaceList faces)
                {
                    for (int i = 0; i < faces.Count; ++i)
                    {
                        TriFace face = faces[i];
                        int a = face.A;
                        int b = face.B;
                        int c = face.C;

                        vertices[a].AddConnectedFace(i);
                        vertices[b].AddConnectedFace(i);
                        vertices[c].AddConnectedFace(i);
                    }
                }

                public void AddConnectedVertices(EdgeList edges)
                {
                    foreach(Vertex vert in vertices)
                    {
                        List<int> connected_edges = vert.getConnectedEdges();
                        List<int> connected_vertices = vert.getConnectedVertices();
                        foreach(int edge in connected_edges)
                        {
                            int v1 = edges[edge].getStart();
                            int v2 = edges[edge].getEnd();
                            
                            if(v1 == vert.getIndex() && !connected_vertices.Contains(v2))
                            {
                                vert.AddConnectedVertex(v2);
                            }
                            else if(v2 == vert.getIndex() && !connected_vertices.Contains(v1))
                            {
                                vert.AddConnectedVertex(v1);
                            }
                        }
                    }
                }

                public void AddConnectedVertices(List<Hex> hexes)
                {
                    foreach(Hex hex in hexes)
                    {
                        Vertex vert1 = vertices[hex.A];
                        vert1.AddConnectedVertex(hex.B);
                        vert1.AddConnectedVertex(hex.D);
                        vert1.AddConnectedVertex(hex.E);
                        
                        Vertex vert2 = vertices[hex.B];
                        vert2.AddConnectedVertex(hex.A);
                        vert2.AddConnectedVertex(hex.C);
                        vert2.AddConnectedVertex(hex.F);
                       
                        Vertex vert3 = vertices[hex.C];
                        vert3.AddConnectedVertex(hex.B);
                        vert3.AddConnectedVertex(hex.D);
                        vert3.AddConnectedVertex(hex.G);

                        Vertex vert4 = vertices[hex.D];
                        vert4.AddConnectedVertex(hex.A);
                        vert4.AddConnectedVertex(hex.C);
                        vert4.AddConnectedVertex(hex.H);

                        Vertex vert5 = vertices[hex.E];
                        vert5.AddConnectedVertex(hex.A);
                        vert5.AddConnectedVertex(hex.F);
                        vert5.AddConnectedVertex(hex.H);

                        Vertex vert6 = vertices[hex.F];
                        vert6.AddConnectedVertex(hex.B);
                        vert6.AddConnectedVertex(hex.E);
                        vert6.AddConnectedVertex(hex.G);

                        Vertex vert7 = vertices[hex.G];
                        vert7.AddConnectedVertex(hex.C);
                        vert7.AddConnectedVertex(hex.F);
                        vert7.AddConnectedVertex(hex.H);

                        Vertex vert8 = vertices[hex.H];
                        vert8.AddConnectedVertex(hex.D);
                        vert8.AddConnectedVertex(hex.E);
                        vert8.AddConnectedVertex(hex.G);
                    }
                }

                public void AddConnectedVertices(List<Tetrahedral> tets)
                {
                    foreach(Tetrahedral tet in tets)
                    {
                        Vertex vert1 = vertices[tet.A];
                        vert1.AddConnectedVertex(tet.B);
                        vert1.AddConnectedVertex(tet.C);
                        vert1.AddConnectedVertex(tet.D);

                        Vertex vert2 = vertices[tet.B];
                        vert2.AddConnectedVertex(tet.A);
                        vert2.AddConnectedVertex(tet.C);
                        vert2.AddConnectedVertex(tet.D);

                        Vertex vert3 = vertices[tet.C];
                        vert3.AddConnectedVertex(tet.A);
                        vert3.AddConnectedVertex(tet.B);
                        vert3.AddConnectedVertex(tet.D);

                        Vertex vert4 = vertices[tet.D];
                        vert4.AddConnectedVertex(tet.A);
                        vert4.AddConnectedVertex(tet.B);
                        vert4.AddConnectedVertex(tet.C);

                    }
                }

                public void ClearConnectedVertices()
                {
                    for(int i = 0; i < vertices.Count; i++)
                    {
                        if (vertices[i] == null)
                            continue;
                        Vertex vert = vertices[i];
                        vert.ClearConnectedVertices();
                    }
                }
            }

            public class EdgeList
            {
                private List<Edge> Edges;

                public EdgeList(List<Edge> Edges)
                {
                    this.Edges = Edges;
                }

                public void Add(Edge Edge)
                {
                    Edges.Add(Edge);
                }

                public int Count
                {
                    get
                    {
                        return Edges.Count;
                    }
                }

                public bool Contains(int vertex1, int vertex2)
                {
                    bool contains = false;
                    if (Edges == null)
                        return false;

                    foreach (Edge e in Edges)
                    {
                        if (e.getStart() == vertex1 && e.getEnd() == vertex2)
                        {
                            contains = true;
                            break;
                        }
                        else if (e.getStart() == vertex2 && e.getEnd() == vertex1)
                        {
                            contains = true;
                            break;
                        }
                    }

                    return contains;
                }

                public Edge this[int index]
                {
                    get
                    {
                        return Edges[index];
                    }
                    set
                    {
                        Edges[index] = value;
                    }
                }
            }

            public class QuadFaceList
            {
                List<QuadFace> Faces;

                public QuadFaceList(List<QuadFace> faces)
                {
                    Faces = faces;
                }

                public void AddFace(QuadFace face)
                {
                    Faces.Add(face);
                }

                public bool Contains(QuadFace face1)
                {
                    bool contains = false;
                    int A1 = face1.A;
                    int B1 = face1.B;
                    int C1 = face1.C;
                    int D1 = face1.D;

                    foreach (QuadFace face2 in Faces)
                    {
                        int A2 = face2.A;
                        int B2 = face2.B;
                        int C2 = face2.C;
                        int D2 = face2.D;

                        int[] set1 = { A1, B1, C1, D1 };
                        int[] set2 = { A2, B2, C2, D2 };

                        HashSet<int> hash_set1 = new HashSet<int>(set1);
                        HashSet<int> hash_set2 = new HashSet<int>(set2);

                        bool output = hash_set1.SetEquals(hash_set2);
                        if (output)
                        {
                            contains = true;
                            break;
                        }
                    }

                    return contains;
                }

                public int Count
                {
                    get
                    {
                        return Faces.Count;
                    }
                }

                public QuadFace this[int i]
                {
                    get
                    {
                        return Faces[i];
                    }
                }
            }

            public class TriFaceList
            {
                private List<TriFace> Faces;

                public TriFaceList(List<TriFace> faces)
                {
                    Faces = faces;
                }

                public void AddFace(TriFace face)
                {
                    Faces.Add(face);
                }

                public TriFace this[int i]
                {
                    get
                    {
                        return Faces[i];
                    }
                }

                public bool Contains(TriFace face1)
                {
                    bool contains = false;
                    int A1 = face1.A;
                    int B1 = face1.B;
                    int C1 = face1.C;

                    foreach (TriFace face2 in Faces)
                    {
                        int A2 = face2.A;
                        int B2 = face2.B;
                        int C2 = face2.C;

                        int[] set1 = { A1, B1, C1 };
                        int[] set2 = { A2, B2, C2 };

                        HashSet<int> hash_set1 = new HashSet<int>(set1);
                        HashSet<int> hash_set2 = new HashSet<int>(set2);

                        bool output = hash_set1.SetEquals(hash_set2);
                        if (output)
                        {
                            contains = true;
                            break;
                        }
                    }

                    return contains;
                }

                public int Count
                {
                    get
                    {
                        return Faces.Count;
                    }
                }
            }
        }

        public static partial class Functions
        {
            public static double Dot(Vector3d vector1, Vector3d vector2)
            {
                double x1 = vector1.X;
                double y1 = vector1.Y;
                double z1 = vector1.Z;
                double x2 = vector2.X;
                double y2 = vector2.Y;
                double z2 = vector2.Z;

                double dot = (x1 * x2) + (y1 * y2) + (z1 * z2);

                return Math.Abs(dot);
            }

            public static Point3d RoundPoint(Point3d point)
            {
                Point3d p = new Point3d(Math.Round(point.X, 3), Math.Round(point.Y, 3), Math.Round(point.Z, 3));

                return p;
            }

            public static List<int> GetConnectedFaces(VertexList vertex_list, List<int> connected_verts)
            {
                if (connected_verts.Count != 4)
                    throw new Exception("connected_verts does not equal 4");

                List<int> connected_faces = new List<int>();
                List<int> all_faces = new List<int>();

                foreach (int v in connected_verts)
                {
                    all_faces.AddRange(vertex_list[v].getConnectedFaces());
                }

                connected_faces = all_faces.GroupBy(x => x)
                        .Where(g => g.Count() > 2)
                        .Select(g => g.Key)
                        .ToList();
                if (connected_faces.Count != 3)
                    throw new Exception("connected faces is not equal to three");
                
                return connected_faces;
            }

            public static List<int> GetPotentialHex(Sculpt2D.Sculpt3D.Collections.VertexList vertex_list, List<int> connected_verts)
            {
                List<int> hex = new List<int>();
                List<int> connected_faces = GetConnectedFaces(vertex_list, connected_verts);

                // Loop through each vertex connected to that is connected
                foreach (int v in connected_verts)
                {
                    // Add the vertex to the hex
                    hex.Add(v);
                    Vertex vert = vertex_list[v];

                    // find the connected vertices to the current vertex (v)
                    List<int> connected_v = vert.getConnectedVertices();
                    // Loop through each of the vertices that are connected to (v)
                    foreach (int i in connected_v)
                    {
                        // Loop through all the faces in connected_faces
                        foreach (int face in connected_faces)
                        {
                            // if the faces connected to (i) are also connected to (v) 
                            // add (i) to the hex
                            List<int> connected_f = vertex_list[i].getConnectedFaces();
                            if (connected_f.Contains(face) && !hex.Contains(i) && !connected_verts.Contains(i))
                            {
                                hex.Add(i);
                                break;
                            }
                        }
                    }
                }
                List<int> connected_vs = new List<int>();
                foreach (int v in hex)
                {
                    if (!connected_verts.Contains(v))
                    {
                        List<int> connections = vertex_list[v].getConnectedVertices();
                        foreach (int con in connections)
                        {
                            if (!hex.Contains(con))
                            {
                                connected_vs.Add(con);
                            }
                        }
                    }
                }
                int H = connected_vs.GroupBy(x => x)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .FirstOrDefault();

                hex.Add(H);
                hex.Sort();

                if (hex.Count != 8)
                    throw new Exception(string.Format("Possible hex has {0} vertices. Hexes are composed of 8 vertices", hex.Count.ToString()));

                return hex;
            }

            public static List<Hex> GetPotentialHexes(VertexList vertex_list, Vertex vertex)
            {
                List<int> connected_verts = vertex.getConnectedVertices();
                List<Hex> potential_hexes = new List<Hex>();

                if (connected_verts.Count == 6)
                //    8 cells
                {
                    List<Tuple<int, int, int>> tuples = new List<Tuple<int, int, int>>();
                    List<Vector3d> vectors = new List<Vector3d>();
                    foreach (int v in connected_verts)
                    {
                        Vector3d vector = vertex.getLocation() - vertex_list[v].getLocation();
                        vectors.Add(vector);
                    }

                    // Group vectors into groups of three in tuples
                    for (int i = 0; i < vectors.Count; ++i)
                    {
                        for (int j = 0; j < vectors.Count; ++j)
                        {
                            for (int k = 0; k < vectors.Count; ++k)
                            {
                                if (i != j && i != k && j != k)
                                {
                                    double dot1 = Dot(vectors[i], vectors[j]);
                                    double dot2 = Dot(vectors[i], vectors[k]);
                                    double dot3 = Dot(vectors[j], vectors[k]);
                                    List<int> list = new List<int> { connected_verts[i], connected_verts[j], connected_verts[k] };
                                    list.Sort();
                                    Tuple<int, int, int> tuple = new Tuple<int, int, int>(list[0], list[1], list[2]);
                                    if (dot1 < 1e-2 && dot2 < 1e-2 && dot3 < 1e-2 && !tuples.Contains(tuple))
                                    {
                                        tuples.Add(tuple);
                                    }
                                }
                            }
                        }
                    }

                    List<int> connected_verts1 = new List<int> { tuples[0].Item1, tuples[0].Item2, tuples[0].Item3, vertex.getIndex() };
                    List<int> connected_verts2 = new List<int> { tuples[1].Item1, tuples[1].Item2, tuples[1].Item3, vertex.getIndex() };
                    List<int> connected_verts3 = new List<int> { tuples[2].Item1, tuples[2].Item2, tuples[2].Item3, vertex.getIndex() };
                    List<int> connected_verts4 = new List<int> { tuples[3].Item1, tuples[3].Item2, tuples[3].Item3, vertex.getIndex() };
                    List<int> connected_verts5 = new List<int> { tuples[4].Item1, tuples[4].Item2, tuples[4].Item3, vertex.getIndex() };
                    List<int> connected_verts6 = new List<int> { tuples[5].Item1, tuples[5].Item2, tuples[5].Item3, vertex.getIndex() };
                    List<int> connected_verts7 = new List<int> { tuples[6].Item1, tuples[6].Item2, tuples[6].Item3, vertex.getIndex() };
                    List<int> connected_verts8 = new List<int> { tuples[7].Item1, tuples[7].Item2, tuples[7].Item3, vertex.getIndex() };

                    List<int> hex1 = GetPotentialHex(vertex_list, connected_verts1);
                    List<int> hex2 = GetPotentialHex(vertex_list, connected_verts2);
                    List<int> hex3 = GetPotentialHex(vertex_list, connected_verts3);
                    List<int> hex4 = GetPotentialHex(vertex_list, connected_verts4);
                    List<int> hex5 = GetPotentialHex(vertex_list, connected_verts5);
                    List<int> hex6 = GetPotentialHex(vertex_list, connected_verts6);
                    List<int> hex7 = GetPotentialHex(vertex_list, connected_verts7);
                    List<int> hex8 = GetPotentialHex(vertex_list, connected_verts8);

                    potential_hexes.Add(new Hex(hex1[0], hex1[1], hex1[2], hex1[3], hex1[4], hex1[5], hex1[6], hex1[7]));
                    potential_hexes.Add(new Hex(hex2[0], hex2[1], hex2[2], hex2[3], hex2[4], hex2[5], hex2[6], hex2[7]));
                    potential_hexes.Add(new Hex(hex3[0], hex3[1], hex3[2], hex3[3], hex3[4], hex3[5], hex3[6], hex3[7]));
                    potential_hexes.Add(new Hex(hex4[0], hex4[1], hex4[2], hex4[3], hex4[4], hex4[5], hex4[6], hex4[7]));
                    potential_hexes.Add(new Hex(hex5[0], hex5[1], hex5[2], hex5[3], hex5[4], hex5[5], hex5[6], hex5[7]));
                    potential_hexes.Add(new Hex(hex6[0], hex6[1], hex6[2], hex6[3], hex6[4], hex6[5], hex6[6], hex6[7]));
                    potential_hexes.Add(new Hex(hex7[0], hex7[1], hex7[2], hex7[3], hex7[4], hex7[5], hex7[6], hex7[7]));
                    potential_hexes.Add(new Hex(hex8[0], hex8[1], hex8[2], hex8[3], hex8[4], hex8[5], hex8[6], hex8[7]));

                }
                else if (connected_verts.Count == 5)
                //    4 cells
                {
                    List<int> hex1 = new List<int>();
                    List<int> hex2 = new List<int>();
                    List<int> hex3 = new List<int>();
                    List<int> hex4 = new List<int>();

                    List<int> connected_verts1 = new List<int>();
                    List<int> connected_verts2 = new List<int>();
                    List<int> connected_verts3 = new List<int>();
                    List<int> connected_verts4 = new List<int>();

                    Point3d point1 = vertex_list[connected_verts[0]].getLocation();
                    Point3d point2 = vertex_list[connected_verts[1]].getLocation();
                    Point3d point3 = vertex_list[connected_verts[2]].getLocation();
                    Point3d point4 = vertex_list[connected_verts[3]].getLocation();
                    Point3d point5 = vertex_list[connected_verts[4]].getLocation();

                // I guess get vertices into groups of three that are all orthogonal to one another
                    List<Tuple<int, int, int>> tuples = new List<Tuple<int, int, int>>();
                    List<Rhino.Geometry.Vector3d> vectors = new List<Rhino.Geometry.Vector3d>();
                    foreach (int v in connected_verts)
                    {
                        Vector3d vector = vertex.getLocation() - vertex_list[v].getLocation();
                        vectors.Add(vector);
                    }
                    // Find the single orthogonal vertex
                    int ortho_vert = -1;
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        int counter = 0;
                        for (int j = 0; j < vectors.Count; j++)
                        {
                            if (i != j)
                            {
                                counter++;
                                double dot = Dot(vectors[i], vectors[j]);
                                if (dot > 1e-2)
                                    break;
                                else if(counter == 4)
                                {
                                    ortho_vert = connected_verts[i];
                                    break;
                                }
                            }
                        }
                    }
                    // Use the otho_vert to group the vertices into respective connected vertices
                    for(int i = 0; i < vectors.Count; ++i)
                    {
                        for(int j = 0; j < vectors.Count; ++j)
                        {
                            List<int> verts = new List<int> { connected_verts[i], connected_verts[j] };
                            verts.Sort();
                            if (i != j && connected_verts[i] != ortho_vert 
                                && connected_verts[j] != ortho_vert 
                                && !tuples.Contains(new Tuple<int, int, int>(verts[0], verts[1], ortho_vert)))
                            {
                                double dot = Dot(vectors[i], vectors[j]);
                                if (dot < 1e-2)
                                { 
                                    tuples.Add(new Tuple<int, int, int>(verts[0], verts[1], ortho_vert));
                                }
                            }
                        }
                    }

                    // separate out the vertex groups from the tuples list
                    connected_verts1 = new List<int> { tuples[0].Item1, tuples[0].Item2, tuples[0].Item3, vertex.getIndex() };
                    connected_verts2 = new List<int> { tuples[1].Item1, tuples[1].Item2, tuples[1].Item3, vertex.getIndex() };
                    connected_verts3 = new List<int> { tuples[2].Item1, tuples[2].Item2, tuples[2].Item3, vertex.getIndex() };
                    connected_verts4 = new List<int> { tuples[3].Item1, tuples[3].Item2, tuples[3].Item3, vertex.getIndex() };

                    hex1 = GetPotentialHex(vertex_list, connected_verts1);
                    hex2 = GetPotentialHex(vertex_list, connected_verts2);
                    hex3 = GetPotentialHex(vertex_list, connected_verts3);
                    hex4 = GetPotentialHex(vertex_list, connected_verts4);

                    potential_hexes.Add(new Hex(hex1[0], hex1[1], hex1[2], hex1[3], hex1[4], hex1[5], hex1[6], hex1[7]));
                    potential_hexes.Add(new Hex(hex2[0], hex2[1], hex2[2], hex2[3], hex2[4], hex2[5], hex2[6], hex2[7]));
                    potential_hexes.Add(new Hex(hex3[0], hex3[1], hex3[2], hex3[3], hex3[4], hex3[5], hex3[6], hex3[7]));
                    potential_hexes.Add(new Hex(hex4[0], hex4[1], hex4[2], hex4[3], hex4[4], hex4[5], hex4[6], hex4[7]));
                }
                else if (connected_verts.Count == 4)
                //    2 cells
                {

                    List<int> hex1 = new List<int>();
                    List<int> hex2 = new List<int>();

                    List<int> connected_verts1 = new List<int>();
                    List<int> connected_verts2 = new List<int>();

                    // use colinear points to determine which vertices will be used to make hexes
                    int hex1_vert = -1;
                    int hex2_vert = -1;

                    List<Rhino.Geometry.Vector3d> vectors = new List<Rhino.Geometry.Vector3d>();
                    foreach (int v in connected_verts)
                    {
                        Vector3d vector = vertex.getLocation() - vertex_list[v].getLocation();
                        vectors.Add(vector);
                    }
                    bool found = false;
                    for (int i = 0; i < vectors.Count; i++)
                    {
                        for (int j = 0; j < vectors.Count; j++)
                        {
                            if (i != j)
                            {
                                Vector3d vector1 = vectors[i];
                                Vector3d vector2 = vectors[j];
                                double dot = Dot(vector1, vector2);
                                if (dot > 1e-2)//put a break here maybe it would work to do two
                                {
                                    hex1_vert = connected_verts[i];
                                    hex2_vert = connected_verts[j];
                                    found = true;
                                }
                            }
                            if (found)
                                break;
                        }
                        if (found)
                            break;
                    }

                    //connected_verts.Add(vertex.getIndex());
                    List<int> connected_vert_list = new List<int>();
                    connected_vert_list.Add(vertex.getIndex());
                    connected_vert_list.AddRange(connected_verts);
                    foreach (int v in connected_vert_list)
                    {
                        if (hex2_vert != v)
                        {
                            connected_verts1.Add(v);
                        }
                        if (hex1_vert != v)
                        {
                            connected_verts2.Add(v);
                        }
                    }

                    hex1 = GetPotentialHex(vertex_list, connected_verts1);
                    hex2 = GetPotentialHex(vertex_list, connected_verts2);

                    potential_hexes.Add(new Hex(hex1[0], hex1[1], hex1[2], hex1[3], hex1[4], hex1[5], hex1[6], hex1[7]));
                    potential_hexes.Add(new Hex(hex2[0], hex2[1], hex2[2], hex2[3], hex2[4], hex2[5], hex2[6], hex2[7]));
                }
                else if (connected_verts.Count == 3)
                //    1 cell
                {
                    List<int> hex = new List<int>(8);
                    List<int> connected_vert_list = new List<int>();
                    connected_vert_list.Add(vertex.getIndex());
                    connected_vert_list.AddRange(connected_verts);
                    //connected_verts.Add(vertex.getIndex());

                    hex = GetPotentialHex(vertex_list, connected_vert_list);

                    potential_hexes.Add(new Hex(hex[0], hex[1], hex[2], hex[3], hex[4], hex[5], hex[6], hex[7]));
                }
                else
                    throw new Exception(string.Format("There are {0} connected vertices. This is an unexpected number", connected_verts.Count.ToString()));

                return potential_hexes; 
            }

            public static bool ValidateHex(Hex potential_hex, VertexList vertex_list, Dictionary<Tuple<int, int>, Edge> edge_dict, Dictionary<Tuple<int, int, int, int>, QuadFace> face_dict)
            {

                List<Tuple<int, int>> edges = potential_hex.GetEdges(vertex_list);
                List<Tuple<int, int, int, int>> faces = potential_hex.GetFaces(vertex_list);

                if (edges.Count != 12 || faces.Count != 6)
                    return false;
                
                foreach ( Tuple<int, int> edge in edges)
                {
                    if (!edge_dict.ContainsKey(edge))
                        return false;
                }

                foreach (Tuple<int, int, int, int> face in faces)
                {
                    if (!face_dict.ContainsKey(face))
                        return false;
                }

                return true;
            }

            public static Dictionary<Tuple<int, int>, Edge> CreateEdgeDictionary(List<Edge> edges)
            {
                Dictionary<Tuple<int, int>, Edge> edge_dict = new Dictionary<Tuple<int, int>, Edge>();

                foreach (Edge edge in edges)
                {
                    int v1 = edge.getStart();
                    int v2 = edge.getEnd();
                    List<int> e = new List<int> { v1, v2 };
                    e.Sort();
                    Tuple<int, int> e_tuple = new Tuple<int, int>(e[0], e[1]);
                    edge_dict.Add(e_tuple, edge);
                }

                return edge_dict;
            }

            public static Dictionary<Tuple<int, int, int, int>, QuadFace> CreateQuadFaceDictionary(List<QuadFace> faces)
            {
                Dictionary<Tuple<int, int, int, int>, QuadFace> face_dict = new Dictionary<Tuple<int, int, int, int>, QuadFace>();

                foreach (QuadFace face in faces)
                {
                    List<int> f = face.getQaudFace();
                    f.Sort();
                    Tuple<int, int, int, int> f_tuple = new Tuple<int, int, int, int>(f[0], f[1], f[2], f[3]);
                    face_dict.Add(f_tuple, face);
                }

                return face_dict;
            }

            public static Dictionary<Tuple<int, int, int, int>, List<int>> CreateQuadFaceDictionary(List<List<int>> hexes)
            {
                Dictionary<Tuple<int, int, int, int>, List<int>> face_dict = new Dictionary<Tuple<int, int, int, int>, List<int>>();

                foreach (List<int> hex in hexes)
                {
                    List<List<int>> faces = new List<List<int>>();
                    faces.Add(new List<int> { hex[0], hex[1], hex[5], hex[4] });
                    faces.Add(new List<int> { hex[1], hex[2], hex[6], hex[5] });
                    faces.Add(new List<int> { hex[2], hex[3], hex[7], hex[6] });
                    faces.Add(new List<int> { hex[0], hex[4], hex[7], hex[3] });
                    faces.Add(new List<int> { hex[0], hex[3], hex[2], hex[1] });
                    faces.Add(new List<int> { hex[4], hex[5], hex[6], hex[7] });
                    foreach (var face in faces)
                    {
                        List<int> f = new List<int>(face);
                        f.Sort();
                        face_dict.TryAdd(new Tuple<int, int, int, int>(f[0], f[1], f[2], f[3]), face);
                    }
                }

                return face_dict;
            }

            public static List<QuadFace> ConnectFacesWithOneHex(QuadFace face1, QuadFace face2, int pinch_point_idx, double x_dist, double y_dist, double z_dist, VertexList vertex_list, out Hex hex)
            {
                List<int> both_faces_verts = new List<int>();
                both_faces_verts.AddRange(face1.getQaudFace());
                both_faces_verts.AddRange(face2.getQaudFace());

                Point3d pinch_point = vertex_list[pinch_point_idx].getLocation();

                List<int> connection = both_faces_verts.GroupBy(x => x)
                                            .Where(g => g.Count() == 2)
                                            .Select(g => g.Key)
                                            .ToList();
                connection.Remove(pinch_point_idx);
                List<int> free = both_faces_verts.GroupBy(x => x)
                                            .Where(g => g.Count() == 1)
                                            .Select(g => g.Key)
                                            .ToList();

                List<int> connection1_verts = new List<int>();
                List<int> connection2_verts = new List<int>();
                Point3d connection_vert1 = new Point3d(pinch_point);
                Point3d connection_vert2 = vertex_list[connection[0]].getLocation();

                //string path = "/../../../../../../../Users/brian/Research/Sandia/crystal";
                //string name = string.Format("face{0}_face{1}.txt", a, b);
                //using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
                //{
                //    filename.WriteLine("face1:{0} {1} {2} {3}", face1.A, face1.B, face1.C, face1.D);
                //    filename.WriteLine("face2:{0} {1} {2} {3}", face2.A, face2.B, face2.C, face2.D);
                //    filename.WriteLine("{0}: {1}", pinch_point_idx, pinch_point);
                //    filename.WriteLine("{0}: {1}", connection[0], connection_vert2);
                //    filename.WriteLine("{0}: {1}", free[0], vertex_list[free[0]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[1], vertex_list[free[1]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[2], vertex_list[free[2]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[3], vertex_list[free[3]].getLocation());
                //}

                double tot_dist = Math.Sqrt(Math.Pow(x_dist / 4, 2) + Math.Pow(y_dist / 4, 2) + Math.Pow(z_dist / 4, 2)) + 0.1;
                foreach (int idx in free)
                {
                    Point3d free_point = vertex_list[idx].getLocation();
                    double dist_vert1 = connection_vert1.DistanceTo(free_point);
                    double dist_vert2 = connection_vert2.DistanceTo(free_point);


                    if (dist_vert1 <= tot_dist)
                        connection1_verts.Add(idx);
                    else if (dist_vert2 <= tot_dist)
                        connection2_verts.Add(idx);
                    else
                        throw new Exception("We are getting that the free vertices are not close enough to the shared vertices");
                }

                Vector3d connection_vector = connection_vert1 - connection_vert2;
                
                Vector3d con1_vec1 = vertex_list[connection1_verts[0]].getLocation() - connection_vert1;
                Vector3d con1_vec2 = vertex_list[connection1_verts[1]].getLocation() - connection_vert1;
                Vector3d con2_vec1 = vertex_list[connection2_verts[0]].getLocation() - connection_vert2;
                Vector3d con2_vec2 = vertex_list[connection2_verts[1]].getLocation() - connection_vert2;

                Vector3d vert1_direction = new Vector3d();
                Vector3d vert2_direction = new Vector3d();
                double dist = 0;
                if(!(Math.Abs(con1_vec1.X - con1_vec2.X) < 1e-2))
                {
                    vert1_direction.X = 0;
                    vert1_direction.Y = con1_vec1.Y;
                    vert1_direction.Z = con1_vec1.Z;
                    dist = Math.Sqrt(Math.Pow(y_dist * 3 / 8, 2) + Math.Pow(z_dist * 3 / 8, 2));
                }
                else if(!(Math.Abs(con1_vec1.Y - con1_vec2.Y) < 1e-2))
                {
                    vert1_direction.X = con1_vec1.X;
                    vert1_direction.Y = 0;
                    vert1_direction.Z = con1_vec1.Z;
                    dist = Math.Sqrt(Math.Pow(x_dist * 3 / 8, 2) + Math.Pow(z_dist * 3 / 8, 2));
                }
                else if(!(Math.Abs(con1_vec1.Z - con1_vec2.Z) < 1e-2))
                {
                    vert1_direction.X = con1_vec1.X;
                    vert1_direction.Y = con1_vec1.Y;
                    vert1_direction.Z = 0;
                    dist = Math.Sqrt(Math.Pow(x_dist * 3 / 8, 2) + Math.Pow(y_dist * 3 / 8, 2));
                }


                if (!(Math.Abs(con2_vec1.X - (con2_vec2.X)) < 1e-2))
                {
                    vert2_direction.X = 0;
                    vert2_direction.Y = con2_vec1.Y;
                    vert2_direction.Z = con2_vec1.Z;
                }
                else if (!(Math.Abs(con2_vec1.Y - (con2_vec2.Y)) < 1e-2))
                {
                    vert2_direction.X = con2_vec1.X;
                    vert2_direction.Y = 0;
                    vert2_direction.Z = con2_vec1.Z;
                }
                else if (!(Math.Abs(con2_vec1.Z - (con2_vec2.Z)) < 1e-2))
                {
                    vert2_direction.X = con2_vec1.X;
                    vert2_direction.Y = con2_vec1.Y;
                    vert2_direction.Z = 0;
                }

                vert1_direction.Unitize();
                vert2_direction.Unitize();

                Point3d vert1 = new Point3d(); // The new vertex location closest to the pinch point (will be shared)
                vert1.X = connection_vert1.X + vert1_direction.X * dist;
                vert1.Y = connection_vert1.Y + vert1_direction.Y * dist;
                vert1.Z = connection_vert1.Z + vert1_direction.Z * dist;
                Point3d vert2 = new Point3d(); // The new vertex location furthest from the pinch point
                vert2.X = connection_vert2.X + vert2_direction.X * dist;
                vert2.Y = connection_vert2.Y + vert2_direction.Y * dist;
                vert2.Z = connection_vert2.Z + vert2_direction.Z * dist;

                int new_idx1 = vertex_list.Count;
                bool add_vert1 = true;
                bool add_vert2 = true;

                for (int j = vertex_list.Count - 1; j >= 0; --j)
                {
                    if (vertex_list[j] == null)
                        continue;
                    Point3d vert = vertex_list[j].getLocation();
                    if (vert.EpsilonEquals(vert1, 1e-2))
                    {
                        new_idx1 = j;
                        add_vert1 = false;
                        break;
                    }
                }
                if (add_vert1)
                {
                    Vertex temp_vertex = new Vertex(new_idx1, vert1);
                    vertex_list.Add(temp_vertex);
                }

                int new_idx2 = vertex_list.Count;
                for (int j = vertex_list.Count - 1; j >= 0; --j)
                {
                    if (vertex_list[j] == null)
                        continue;
                    Point3d vert = vertex_list[j].getLocation();
                    if (vert.EpsilonEquals(vert2, 1e-2))
                    {
                        new_idx2 = j;
                        add_vert2 = false;
                        break;
                    }
                }
                if (add_vert2)
                {
                    Vertex temp_vertex = new Vertex(new_idx2, vert2);
                    vertex_list.Add(temp_vertex);
                }

                List<int> hex_verts = new List<int>();
                hex_verts.Add(pinch_point_idx);
                hex_verts.AddRange(connection);
                hex_verts.AddRange(free);
                hex_verts.Add(new_idx1);
                hex_verts.Add(new_idx2);
                hex_verts.Sort();

                //hex = new Hex(hex_verts[0], hex_verts[1], hex_verts[2], hex_verts[3], hex_verts[4], hex_verts[5], hex_verts[6], hex_verts[7]);

                List<int> face1_verts = new List<int>(face1.getQaudFace());
                List<int> face2_verts = new List<int>(face2.getQaudFace());
                face1_verts.Remove(pinch_point_idx);
                face1_verts.Remove(connection[0]);
                face2_verts.Remove(pinch_point_idx);
                face2_verts.Remove(connection[0]);
                int a1 = -1;
                int b1 = -1;
                int a2 = -1;
                int b2 = -1;
                if (connection1_verts.Contains(face1_verts[0]))
                {
                    a1 = 0;
                    b1 = 1;
                }
                else
                {
                    a1 = 1;
                    b1 = 0;
                }

                if (connection1_verts.Contains(face2_verts[0]))
                {
                    a2 = 0;
                    b2 = 1;
                }
                else
                {
                    a2 = 1;
                    b2 = 0;
                }

                // TODO: Change the way this is structured so that I can use it for pinch point removal
                QuadFace f1 = new QuadFace(pinch_point_idx, connection1_verts[0], new_idx1, connection1_verts[1]);
                QuadFace f2 = new QuadFace(connection[0], connection2_verts[0], new_idx2, connection2_verts[1]);
                QuadFace f3 = new QuadFace(face1_verts[a1], new_idx1, new_idx2, face1_verts[b1]);
                QuadFace f4 = new QuadFace(face2_verts[a2], new_idx1, new_idx2, face2_verts[b2]);
                List<QuadFace> new_faces = new List<QuadFace> { f1, f2, f3, f4 };

                hex = new Hex(pinch_point_idx, face1_verts[a1], new_idx1, face2_verts[a2], 
                    connection[0], face1_verts[b1], new_idx2, face2_verts[b2]);

                return new_faces;
            }

            public static List<List<int>> ConnectFacesWithOneHex(List<int> face1, List<int> face2, 
                                            int pinch_point_idx, double x_dist, double y_dist, 
                                            double z_dist, List<Point3d> vertices, out List<int> hex)
            {
                List<int> both_faces_verts = new List<int>();
                both_faces_verts.AddRange(face1);
                both_faces_verts.AddRange(face2);

                Point3d pinch_point = vertices[pinch_point_idx];

                List<int> connection = both_faces_verts.GroupBy(x => x)
                                            .Where(g => g.Count() == 2)
                                            .Select(g => g.Key)
                                            .ToList();
                connection.Remove(pinch_point_idx);
                List<int> free = both_faces_verts.GroupBy(x => x)
                                            .Where(g => g.Count() == 1)
                                            .Select(g => g.Key)
                                            .ToList();

                List<int> connection1_verts = new List<int>(2);
                List<int> connection2_verts = new List<int>(2);
                Point3d connection_vert1 = new Point3d(pinch_point);
                Point3d connection_vert2 = vertices[connection[0]];

                //string path = "/../../../../../../../Users/brian/Research/Sandia/crystal";
                //string name = string.Format("face{0}_face{1}.txt", a, b);
                //using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
                //{
                //    filename.WriteLine("face1:{0} {1} {2} {3}", face1.A, face1.B, face1.C, face1.D);
                //    filename.WriteLine("face2:{0} {1} {2} {3}", face2.A, face2.B, face2.C, face2.D);
                //    filename.WriteLine("{0}: {1}", pinch_point_idx, pinch_point);
                //    filename.WriteLine("{0}: {1}", connection[0], connection_vert2);
                //    filename.WriteLine("{0}: {1}", free[0], vertex_list[free[0]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[1], vertex_list[free[1]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[2], vertex_list[free[2]].getLocation());
                //    filename.WriteLine("{0}: {1}", free[3], vertex_list[free[3]].getLocation());
                //}

                double tot_dist = Math.Sqrt(Math.Pow(x_dist / 4, 2) + Math.Pow(y_dist / 4, 2) + Math.Pow(z_dist / 4, 2)) + 0.1;
                foreach (int idx in free)
                {
                    Point3d free_point = vertices[idx];
                    double dist_vert1 = connection_vert1.DistanceTo(free_point);
                    double dist_vert2 = connection_vert2.DistanceTo(free_point);


                    if (dist_vert1 <= tot_dist && dist_vert1 < dist_vert2)
                        connection1_verts.Add(idx);
                    else if (dist_vert2 <= tot_dist && dist_vert2 < dist_vert1)
                        connection2_verts.Add(idx);
                    else if (dist_vert2 != dist_vert1)
                        throw new Exception("We are getting that the free vertices are not close enough to the shared vertices");
                    else
                        throw new Exception("The distance from vert1 and vert2 are equal");
                }

                Vector3d connection_vector = connection_vert1 - connection_vert2;

                Vector3d con1_vec1 = vertices[connection1_verts[0]] - connection_vert1;
                Vector3d con1_vec2 = vertices[connection1_verts[1]] - connection_vert1;
                Vector3d con2_vec1 = vertices[connection2_verts[0]] - connection_vert2;
                Vector3d con2_vec2 = vertices[connection2_verts[1]] - connection_vert2;

                Vector3d vert1_direction = new Vector3d();
                Vector3d vert2_direction = new Vector3d();
                double dist = 0;
                if (!(Math.Abs(con1_vec1.X - con1_vec2.X) < 1e-2))
                {
                    vert1_direction.X = 0;
                    vert1_direction.Y = con1_vec1.Y;
                    vert1_direction.Z = con1_vec1.Z;
                    dist = Math.Sqrt(Math.Pow(y_dist * 3 / 8, 2) + Math.Pow(z_dist * 3 / 8, 2));
                }
                else if (!(Math.Abs(con1_vec1.Y - con1_vec2.Y) < 1e-2))
                {
                    vert1_direction.X = con1_vec1.X;
                    vert1_direction.Y = 0;
                    vert1_direction.Z = con1_vec1.Z;
                    dist = Math.Sqrt(Math.Pow(x_dist * 3 / 8, 2) + Math.Pow(z_dist * 3 / 8, 2));
                }
                else if (!(Math.Abs(con1_vec1.Z - con1_vec2.Z) < 1e-2))
                {
                    vert1_direction.X = con1_vec1.X;
                    vert1_direction.Y = con1_vec1.Y;
                    vert1_direction.Z = 0;
                    dist = Math.Sqrt(Math.Pow(x_dist * 3 / 8, 2) + Math.Pow(y_dist * 3 / 8, 2));
                }


                if (!(Math.Abs(con2_vec1.X - (con2_vec2.X)) < 1e-2))
                {
                    vert2_direction.X = 0;
                    vert2_direction.Y = con2_vec1.Y;
                    vert2_direction.Z = con2_vec1.Z;
                }
                else if (!(Math.Abs(con2_vec1.Y - (con2_vec2.Y)) < 1e-2))
                {
                    vert2_direction.X = con2_vec1.X;
                    vert2_direction.Y = 0;
                    vert2_direction.Z = con2_vec1.Z;
                }
                else if (!(Math.Abs(con2_vec1.Z - (con2_vec2.Z)) < 1e-2))
                {
                    vert2_direction.X = con2_vec1.X;
                    vert2_direction.Y = con2_vec1.Y;
                    vert2_direction.Z = 0;
                }

                vert1_direction.Unitize();
                vert2_direction.Unitize();

                Point3d vert1 = new Point3d(); // The new vertex location closest to the pinch point (will be shared)
                vert1.X = connection_vert1.X + vert1_direction.X * dist;
                vert1.Y = connection_vert1.Y + vert1_direction.Y * dist;
                vert1.Z = connection_vert1.Z + vert1_direction.Z * dist;
                Point3d vert2 = new Point3d(); // The new vertex location furthest from the pinch point
                vert2.X = connection_vert2.X + vert2_direction.X * dist;
                vert2.Y = connection_vert2.Y + vert2_direction.Y * dist;
                vert2.Z = connection_vert2.Z + vert2_direction.Z * dist;

                int new_idx1 = vertices.Count;
                bool add_vert1 = true;
                bool add_vert2 = true;

                for (int j = vertices.Count - 1; j >= 0; --j)
                {
                    //if (vertices[j] == null)
                    //    continue;
                    Point3d vert = vertices[j];
                    if (vert.EpsilonEquals(vert1, 1e-2))
                    {
                        new_idx1 = j;
                        add_vert1 = false;
                        break;
                    }
                }
                if (add_vert1)
                {
                    //Vertex temp_vertex = new Vertex(new_idx1, vert1);
                    //vertex_list.Add(temp_vertex);
                    vertices.Add(vert1);
                }

                int new_idx2 = vertices.Count;
                for (int j = vertices.Count - 1; j >= 0; --j)
                {
                    //if (vertices[j] == null)
                    //    continue;
                    Point3d vert = vertices[j];
                    if (vert.EpsilonEquals(vert2, 1e-2))
                    {
                        new_idx2 = j;
                        add_vert2 = false;
                        break;
                    }
                }
                if (add_vert2)
                {
                    //Vertex temp_vertex = new Vertex(new_idx2, vert2);
                    //vertex_list.Add(temp_vertex);
                    vertices.Add(vert2);
                }

                List<int> hex_verts = new List<int>();
                hex_verts.Add(pinch_point_idx);
                hex_verts.AddRange(connection);
                hex_verts.AddRange(free);
                hex_verts.Add(new_idx1);
                hex_verts.Add(new_idx2);
                hex_verts.Sort();

                //hex = new Hex(hex_verts[0], hex_verts[1], hex_verts[2], hex_verts[3], hex_verts[4], hex_verts[5], hex_verts[6], hex_verts[7]);

                List<int> face1_verts = new List<int>(face1);
                List<int> face2_verts = new List<int>(face2);
                face1_verts.Remove(pinch_point_idx);
                face1_verts.Remove(connection[0]);
                face2_verts.Remove(pinch_point_idx);
                face2_verts.Remove(connection[0]);
                int a1 = -1;
                int b1 = -1;
                int a2 = -1;
                int b2 = -1;
                if (connection1_verts.Contains(face1_verts[0]))
                {
                    a1 = 0;
                    b1 = 1;
                }
                else
                {
                    a1 = 1;
                    b1 = 0;
                }

                if (connection1_verts.Contains(face2_verts[0]))
                {
                    a2 = 0;
                    b2 = 1;
                }
                else
                {
                    a2 = 1;
                    b2 = 0;
                }

                // TODO: Change the way this is structured so that I can use it for pinch point removal
                List<int> f1 = new List<int> { pinch_point_idx, connection1_verts[0], new_idx1, connection1_verts[1] };
                List<int> f2 = new List<int> { connection[0], connection2_verts[0], new_idx2, connection2_verts[1] };
                List<int> f3 = new List<int> { face1_verts[a1], new_idx1, new_idx2, face1_verts[b1] };
                List<int> f4 = new List<int> { face2_verts[a2], new_idx1, new_idx2, face2_verts[b2] };
                List<List<int>> new_faces = new List<List<int>> { f1, f2, f3, f4 };

                hex = new List<int> { pinch_point_idx, face1_verts[a1], new_idx1, face2_verts[a2],
                    connection[0], face1_verts[b1], new_idx2, face2_verts[b2] };

                return new_faces;
            }

            public static List<QuadFace> ConnectFacesWithTwoHexes(QuadFace face1, QuadFace face2, int pinch_point_idx, double x_dist, double y_dist, double z_dist, VertexList vertex_list, out Hex hex1_out, out Hex hex2_out)
            {
                List<int> both_faces_verts = new List<int>();
                both_faces_verts.AddRange(face1.getQaudFace());
                both_faces_verts.AddRange(face2.getQaudFace());

                Point3d pinch_point = vertex_list[pinch_point_idx].getLocation();

                Vector3d normal1 = face1.Normal(vertex_list);

                List<int> connection = both_faces_verts.GroupBy(x => x)
                                           .Where(g => g.Count() == 2)
                                           .Select(g => g.Key)
                                           .ToList();

                List<int> face1_free = face1.getQaudFace();
                face1_free.Remove(connection[0]);
                face1_free.Remove(connection[1]);
                List<int> face2_free = face2.getQaudFace();
                face2_free.Remove(connection[0]);
                face2_free.Remove(connection[1]);

                List<double> vert1_dist = new List<double>();
                List<double> vert2_dist = new List<double>();
                foreach(var vert1 in face1_free)
                {
                    Point3d vert1_location = vertex_list[vert1].getLocation();
                    double distance = pinch_point.DistanceTo(vert1_location);
                    vert1_dist.Add(distance);
                }
                foreach(var vert2 in face2_free)
                {
                    Point3d vert2_location = vertex_list[vert2].getLocation();
                    double distance = pinch_point.DistanceTo(vert2_location);
                    vert2_dist.Add(distance);
                }

                int i1 = vert1_dist.IndexOf(vert1_dist.Min());
                int i2 = vert2_dist.IndexOf(vert2_dist.Min());

                Point3d free_point1 = vertex_list[face1_free[i1]].getLocation();
                Point3d free_point2 = vertex_list[face2_free[i2]].getLocation();

                connection.Remove(pinch_point_idx);

                Vector3d connection_vector = vertex_list[connection[0]].getLocation() - pinch_point;
                //connection_vector.Unitize();

                Point3d edge_vert = new Point3d();
                Point3d new_point = new Point3d();
                int edge_idx = -1;
                bool edge_found = false;
                if (Math.Abs(connection_vector.X) > 1e-5)
                {
                    edge_vert.X = pinch_point.X + connection_vector.X / 4;
                    edge_vert.Y = free_point1.Y;
                    edge_vert.Z = free_point2.Z;

                    for (int j = vertex_list.Count - 1; j >= 0; --j)
                    {
                        if (vertex_list[j] == null)
                            continue;
                        Point3d vert = vertex_list[j].getLocation();
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }
;
                    if (!edge_found)
                    {
                        edge_vert.Y = free_point2.Y;
                        edge_vert.Z = free_point1.Z;

                        for (int j = vertex_list.Count - 1; j >= 0; --j)
                        {
                            if (vertex_list[j] == null)
                                continue;
                            Point3d vert = vertex_list[j].getLocation();
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = new Point3d(edge_vert);
                    new_point.X += connection_vector.X / 2;
                }
                else if (Math.Abs(connection_vector.Y) > 1e-5)
                {
                    edge_vert.X = free_point1.X;
                    edge_vert.Y = pinch_point.Y + connection_vector.Y / 4;
                    edge_vert.Z = free_point2.Z;

                    for (int j = vertex_list.Count - 1; j >= 0; --j)
                    {
                        if (vertex_list[j] == null)
                            continue;
                        Point3d vert = vertex_list[j].getLocation();
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }

                    if (!edge_found)
                    {
                        edge_vert.X = free_point2.X;
                        edge_vert.Z = free_point1.Z;

                        for (int j = vertex_list.Count - 1; j >= 0; --j)
                        {
                            if (vertex_list[j] == null)
                                continue;
                            Point3d vert = vertex_list[j].getLocation();
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = edge_vert;
                    new_point.Y += connection_vector.Y / 2;
                }
                else if (Math.Abs(connection_vector.Z) > 1e-5) // Fix y and z to work like x
                {
                    edge_vert.X = free_point1.X;
                    edge_vert.Y = free_point2.Y;
                    edge_vert.Z = pinch_point.Z + connection_vector.Z / 4;

                    for (int j = vertex_list.Count - 1; j >= 0; --j)
                    {
                        if (vertex_list[j] == null)
                            continue;
                        Point3d vert = vertex_list[j].getLocation();
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }

                    if (!edge_found)
                    {
                        edge_vert.X = free_point2.X;
                        edge_vert.Y = free_point1.Y;

                        for (int j = vertex_list.Count - 1; j >= 0; --j)
                        {
                            if (vertex_list[j] == null)
                                continue;
                            Point3d vert = vertex_list[j].getLocation();
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = edge_vert;
                    new_point.Z += connection_vector.Z / 2;
                }
                if (!edge_found)
                    throw new Exception(string.Format("The edge vertex:{0} was not found in the existing vertices", edge_vert));

                int new_idx = vertex_list.Count;
                Vertex new_vert = new Vertex(new_idx, new_point);
                vertex_list.Add(new_vert);

                QuadFace new_face = new QuadFace(pinch_point_idx, edge_idx, new_idx, connection[0]);

                List<QuadFace> faces = new List<QuadFace>();
                List<QuadFace> faces1 = ConnectFacesWithOneHex(face1, new_face, pinch_point_idx, x_dist, y_dist, z_dist, vertex_list, out hex1_out);
                List<QuadFace> faces2 = ConnectFacesWithOneHex(face2, new_face, pinch_point_idx, x_dist, y_dist, z_dist, vertex_list, out hex2_out);

                faces.AddRange(faces1);
                faces.AddRange(faces2);

                return faces;
            }

            public static List<List<int>> ConnectFacesWithTwoHexes(List<int> face1, List<int> face2, int pinch_point_idx, double x_dist, double y_dist, double z_dist, List<Point3d> vertices, out List<int> hex1_out, out List<int> hex2_out)
            {
                List<int> both_faces_verts = new List<int>();
                both_faces_verts.AddRange(face1);
                both_faces_verts.AddRange(face2);

                Point3d pinch_point = vertices[pinch_point_idx];

                Vector3d normal1 = Normal(face1, vertices);

                List<int> connection = both_faces_verts.GroupBy(x => x)
                                           .Where(g => g.Count() == 2)
                                           .Select(g => g.Key)
                                           .ToList();

                List<int> face1_free = new List<int>(face1);
                face1_free.Remove(connection[0]);
                face1_free.Remove(connection[1]);
                List<int> face2_free = new List<int>(face2);
                face2_free.Remove(connection[0]);
                face2_free.Remove(connection[1]);

                List<double> vert1_dist = new List<double>();
                List<double> vert2_dist = new List<double>();
                foreach (var vert1 in face1_free)
                {
                    Point3d vert1_location = vertices[vert1];
                    double distance = pinch_point.DistanceTo(vert1_location);
                    vert1_dist.Add(distance);
                }
                foreach (var vert2 in face2_free)
                {
                    Point3d vert2_location = vertices[vert2];
                    double distance = pinch_point.DistanceTo(vert2_location);
                    vert2_dist.Add(distance);
                }

                int i1 = vert1_dist.IndexOf(vert1_dist.Min());
                int i2 = vert2_dist.IndexOf(vert2_dist.Min());

                Point3d free_point1 = new Point3d(vertices[face1_free[i1]]);
                Point3d free_point2 = new Point3d(vertices[face2_free[i2]]);

                connection.Remove(pinch_point_idx);

                Vector3d connection_vector = vertices[connection[0]] - pinch_point;
                //connection_vector.Unitize();

                Point3d edge_vert = new Point3d();
                Point3d new_point = new Point3d();
                int edge_idx = -1;
                bool edge_found = false;
                if (Math.Abs(connection_vector.X) > 1e-5)
                {
                    edge_vert.X = pinch_point.X + connection_vector.X / 4;
                    edge_vert.Y = free_point1.Y;
                    edge_vert.Z = free_point2.Z;

                    for (int j = vertices.Count - 1; j >= 0; --j)
                    {
                        //if (vertex_list[j] == null)
                        //    continue;
                        Point3d vert = vertices[j];
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }
;
                    if (!edge_found)
                    {
                        edge_vert.Y = free_point2.Y;
                        edge_vert.Z = free_point1.Z;

                        for (int j = vertices.Count - 1; j >= 0; --j)
                        {
                            //if (vertex_list[j] == null)
                            //    continue;
                            Point3d vert = vertices[j];
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = new Point3d(edge_vert);
                    new_point.X += connection_vector.X / 2;
                }
                else if (Math.Abs(connection_vector.Y) > 1e-5)
                {
                    edge_vert.X = free_point1.X;
                    edge_vert.Y = pinch_point.Y + connection_vector.Y / 4;
                    edge_vert.Z = free_point2.Z;

                    for (int j = vertices.Count - 1; j >= 0; --j)
                    {
                        //if (vertex_list[j] == null)
                        //    continue;
                        Point3d vert = vertices[j];
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }

                    if (!edge_found)
                    {
                        edge_vert.X = free_point2.X;
                        edge_vert.Z = free_point1.Z;

                        for (int j = vertices.Count - 1; j >= 0; --j)
                        {
                            //if (vertex_list[j] == null)
                            //    continue;
                            Point3d vert = vertices[j];
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = edge_vert;
                    new_point.Y += connection_vector.Y / 2;
                }
                else if (Math.Abs(connection_vector.Z) > 1e-5) // Fix y and z to work like x
                {
                    edge_vert.X = free_point1.X;
                    edge_vert.Y = free_point2.Y;
                    edge_vert.Z = pinch_point.Z + connection_vector.Z / 4;

                    for (int j = vertices.Count - 1; j >= 0; --j)
                    {
                        //if (verticest[j] == null)
                        //    continue;
                        Point3d vert = vertices[j];
                        if (vert.EpsilonEquals(edge_vert, 1e-2))
                        {
                            edge_idx = j;
                            edge_found = true;
                            break;
                        }
                    }

                    if (!edge_found)
                    {
                        edge_vert.X = free_point2.X;
                        edge_vert.Y = free_point1.Y;

                        for (int j = vertices.Count - 1; j >= 0; --j)
                        {
                            //if (vertex_list[j] == null)
                            //    continue;
                            Point3d vert = vertices[j];
                            if (vert.EpsilonEquals(edge_vert, 1e-2))
                            {
                                edge_idx = j;
                                edge_found = true;
                                break;
                            }
                        }
                    }

                    new_point = edge_vert;
                    new_point.Z += connection_vector.Z / 2;
                }
                if (!edge_found)
                    throw new Exception(string.Format("The edge vertex:{0} was not found in the existing vertices", edge_vert));

                int new_idx = vertices.Count;
                //Vertex new_vert = new Vertex(new_idx, new_point);
                //vertex_list.Add(new_vert);
                vertices.Add(new_point);

                List<int> new_face = new List<int> { pinch_point_idx, edge_idx, new_idx, connection[0] };

                List<List<int>> faces = new List<List<int>>();
                List<List<int>> faces1 = ConnectFacesWithOneHex(face1, new_face, pinch_point_idx, x_dist, y_dist, z_dist, vertices, out hex1_out);
                List<List<int>> faces2 = ConnectFacesWithOneHex(face2, new_face, pinch_point_idx, x_dist, y_dist, z_dist, vertices, out hex2_out);

                faces.AddRange(faces1);
                faces.AddRange(faces2);

                return faces;
            }

            public static List<Vertex> Compact(List<Vertex> vertices, List<Hex> hexes)
            {
                List<int> hex_verts = new List<int>();
                foreach (Hex hex in hexes)
                    hex_verts.AddRange(hex.GetVertices());

                List<int> remove_verts_at = new List<int>();
                for(int i = 0; i < vertices.Count; i++)
                {
                    if (!hex_verts.Contains(i))
                        remove_verts_at.Add(i);
                }

                foreach (int i in remove_verts_at)
                    vertices[i] = null;

                return vertices;

            }

            public static int ClosestVertex(Point3d point, VertexList vertex_list, List<Tetrahedral> tets)
            {
                double min_dist = double.MaxValue;
                int min_idx = -1;
                foreach (Tetrahedral tet in tets)
                {
                    List<int> verts = new List<int> { tet.A, tet.B, tet.C };
                    foreach(int v in verts)
                    {
                        Point3d p = vertex_list[v].getLocation();
                        if (point.DistanceTo(p) < min_dist)
                        {
                            min_idx = v;
                            min_dist = point.DistanceTo(p);
                        }
                    }
                }

                return min_idx;
            }

            public static int ClosestVertex(Point3d point, VertexList vertex_list, List<Hex> hexes)
            {
                double min_dist = double.MaxValue;
                int min_idx = -1;
                foreach(Hex hex in hexes)
                {
                    List<int> verts = hex.GetVertices();
                    foreach(int v in verts)
                    {
                        Point3d p = vertex_list[v].getLocation();
                        if (point.DistanceTo(p) < min_dist)
                        {
                            min_idx = v;
                            min_dist = point.DistanceTo(p);
                        }
                    }
                }

                return min_idx;
            }

            public static List<List<int>> GetHexFaces(List<int> hex, bool sort)
            {
                List<int> face1 = new List<int> { hex[0], hex[1], hex[5], hex[4] };
                List<int> face2 = new List<int> { hex[1], hex[2], hex[6], hex[5] };
                List<int> face3 = new List<int> { hex[2], hex[3], hex[7], hex[6] };
                List<int> face4 = new List<int> { hex[0], hex[4], hex[7], hex[3] };
                List<int> face5 = new List<int> { hex[0], hex[3], hex[2], hex[1] };
                List<int> face6 = new List<int> { hex[4], hex[5], hex[6], hex[7] };

                if (sort)
                {
                    face1.Sort();
                    face2.Sort();
                    face3.Sort();
                    face4.Sort();
                    face5.Sort();
                    face6.Sort();
                }

                List<List<int>> faces = new List<List<int>> { face1, face2, face3, face4, face5, face6 };
                return faces;
            }

            public static Dictionary<int, List<int>> MakeConnectedVertexDict(List<List<int>> hexes)
            {
                Dictionary<int, List<int>> connected_verts = new Dictionary<int, List<int>>();
                foreach (List<int> hex in hexes)
                {
                    if (!connected_verts.TryAdd(hex[0], new List<int> { hex[1], hex[3], hex[4] }))
                    {
                        connected_verts[hex[0]].Add(hex[1]);
                        connected_verts[hex[0]].Add(hex[3]);
                        connected_verts[hex[0]].Add(hex[4]);
                    }

                    if (!connected_verts.TryAdd(hex[1], new List<int> { hex[0], hex[2], hex[5] }))
                    {
                        connected_verts[hex[1]].Add(hex[0]);
                        connected_verts[hex[1]].Add(hex[2]);
                        connected_verts[hex[1]].Add(hex[5]);
                    }

                    if (!connected_verts.TryAdd(hex[2], new List<int> { hex[1], hex[3], hex[6] }))
                    {
                        connected_verts[hex[2]].Add(hex[1]);
                        connected_verts[hex[2]].Add(hex[3]);
                        connected_verts[hex[2]].Add(hex[6]);
                    }

                    if (!connected_verts.TryAdd(hex[3], new List<int> { hex[0], hex[2], hex[7] }))
                    {
                        connected_verts[hex[3]].Add(hex[0]);
                        connected_verts[hex[3]].Add(hex[2]);
                        connected_verts[hex[3]].Add(hex[7]);
                    }

                    if (!connected_verts.TryAdd(hex[4], new List<int> { hex[0], hex[5], hex[7] }))
                    {
                        connected_verts[hex[4]].Add(hex[0]);
                        connected_verts[hex[4]].Add(hex[5]);
                        connected_verts[hex[4]].Add(hex[7]);
                    }

                    if (!connected_verts.TryAdd(hex[5], new List<int> { hex[1], hex[4], hex[6] }))
                    {
                        connected_verts[hex[5]].Add(hex[1]);
                        connected_verts[hex[5]].Add(hex[4]);
                        connected_verts[hex[5]].Add(hex[6]);
                    }

                    if (!connected_verts.TryAdd(hex[6], new List<int> { hex[2], hex[5], hex[7] }))
                    {
                        connected_verts[hex[6]].Add(hex[2]);
                        connected_verts[hex[6]].Add(hex[5]);
                        connected_verts[hex[6]].Add(hex[7]);
                    }

                    if (!connected_verts.TryAdd(hex[7], new List<int> { hex[3], hex[4], hex[6]  }))
                    {
                        connected_verts[hex[7]].Add(hex[3]);
                        connected_verts[hex[7]].Add(hex[4]);
                        connected_verts[hex[7]].Add(hex[6]);
                    }
                }

                foreach (int key in connected_verts.Keys)
                {
                    List<int> list = new List<int>(connected_verts[key]);

                    list = list.GroupBy(x => x).Select(x => x.Key).ToList();

                    connected_verts[key] = list;
                }

                return connected_verts;
            }

            public static Point3d GetCentroid(List<int> quad_face, List<Point3d> vertices)
            {
                Point3d A = vertices[quad_face[0]];
                Point3d B = vertices[quad_face[1]];
                Point3d C = vertices[quad_face[2]];
                Point3d D = vertices[quad_face[3]];

                Point3d centroid = (A + B + C + D) / 4;

                return centroid;
            }

            public static Point3d GetCentroid(List<Point3d> vertices, List<int> hex)
            {
                Point3d A = vertices[hex[0]];
                Point3d B = vertices[hex[1]];
                Point3d C = vertices[hex[2]];
                Point3d D = vertices[hex[3]];
                Point3d E = vertices[hex[4]];
                Point3d F = vertices[hex[5]];
                Point3d G = vertices[hex[6]];
                Point3d H = vertices[hex[7]];

                Point3d centroid = (A + B + C + D + E + F + G + H) / 8;
                return centroid;
            }

            public static Vector3d Normal(List<int> quad_face, List<Point3d> vertices)
            {
                Point3d A = vertices[quad_face[0]];
                Point3d B = vertices[quad_face[1]];
                Point3d C = vertices[quad_face[2]];

                Vector3d AB = B - A;
                Vector3d AC = C - A;

                Vector3d normal = Vector3d.CrossProduct(AC, AB);
                normal.Unitize();

                return normal;
            }

            public static bool IsSquare(List<int> hex, List<Point3d> vertices)
            {
                List<Point3d> points = new List<Point3d>();

                for (int i = 0; i < hex.Count; i++)
                {
                    points.Add(vertices[hex[i]]);
                }

                List<double> x = new List<double>();
                List<double> y = new List<double>();
                List<double> z = new List<double>();
                foreach (Point3d point in points)
                {
                    x.Add(Math.Round(point.X, 5));
                    y.Add(Math.Round(point.Y, 5));
                    z.Add(Math.Round(point.Z, 5));
                }

                x = x.GroupBy(x => x).Select(x => x.Key).ToList();
                y = y.GroupBy(y => y).Select(y => y.Key).ToList();
                z = z.GroupBy(z => z).Select(z => z.Key).ToList();

                if (x.Count == 2 && y.Count == 2 && z.Count == 2)
                    return true;
                else
                    return false;
            }

            public static List<int> GetAdjacentVertices(int start, int x_div, int y_div, int z_div)
            {
                List<int> adjacent_vertices = new List<int>();
                adjacent_vertices.Add(start + ((z_div + 1) * (y_div + 1)));                     // 0
                adjacent_vertices.Add(start + (z_div + 1) + ((z_div + 1) * (y_div + 1)));       // 1
                adjacent_vertices.Add(start + (z_div + 1));                                     // 2
                adjacent_vertices.Add(start + 1);                                               // 3
                adjacent_vertices.Add(start - 1);                                               // 4
                adjacent_vertices.Add(adjacent_vertices[0] + 1);                                // 5
                adjacent_vertices.Add(adjacent_vertices[1] + 1);                                // 6
                adjacent_vertices.Add(adjacent_vertices[2] + 1);                                // 7
                adjacent_vertices.Add(adjacent_vertices[0] - 1);                                // 8
                adjacent_vertices.Add(adjacent_vertices[1] - 1);                                // 9
                adjacent_vertices.Add(adjacent_vertices[2] - 1);                                // 10
                adjacent_vertices.Add(start - ((z_div + 1) * (y_div + 1)));                     // 11
                adjacent_vertices.Add(start - (z_div + 1) - ((z_div + 1) * (y_div + 1)));       // 12
                adjacent_vertices.Add(start - (z_div + 1));                                     // 13
                adjacent_vertices.Add(adjacent_vertices[11] + 1);                               // 14
                adjacent_vertices.Add(adjacent_vertices[12] + 1);                               // 15
                adjacent_vertices.Add(adjacent_vertices[13] + 1);                               // 16
                adjacent_vertices.Add(adjacent_vertices[11] - 1);                               // 17
                adjacent_vertices.Add(adjacent_vertices[12] - 1);                               // 18
                adjacent_vertices.Add(adjacent_vertices[13] - 1);                               // 19
                adjacent_vertices.Add(adjacent_vertices[13] + ((z_div + 1) * (y_div + 1)));     // 20
                adjacent_vertices.Add(adjacent_vertices[20] + 1);                               // 21 
                adjacent_vertices.Add(adjacent_vertices[20] - 1);                               // 22
                adjacent_vertices.Add(adjacent_vertices[2] - ((z_div + 1) * (y_div + 1)));      // 23
                adjacent_vertices.Add(adjacent_vertices[23] + 1);                               // 24
                adjacent_vertices.Add(adjacent_vertices[23] - 1);                               // 25

                return adjacent_vertices;
            }

            public static List<int> GetAdjacentHexes(int hex, int x_div, int y_div, int z_div)
            {
                List<int> adjacent_hexes = new List<int>();

                adjacent_hexes.Add(hex + 1);            // 1
                adjacent_hexes.Add(hex - 1);            // 2
                adjacent_hexes.Add(hex - z_div);        // 3
                adjacent_hexes.Add(hex + z_div);        // 4
                adjacent_hexes.Add(hex - z_div + 1);    // 5
                adjacent_hexes.Add(hex - z_div - 1);    // 6
                adjacent_hexes.Add(hex + z_div + 1);    // 7
                adjacent_hexes.Add(hex - z_div - 1);    // 8
                adjacent_hexes.Add(hex + z_div * y_div);    // 9
                adjacent_hexes.Add(hex - z_div * y_div);    //10
                adjacent_hexes.Add(hex + z_div * y_div + 1);    // 11
                adjacent_hexes.Add(hex + z_div * y_div - 1);    // 12
                adjacent_hexes.Add(hex - z_div * y_div + 1);    // 13
                adjacent_hexes.Add(hex - z_div * y_div + 1);    // 14
                adjacent_hexes.Add(hex + z_div * y_div + z_div);    // 15
                adjacent_hexes.Add(hex + z_div * y_div - z_div);    // 16
                adjacent_hexes.Add(hex - z_div * y_div + z_div);    // 17
                adjacent_hexes.Add(hex + z_div * y_div - z_div);    // 18
                adjacent_hexes.Add(hex + z_div * y_div + z_div + 1);    // 19
                adjacent_hexes.Add(hex + z_div * y_div - z_div + 1);    // 20
                adjacent_hexes.Add(hex - z_div * y_div + z_div + 1);    // 21
                adjacent_hexes.Add(hex + z_div * y_div - z_div + 1);    // 22
                adjacent_hexes.Add(hex + z_div * y_div + z_div - 1);    // 23
                adjacent_hexes.Add(hex + z_div * y_div - z_div - 1);    // 24
                adjacent_hexes.Add(hex - z_div * y_div + z_div - 1);    // 25
                adjacent_hexes.Add(hex + z_div * y_div - z_div - 1);    // 26

                return adjacent_hexes;
            }

            public static List<int> GetAdjacentFaces(int face, Mesh mesh)
            {
                List<int> connected_faces = new List<int>();
                List<int> vert_idxs = new List<int> { mesh.Faces[face].A, mesh.Faces[face].B, mesh.Faces[face].C, mesh.Faces[face].D };
                foreach (int vert in vert_idxs)
                {
                    int[] faces = mesh.TopologyVertices.ConnectedFaces(vert);
                    connected_faces.AddRange(faces);
                }

                connected_faces.GroupBy(x => x).Where(g => g.Count() > 0).Select(g => g.Key).ToList();
                connected_faces.Remove(face);

                return connected_faces;
            }

            public static List<List<int>> FindAllPaths(HashSet<Tuple<int, int>> edges, 
                int start, int end)
            {
                Dictionary<int, List<int>> adjacency_list = new Dictionary<int, List<int>>();

                foreach (var edge in edges)
                {
                    if (!adjacency_list.ContainsKey(edge.Item1))
                        adjacency_list[edge.Item1] = new List<int>();
                    adjacency_list[edge.Item1].Add(edge.Item2);

                    if (!adjacency_list.ContainsKey(edge.Item2))
                        adjacency_list[edge.Item2] = new List<int>();
                    adjacency_list[edge.Item2].Add(edge.Item1);
                }

                List<List<int>> all_paths = new List<List<int>>();
                List<int> current_path = new List<int>();
                HashSet<int> visited = new HashSet<int>();

                DFS(adjacency_list, start, end, visited, current_path, all_paths);

                return all_paths;
            }

            public static void DFS(Dictionary<int, List<int>> adjacency_list, 
                int current_vertex, int end_vertex, HashSet<int> visited, 
                List<int> current_path, List<List<int>> all_paths)
            {
                visited.Add(current_vertex);
                current_path.Add(current_vertex);

                if (current_vertex == end_vertex)
                {
                    all_paths.Add(new List<int>(current_path));
                }
                else
                {
                    if (adjacency_list.ContainsKey(current_vertex))
                    {
                        foreach (int neighbor in adjacency_list[current_vertex])
                        {
                            if (!visited.Contains(neighbor))
                            {
                                DFS(adjacency_list, neighbor, end_vertex, visited, current_path, all_paths);
                            }
                        }
                    }
                }

                // Backtrack: remove the current vertex from the path and mark it as unvisited
                visited.Remove(current_vertex);
                current_path.RemoveAt(current_path.Count - 1);
            }

            public static void ConnectByVertexPath(Mesh background, Mesh sculpted_mesh, List<int> faces_to_keep, List<int> path, Dictionary<Point3d, int> location_to_index)
            {
                double x_dist = new double();
                double y_dist = new double();
                for (int i = 0; i < background.Vertices.Count; i++)
                {
                    Point3d vertex = background.Vertices[i];
                    if (i == 1)
                    {
                        x_dist = vertex.X - background.Vertices[0].X;
                    }
                    else if ((vertex.Y - background.Vertices[0].Y) > 1e-5)
                    {
                        y_dist = vertex.Y - background.Vertices[0].Y;
                        break;
                    }
                }

                // Mesh edges with Trapazoids
                //Mesh trapazoids = new Mesh();
                for(int i = 0; i < path.Count - 1; i++)
                {
                    int v1 = path[i];
                    int v2 = path[i + 1];
                    MeshEdge(sculpted_mesh, background, v1, v2, x_dist, y_dist, location_to_index);
                }

                int[] start_faces = background.TopologyVertices.ConnectedFaces(path[0]);
                int[] end_faces = background.TopologyVertices.ConnectedFaces(path.Last());

                foreach(int face in start_faces)
                {
                    if (faces_to_keep.Contains(face))
                    {
                        MeshComponentFace(sculpted_mesh, background, background.Faces[face],
                            path[0], path[1], x_dist, y_dist, location_to_index);
                    }
                }
                foreach(int face in end_faces)
                {
                    if(faces_to_keep.Contains(face))
                    {
                        MeshComponentFace(sculpted_mesh, background, background.Faces[face], 
                            path[path.Count - 1], path[path.Count - 2], x_dist, y_dist, location_to_index);
                    }
                }

                for (int i = 0; i < sculpted_mesh.TopologyVertices.Count; i++)
                {
                    int[] connected_faces = sculpted_mesh.TopologyVertices.ConnectedFaces(i);
                    int[] connected_edges = sculpted_mesh.TopologyVertices.ConnectedEdges(i);
                    int[] connected_vertices = sculpted_mesh.TopologyVertices.ConnectedTopologyVertices(i);

                    if (connected_faces.Length == 4 && connected_edges.Length == 6)
                    {
                        int x_counter = 0;
                        int y_counter = 0;
                        Point3d current_vertex = sculpted_mesh.TopologyVertices[i];
                        List<int> v_idxs = new List<int>();
                        foreach (var v in connected_vertices)
                        {
                            Point3d vertex = sculpted_mesh.TopologyVertices[v];
                            double distance = vertex.DistanceTo(current_vertex);

                            if (Math.Abs(distance - x_dist) < 1e-5)
                            {
                                x_counter++;
                                v_idxs.Add(v);
                            }
                            else if (Math.Abs(distance - y_dist) < 1e-5)
                            {
                                y_counter++;
                                v_idxs.Add(v);
                            }
                        }

                        if (x_counter == 2 && y_counter == 0)
                        {
                            MeshPinchVertexX(sculpted_mesh, i, x_dist, y_dist, location_to_index);
                        }
                        else if (y_counter == 2 && x_counter == 0)
                        {
                            MeshPinchVertexY(sculpted_mesh, i, x_dist, y_dist, location_to_index);
                        }
                    }
                }

                //sculpted_mesh.Append(trapazoids);

                //return trapazoids;
            }

            public static void MeshEdge(Mesh sculpted_mesh, Mesh background, int v1, 
                int v2, double x_dist, double y_dist, Dictionary<Point3d, int> l_to_i)
            {
                Point3d point0 = new Point3d();
                Point3d point1 = new Point3d();
                Point3d point2 = new Point3d();
                Point3d point3 = new Point3d();
                Point3d point4 = new Point3d();
                Point3d point5 = new Point3d();

                if (v1 < v2)
                {
                    point0 = background.Vertices[v1];
                    point1 = background.Vertices[v2];
                }
                else
                {
                    point0 = background.Vertices[v2];
                    point1 = background.Vertices[v1];
                }
                Vector3d vector = point1 - point0;

                if (Math.Abs(vector.Y) < 1e-5)
                {
                    point2 = new Point3d(point0.X + x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                    point3 = new Point3d(point2.X + x_dist / 3, point2.Y, point2.Z);
                    point4 = new Point3d(point0.X + x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                    point5 = new Point3d(point4.X + x_dist / 3, point4.Y, point4.Z);
                }
                else if (Math.Abs(vector.X) < 1e-5)
                {
                    point2 = new Point3d(point0.X - x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                    point3 = new Point3d(point2.X, point2.Y + y_dist / 3, point2.Z);
                    point4 = new Point3d(point0.X + x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                    point5 = new Point3d(point4.X, point4.Y + y_dist / 3, point4.Z);
                }

                List<Point3d> vertices = new List<Point3d> { point0, point1, point2, point3, point4, point5 };
                List<int> idxs = new List<int>();
                foreach(var vert in vertices)
                {
                    if(l_to_i.ContainsKey(RoundPoint(vert)))
                    {
                        idxs.Add(l_to_i[RoundPoint(vert)]);
                    }
                    else
                    {
                        int idx = sculpted_mesh.Vertices.Add(vert);
                        idxs.Add(idx);
                        l_to_i.Add(RoundPoint(vert), idx);
                    }
                }

                //Mesh mesh1 = new Mesh();
                //mesh1.Vertices.AddVertices(vertices);
                //mesh1.Faces.AddFace(0, 1, 3, 2);
                //mesh1.Faces.AddFace(0, 4, 5, 1);

                //trapazoids.Append(mesh1);
                //trapazoids.Compact();

                sculpted_mesh.Faces.AddFace(idxs[0], idxs[1], idxs[3], idxs[2]);
                sculpted_mesh.Faces.AddFace(idxs[0], idxs[4], idxs[5], idxs[1]);
            }

            public static void MeshComponentFace(Mesh sculpted_mesh, Mesh background, 
                MeshFace face, int v_face, int v, double x_dist, double y_dist, Dictionary<Point3d, int> l_to_i)
            {
                Vector3d vector = background.Vertices[v_face] - background.Vertices[v];

                Point3d point0 = new Point3d();
                Point3d point1 = new Point3d();
                Point3d point2 = new Point3d();
                Point3d point3 = new Point3d();

                if (Math.Abs(vector.X) < 1e-5)
                {
                    if (face.A == v_face || face.B == v_face)
                    {
                        point0 = background.Vertices[face.A];
                        point1 = background.Vertices[face.B];
                        point2 = new Point3d(point0.X + x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                        point3 = new Point3d(point2.X + x_dist / 3, point2.Y, point2.Z);
                    }
                    else if (face.C == v_face || face.D == v_face)
                    {
                        point0 = background.Vertices[face.C];
                        point1 = background.Vertices[face.D];
                        point2 = new Point3d(point0.X - x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                        point3 = new Point3d(point2.X - x_dist / 3, point2.Y, point2.Z);
                    }
                    else
                        throw new Exception("The face vertex ({0}) does not belong to the face");
                }
                else if (Math.Abs(vector.Y) < 1e-5)
                {
                    if (face.A == v_face || face.D == v_face)
                    {
                        point0 = background.Vertices[face.D];
                        point1 = background.Vertices[face.A];
                        point2 = new Point3d(point0.X - x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                        point3 = new Point3d(point2.X, point2.Y - y_dist / 3, point2.Z);
                    }
                    else if (face.B == v_face || face.C == v_face)
                    {
                        point0 = background.Vertices[face.B];
                        point1 = background.Vertices[face.C];
                        point2 = new Point3d(point0.X + x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                        point3 = new Point3d(point2.X, point2.Y + y_dist / 3, point2.Z);
                    }
                    else
                        throw new Exception("The face vertex ({0}) does not belong to the face");
                }

                List<Point3d> vertices = new List<Point3d> { point0, point1, point2, point3 };
                List<int> idxs = new List<int>();
                foreach (var vert in vertices)
                {
                    if (l_to_i.ContainsKey(RoundPoint(vert)))
                    {
                        idxs.Add(l_to_i[RoundPoint(vert)]);
                    }
                    else
                    {
                        int idx = sculpted_mesh.Vertices.Add(vert);
                        idxs.Add(idx);
                        l_to_i.Add(RoundPoint(vert), idx);
                    }
                }

                //Mesh mesh1 = new Mesh();
                //mesh1.Vertices.AddVertices(vertices);
                //mesh1.Faces.AddFace(0, 2, 3, 1);

                //trapazoids.Append(mesh1);
                //trapazoids.Compact();

                sculpted_mesh.Faces.AddFace(idxs[0], idxs[2], idxs[3], idxs[1]);
            }

            public static void MeshPinchVertexX(Mesh sculpted_mesh, int v, double x_dist, double y_dist, Dictionary<Point3d, int> l_to_i)
            {
                Point3d point0 = sculpted_mesh.Vertices[v];
                Point3d point1 = new Point3d(point0.X - x_dist / 3, point0.Y + y_dist / 3, point0.Z);
                Point3d point2 = new Point3d(point0.X, point0.Y + y_dist / 2, point0.Z);
                Point3d point3 = new Point3d(point1.X + 2 * x_dist / 3, point1.Y, point1.Z);
                Point3d point4 = new Point3d(point0.X - x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                Point3d point5 = new Point3d(point0.X, point0.Y - y_dist / 2, point0.Z);
                Point3d point6 = new Point3d(point4.X + 2 * x_dist / 3, point4.Y, point4.Z);
                
                List<Point3d> vertices = new List<Point3d> 
                { point0, point1, point2, point3, point4, point5, point6 };
                List<int> idxs = new List<int>();
                foreach (var vert in vertices)
                {
                    if (l_to_i.ContainsKey(RoundPoint(vert)))
                    {
                        idxs.Add(l_to_i[RoundPoint(vert)]);
                    }
                    else
                    {
                        int idx = sculpted_mesh.Vertices.Add(vert);
                        idxs.Add(idx);
                        l_to_i.Add(RoundPoint(vert), idx);
                    }
                }

                //Mesh mesh = new Mesh();
                //mesh.Vertices.AddVertices(points);
                //mesh.Faces.AddFace(0, 3, 2, 1);
                //mesh.Faces.AddFace(0, 4, 5, 6);

                //trapazoids.Append(mesh);
                //trapazoids.Compact();

                sculpted_mesh.Faces.AddFace(idxs[0], idxs[3], idxs[2], idxs[1]);
                sculpted_mesh.Faces.AddFace(idxs[0], idxs[4], idxs[5], idxs[6]);
            }

            public static void MeshPinchVertexY(Mesh sculpted_mesh, int v, double x_dist, double y_dist, Dictionary<Point3d, int> l_to_i)
            {
                Point3d point0 = sculpted_mesh.Vertices[v];
                Point3d point1 = new Point3d(point0.X - x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                Point3d point2 = new Point3d(point0.X - x_dist / 2, point0.Y, point0.Z);
                Point3d point3 = new Point3d(point1.X, point1.Y + 2 * y_dist / 3, point1.Z);
                Point3d point4 = new Point3d(point0.X + x_dist / 3, point0.Y - y_dist / 3, point0.Z);
                Point3d point5 = new Point3d(point0.X + x_dist / 2, point0.Y, point0.Z);
                Point3d point6 = new Point3d(point4.X, point4.Y + 2 * y_dist / 3, point4.Z);
                
                List<Point3d> vertices = new List<Point3d>
                { point0, point1, point2, point3, point4, point5, point6 };
                List<int> idxs = new List<int>();
                foreach (var vert in vertices)
                {
                    if (l_to_i.ContainsKey(RoundPoint(vert)))
                    {
                        idxs.Add(l_to_i[RoundPoint(vert)]);
                    }
                    else
                    {
                        int idx = sculpted_mesh.Vertices.Add(vert);
                        idxs.Add(idx);
                        l_to_i.Add(RoundPoint(vert), idx);
                    }
                }

                //Mesh mesh = new Mesh();
                //mesh.Vertices.AddVertices(points);
                //mesh.Faces.AddFace(0, 3, 2, 1);
                //mesh.Faces.AddFace(0, 4, 5, 6);

                //trapazoids.Append(mesh);
                //trapazoids.Compact();

                sculpted_mesh.Faces.AddFace(idxs[0], idxs[3], idxs[2], idxs[1]);
                sculpted_mesh.Faces.AddFace(idxs[0], idxs[4], idxs[5], idxs[6]);
            }

            public static void ConnectPinch(Mesh mesh, int v, Dictionary<Point3d, int> l_to_i)
            {
                var vert_edges = mesh.TopologyVertices.ConnectedEdges(v);
                //if (vert_edges.Length != 4)
                //{
                //    throw new Exception(string.Format("We somehow got a pinch point with {0} connected edges", vert_edges.Length));
                //}

                var edge_list = mesh.TopologyEdges;
                var vert_list = mesh.TopologyVertices;
                // made with assumption that
                IndexPair edge1 = edge_list.GetTopologyVertices(vert_edges[0]); // edge1 is in positive y direction  from vertex
                IndexPair edge2 = edge_list.GetTopologyVertices(vert_edges[1]);
                IndexPair edge3 = edge_list.GetTopologyVertices(vert_edges[2]);
                IndexPair edge4 = edge_list.GetTopologyVertices(vert_edges[3]);
                List<IndexPair> edges = new List<IndexPair>();
                edges.Add(edge1);
                edges.Add(edge2);
                edges.Add(edge3);
                edges.Add(edge4);

                int[] edge1_face = edge_list.GetConnectedFaces(vert_edges[0]);
                int[] edge2_face = edge_list.GetConnectedFaces(vert_edges[1]);
                int[] edge3_face = edge_list.GetConnectedFaces(vert_edges[2]);
                int[] edge4_face = edge_list.GetConnectedFaces(vert_edges[3]);
                // Made with assumption that the following is always true
                Point3d edge1_vert1 = vert_list[edge1[0]]; // unique
                Point3d edge1_vert2 = vert_list[edge1[1]]; // shared
                Point3d edge2_vert1 = vert_list[edge2[0]]; // unique
                Point3d edge2_vert2 = vert_list[edge2[1]]; // shared
                Point3d edge3_vert1 = vert_list[edge3[0]]; // shared
                Point3d edge3_vert2 = vert_list[edge3[1]]; // unique
                Point3d edge4_vert1 = vert_list[edge4[0]]; // shared
                Point3d edge4_vert2 = vert_list[edge4[1]]; // unique

                List<Point3d> points = new List<Point3d>();
                points.Add(edge1_vert1);
                points.Add(edge2_vert1);
                points.Add(edge3_vert2);
                points.Add(edge4_vert2);

                Point3d shared_point = vert_list[v];

                List<Vector2d> vectors = new List<Vector2d>();
                Vector2d zero = new Vector2d(0, 0);
                for (int i = 0; i < points.Count; ++i)
                {
                    Vector2d vector = new Vector2d();
                    vector.X = shared_point.X - points[i].X;
                    vector.Y = shared_point.Y - points[i].Y;

                    if (vector != zero)
                        vectors.Add(vector);
                }

                Point3d vert0 = new Point3d();
                Point3d vert1 = new Point3d();
                Point3d vert2 = new Point3d();
                Point3d vert3 = new Point3d();
                Point3d vert4 = new Point3d();
                Point3d vert5 = new Point3d();
                double x = shared_point.X;
                double y = shared_point.Y;
                double l1 = Math.Sqrt(Math.Pow(vectors[0].X, 2) + Math.Pow(vectors[0].Y, 2));
                double l2 = Math.Sqrt(Math.Pow(vectors[1].X, 2) + Math.Pow(vectors[1].Y, 2));

                // add faces to remove pinch points
                if (edge1_face[0] == edge2_face[0])
                {
                    //edge1_face == edge2_face and edge3_face = edge4_face
                    //edge1 corresponds to edge4
                    //create mesh on this side to make it nonmanifold
                    vert0 = new Point3d((x - l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x - 2 * l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x - l2 / 3), (y + 2 * l1 / 3), shared_point.Z);
                    vert3 = points[3];
                    vert4 = points[1];
                    vert5 = shared_point;

                    List<Point3d> verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5 };
                    List<int> idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[4], idxs[5]);
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[3], idxs[2]);
                    mesh.Compact();

                    //edge2 corresponds to edge3
                    //create mesh on other side to make it nonmanifold
                    vert0 = new Point3d((x + l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x + 2 * l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x + l2 / 3), (y - 2 * l1 / 3), shared_point.Z);
                    vert3 = points[0];
                    vert4 = points[2];

                    verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5 };
                    idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[4], idxs[5]);
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[3], idxs[2]);
                    mesh.Compact();
                }
                else if (edge1_face[0] == edge3_face[0])
                {
                    //edge1_face == edge4_face and edge2_face == edge3_face
                    //edge1 corresponds to edge2
                    vert0 = new Point3d((x + l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x + 2 * l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x + l2 / 3), (y + 2 * l1 / 3), shared_point.Z);
                    vert3 = points[3];
                    vert4 = points[2];
                    vert5 = shared_point;

                    List<Point3d> verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5 };
                    List<int> idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[4], idxs[1]);
                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[3], idxs[5]);
                    mesh.Compact();

                    //edge3 corresponds to edge4
                    vert0 = new Point3d((x - l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x - 2 * l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x - l2 / 3), (y - 2 * l1 / 3), shared_point.Z);
                    vert3 = points[0];
                    vert4 = points[1];

                    verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5 };
                    idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[4], idxs[1]);
                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[3], idxs[5]);
                    mesh.Compact();
                }
            }

            public static void SeparatePinch(Mesh mesh, int v, List<int> remove_face_at, Dictionary<Point3d, int> l_to_i)
            {
                var vert_list = mesh.TopologyVertices;
                var edge_list = mesh.TopologyEdges;
                var vert_edges = vert_list.ConnectedEdges(v);
                //if (vert_edges.Length != 4)
                //{
                //    throw new Exception("We somehow got a pinch point with less than 4 connected edges");
                //}

                IndexPair edge1 = edge_list.GetTopologyVertices(vert_edges[0]);
                IndexPair edge2 = edge_list.GetTopologyVertices(vert_edges[1]);
                IndexPair edge3 = edge_list.GetTopologyVertices(vert_edges[2]);
                IndexPair edge4 = edge_list.GetTopologyVertices(vert_edges[3]);
                List<IndexPair> edges = new List<IndexPair>();
                edges.Add(edge1);
                edges.Add(edge2);
                edges.Add(edge3);
                edges.Add(edge4);

                int[] edge1_face = edge_list.GetConnectedFaces(vert_edges[0]);
                int[] edge2_face = edge_list.GetConnectedFaces(vert_edges[1]);
                int[] edge3_face = edge_list.GetConnectedFaces(vert_edges[2]);
                int[] edge4_face = edge_list.GetConnectedFaces(vert_edges[3]);
                // Made with assumption that the following is always true
                Point3d edge1_vert1 = vert_list[edge1[0]]; // unique
                Point3d edge1_vert2 = vert_list[edge1[1]]; // shared
                Point3d edge2_vert1 = vert_list[edge2[0]]; // unique
                Point3d edge2_vert2 = vert_list[edge2[1]]; // shared
                Point3d edge3_vert1 = vert_list[edge3[0]]; // shared
                Point3d edge3_vert2 = vert_list[edge3[1]]; // unique
                Point3d edge4_vert1 = vert_list[edge4[0]]; // shared
                Point3d edge4_vert2 = vert_list[edge4[1]]; // unique

                List<Point3d> points = new List<Point3d>();
                points.Add(edge1_vert1);
                points.Add(edge2_vert1);
                points.Add(edge3_vert2);
                points.Add(edge4_vert2);

                Point3d shared_point = vert_list[v];

                List<Vector2d> vectors = new List<Vector2d>();
                Vector2d zero = new Vector2d(0, 0);
                for (int i = 0; i < points.Count; ++i)
                {
                    Vector2d vector = new Vector2d();
                    vector.X = shared_point.X - points[i].X;
                    vector.Y = shared_point.Y - points[i].Y;

                    if (vector != zero)
                        vectors.Add(vector);
                }

                Point3d vert0 = new Point3d();
                Point3d vert1 = new Point3d();
                Point3d vert2 = new Point3d();
                Point3d vert3 = new Point3d();
                Point3d vert4 = new Point3d();
                Point3d vert5 = new Point3d();
                Point3d vert6 = new Point3d();
                double x = shared_point.X;
                double y = shared_point.Y;
                double l1 = Math.Sqrt(Math.Pow(vectors[0].X, 2) + Math.Pow(vectors[0].Y, 2));
                double l2 = Math.Sqrt(Math.Pow(vectors[1].X, 2) + Math.Pow(vectors[1].Y, 2));

                // remove faces that cause pinch points
                MeshFace face = mesh.Faces[edge1_face[0]];
                if (edge1_face[0] == edge2_face[0])
                {
                    int face1_corner_idx = mesh.Faces[edge1_face[0]].A;
                    int face2_corner_idx = mesh.Faces[edge3_face[0]].C;

                    Point3d face1_corner = mesh.Vertices[face1_corner_idx]; // bottom
                    Point3d face2_corner = mesh.Vertices[face2_corner_idx]; // top

                    var connected_faces1 = vert_list.ConnectedFaces(face1_corner_idx);
                    var connected_faces2 = vert_list.ConnectedFaces(face2_corner_idx);

                    // For bottom
                    shared_point = face1_corner;
                    x = shared_point.X;
                    y = shared_point.Y;

                    vert0 = new Point3d((x + l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x + 2 * l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x + l2 / 3), (y + 2 * l1 / 3), shared_point.Z);
                    vert3 = points[0];
                    vert4 = points[1];
                    vert5 = shared_point;
                    vert6 = new Point3d((x + 2 * l2 / 3), (y + 2 * l1 / 3), shared_point.Z);

                    List<Point3d> verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5, vert6 };
                    List<int> idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    int[] v_con_faces = vert_list.ConnectedFaces(face1_corner_idx);
                    int[] connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face1_corner_idx, edge1[0]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[3], idxs[1]);
                    connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face1_corner_idx, edge2[0]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[4], idxs[5]);

                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[6], idxs[2]);


                    // for top
                    shared_point = face2_corner;
                    x = shared_point.X;
                    y = shared_point.Y;

                    vert0 = new Point3d((x - l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x - 2 * l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x - l2 / 3), (y - 2 * l1 / 3), shared_point.Z);
                    vert3 = points[2];
                    vert4 = points[3];
                    vert5 = shared_point;
                    vert6 = new Point3d((x - 2 * l2 / 3), (y - 2 * l1 / 3), shared_point.Z);

                    verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5, vert6 };
                    idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    v_con_faces = vert_list.ConnectedFaces(face2_corner_idx);
                    connected_faces = edge_list.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face2_corner_idx, edge4[1]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[4], idxs[1]);
                    connected_faces = edge_list.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face2_corner_idx, edge3[1]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[3], idxs[5]);

                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[6], idxs[2]);
                    mesh.Compact();


                    //store faces to be removed
                    //remove_face_at.Add(edge1_face[0]);
                    //remove_face_at.Add(edge4_face[0]);
                }
                if (edge1_face[0] == edge3_face[0])
                {
                    int face1_corner_idx = mesh.Faces[edge1_face[0]].B;
                    int face2_corner_idx = mesh.Faces[edge4_face[0]].D;

                    Point3d face1_corner = mesh.Vertices[face1_corner_idx]; // bottom
                    Point3d face2_corner = mesh.Vertices[face2_corner_idx]; // top

                    var connected_faces1 = vert_list.ConnectedFaces(face1_corner_idx);
                    var connected_faces2 = vert_list.ConnectedFaces(face2_corner_idx);

                    // For bottom first
                    shared_point = face1_corner;
                    x = shared_point.X;
                    y = shared_point.Y;

                    vert0 = new Point3d((x - l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x - 2 * l2 / 3), (y + l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x - l2 / 3), (y + 2 * l1 / 3), shared_point.Z);
                    vert3 = points[2];
                    vert4 = points[0];
                    vert5 = shared_point;
                    vert6 = new Point3d((x - 2 * l2 / 3), (y + 2 * l1 / 3), shared_point.Z);

                    List<Point3d> verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5, vert6 };
                    List<int> idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    int[] v_con_faces = vert_list.ConnectedFaces(face1_corner_idx);
                    int[] connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face1_corner_idx, edge1[0]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[4], idxs[5]);
                    connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face1_corner_idx, edge3[1]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[3], idxs[2]);

                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[6], idxs[1]);
                    mesh.Compact();


                    // for top                      
                    shared_point = face2_corner;
                    x = shared_point.X;
                    y = shared_point.Y;

                    vert0 = new Point3d((x + l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert1 = new Point3d((x + 2 * l2 / 3), (y - l1 / 3), shared_point.Z);
                    vert2 = new Point3d((x + l2 / 3), (y - 2 * l1 / 3), shared_point.Z);
                    vert3 = points[1];
                    vert4 = points[3];
                    vert5 = shared_point;
                    vert6 = new Point3d((x + 2 * l2 / 3), (y - 2 * l1 / 3), shared_point.Z);

                    verts = new List<Point3d> { vert0, vert1, vert2, vert3, vert4, vert5, vert6 };
                    idxs = new List<int>();
                    foreach (Point3d vert in verts)
                    {
                        Point3d r_vert = RoundPoint(vert);
                        if (l_to_i.ContainsKey(r_vert))
                        {
                            idxs.Add(l_to_i[r_vert]);
                        }
                        else
                        {
                            int idx = mesh.Vertices.Add(vert);
                            idxs.Add(idx);
                            l_to_i.Add(r_vert, idx);
                        }
                    }

                    v_con_faces = vert_list.ConnectedFaces(face2_corner_idx);
                    connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face2_corner_idx, edge4[1]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[1], idxs[4], idxs[5]);
                    connected_faces = mesh.TopologyEdges.GetConnectedFaces(mesh.TopologyEdges.GetEdgeIndex(face2_corner_idx, edge2[0]));
                    //if (connected_faces.Count() == 2 || v_con_faces.Count() == 1 || v_con_faces.Count() == 4)
                    mesh.Faces.AddFace(idxs[0], idxs[5], idxs[3], idxs[2]);

                    mesh.Faces.AddFace(idxs[0], idxs[2], idxs[6], idxs[1]);
                    mesh.Compact();


                    //store faces to be removed
                    //remove_face_at.Add(edge1_face[0]);
                    //remove_face_at.Add(edge4_face[0]);
                }
            }
        }
    }
}


