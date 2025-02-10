using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class ExporttoAleph : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ExporttoAleph()
          : base("Export Mesh to Aleph", "ex_aleph",
              "Export file with simplicial complex information to Aleph",
              "Sculpt3D", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "Vertices of mesh (Works only for Tri3D neg = True)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Tets", "tets", "Tetrahedrals", GH_ParamAccess.list);
            pManager.AddTextParameter("Name", "name", "File name", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "path", "Path to directory to be stored", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Filtration", "filt", "0 - x, 1 - y, 2 - z, 3 - w", GH_ParamAccess.item);
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
            List<Point4d> vertices = new List<Point4d>();
            List<List<int>> tets = new List<List<int>>();
            List<double> volume_fractions = new List<double>();
            string name = null;
            string path = null;
            bool tet = false;
            bool min = false;
            int filtration = -1;

            DA.GetDataList(0, vertices);
            DA.GetDataList(1, tets);
            DA.GetData(2, ref name);
            DA.GetData(3, ref path);
            DA.GetData(4, ref filtration);

            Dictionary<Tuple<int, int>, float> edges = new Dictionary<Tuple<int, int>, float>();
            Dictionary<Tuple<int, int, int>, float> faces = new Dictionary<Tuple<int, int, int>, float>();

            foreach (List<int> t in tets)
            {
                int A = t[0];
                int B = t[1];
                int C = t[2];
                int D = t[3];
                float vol_a = 0;
                float vol_b = 0;
                float vol_c = 0;
                float vol_d = 0;
                if (filtration == 3)
                {
                    vol_a = (float)Math.Round(vertices[A].W, 4);
                    vol_b = (float)Math.Round(vertices[B].W, 4);
                    vol_c = (float)Math.Round(vertices[C].W, 4);
                    vol_d = (float)Math.Round(vertices[D].W, 4);
                }
                else if (filtration == 2)
                {
                    vol_a = (float)Math.Round(vertices[A].Z, 4);
                    vol_b = (float)Math.Round(vertices[B].Z, 4);
                    vol_c = (float)Math.Round(vertices[C].Z, 4);
                    vol_d = (float)Math.Round(vertices[D].Z, 4);
                }
                else if (filtration == 1)
                {
                    vol_a = (float)Math.Round(vertices[A].Y, 4);
                    vol_b = (float)Math.Round(vertices[B].Y, 4);
                    vol_c = (float)Math.Round(vertices[C].Y, 4);
                    vol_d = (float)Math.Round(vertices[D].Y, 4);
                }
                else if (filtration == 0)
                {
                    vol_a = (float)Math.Round(vertices[A].X, 4);
                    vol_b = (float)Math.Round(vertices[B].X, 4);
                    vol_c = (float)Math.Round(vertices[C].X, 4);
                    vol_d = (float)Math.Round(vertices[D].X, 4);
                }
            
                A++;
                B++;
                C++;
                D++;

                // Edges
                List<int> e = new List<int> { A, B };
                e.Sort();
                List<float> volumes = new List<float> { vol_a, vol_b };
                float v = new float();
                v = volumes.Min();
                Tuple<int, int> tup = new Tuple<int, int>(e[0], e[1]);
                if(!edges.ContainsKey(tup))
                    edges.Add(tup, v);


                e = new List<int> { A, C };
                e.Sort();
                volumes = new List<float> { vol_a, vol_c };
                v = new float();
                volumes.Min();
                tup = new Tuple<int, int>(e[0], e[1]);
                if (!edges.ContainsKey(tup))
                    edges.Add(tup, v);

                e = new List<int> { A, D };
                e.Sort();
                volumes = new List<float> { vol_a, vol_d };
                v = new float();
                v = volumes.Min();
                tup = new Tuple<int, int>(e[0], e[1]);
                if (!edges.ContainsKey(tup))
                    edges.Add(tup, v);

                e = new List<int> { B, C };
                e.Sort();
                volumes = new List<float> { vol_b, vol_c };
                v = new float();
                v = volumes.Min();
                tup = new Tuple<int, int>(e[0], e[1]);
                if (!edges.ContainsKey(tup))
                    edges.Add(tup, v);

                e = new List<int> { B, D };
                e.Sort();
                volumes = new List<float> { vol_b, vol_d };
                v = new float();
                v = volumes.Min();
                tup = new Tuple<int, int>(e[0], e[1]);
                if (!edges.ContainsKey(tup))
                    edges.Add(tup, v);

                e = new List<int> { C, D };
                e.Sort();
                volumes = new List<float> { vol_c, vol_d };
                v = new float();
                v = volumes.Min();
                tup = new Tuple<int, int>(e[0], e[1]);
                if (!edges.ContainsKey(tup))
                    edges.Add(tup, v);

                // Faces
                List<int> f = new List<int> { A, B, C };
                f.Sort();
                volumes = new List<float> { vol_a, vol_b, vol_c };
                v = volumes.Min();
                Tuple<int, int, int> tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                if (!faces.ContainsKey(tuple))
                    faces.Add(tuple, v);

                f = new List<int> { A, B, D };
                f.Sort();
                volumes = new List<float> { vol_a, vol_b, vol_d};
                v = volumes.Min();
                tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                if (!faces.ContainsKey(tuple))
                    faces.Add(tuple, v);

                f = new List<int> { A, C, D };
                f.Sort();
                volumes = new List<float> { vol_a, vol_c, vol_d };
                v = volumes.Min();
                tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                if (!faces.ContainsKey(tuple))
                    faces.Add(tuple, v);

                f = new List<int> { B, C, D };
                f.Sort();
                volumes = new List<float> { vol_b, vol_c, vol_d };
                v = volumes.Min();
                tuple = new Tuple<int, int, int>(f[0], f[1], f[2]);
                if (!faces.ContainsKey(tuple))
                    faces.Add(tuple, v);
            }
            
            

            using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
            {
                // Exodus export information
                //filename.WriteLine(string.Format("Vertices: {0}", vertices.Count));
                //filename.WriteLine(string.Format("Hexes: {0}", hexes.Count));
                //filename.WriteLine("        X       Y       Z");
                //foreach (Vertex vertex in vertices)
                //{
                //    Point3d v = vertex.getLocation();
                //    filename.WriteLine(string.Format("{0} {1} {2}", v.X, v.Y, v.Z));
                //}
                //for (int i = 0; i < hexes.Count; i++)
                //{
                //    Hex hex = hexes[i];
                //    double vol = volume_fractions[i];
                //    filename.WriteLine(string.Format("{0} {1} {2} {3} {4} {5} {6} {7} {8}",
                //    hex.A + 1, hex.B + 1, hex.C + 1, hex.D + 1, hex.E + 1, hex.F + 1, hex.G + 1, hex.H + 1, vol));
                //}


                filename.WriteLine("verts");
                foreach (Point4d vertex in vertices)
                {
                    List<double> vert = new List<double> { vertex.X, vertex.Y, vertex.Z, vertex.W };
                    filename.WriteLine(string.Format("{0}", Math.Round(vert[filtration], 4)));
                }
                filename.WriteLine("edges");
                foreach (var kvp in edges)
                {
                    filename.WriteLine(kvp.Key.Item1.ToString());
                    filename.WriteLine(kvp.Key.Item2.ToString());
                    filename.WriteLine((kvp.Value).ToString());

                }
                filename.WriteLine("faces");
                foreach (var kvp in faces)
                {
                    filename.WriteLine(kvp.Key.Item1.ToString());
                    filename.WriteLine(kvp.Key.Item2.ToString());
                    filename.WriteLine(kvp.Key.Item3.ToString());
                    filename.WriteLine((kvp.Value).ToString());
                }
                filename.WriteLine("tets");
                for (int i = 0; i < tets.Count; i++)
                {
                    List<int> t = tets[i];
                    float v = 0.0f;
                    List<double> volumes = new List<double> 
                    { vertices[t[0]].W, vertices[t[1]].W, vertices[t[2]].W, vertices[t[3]].W};
                    double vol = Math.Round(volumes.Min(), 4);
                    filename.WriteLine((t[0] + 1).ToString());
                    filename.WriteLine((t[1] + 1).ToString());
                    filename.WriteLine((t[2] + 1).ToString());
                    filename.WriteLine((t[3] + 1).ToString());
                    filename.WriteLine(vol.ToString());
                }
                
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
            get { return new Guid("61658AD1-1671-40AD-9E9F-48772E808664"); }
        }
    }
}