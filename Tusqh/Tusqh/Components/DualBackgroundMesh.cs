using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
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
            Stopwatch total_timer = Stopwatch.StartNew();
            Stopwatch phase_timer = Stopwatch.StartNew();
            List<string> timings = new List<string>();

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

            timings.Add($"01 inputs + regular mesh creation ({x}x{y}): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

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

            timings.Add($"02 boundary point extraction ({x_pts.Count:N0} x, {y_pts.Count:N0} y): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            List<double> dual_xvals = SubdivideDualIntervalList(x_pts);
            List<double> dual_yvals = SubdivideDualIntervalList(y_pts);

            // Built directly by index instead of via Mesh.CreateFromLines,
            // which was the dominant cost here.
            int dual_x_count = dual_xvals.Count;
            int dual_y_count = dual_yvals.Count;
            Mesh dual_mesh = new Mesh();
            for (int j = 0; j < dual_y_count; ++j)
                for (int i = 0; i < dual_x_count; ++i)
                    dual_mesh.Vertices.Add(dual_xvals[i], dual_yvals[j], 0);

            for (int j = 0; j < dual_y_count - 1; ++j)
            {
                for (int i = 0; i < dual_x_count - 1; ++i)
                {
                    int a = j * dual_x_count + i;
                    int b = j * dual_x_count + (i + 1);
                    int c = (j + 1) * dual_x_count + (i + 1);
                    int d = (j + 1) * dual_x_count + i;
                    dual_mesh.Faces.AddFace(a, b, c, d);
                }
            }
            dual_mesh.Normals.ComputeNormals();
            dual_mesh.FaceNormals.ComputeFaceNormals();

            timings.Add($"03 dual mesh construction (direct grid, {dual_mesh.Vertices.Count:N0} verts, "
                + $"{dual_mesh.Faces.Count:N0} faces): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

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

            timings.Add($"04 publish outputs: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            total_timer.Stop();
            timings.Add($"TOTAL: {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(2, timings);
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
