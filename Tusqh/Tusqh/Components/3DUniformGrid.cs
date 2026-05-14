using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class UniformGrid : GH_Component
    {
        public UniformGrid()
          : base("Uniform Grid Divisions", "uniform_grid",
              "Computes divisions from a bounding box and nx so that all cells are equal-sized. " +
              "2D mode is triggered automatically when the box has zero z extent; outputs [nx, ny]. " +
              "3D mode when z extent is non-zero; outputs [nx, ny, nz].",
              "Sculpt3D", "Sculpt")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Bounding Box", "box", "Bounding box of the region to mesh. Zero z extent triggers 2D mode.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("X Divisions", "nx", "Number of cells in the x direction", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("X Divisions", "nx", "Number of cells in x", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Y Divisions", "ny", "Number of cells in y", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Z Divisions", "nz", "Number of cells in z (3D mode only; null in 2D mode)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Cell Size", "size", "Side length of each square/cubic cell", GH_ParamAccess.item);
            pManager.AddBoxParameter("Snapped Box", "snapbox", "Geometry snapped to exact cell boundaries", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Divisions", "divs", "[nx, ny] in 2D mode or [nx, ny, nz] in 3D mode — connect directly to ExportSPN", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            int nx = 0;

            if (!DA.GetData(0, ref box)) return;
            if (!DA.GetData(1, ref nx)) return;

            if (nx <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "X Divisions must be greater than zero.");
                return;
            }

            double lx = box.X.Length;
            double ly = box.Y.Length;
            double lz = box.Z.Length;

            if (lx <= 0 || ly <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bounding box must have positive extent in x and y.");
                return;
            }

            double cell_size = lx / nx;
            int ny = Math.Max(1, (int)Math.Round(ly / cell_size));

            bool is_2d = lz <= 0;

            Plane plane = box.Plane;
            Interval x_interval = new Interval(box.X.T0, box.X.T0 + nx * cell_size);
            Interval y_interval = new Interval(box.Y.T0, box.Y.T0 + ny * cell_size);

            DA.SetData(0, nx);
            DA.SetData(1, ny);
            DA.SetData(3, cell_size);

            if (is_2d)
            {
                Interval z_interval = new Interval(box.Z.T0, box.Z.T0);
                Box snapped_box = new Box(plane, x_interval, y_interval, z_interval);
                DA.SetData(4, snapped_box);
                DA.SetDataList(5, new List<int> { nx, ny });
            }
            else
            {
                int nz = Math.Max(1, (int)Math.Round(lz / cell_size));
                Interval z_interval = new Interval(box.Z.T0, box.Z.T0 + nz * cell_size);
                Box snapped_box = new Box(plane, x_interval, y_interval, z_interval);
                DA.SetData(2, nz);
                DA.SetData(4, snapped_box);
                DA.SetDataList(5, new List<int> { nx, ny, nz });
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("B2E4F7A1-3D85-4C92-8E6F-1A9D3C05B8E2");
    }
}
