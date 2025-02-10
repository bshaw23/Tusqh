using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
  public class DualBackgroundMesh : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public DualBackgroundMesh()
      : base("Dual Background Mesh", "dualback",
            "Creates a dual to a background mesh from a bounding box",
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
            pManager.AddMeshParameter("Regular Grid", "grid", "2D rectangular grid", GH_ParamAccess.item);
            pManager.AddMeshParameter("Dual Grid", "dual", "2D dual grid", GH_ParamAccess.item);
        }


        private List<double> SubdivideDualIntervalList(List<double> pts)
        {
            List<double> dual = new List<double>();
            dual.Add(pts[0]);
            for (int i = 1; i < pts.Count; ++i)
                dual.Add(pts[i-1] + (pts[i] - pts[i - 1])/2.0);
            dual.Add(pts.Last());

            return dual;
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

            List<double> x_pts = new List<double>();
            List<double> y_pts = new List<double>();

            var bdry = regular_mesh.GetNakedEdges()[0];
            Point3d pt;
            for(int i = 0; i < bdry.Count-1; ++i)
            {
                pt = bdry[i];
                if (Rhino.RhinoMath.EpsilonEquals(pt.X, bounding_box.Corner(0).X, 1e-6))
                    y_pts.Add(pt.Y);
                if (Rhino.RhinoMath.EpsilonEquals(pt.Y, bounding_box.Corner(0).Y, 1e-6))
                    x_pts.Add(pt.X);
            }
            
            x_pts.Sort();
            y_pts.Sort();


            List<double> dual_xvals = SubdivideDualIntervalList(x_pts);
            List<double> dual_yvals = SubdivideDualIntervalList(y_pts);

            List<Line> mesh_grid = new List<Line>();
            for (int i = 0; i < dual_xvals.Count; ++i)
            {
                for(int j = 1; j < dual_yvals.Count; ++j)
                    mesh_grid.Add(new Line(dual_xvals[i], dual_yvals[j-1], 0, dual_xvals[i], dual_yvals[j], 0));
            }
            for (int j = 0; j < dual_yvals.Count; ++j)
            {
                for(int i = 1; i < dual_xvals.Count; ++i)
                    mesh_grid.Add(new Line(dual_xvals[i-1], dual_yvals[j], 0, dual_xvals[i], dual_yvals[j], 0));
            }
            NurbsCurve[] linecurves = new NurbsCurve[mesh_grid.Count];
            for (int i = 0; i < mesh_grid.Count; ++i)
                linecurves[i] = (mesh_grid[i].ToNurbsCurve());

            var dual_mesh = Mesh.CreateFromLines(linecurves, 4, 1e-6);

            //for(int i = 0; i < regular_mesh.TopologyVertices.Count; ++i)
            //{
            //    var pt = dual_mesh.TopologyVertices[i];
            //    bool equals_minx = Rhino.RhinoMath.EpsilonEquals(pt.X, bounding_box.Corner(0).X, 1e-6);
            //    bool equals_miny = Rhino.RhinoMath.EpsilonEquals(pt.Y, bounding_box.Corner(0).Y, 1e-6);
            //    bool equals_maxx = Rhino.RhinoMath.EpsilonEquals(pt.X, bounding_box.Corner(2).X, 1e-6);
            //    bool equals_maxy = Rhino.RhinoMath.EpsilonEquals(pt.Y, bounding_box.Corner(2).Y, 1e-6);

            //    bool average_x_below = false;
            //    bool average_y_below = false;
            //    if (equals_minx && equals_miny)
            //        dual_pts[0]= pt;
            //    else if (equals_maxx && equals_miny)
            //    {
            //        dual_pts[x] = pt;
            //        average_y_below = true;
            //    }
            //    else if (equals_minx && equals_maxy)
            //    {
            //        dual_pts[(x+1)*(y)] = pt;
            //        average_x_below = true;
            //    }
            //    else if (equals_maxy && equals_maxx)
            //    {
            //        dual_pts[(x + 1) * (y+1) - 1] = pt;
            //        average_x_below = true;
            //        average_y_below = true;
            //    }
            //    else if (equals_maxx || equals_minx)
            //        average_y_below = true;
            //    else if (equals_maxy || equals_miny)
            //        average_x_below = true;

            //    if (average_x_below)
            //    {
            //        var con_verts = regular_mesh.TopologyVertices.ConnectedTopologyVertices(i);
            //        foreach (int j in con_verts)
            //        {
            //            var other = regular_mesh.TopologyVertices[j];
            //            if (other.Y < pt.Y)
            //            {
            //                dual_pts.Add(new Point3f((other.X + pt.X) / 2, (other.Y + pt.Y) / 2, 0));
            //                break;
            //            }
            //        }
            //    }
            //    if (average_y_below)
            //    {
            //        var con_verts = regular_mesh.TopologyVertices.ConnectedTopologyVertices(i);
            //        foreach (int j in con_verts)
            //        {
            //            var other = regular_mesh.TopologyVertices[j];
            //            if (other.X < pt.X)
            //            {
            //                dual_pts.Add(new Point3f((other.X + pt.X) / 2, (other.Y + pt.Y) / 2, 0));
            //                break;
            //            }
            //        }
            //    }

            //}

            DA.SetData(0, regular_mesh);
            DA.SetData(1, dual_mesh);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
    {
      get
      { 
        // You can add image files to your project resources and access them like this:
        //return Resources.IconForThisComponent;
        return null;
      }
    }

    /// <summary>
    /// Each component must have a unique Guid to identify it. 
    /// It is vital this Guid doesn't change otherwise old ghx files 
    /// that use the old ID will partially fail during loading.
    /// </summary>
    public override Guid ComponentGuid
    {
      get { return new Guid("3eee1091-6a04-414e-9497-44621684b007"); }
    }
  }
}
