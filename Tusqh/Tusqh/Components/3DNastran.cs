using System;
using System.Collections.Generic;
using System.IO;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class Nastran3D : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Nastran3D()
          : base("Nastran3D", "nas3d",
              "Create a nastran file to be opened in cubit",
              "Sculpt3d", "Geometry")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Vertices", "verts", "Vertices of mesh", GH_ParamAccess.list);
            pManager.AddGenericParameter("Hexes", "hexes", "Hexes of mesh", GH_ParamAccess.list);
            pManager.AddTextParameter("Name", "name", "Name of file", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "path", "Path of file", GH_ParamAccess.item);
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
            List<Point3d> vertices = new List<Point3d>();
            List<List<int>> hexes = new List<List<int>>();
            string name = "";
            string path = "";

            DA.GetDataList(0, vertices);
            DA.GetDataList(1, hexes);
            DA.GetData(2, ref name);
            DA.GetData(3, ref path);

            Dictionary<int, Point3d> vert_dict = new Dictionary<int, Point3d>();
            for(int i = 0; i < hexes.Count; i++)
            {
                List<int> hex = hexes[i];
                foreach(int j in hex)
                    vert_dict.TryAdd(j + 1, vertices[j]);
            }

            using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
            {
                //filename.WriteLine("TIME 10\\");
                //filename.WriteLine("SOL 101\\");
                //filename.WriteLine("CEND\\");
                //filename.WriteLine("TITLE = SYMMETRIC THREE BAR TRUSS DESIGN OPTIMIZATION - DSOUG1\\");
                //filename.WriteLine("SUBTITLE = BASELINE - 2 CROSS SECTIONAL AREAS AS DESIGN VARIABLES\\");
                //filename.WriteLine("ECHO        = NONE\\");
                //filename.WriteLine("SPC         = 100\\");
                //filename.WriteLine("DISPLACEMENT(SORT1,REAL)=ALL\\");
                //filename.WriteLine("SPCFORCES(SORT1,REAL)=ALL\\");
                //filename.WriteLine("STRESS(SORT1,REAL,VONMISES,BILIN)=ALL\\");

                filename.Write("ID MSC  DSOUG1\\\r\n" +
                    "TIME  10\\\r\n" +
                    "SOL 101\\\r\nCEND\\\r\n" +
                    "TITLE = SYMMETRIC THREE BAR TRUSS DESIGN OPTIMIZATION  -       DSOUG1\\\r\n" +
                    "SUBTITLE = BASELINE - 2 CROSS SECTIONAL AREAS AS DESIGN VARIABLES\\\r\n" +
                    "ECHO        = NONE\\\r\n" +
                    "SPC         = 100\\\r\n" +
                    "DISPLACEMENT(SORT1,REAL)=ALL\\\r\n" +
                    "SPCFORCES(SORT1,REAL)=ALL\\\r\n" +
                    "STRESS(SORT1,REAL,VONMISES,BILIN)=ALL\\\r\n" +
                    "$ Subcases\\\r\n" +
                    "SUBCASE 1\\\r\n" +
                    "   LABEL = LOAD CONDITION 1\\\r\n" +
                    "   LOAD  = 300\\\r\n" +
                    "SUBCASE 2\\\r\n" +
                    "   LABEL = LOAD CONDITION 2\\\r\n" +
                    "   LOAD  = 310\\\r\n" +
                    "BEGIN BULK\\\r\n");

                filename.Write("$__1___||__2___||__3___||__4___||__5___||__6___||__7___||__8___||__9___||__10__|\\\r\n" +
                    "param   post    1\\\r\n");

                foreach(var kvp in vert_dict)
                {
                    filename.Write("GRID    " +
                        kvp.Key.ToString().PadRight(16) +
                        Math.Round(kvp.Value.X, 4).ToString().PadLeft(8) +
                        Math.Round(kvp.Value.Y, 4).ToString().PadLeft(8) +
                        Math.Round(kvp.Value.Z, 4).ToString().PadLeft(8) + "\\\r\n"); ;
                }

                filename.Write("PSOLID  1       1         0.     TWO    GAUSS   FULL\r\n");

                foreach(var hex in hexes)
                {
                    filename.Write("CHEXA   1       1       " + 
                        (hex[0] + 1).ToString().PadRight(8) + 
                        (hex[1] + 1).ToString().PadRight(8) +
                        (hex[2] + 1).ToString().PadRight(8) +
                        (hex[3] + 1).ToString().PadRight(8) +
                        (hex[4] + 1).ToString().PadRight(8) +
                        (hex[5] + 1).ToString() + 
                        "\\\r\n        " + 
                        (hex[6] + 1).ToString().PadRight(8) +
                        (hex[7] + 1).ToString() +
                        "\\\r\n");
                }

                filename.Write("MAT1    1       1.0E+7          0.33    0.1\\\r\n");
                filename.Write("ENDDATA\\\r\n}");
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
            get { return new Guid("21B1D53E-3698-49EB-ADD0-54A86E7B765C"); }
        }
    }
}