using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace Sculpt2D.Components
{
    public class DisconnectandShrink : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public DisconnectandShrink()
          : base("RemovePinchPoints", "pinch",
              "Removes pinch points by shrinking cell",
              "Sculpt2D", "Sculpt")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Non-manifold Mesh", "mesh", "Non-manifold mesh to be made manifold", GH_ParamAccess.item);
            pManager.AddNumberParameter("Divisor", "div", "The number by which cell length is divided (default = 8)", GH_ParamAccess.item);
            pManager[1].Optional = true;
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
            double divisor = new double();

            DA.GetData(0, ref input_mesh);
            if (!DA.GetData(1, ref divisor))
                divisor = 8;

            Mesh output_mesh = input_mesh.DuplicateMesh();

            MeshTopologyVertexList vert_list = input_mesh.TopologyVertices;
            MeshTopologyEdgeList edge_list = input_mesh.TopologyEdges;
            List<int> nonmanifold_verts = new List<int>();

            int two_faces_counter = 0;

            for (int i = 0; i < vert_list.Count; i++)
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


            int A = input_mesh.Faces[0].A;
            int B = input_mesh.Faces[0].B;
            int C = input_mesh.Faces[0].C;
            int D = input_mesh.Faces[0].D;


            double x_length = Math.Abs(vert_list[A].X - vert_list[B].X);
            double y_length = Math.Abs(vert_list[A].Y - vert_list[D].Y);

            Dictionary<int, List<int>> face_to_nonmanifold_verts = new Dictionary<int, List<int>>();
            foreach(int idx in nonmanifold_verts)
            {
                int[] face_idxs = vert_list.ConnectedFaces(idx);

                if (face_to_nonmanifold_verts.ContainsKey(face_idxs[0]))
                    face_to_nonmanifold_verts[face_idxs[0]].Add(idx);
                else
                    face_to_nonmanifold_verts.Add(face_idxs[0], new List<int> { idx });

                if (face_to_nonmanifold_verts.ContainsKey(face_idxs[1]))
                    face_to_nonmanifold_verts[face_idxs[1]].Add(idx);
                else
                    face_to_nonmanifold_verts.Add(face_idxs[1], new List<int> { idx });
            }

            foreach(int face in  face_to_nonmanifold_verts.Keys)
            {
                A = output_mesh.Faces[face].A;
                B = output_mesh.Faces[face].B;
                C = output_mesh.Faces[face].C;
                D = output_mesh.Faces[face].D;

                Point3d A_pt = output_mesh.Vertices[A];
                Point3d B_pt = output_mesh.Vertices[B];
                Point3d C_pt = output_mesh.Vertices[C];
                Point3d D_pt = output_mesh.Vertices[D];

                List<int> face_verts = new List<int> { A, B, C, D };

                List<int> verts = face_to_nonmanifold_verts[face];

                foreach(int idx in verts)
                {
                    int vert_idx = face_verts.IndexOf(idx);

                    if (vert_idx == 3) // D
                    {
                        D_pt.X += x_length / divisor;
                        D_pt.Y -= y_length / divisor;
                    }
                    else if (vert_idx == 2) // C
                    {
                        C_pt.X -= x_length / divisor;
                        C_pt.Y -= x_length / divisor;
                    }
                    else if (vert_idx == 1) // B
                    {
                        B_pt.X -= x_length / divisor;
                        B_pt.Y += y_length / divisor;
                    }
                    else if (vert_idx == 0) // A
                    {
                        A_pt.X += x_length / divisor;
                        A_pt.Y += y_length / divisor;
                    }
                    else
                        throw new Exception(String.Format("At face {0}, vert_idx = {1}. As implied by this appearance of the message, this is a problem", face.ToString(), vert_idx.ToString()));
                }

                A = output_mesh.Vertices.Add(A_pt);
                B = output_mesh.Vertices.Add(B_pt);
                C = output_mesh.Vertices.Add(C_pt);
                D = output_mesh.Vertices.Add(D_pt);

                MeshFace meshface = new MeshFace(A, B, C, D);

                output_mesh.Faces.AddFace(meshface);
            }

            output_mesh.Vertices.Remove(nonmanifold_verts, false);

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
            get { return new Guid("3590D54E-CDEA-4596-BD71-2DA577F90640"); }
        }
    }
}