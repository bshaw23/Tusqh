using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using EigenWrapper.Eigen;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Geometry.Delaunay;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Collections;
using Rhino.UI;
using Rhino.UI.Controls;
using Sculpt2D;
using Sculpt2D.Sculpt3D;

namespace Sculpt2D.Components
{
    public class MakeManifold : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MakeManifold()
          : base("Make Manifold", "manifold",
              "Makes a 2D Manifold Mesh",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Non-manifold Mesh", "mesh", "Non-manifold mesh to be made manifold", GH_ParamAccess.item);
            pManager.AddMeshParameter("Background Mesh", "back", "Background mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Inside Vertices", "in_vs", "Vertices that are 'inside'", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Manifold Mesh", "mesh", "Manifold mesh", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rhino.Geometry.Mesh input_mesh = new Rhino.Geometry.Mesh();
            Mesh background = new Mesh();
            List<int> verts = new List<int>();

            DA.GetData(0, ref input_mesh);
            DA.GetData(1, ref background);
            DA.GetDataList(2, verts);

            Mesh output_mesh = input_mesh.DuplicateMesh();
            int init_face_count = output_mesh.Faces.Count;

            HashSet<int> in_verts = new HashSet<int>();
            foreach (int vert in verts)
                in_verts.Add(vert);

            Dictionary<Point3d, int> location_to_index = new Dictionary<Point3d, int>();
            for (int v = 0; v < input_mesh.Vertices.Count; v++)
            {
                Point3d vert = input_mesh.Vertices[v];
                Point3d rounded_vert = Functions.RoundPoint(vert);
                
                location_to_index.Add(rounded_vert, v);
            }

            MeshTopologyVertexList vert_list = input_mesh.TopologyVertices;
            MeshTopologyEdgeList edge_list = input_mesh.TopologyEdges;
            //List<int> nonmanifold_verts = new List<int>();
            HashSet<int> nonmanifold_verts = new HashSet<int>();

            int two_faces_counter = 0;

            for(int i = 0; i < vert_list.Count; i++)
            {
                var vert_faces = vert_list.ConnectedFaces(i);

                if (vert_faces.Length == 2)
                {
                    var vert_edges = vert_list.ConnectedEdges(i);
                    two_faces_counter++;
                    if (vert_edges.Length == 4)
                    {
                        nonmanifold_verts.Add(i);
                    }
                }
            }

            Dictionary<int, int> in_to_back = new Dictionary<int, int>(); // Dictionary of input vertex indexes to background vertex indexes
            for (int v = 0; v < input_mesh.Vertices.Count; v++) 
            {
                Point3d vert = input_mesh.Vertices[v];
                for (int w = 0; w < background.Vertices.Count; w++)
                {
                    Point3d back_vert = background.Vertices[w];
                    if (vert.EpsilonEquals(back_vert, 1e-5))
                    {
                        in_to_back.Add(v, w);
                        break;
                    }
                }
            }

            // Breadth first search to find connected nonmanifold_verts
            List<HashSet<int>> connected_pinches = new List<HashSet<int>>();
            HashSet<int> verts_in_series = new HashSet<int>();
            foreach (int vert in nonmanifold_verts)
            {
                HashSet<int> list = new HashSet<int>();
                list.Add(vert);
                if (verts_in_series.Contains(vert))
                    continue;

                Queue<int> queue = new Queue<int>();
                HashSet<int> visited = new HashSet<int>();

                visited.Add(vert);
                queue.Enqueue(vert);

                while (queue.Count != 0)
                {
                    int current_vert = queue.Dequeue();
                    visited.Add(current_vert);
                    int[] connected_faces = input_mesh.TopologyVertices.ConnectedFaces(current_vert);
                    MeshFace f1 = input_mesh.Faces[connected_faces[0]];
                    MeshFace f2 = input_mesh.Faces[connected_faces[1]];
                    List<int> adj_verts = new List<int> { f1.A, f1.B, f1.C, f1.D, f2.A, f2.B, f2.C, f2.D };
                
                    foreach (int adj in adj_verts)
                    {
                        if (nonmanifold_verts.Contains(adj) && !visited.Contains(adj) && !list.Contains(adj))
                        {
                            queue.Enqueue(adj);
                            list.Add(adj);
                            verts_in_series.Add(adj);
                            verts_in_series.Add(vert);
                        }
                    }
                }

                connected_pinches.Add(list);
            }

            List<int> remove_face_at = new List<int>();
            List<int> added_faces = new List<int>();

            foreach (var set in connected_pinches)
            {
                int separate_counter = 0;
                int connect_counter = 0;
                foreach (var vert in set)
                {
                    if (in_verts.Contains(in_to_back[vert]))
                        connect_counter++;
                    else
                        separate_counter++;
                }

                bool separate = true;
                if (separate_counter > connect_counter)
                    separate = true;
                else if (separate_counter < connect_counter)
                    separate = false;
                else
                    separate = true;

                // Add faces
                if (!separate)
                {
                    foreach (int vert in set)
                    {
                        Functions.ConnectPinch(output_mesh, vert, location_to_index);
                    }
                }
                // Remove faces
                else
                {
                    foreach (int vert in set)
                    {
                        Functions.SeparatePinch(output_mesh, vert, remove_face_at, location_to_index);
                    }

                    for(int i = 0; i < output_mesh.Faces.Count; i++)
                    {
                        var face = output_mesh.Faces[i];
                        List<int> face_verts = new List<int> { face.A, face.B, face.C, face.D };
                        //if (face_verts.Contains(12) && face_verts.Contains(6))
                        //{
                        //    face_verts.Add(-1);
                        //}
                        foreach (var v in face_verts)
                        {
                            if (set.Contains(v))
                                remove_face_at.Add(i);
                        }
                    }
                }
            }

            int removed_faces = output_mesh.Faces.DeleteFaces(remove_face_at);

            output_mesh.Faces.ExtractDuplicateFaces();
            output_mesh.UnifyNormals();
            output_mesh.Compact();

            MeshFaceNormalList normals = output_mesh.FaceNormals;

            DA.SetData(0, output_mesh);
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
            get { return new Guid("E052DBC4-2ADC-4AF3-80A7-15E1F280AA71"); }
        }
    }
}