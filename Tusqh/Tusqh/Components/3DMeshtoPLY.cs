using System;
using System.Collections.Generic;
using System.IO;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Render.ChangeQueue;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class MeshtoPLY3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MeshtoPLY3D()
          : base("MeshtoPLY3D", "PLY3D",
              "Converts a mesh to a .ply file",
              "Sculpt3D", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "Vertices of triangulated dual mesh", GH_ParamAccess.list);
            pManager.AddGenericParameter("Tets", "tets", "Tetrahedron of triangulated dual mesh", GH_ParamAccess.list);
            pManager.AddTextParameter("File Name", "name", "The name of the object file", GH_ParamAccess.item);
            pManager.AddTextParameter("File Destination Path", "path", "The path to the file destination", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            /*
             * Currently this works great for betti0, but for betti1 I think it is wrong
             * It could also just be that I do not understand what betti1 is when they are 3d objects though
             */



            List<Vertex> vertices = new List<Vertex>();
            //List<Edge> edges = trimesh.GetEdges();
            List<Tetrahedral> tets = new List<Tetrahedral>();
            string name = null;
            string path = null;
            
            DA.GetDataList(0, vertices);
            DA.GetDataList(1, tets);
            DA.GetData(2, ref name);
            DA.GetData(3, ref path);

            List<Tuple<int, int, int>> face_tuple = new List<Tuple<int, int, int>>();
            List<TriFace> faces = new List<TriFace>();
            foreach(Tetrahedral tet in tets)
            {
                List<int> face1 = new List<int> { tet.A, tet.B, tet.D };
                List<int> face2 = new List<int> { tet.B, tet.C, tet.D };
                List<int> face3 = new List<int> { tet.A, tet.D, tet.C };
                List<int> face4 = new List<int> { tet.A, tet.C, tet.B };
                List<List<int>> ordered_faces = new List<List<int>> { face1, face2, face3, face4 };
                face1.Sort();
                face2.Sort();
                face3.Sort();
                face4.Sort();
                List<List<int>> sorted_faces = new List<List<int>> { face1, face2, face3, face4 };
                for(int i = 0; i < 4; i++)
                {
                    List<int> sf = sorted_faces[i];
                    Tuple<int, int, int> tuple = new Tuple<int, int, int>(sf[0], sf[1], sf[2]);
                    if(!face_tuple.Contains(tuple))
                    {
                        face_tuple.Add(tuple);
                        List<int> f = ordered_faces[i];
                        faces.Add(new TriFace(f[0], f[1], f[2]));
                    }
                }

            }

            // Write the string array to a new file named "WriteLines.txt".
            using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
            {
                filename.WriteLine("ply");
                filename.WriteLine("format ascii 1.0");
                filename.WriteLine("comment File Created by Kendrick Shepherd");
                filename.WriteLine(string.Format("element vertex {0}", vertices.Count));
                filename.WriteLine("property float x");
                filename.WriteLine("property float y");
                filename.WriteLine("property float z");
                filename.WriteLine(string.Format("element face {0}", faces.Count));
                filename.WriteLine("property list uchar uint vertex_indices");
                filename.WriteLine("end_header");

                foreach (var vertex in vertices)
                {
                    var v = vertex.getLocation();
                    filename.WriteLine(string.Format("{0} {1} {2} {3}", Math.Round(v.X, 8), Math.Round(v.Y, 8), vertex.getVolumeFraction(), Math.Round(v.Z, 8)));
                }

                foreach (var f in faces)
                    filename.WriteLine(string.Format("3 {0} {1} {2}", f.A, f.B, f.C));
            }
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
            get { return new Guid("429E5103-CDBF-4E8E-99AE-E187D51E9B16"); }
        }
    }
}