using System;
using System.Collections.Generic;
using System.IO;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class Nastran : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Nastran()
          : base("Nastran", "nas",
              "Create a nastran file to be opened in cubit",
              "Sculpt2D", "Geometry")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "Sculpted mesh", GH_ParamAccess.item);
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
            Mesh mesh = new Mesh();
            string name = "";
            string path = "";
            
            DA.GetData(0, ref mesh);
            DA.GetData(1, ref name);
            DA.GetData(2, ref path);

            using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
            {

                filename.Write("ID MSC  DSOUG1\\\r\n" +
                    "TIME  10\\\r\n" +
                    "SOL 101\\\r\n" +
                    "CEND\\\r\n" +
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

                int num = mesh.Vertices.Count;
                for(int i = 0; i < 2 * num; i++)
                {
                    Point3f vert = new Point3f();
                    if (i < num)
                        vert = mesh.Vertices[i];
                    else
                    {
                        vert = mesh.Vertices[i - num];
                        vert.Z++;
                    }

                    filename.Write("GRID    " +
                        (i + 1).ToString().PadRight(16) +
                        Math.Round(vert.X, 3).ToString().PadLeft(8) +
                        Math.Round(vert.Y, 3).ToString().PadLeft(8) +
                        Math.Round(vert.Z, 3).ToString().PadLeft(8) + "\\\r\n");
                }

                filename.Write("PSOLID  1       1         0.     TWO    GAUSS   FULL\r\n");

                foreach(var face in mesh.Faces)
                {
                    filename.Write("CHEXA   1       1       " +
                        (face.A + 1).ToString().PadRight(8) +
                        (face.B + 1).ToString().PadRight(8) +
                        (face.C + 1).ToString().PadRight(8) +
                        (face.D + 1).ToString().PadRight(8) +
                        (face.A + num + 1).ToString().PadRight(8) +
                        (face.B + num + 1).ToString() +
                        "\\\r\n        " +
                        (face.C + num + 1).ToString().PadRight(8) +
                        (face.D + num + 1).ToString() +
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
            get { return new Guid("0922B902-27F4-41CE-AF7A-24437B90BAF4"); }
        }
    }
}