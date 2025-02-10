using System;
using System.Collections.Generic;
using System.IO;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class VTKForMFEM : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public VTKForMFEM()
          : base("VTK", "vtk",
              "Export VTK file for mfem",
              "Sculpt2D", "MFEM")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "Mesh for analysis", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "name", "File name", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "path", "Path to directory to be stored", GH_ParamAccess.item);
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
                filename.WriteLine("# vtk DataFile Version 3.0");
                filename.WriteLine(name);
                filename.WriteLine("ASCII");
                filename.WriteLine("DATASET UNSTRUCTURED_GRID");
                filename.WriteLine("POINTS " + mesh.Vertices.Count + " float");

                foreach(var vert in mesh.Vertices)
                {
                    filename.WriteLine(string.Format("{0} {1} {2}", (float)vert.X, (float)vert.Y, (float)vert.Z));
                }

                filename.WriteLine("CELLS " + mesh.Faces.Count + " " + mesh.Faces.Count * 5);

                foreach (var face in mesh.Faces)
                {
                    filename.WriteLine(string.Format("4 {0} {1} {2} {3}", face.A, face.B, face.C, face.D));
                }

                filename.WriteLine("CELL_TYPES " + mesh.Faces.Count);

                foreach (var face in mesh.Faces)
                {
                    filename.WriteLine("9");
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
            get { return new Guid("51D5D505-3C73-41A0-A3C8-47E57EC9885E"); }
        }
    }
}