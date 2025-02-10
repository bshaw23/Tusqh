using System;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace Sculpt2D.Components
{
    public class ObjectFileofMesh : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ObjectFileofMesh()
          : base("Mesh to Object File", "mesh2obj",
              "Convertes a Rhino Mesh to an object file",
              "Sculpt2D", "Aleph")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "A mesh to be output in *.obj format", GH_ParamAccess.item);
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
            // define input variables
            Rhino.Geometry.Mesh mesh = new Rhino.Geometry.Mesh();
            string name = null;
            string path = null;

            // register input variables
            DA.GetData(0, ref mesh);
            DA.GetData(1, ref name);
            DA.GetData(2, ref path);

            AlephSupport.ExportToOBJ(path, name, mesh);
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
            get { return new Guid("D1C9A0E4-D3E8-4763-B560-22FEA1011B92"); }
        }
    }
}