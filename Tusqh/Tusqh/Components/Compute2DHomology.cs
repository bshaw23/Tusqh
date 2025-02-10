using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
  public class Compute2DHomology : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public Compute2DHomology()
      : base("Compute 2D Homology", "2DHomology",
        "Compute the number of generators for the two-dimensional homology classes",
        "Sculpt2D", "Sculpt")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
            pManager.AddMeshParameter("Sculpt background mesh with removed faces", "bg", "The background mesh with removed faces due to volume fraction computations", GH_ParamAccess.item);
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
            pManager.AddIntegerParameter("H0 generators", "H0", "Number of generators for H0", GH_ParamAccess.item);
            pManager.AddIntegerParameter("H1 generators", "H1", "Number of generators for H1", GH_ParamAccess.item);
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
    /// to store data in output parameters.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
            Mesh mesh = null;
            DA.GetData(0, ref mesh);

            HashSet<int> visited_verts = new HashSet<int>();
            int connected_components = 0;

            for (int vert_idx = 0; vert_idx < mesh.TopologyVertices.Count; ++vert_idx)
            {
                if (visited_verts.Contains(vert_idx))
                    continue;
                else
                {
                    ++connected_components;
                    visited_verts.Add(vert_idx);
                    var adj_verts = mesh.TopologyVertices.ConnectedTopologyVertices(vert_idx);
                    Queue<int> component_queue = new Queue<int>();
                    foreach (var idx in adj_verts)
                        component_queue.Enqueue(idx);
                    while (component_queue.Count>0)
                    {
                        int idx = component_queue.Dequeue();
                        if (visited_verts.Contains(idx))
                            continue;
                        else
                        {
                            visited_verts.Add(idx);
                            adj_verts = mesh.TopologyVertices.ConnectedTopologyVertices(idx);
                            foreach (var adj_idx in adj_verts)
                                component_queue.Enqueue(adj_idx);
                        }
                    }
                }
            }

            // Determine the Euler Characteristic
            int n_verts = mesh.TopologyVertices.Count;
            int n_edges = mesh.TopologyEdges.Count;
            int n_faces = mesh.Faces.Count;

            int euler = n_verts - n_edges + n_faces;

            DA.SetData(0, connected_components);
            DA.SetData(1, connected_components - euler);

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
      get { return new Guid("ca05d1bc-765d-419b-9995-700909564fcb"); }
    }
  }
}
