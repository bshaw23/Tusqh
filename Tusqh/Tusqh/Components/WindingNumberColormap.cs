using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class WindingNumberColormap : GH_Component
    {
        public WindingNumberColormap()
          : base("Winding Number Colormap", "wn_colormap",
              "Visualizes winding numbers on a 2D background mesh as a blue-to-red colormap",
              "Sculpt2D", "Visualize")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Background Mesh", "mesh", "2D background mesh (from BackgroundMesh)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Winding Numbers", "wn", "Winding number per face (from VolumeFractions)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Value", "min", "Minimum value for colormap range (optional, defaults to data min)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Value", "max", "Maximum value for colormap range (optional, defaults to data max)", GH_ParamAccess.item);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Colored Mesh", "mesh", "Background mesh with per-face winding number colors", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Value", "min", "Colormap minimum value used", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Value", "max", "Colormap maximum value used", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = new Mesh();
            List<double> winding_numbers = new List<double>();
            double min_val = double.NaN;
            double max_val = double.NaN;

            if (!DA.GetData(0, ref mesh)) return;
            if (!DA.GetDataList(1, winding_numbers)) return;
            DA.GetData(2, ref min_val);
            DA.GetData(3, ref max_val);

            if (winding_numbers.Count != mesh.Faces.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Winding number count ({winding_numbers.Count}) must match face count ({mesh.Faces.Count}).");
                return;
            }

            if (double.IsNaN(min_val)) min_val = winding_numbers.Min();
            if (double.IsNaN(max_val)) max_val = winding_numbers.Max();

            if (Math.Abs(max_val - min_val) < 1e-12)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "All winding numbers are identical — colormap will be uniform.");
                max_val = min_val + 1.0;
            }

            Mesh colored = mesh.DuplicateMesh();
            colored.VertexColors.CreateMonotoneMesh(Color.White);

            for (int i = 0; i < colored.Faces.Count; i++)
            {
                double t = (winding_numbers[i] - min_val) / (max_val - min_val);
                t = Math.Max(0.0, Math.Min(1.0, t));
                Color c = MapToColor(t);
                MeshFace face = colored.Faces[i];
                colored.VertexColors[face.A] = c;
                colored.VertexColors[face.B] = c;
                colored.VertexColors[face.C] = c;
                if (face.IsQuad)
                    colored.VertexColors[face.D] = c;
            }

            DA.SetData(0, colored);
            DA.SetData(1, min_val);
            DA.SetData(2, max_val);
        }

        private static Color MapToColor(double t)
        {
            // blue (240°) → cyan (180°) → green (120°) → yellow (60°) → red (0°)
            double hue = (1.0 - t) * 240.0;
            return HslToColor(hue, 1.0, 0.5);
        }

        private static Color HslToColor(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = l - c / 2.0;
            double r, g, b;
            if      (h < 60)  { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else              { r = c; g = 0; b = x; }
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("C5D8E2F4-7A03-4B91-9E5D-2C4F8A16D3B7");
    }
}
