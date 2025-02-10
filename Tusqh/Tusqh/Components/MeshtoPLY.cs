using System;
using System.Collections.Generic;
using System.IO;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class MeshtoPLY : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MeshtoPLY()
          : base("MeshtoPLY", "PLY",
              "Converts a mesh into a ply file format",
              "Sculpt2D", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "Input mesh to be converted to .ply", GH_ParamAccess.item);
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
            Rhino.Geometry.Mesh mesh = new Rhino.Geometry.Mesh();
            string name = null;
            string path = null;

            // register input variables
            DA.GetData(0, ref mesh);
            DA.GetData(1, ref name);
            DA.GetData(2, ref path);

            // Write the string array to a new file named "WriteLines.txt".
            using (StreamWriter filename = new StreamWriter(Path.Combine(path, name)))
            {
                filename.WriteLine("ply");
                filename.WriteLine("format ascii 1.0");
                filename.WriteLine("comment File Created by Kendrick Shepherd");
                filename.WriteLine("element vertex " + mesh.Vertices.Count);
                filename.WriteLine("property float x");
                filename.WriteLine("property float y");
                filename.WriteLine("property float z");
                filename.WriteLine("element face " + mesh.Faces.Count);
                filename.WriteLine("property list uchar uint vertex_indices");
                filename.WriteLine("end_header");

                foreach (var v in mesh.Vertices)
                {
                    filename.WriteLine(v.X + " " + v.Y + " " + v.Z);
                }

                foreach (var f in mesh.Faces)
                    filename.WriteLine("3 " + f.A + " " + f.B + " " + f.C);
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
            get { return new Guid("532695E4-5671-488D-97AA-E7FE45C23BF4"); }
        }
    }
}