using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class Scultp : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public Scultp()
          : base("Sculpt", "sculpt",
              "Scultps background grid based on valume fractions",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background Grid", "grid", "Background grid created from bounding box", GH_ParamAccess.item);
            pManager.AddNumberParameter("Volume Fractions", "vol", "List of volume fractions for the background grid", GH_ParamAccess.list);
            pManager.AddNumberParameter("Minimum Volume Fraction", "frac", "Minumum volume fraction that will be included in sculpted mesh", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Sculpted Mesh", "SM", "Background mesh sculpted based on minimum volume fraction", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rhino.Geometry.Mesh background_grid = new Rhino.Geometry.Mesh();
            List<double> volume_fractions = new List<double>();
            double fraction = new double();

            DA.GetData(0, ref background_grid);
            DA.GetDataList(1, volume_fractions);
            DA.GetData(2, ref fraction);

            List<Rhino.Geometry.MeshFace> faces = new List<MeshFace>();
            Rhino.Geometry.Mesh sculpted_mesh = background_grid.DuplicateMesh();

            int num_faces = background_grid.Faces.Count;
            for( int i = num_faces-1; i >= 0; --i)
            {
                if (volume_fractions[i] < fraction) // && !faces_to_add.Contains(i)
                    sculpted_mesh.Faces.RemoveAt(i,true);
                    //faces.Add(face);

            }

            DA.SetData(0, sculpted_mesh);
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
            get { return new Guid("FCEF04BC-099A-43A1-B83A-DE818D1442EA"); }
        }
    }
}