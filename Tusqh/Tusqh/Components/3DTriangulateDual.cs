using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing.Printing;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Rhino.Geometry;
using Rhino.UI;
using Sculpt2D.Sculpt3D;
using Sculpt2D.Sculpt3D.Collections;

namespace Sculpt2D.Components
{
    public class TriangulateDual3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public TriangulateDual3D()
          : base("Triangulate Dual 3D", "Tri3D",
              "Trianglulates the 3D dual of the background mesh",
              "Sculpt3d", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Vertex List", "verts", "Vertices of Dual Mesh", GH_ParamAccess.list);                   // 0
            pManager.AddGenericParameter("Hexes", "hexes", "List of hexes as list of vertex indicies", GH_ParamAccess.list);    // 1
            pManager.AddNumberParameter("Volume Fractions", "vols", "List of volume fractions", GH_ParamAccess.list);           // 2
            pManager.AddPointParameter("Centroids", "cents", "Centroids of original hex mesh", GH_ParamAccess.list);            // 3
            pManager.AddNumberParameter("x distance", "xdist", "Length of non-dual hexes in x-direction", GH_ParamAccess.item); // 4
            pManager.AddNumberParameter("y distance", "ydist", "Length of non-dual hexes in y-direction", GH_ParamAccess.item); // 5
            pManager.AddNumberParameter("z distance", "zdist", "Length of non-dual hexes in z-direction", GH_ParamAccess.item); // 6
            pManager.AddBooleanParameter("Negative", "neg", "Use negative of volume fraction", GH_ParamAccess.item);            // 7
            pManager.AddBooleanParameter("Minimum", "cm", "Use minimum volume fraction", GH_ParamAccess.item);                  // 8
            pManager.AddBooleanParameter("Visualize Mesh", "viz", "Render a visualization of mesh", GH_ParamAccess.item);       // 9
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "Vertices of triangulated dual mesh", GH_ParamAccess.list);
            pManager.AddGenericParameter("Tets", "tets", "Tetrahedron", GH_ParamAccess.list);
            pManager.AddMeshParameter("Visualization", "viz", "Visualization of the mesh", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Point3d> vertices = new List<Point3d>();
            List<List<int>> hexes = new List<List<int>>();
            List<Point3d> centroids = new List<Point3d>();
            List<double> volume_fractions = new List<double>();
            double x_dist = 0;
            double y_dist = 0;
            double z_dist = 0;
            bool negative = true;
            bool use_min = true;
            bool viz = false;

            DA.GetDataList(0, vertices);
            DA.GetDataList(1, hexes);
            DA.GetDataList(2, volume_fractions);
            DA.GetDataList(3, centroids);
            DA.GetData(4, ref x_dist);
            DA.GetData(5, ref y_dist);
            DA.GetData(6, ref z_dist);
            DA.GetData(7, ref negative);
            DA.GetData(8, ref use_min);
            DA.GetData(9, ref viz);

            Dictionary<Point3d, Tuple<int, double>> Points = new Dictionary<Point3d, Tuple<int, double>>();

            List<Point3d> dual_centroids = new List<Point3d>();
            foreach (List<int> hex in hexes)
            {
                Point3d centroid = new Point3d();
                foreach (int idx in hex)
                {
                    centroid += vertices[idx];
                }
                centroid /= 8;

                dual_centroids.Add(centroid);
            }

            // Move the volume fraction to the appropriate vertex
            for(int j = 0; j < vertices.Count; j++)
            {
                Point3d vert = vertices[j];
                double mindist = double.PositiveInfinity;
                int idx = -1;
                double tempdist;
                for (int i = 0; i < centroids.Count; i++)
                {
                    var pt = centroids[i];
                    tempdist = pt.DistanceToSquared(vert);
                    if (tempdist < mindist)
                    {
                        mindist = tempdist;
                        idx = i;
                    }
                }
                Points.Add(vert, new Tuple<int, double>(j, volume_fractions[idx]));
            }

            List<List<int>> tets = new List<List<int>>();
            int counter = 0;
            foreach (List<int> hex in hexes)
            {                
                List<List<int>> faces = Functions.GetHexFaces(hex, false);
                List<int> face_centroid_idxs = new List<int>();
                int idx = 0;
                foreach(List<int> face in faces)
                {
                    Point3d A = vertices[face[0]];
                    Point3d B = vertices[face[1]];
                    Point3d C = vertices[face[2]];
                    Point3d D = vertices[face[3]];

                    Point3d face_centroid = (A + B + C + D) / 4;
                    idx = Points.Count;
                    bool new_centroid = Points.TryAdd(face_centroid, new Tuple<int, double>(idx, 0));

                    if (new_centroid)
                    {
                        List<double> vols = new List<double> { Points[A].Item2, Points[B].Item2, Points[C].Item2, Points[D].Item2 };
                        if (use_min)
                            Points[face_centroid] = new Tuple<int, double>(idx, vols.Min());
                        else
                            Points[face_centroid] = new Tuple<int, double>(idx, vols.Max());
                        face_centroid_idxs.Add(idx);
                    }
                    else
                    {
                        face_centroid_idxs.Add(Points[face_centroid].Item1);
                    }
                }

                Point3d centroid = new Point3d();
                List<double> volumes = new List<double>();
                foreach (int i in hex)
                {
                    centroid += vertices[i];
                    volumes.Add(Points[vertices[i]].Item2);
                }
                centroid /= 8;
                idx = Points.Count;
                if (use_min)
                {
                    if (!Points.TryAdd(centroid, new Tuple<int, double>(idx, volumes.Min())))
                        throw new Exception("Hex " + counter + " has already been added. Check hex construction in 3DDual.cs");
                }
                else
                    Points.Add(centroid, new Tuple<int, double>(idx, volumes.Max()));

                for (int i = 0; i < faces.Count; i++)
                {
                    List<int> face = faces[i];
                    int face_cent_idx = face_centroid_idxs[i];
                    tets.Add(new List<int> { face[0], face_cent_idx, face[1], idx });
                    tets.Add(new List<int> { face[1], face_cent_idx, face[2], idx });
                    tets.Add(new List<int> { face[2], face_cent_idx, face[3], idx });
                    tets.Add(new List<int> { face[3], face_cent_idx, face[0], idx });
                }

                counter++;
            }

            List<Point4d> points = new List<Point4d>();
            if (negative)
            {
                foreach (var kvp in Points)
                {
                    points.Add(new Point4d(kvp.Key.X, kvp.Key.Y, kvp.Key.Z, -1 * kvp.Value.Item2));
                }
            }
            else
            {
                foreach (var kvp in Points)
                {
                    points.Add(new Point4d(kvp.Key.X, kvp.Key.Y, kvp.Key.Z, kvp.Value.Item2));
                }
            }

            List<Mesh> visualization = new List<Mesh>();
            if (viz)
            {
                List<Tuple<int, int, int>> meshed_faces = new List<Tuple<int, int, int>>();
                foreach (List<int> tet in tets)
                {
                    List<Point3d> point3ds = new List<Point3d>();
                    foreach (int idx in tet)
                    {
                        point3ds.Add(new Point3d(points[idx].X, points[idx].Y, points[idx].Z));
                    }
                    Mesh mesh1 = new Mesh();
                    Mesh mesh2 = new Mesh();
                    Mesh mesh3 = new Mesh();
                    Mesh mesh4 = new Mesh();
                    mesh1.Vertices.AddVertices(point3ds);
                    mesh2.Vertices.AddVertices(point3ds);
                    mesh3.Vertices.AddVertices(point3ds);
                    mesh4.Vertices.AddVertices(point3ds);
                    List<int> f = new List<int> { tet[0], tet[1], tet[3] };
                    f.Sort();
                    Tuple<int, int, int> tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                    if (!meshed_faces.Contains(tuple))
                    {
                        mesh1.Faces.AddFace(0, 1, 3);
                        visualization.Add(mesh1);
                        meshed_faces.Add(tuple);
                    }

                    f = new List<int> { tet[1], tet[2], tet[3] };
                    f.Sort();
                    tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                    if (!meshed_faces.Contains(tuple))
                    {
                        mesh2.Faces.AddFace(1, 2, 3);
                        visualization.Add(mesh2);
                        meshed_faces.Add(tuple);
                    }

                    f = new List<int> { tet[0], tet[3], tet[2] };
                    f.Sort();
                    tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                    if (!meshed_faces.Contains(tuple))
                    {
                        mesh3.Faces.AddFace(0, 3, 2);
                        visualization.Add(mesh3);
                        meshed_faces.Add(tuple);
                    }

                    f = new List<int> { tet[0], tet[2], tet[1] };
                    f.Sort();
                    tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                    if (!meshed_faces.Contains(tuple))
                    {
                        mesh4.Faces.AddFace(0, 2, 1);
                        visualization.Add(mesh4);
                        meshed_faces.Add(tuple);
                    }
                }
            }

            DA.SetDataList(0, points);
            DA.SetDataList(1, tets);
            DA.SetDataList(2, visualization);
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
            get { return new Guid("CE0A1C1E-C1F7-4704-B142-BDF6B9180F4E"); }
        }
    }
}