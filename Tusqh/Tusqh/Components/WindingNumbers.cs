using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Intrinsics;
using EigenWrapper.Eigen;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class WindingNumbers : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public WindingNumbers()
          : base("Winding Numbers", "wind",
              "Utilizes the winding numbers to get a volume fraction",
              "Sculpt2D", "Volume")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "Mesh to be sculpted", GH_ParamAccess.item);
            pManager.AddMeshParameter("Background Grid", "grid", "Background grid mesh used for sculpting", GH_ParamAccess.item);
            pManager.AddIntegerParameter("X Points", "x", "Number of points in x direction per face", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Y Points", "y", "Number of points in y direction per face", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Volume Fractions", "vol", "Volume fractions for background grid faces", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centroid as List", "cent", "Centroid of each face as a list of points", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Points per face", "pts/face", "The number of test points per face of the background grid", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            /*
            // define variables for inputs
            Mesh mesh = new Mesh();
            Mesh background_grid = new Mesh();
            int pts_in_x = new int();
            int pts_in_y = new int();

            // get input data for variables
            DA.GetData(0, ref mesh);
            DA.GetData(1, ref background_grid);
            DA.GetData(2, ref pts_in_x);
            DA.GetData(3, ref pts_in_y);

            // define output variable
            Span<double> winding_numbers = new Span<double>();

            // define other variables
            int n_verts;
            int vert_dim;
            int n_faces;
            int face_dim;
            int n_points;
            int point_dim;
            int pts_per_face = 0;

            Rhino.Geometry.Collections.MeshTopologyVertexList verts = mesh.TopologyVertices;
            List<Point3d> centroid = new List<Point3d>();
            List<double> x_list = new List<double>();
            List<double> y_list = new List<double>();
            List<double> vertices_xlist_ylist = new List<double>();
            List<double> vertices_x_y = new List<double>();
            List<double> point_grids_x_y = new List<double>();
            List<double> point_grids_x = new List<double>();
            List<double> point_grids_y = new List<double>();
            List<int> edges_v1_v2 = new List<int>();
            ReadOnlySpan<int> edges_rospan = new ReadOnlySpan<int>(); //need to initialize it with the correct number of terms
            ReadOnlySpan<double> point_grids_rospan = new ReadOnlySpan<double>(); //need to initialize it with the correct number of terms

            //Matrix point_grids = AlephSupport.BackgroundGridPts(background_grid, pts_in_x, pts_in_y, out centroid, out pts_per_face, out point_grids_rospan);
            //Matrix edge_matrix = AlephSupport.EdgeListToBoundaries(mesh, out edges_rospan);

            // get vertex information
            // vertex list alternating x and y information
            foreach (Point3d vertex in verts)
            {
                double x = vertex.X;
                double y = vertex.Y;

                x_list.Add(x);
                y_list.Add(y);

                vertices_x_y.Add(x);
                vertices_x_y.Add(y);
            }

            // vertex span and read only span
            double[] vertex_counter = new double[vertices_x_y.Count];
            Span<double> vertices_span = new Span<double>(vertex_counter);
            for(int i = 0; i < vertices_x_y.Count; i++)
            {
                vertices_span[i] = vertices_x_y[i];
                vertex_counter[i] = vertices_x_y[i];
            }

            ReadOnlySpan<double> vertices_rospan = vertices_span;

            // vertex list of all x information followed by all y information
            foreach (double x in x_list)
                vertices_xlist_ylist.Add(x);
            foreach (double y in y_list)
                vertices_xlist_ylist.Add(y);

            // vertix information listed in a matrix
            // column 0 - x; column 1 - y;
            Matrix vertices_matrix = new Matrix(x_list.Count, 2);

            // get edge information
            // listed in alternating x and y
            List<int> v1 = new List<int>();
            List<int> v2 = new List<int>();
            for (int i = 0; i < verts.Count; i++)
            {
                v1.Add((int)edge_matrix[i, 0]);
                v2.Add((int)edge_matrix[i, 1]);

                edges_v1_v2.Add((int)edge_matrix[i, 0]);
                edges_v1_v2.Add((int)edge_matrix[i, 1]);
            }

            // listed as full x list followed by full y list
            List<int> edge_list = new List<int>();
            foreach (int vertex in v1)
                edge_list.Add(vertex);
            foreach (int vertex in v2)
                edge_list.Add(vertex);

            // create a list of point grids alternating x and y information
            for (int i = 0; i < point_grids.RowCount; i++)
            {
                point_grids_x_y.Add(point_grids[i, 0]);
                point_grids_x_y.Add(point_grids[i, 1]);

                point_grids_x.Add(point_grids[i, 0]);
                point_grids_y.Add(point_grids[i, 1]);
            }

            // create a list of point grid with all x information followed by all y information
            List<double> point_grids_xlist_ylist = new List<double>();
            foreach (int point in point_grids_x)
                point_grids_xlist_ylist.Add(point);
            foreach (int point in point_grids_y)
                point_grids_xlist_ylist.Add(point);

            // define other variable values
            n_verts = vertices_matrix.RowCount;
            vert_dim = 2;
            n_faces = edge_matrix.RowCount;
            face_dim = 2;
            n_points = point_grids.RowCount;
            point_dim = 2;

            // winding number function using lists
            EigenDenseUtilities.WindingNumber(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices_xlist_ylist), n_verts, vert_dim, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edge_list),
                n_faces, face_dim, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(point_grids_xlist_ylist), n_points, point_dim, winding_numbers);

            //// winding number function using spans
            //EigenDenseUtilities.WindingNumber(vertices_rospan, n_verts, vert_dim, edges_rospan, n_faces, face_dim, point_grids_rospan, n_points, point_dim, winding_numbers);

            List<double> winding_numbers_list = new List<double>();
            for (int i = 0; i < winding_numbers.Length; i++)
            {
                winding_numbers_list.Add(winding_numbers[i]);
            }
            
            DA.SetDataList(0, winding_numbers_list);
            DA.SetDataList(1, centroid);
            DA.SetData(2, pts_per_face);
            */

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
            get { return new Guid("1B39BDCB-E89E-47FB-9A74-5AF0C593CE75"); }
        }
    }
}