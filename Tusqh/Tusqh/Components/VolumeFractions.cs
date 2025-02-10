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
    public class VolumeFractions : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public VolumeFractions()
          : base("PointsInMesh", "meshpts",
              "For each face a grid of points are checked if they are in the mesh or not",
              "Sculpt2D", "Volume")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background Grid", "grid", "Background Grid from bounding box", GH_ParamAccess.item);
            pManager.AddIntegerParameter("u points", "upts", "number of points to check in u direction", GH_ParamAccess.item);
            pManager.AddIntegerParameter("v points", "vpts", "number of points to check in v direction", GH_ParamAccess.item);
            pManager.AddMeshParameter("Mesh", "M", "Mesh of interest being bounded", GH_ParamAccess.list);
            pManager.AddSurfaceParameter("Surface", "S", "Surface of interest being bounded", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Winding Number", "wn", "Use winding number or use default computations in Rhino", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Reverse Orientation", "rev", "Reverse orientation of the boundary curve", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Volume Fraction", "vol", "Returns a list of volume fractions", GH_ParamAccess.list);
            pManager.AddNumberParameter("Surface Volume Fraction", "svol", "Returns a list of volume fractions for the surface", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centroid", "cent", "Center of the face whose volume fraction was just computed", GH_ParamAccess.list);
            pManager.AddCurveParameter("Boundary curves", "bc", "Boundary curves of the mesh used", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Sample Points", "sample", "sample points of the face", GH_ParamAccess.list);
            pManager.AddNumberParameter("Point Winding Number", "wpt", "Computed winding number of the point", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh background_grid = new Mesh();
            int u_pts = new int();
            int v_pts = new int();
            List<Mesh> meshes = new List<Mesh>();
            List<Brep> breps = new List<Brep>();

            bool use_winding_number = false;
            bool reverse_boundary_orient = false;

            DA.GetData(0, ref background_grid);
            DA.GetData(1, ref u_pts);
            DA.GetData(2, ref v_pts);
            if (!DA.GetDataList(3, meshes))
                meshes = new List<Mesh>();
            if (!DA.GetDataList(4, breps))
                breps = new List<Brep>();
            if (!DA.GetData(5, ref use_winding_number))
                use_winding_number = false;
            if (!DA.GetData(6, ref reverse_boundary_orient))
                reverse_boundary_orient = false;

            List<double> volume_fractions = new List<double>();
            List<double> surface_volume_fractions = new List<double>();
            double u_pts_double = (double)u_pts;
            double v_pts_double = (double)v_pts;

            int A;
            int B;
            int C;
            int D;
            Point3d point_a = new Point3d();
            Point3d point_b = new Point3d();
            Point3d point_c = new Point3d();
            Point3d point_d = new Point3d();
            double u_dist;//for now x distance
            double v_dist;//for now y distance
            var u_segments = 0.0;
            var v_segments = 0.0;
            string u_string = u_pts.ToString();
            string v_string = v_pts.ToString();
            Point3d point = new Point3d();
            List<Point3d> point_grid = new List<Point3d>();
            List<Point3d> centroid = new List<Point3d>();
            double x;
            double y;
            double z;

            double points_in = new double();
            double volume_fraction = new double();
            double surface_volume_fraction = new double();
            List<Point3d> pt_grid = new List<Point3d>(background_grid.Faces.Count*u_pts*v_pts);
            List<double> pt_winding = new List<double>(pt_grid.Capacity);

            bool point_in = false;

            if (!use_winding_number)
            {
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

                    u_dist = point_b.X - point_a.X;
                    v_dist = point_d.Y - point_a.Y;

                    u_segments = u_dist / u_pts_double;
                    v_segments = v_dist / v_pts_double;

                    point_grid.Clear();

                    for (int i = 0; i < u_pts; i++)
                    {
                        x = 0.0;
                        y = 0.0;
                        z = 0.0;

                        for (int j = 0; j < v_pts; j++)
                        {
                            x = point_a.X + u_segments / 2 + u_segments * i;
                            y = point_a.Y + v_segments / 2 + v_segments * j;
                            z = point_a.Z;

                            point.X = x;
                            point.Y = y;
                            point.Z = z;

                            point_grid.Add(point);
                        }
                    }

                    points_in = 0.0;

                    foreach (Point3d test_point in point_grid)
                    {
                        point_in = false;
                        foreach (var mesh in meshes)
                        {
                            int temp = mesh.ClosestPoint(test_point, out point, 1);

                            if (temp < 0)
                                continue;

                            x = point.X - test_point.X;
                            y = point.Y - test_point.Y;
                            z = point.Z - test_point.Z;
                            if (Math.Abs(x) < 0.0001 && Math.Abs(y) < 0.0001 && Math.Abs(z) < 0.0001)
                            {
                                points_in += 1.0;
                                point_in = true;
                            }

                            // no need to test further---the point is interior to one of the meshes
                            if (point_in)
                                break;
                        }
                        pt_winding.Add(point_in ? 1 : 0);
                        pt_grid.Add(test_point);
                    }

                    var divisor = 0.0;
                    divisor = u_pts_double * v_pts_double;

                    volume_fraction = points_in / divisor;
                    volume_fractions.Add(volume_fraction);

                    points_in = 0.0;
                    foreach (Point3d test_point in point_grid)
                    {
                        point_in = false;

                        foreach (Brep brep in breps)
                        {
                            bool temp = brep.ClosestPoint(test_point, out _, out var ci, out _, out _, 1, out _);
                            if (!temp)
                                continue;
                            else if (ci.ComponentIndexType != ComponentIndexType.BrepEdge)
                            {
                                points_in += 1;
                                point_in = true;
                            }
                            if (point_in)
                                break;
                        }
                    }

                    surface_volume_fraction = points_in / divisor;
                    surface_volume_fractions.Add(surface_volume_fraction);
                }
            }
            else
            {
                List<Tuple<double, double>> vert_array = new List<Tuple<double, double>>(meshes[0].Vertices.Count);
                List<Tuple<uint, uint>> edge_array = new List<Tuple<uint, uint>>(meshes[0].TopologyEdges.Count);
                uint vertex_count = 0;
                List<Polyline> boundary_edges = new List<Polyline>();
                List<Mesh> disjoint_meshes = meshes.SelectMany(m => m.SplitDisjointPieces()).ToList();

                foreach (var mesh in disjoint_meshes)
                {
                    var boundary_edges_temp = mesh.GetNakedEdges();
                    boundary_edges.AddRange(boundary_edges_temp);

                    // iterate through all boundaries of the mesh
                    List<Point3d> plpts = new List<Point3d>();
                    // extract the outermost boundary
                    var far_pt = mesh.GetBoundingBox(false).Corner(false, false, false);
                    int count = 0;
                    int min_idx = 0;
                    double min_dist = double.PositiveInfinity;
                    double temp_dist;
                    foreach (var polylineiter in boundary_edges)
                    {
                        var closest = polylineiter.ClosestPoint(far_pt);
                        temp_dist = closest.DistanceToSquared(far_pt);
                        if(temp_dist < min_dist)
                        {
                            min_dist = temp_dist;
                            min_idx = count;
                        }
                        count += 1;
                    }
                    count = 0;
                    foreach (var polylineiter in boundary_edges)
                    {
                        // potentially reverse the orientation of the mesh boundary
                        // if the inverse mesh is desired
                        var polyline = polylineiter;
                        var orient = polyline.ToPolylineCurve().ClosedCurveOrientation();
                        if (count == min_idx)
                        {
                            if (orient == CurveOrientation.Clockwise)
                                polyline.Reverse();
                        }
                        else
                        {
                            if (orient == CurveOrientation.CounterClockwise)
                                polyline.Reverse();
                        }
                        if (reverse_boundary_orient)
                            polyline.Reverse();
                        uint pt_count = 0;
                        // store all vertices
                        foreach (var pt in polyline)
                        {
                            plpts.Add(pt);
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
                        if (polyline.IsClosed)
                            edge_array.Add(new Tuple<uint, uint>(vertex_count + pt_count - 1, vertex_count));

                        // iterate to the next boundary
                        vertex_count += pt_count;
                        count += 1;
                    }
                }

                // points to querry in the background mesh... just sample the centroid for now
                List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>(background_grid.Faces.Count*u_pts*v_pts);
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

                    u_dist = point_b.X - point_a.X;
                    v_dist = point_d.Y - point_a.Y;

                    u_segments = u_dist / u_pts_double;
                    v_segments = v_dist / v_pts_double;

                    point_grid.Clear();

                    for (int i = 0; i < u_pts; i++)
                    {
                        x = 0.0;
                        y = 0.0;
                        z = 0.0;

                        for (int j = 0; j < v_pts; j++)
                        {
                            x = point_a.X + u_segments / 2 + u_segments * i;
                            y = point_a.Y + v_segments / 2 + v_segments * j;
                            z = point_a.Z;

                            querry_pts.Add(new Tuple<double, double>(x, y));
                            pt_grid.Add(new Point3d(x, y, 0));
                        }
                    }
                }

                /*
                // points to querry in the background mesh... just sample the centroid for now
                List<Tuple<double, double>> querry_pts = new List<Tuple<double, double>>(background_grid.Faces.Count);
                for (int i = 0; i < background_grid.Faces.Count; ++i)
                {
                    var face = background_grid.Faces[i];
                    var pt = background_grid.Vertices[face.A] + background_grid.Vertices[face.B] + background_grid.Vertices[face.C] + background_grid.Vertices[face.D];
                    centroid.Add((new Point3d(pt))/4);

                    querry_pts.Add(new Tuple<double, double>(centroid.Last().X,centroid.Last().Y));
                }
                */




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

                double divisor = u_pts_double * v_pts_double;

                int n_pts = u_pts * v_pts;
                int counter = 0;
                double cur_wind;
                foreach (MeshFace face in background_grid.Faces)
                {
                    volume_fraction = 0;
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

                //volume_fractions = winding;
                surface_volume_fractions = new List<double>();

                DA.SetDataList(3, boundary_edges);
                //DA.SetDataList(4, plpts);

            }

            DA.SetDataList(0, volume_fractions);
            DA.SetDataList(1, surface_volume_fractions);
            DA.SetDataList(2, centroid);

            DA.SetDataList(4, pt_grid);
            DA.SetDataList(5, pt_winding);

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
            get { return new Guid("D3620B7E-4312-4C3F-8CAC-3EBE48C669F3"); }
        }
    }
}