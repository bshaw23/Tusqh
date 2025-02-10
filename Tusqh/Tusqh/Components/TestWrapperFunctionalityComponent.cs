using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

using EigenWrapper.Eigen;
using System.Runtime.Intrinsics;
using Rhino.DocObjects.Tables;
using System.Security.Cryptography;
using GH_IO.Serialization;

namespace Sculpt2D.Components
{
  public class TestWrapperFunctionalityComponent : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public TestWrapperFunctionalityComponent()
      : base("Test Wrapper Functionality Component", "Test",
        "Test functionality of a wrapper around Eigen",
        "Sculpt2D", "Sculpt")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
            pManager.AddNumberParameter("List 1", "l1", "First list for dot product", GH_ParamAccess.list);
            pManager.AddNumberParameter("List 2", "l2", "Second list for dot product", GH_ParamAccess.list);
    }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Dot product", "dp", "Dot product between the lists", GH_ParamAccess.item);
            pManager.AddNumberParameter("Winding Number", "wn", "Winding Numbers for a mesh", GH_ParamAccess.list);
        }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
    /// to store data in output parameters.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
            List<double> list1 = new List<double>();
            List<double> list2 = new List<double>();
            DA.GetDataList(0, list1);
            DA.GetDataList(1, list2);

            
            double val = EigenDenseUtilities.Dot(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list1), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list2), list1.Count);

            DA.SetData(0, val);

           



            // TEST WindingNumber FUNCTION

            // define vertex positions
            Point2d v1 = new Point2d();
            Point2d v2 = new Point2d();
            Point2d v3 = new Point2d();
            Point2d v4 = new Point2d();
            v1.X = 0; v1.Y = 0; // v1 = (0,0,0)
            v2.X = 5; v2.Y = 0; // v2 = (5,0,0)
            v3.X = 0; v3.Y = 5; // v3 = (0,5,0)
            v4.X = 5; v4.Y = 5; // v4 = (5,5,0)

            double[] vertices_list = new double[8];

            Dictionary<int, Point2d> vertices_dict = new Dictionary<int, Point2d>();
            vertices_dict.Add(0, v1); vertices_dict.Add(1, v2); vertices_dict.Add(2, v3);
            vertices_dict.Add(3, v4);

            Span<double> vertices_span = new Span<double>(vertices_list);
            Span<double> verts_span_xlist_ylist = new Span<double>(vertices_list);
            for(int i = 0; i < 4; i++)
            {
                vertices_span[2 * i] = vertices_dict[i][0];
                vertices_span[2 * i + 1] = vertices_dict[i][1];

                verts_span_xlist_ylist[i] = vertices_dict[i][0];
                verts_span_xlist_ylist[i + 4] = vertices_dict[i][1];
            }

            Matrix V = new Matrix(4, 2);
            for (int i = 0; i < 4; i++)
            {
                V[i, 0] = vertices_dict[i][0];
                V[i, 1] = vertices_dict[i][1];
            }

            // Define mesh edges
            int[] e1 = new int[2] { 0, 1 };
            int[] e2 = new int[2] { 0, 2 };
            int[] e3 = new int[2] { 0, 3 };
            int[] e4 = new int[2] { 1, 3 };
            //int[] e5 = new int[2] { 2, 3 };

            int[] edge_list = new int[10];

            Dictionary<int, int[]> edges_dict = new Dictionary<int, int[]>();
            edges_dict.Add(0, e1); edges_dict.Add(1, e2); edges_dict.Add(2, e3);
            edges_dict.Add(3, e4); /*edges_dict.Add(4, e5);*/

            Span<int> edges_span = new Span<int>(edge_list);
            Span<int> edges_e1list_e2list = new Span<int>(edge_list);
            for (int i = 0; i < 3; i++)
            {
                edges_span[2 * i] = edges_dict[i][0];
                edges_span[2 * i + 1] = edges_dict[i][1];

                edges_e1list_e2list[i] = edges_dict[i][0];
                edges_e1list_e2list[i+4] = edges_dict[i][1];
            }

            // Define test points
            Point2d p1 = new Point2d();
            Point2d p2 = new Point2d();
            Point2d p3 = new Point2d();
            Point2d p4 = new Point2d();
            Point2d p5 = new Point2d();
            p1.X = 1.25; p1.Y = 1.25; // p1 = (1.25, 1.25)
            p2.X = 3.75; p2.Y = 1.25; // p2 = (3.75, 1.25)
            p3.X = 1.25; p3.Y = 3.75; // p3 = (1.25, 3.75)
            p4.X = 3.75; p4.Y = 3.75; // p4 = (3.75, 3.75)
            p5.X = 2.50; p5.Y = 2.50; // p5 = (2.50, 2.50)

            Dictionary<int, Point2d> test_points_dict = new Dictionary<int, Point2d>();
            test_points_dict.Add(0, p1); test_points_dict.Add(1, p2);
            test_points_dict.Add(2, p3); test_points_dict.Add(3, p4);
            test_points_dict.Add(4, p5);

            double[] test_list = new double[10];
            double[] test_point = new double[2];

            Span<double> test_points_span = new Span<double>(test_list);
            Span<double> test_points_xlist_ylist = new Span<double>(test_list);
            Span<double> point_span = new Span<double>(test_point);
            for (int i = 0; i < 5; i++)
            {
                test_points_span[2 * i] = test_points_dict[i][0];
                test_points_span[2 * i + 1] = test_points_dict[i][1];

                test_points_xlist_ylist[i] = test_points_dict[i][0];
                test_points_xlist_ylist[i + 5] = test_points_dict[i][1];
            }

            point_span[0] = test_points_dict[0][0];
            point_span[1] = test_points_dict[0][1];

            int n_verts = 4;
            int vert_dim = 2;
            int n_faces = 5;
            int face_dim = 2;
            int n_points = 1;
            int point_dim = 2;

            double[] winding_number_list = new double[5];
            double[] winding_number = new double[1];

            Span<double> winding_numbers = new Span<double>(winding_number);
            
            EigenDenseUtilities.WindingNumber(vertices_span, n_verts, vert_dim,
                edges_span, n_faces, face_dim,
                point_span, n_points, point_dim,
                winding_numbers);

            //EigenDenseUtilities.WindingNumber(verts_span_xlist_ylist, n_verts, vert_dim,
            //   edges_e1list_e2list, n_faces, face_dim,
            //   test_points_xlist_ylist, n_points, point_dim,
            //   winding_numbers);

            for (int i = 0; i < winding_numbers.Length; i++)
            {
                winding_number_list[i] = winding_numbers[i];
            }

            DA.SetDataList(1, winding_number_list);

            // Try it another way
            unsafe
            {
                var vspan = new Span<double>();

                double[,] v =
                {
                    { 0, 5, 0, 5 },
                    { 0, 0, 5, 5 }
                };

                var length = v.GetLength(0) * v.GetLength(1);
                fixed (double* pointer = v)
                {
                    vspan = new Span<double>(pointer, length);
                }

                var espan = new Span<int>();

                int[,] e =
                {
                    { 0, 0, 0, 1, 2 },
                    { 1, 2, 3, 3, 3 }
                };

                var elength = e.GetLength(0) * e.GetLength(1);
                fixed (int* epointer = e)
                {
                    espan = new Span<int>(epointer, elength);
                }

                var pspan = new Span<double>();

                double[,] p =
                {
                    { 1.25, 3.75, 1.25, 3.75, 2.50 },
                    { 1.25, 1.25, 3.75, 3.75, 2.50 }
                };

                var plength = p.GetLength(0) * p.GetLength(1);
                fixed (double* ppointer = p)
                {
                    pspan = new Span<double>(ppointer, elength);
                }

                double[] unsafe_list = new double[5];

                Span<double> unsafe_winding_numbers = new Span<double>(unsafe_list);

                EigenDenseUtilities.WindingNumber(vspan, n_verts, vert_dim,
                espan, n_faces, face_dim,
                pspan, n_points, point_dim,
                unsafe_winding_numbers);
            }


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
      get { return new Guid("6a876d46-e334-41ac-aa33-cd171dab2226"); }
    }
  }
}
