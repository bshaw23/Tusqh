using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D
{
    public class BackgroundMesh : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public BackgroundMesh()
          : base("Background Mesh", "background",
            "Creates a background mesh from the bounding box",
            "Sculpt2D", "Background")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Bounding Box", "bb", "2D rectangle bounding a geometry", GH_ParamAccess.item);
            pManager.AddIntegerParameter("X", "x", "x parameter", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Y", "y", "y parameter", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Rectangular Grid", "grid", "2D rectangular grid", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rhino.Geometry.Rectangle3d bounding_box = new Rectangle3d();
            int x = new int();
            int y = new int();

            DA.GetData(0, ref bounding_box);
            DA.GetData(1, ref x);
            DA.GetData(2, ref y);

            Rhino.Geometry.Plane rectangle = new Rhino.Geometry.Plane(bounding_box.Plane);
            var corner = bounding_box.Corner(0);
            var x_corner = bounding_box.Corner(1);
            var y_corner = bounding_box.Corner(3);
            Rhino.Geometry.Interval X = new Interval(corner.X, x_corner.X);
            Rhino.Geometry.Interval Y = new Interval(corner.Y, y_corner.Y);
            var regular_mesh = Rhino.Geometry.Mesh.CreateFromPlane(rectangle, X, Y, x, y);

            DA.SetData(0, regular_mesh);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("64e78460-9a9f-4b35-9dca-c1f48dcd4078");
    }
}