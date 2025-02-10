using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
  public class RefineDualForVolumeFraction : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public RefineDualForVolumeFraction()
      : base("RefineDualForVolumeFraction", "DualVF",
        "Refine Dual For Volume Fraction output to Aleph",
        "Sculpt2D", "Aleph")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
            pManager.AddMeshParameter("Dual Grid", "dual", "2D dual grid", GH_ParamAccess.item);
            pManager.AddPointParameter("Face Centroid", "cent", "Center of the face whose volume fraction was just computed", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fraction", "vol", "List of volume fractions", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Use Minimum for Centroid", "cm", "Use the minimum coordinate value for the centroid", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Negate Z Values", "neg", "Use the negative of the mesh's Z values", GH_ParamAccess.item);
            pManager.AddNumberParameter("Shift Z Values", "sft", "Shift the mesh's Z values", GH_ParamAccess.item);
            pManager[5].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
            pManager.AddMeshParameter("Triangulation", "tri", "Triangulation with volume fraction data ready for Aleph", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
    /// to store data in output parameters.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
            Mesh dual_mesh_init = null;
            List<Point3d> centroids = new List<Point3d>();
            List<double> volume_fracs = new List<double>();
            bool use_min = false;
            bool negate_z = false;
            double shift_d = 0;

            DA.GetData<Mesh>(0, ref dual_mesh_init);
            DA.GetDataList<Point3d>(1, centroids);
            DA.GetDataList<double>(2, volume_fracs);
            DA.GetData(3, ref use_min);
            DA.GetData(4, ref negate_z);
            if (!DA.GetData(5, ref shift_d))
                shift_d = 0;

            float shift = (float)shift_d;

            Mesh dual_mesh = dual_mesh_init.DuplicateMesh();

            // move the volume fraction data to appropriate face
            // for boundary ones, move them to the closest face centroid
            for (int j = 0; j < dual_mesh.Vertices.Count; ++j)
            {
                Point3d vert = dual_mesh.Vertices[j];
                double mindist = double.PositiveInfinity;
                int minidx = -1;
                double tempdist;
                for(int i = 0; i < centroids.Count; ++i)
                {
                    var pt = centroids[i];
                    tempdist = pt.DistanceToSquared(vert);
                    if (tempdist < mindist)
                    {
                        mindist = tempdist;
                        minidx = i;
                    }
                }

                vert.Z = volume_fracs[minidx];
                dual_mesh.Vertices[j] = (Point3f)vert;
            }

            // next, modify each quadrilateral face to be subdivided into 4 triangles
            int init_face_count = dual_mesh.Faces.Count;
            for (int i = init_face_count - 1; i > -1; --i) 
            {
                var quad_face = dual_mesh.Faces[i];
                var centroid = dual_mesh.Faces.GetFaceCenter(i);
                // extract the z coordinates of all adjacent vertices and use the lowest one
                List<int> iter_list = new List<int>(4) { quad_face.A, quad_face.B, quad_face.C, quad_face.D };
                foreach (int idx in iter_list)
                {
                    // Oddly the homology does not appear to change when I change which one I use
                    // What is up with that?
                    // Should I change the negate z from multiplying by -1 to just shift them?

                    if (dual_mesh.Vertices[idx].Z < centroid.Z && use_min)
                        centroid.Z = dual_mesh.Vertices[idx].Z;
                    else if (dual_mesh.Vertices[idx].Z > centroid.Z && !use_min)
                        centroid.Z = dual_mesh.Vertices[idx].Z;

                }

                dual_mesh.Faces.RemoveAt(i, false);
                int new_idx = dual_mesh.Vertices.Add(centroid);
                dual_mesh.Faces.AddFace(quad_face.A, quad_face.B, new_idx);
                dual_mesh.Faces.AddFace(quad_face.B, quad_face.C, new_idx);
                dual_mesh.Faces.AddFace(quad_face.C, quad_face.D, new_idx);
                dual_mesh.Faces.AddFace(quad_face.D, quad_face.A, new_idx);
            }

            if (negate_z)
            {
                Point3f temp_pt;
                for (int i = 0; i < dual_mesh.Vertices.Count; ++i)
                {
                    temp_pt = dual_mesh.Vertices[i];
                    temp_pt.Z *= -1;
                    dual_mesh.Vertices[i] = temp_pt;
                }
            }

            if (shift != 0)
            {
                Point3f temp_pt;
                for (int i = 0; i < dual_mesh.Vertices.Count; ++i)
                {
                    temp_pt = dual_mesh.Vertices[i];
                    temp_pt.Z += shift;
                    dual_mesh.Vertices[i] = temp_pt;
                }
            }

            DA.SetData(0, dual_mesh);
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
      get { return new Guid("60b9f393-dc44-4781-ad23-30fa2b036dae"); }
    }
  }
}
