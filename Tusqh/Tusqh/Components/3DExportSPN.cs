using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Grasshopper.Kernel;

namespace Sculpt2D.Components
{
    public class ExportSPN : GH_Component
    {
        public ExportSPN()
          : base("Export to SPN", "ex_spn",
              "Export volume fractions to a Sculpt .spn microstructure file",
              "Sculpt3D", "Sculpt")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Volume Fractions", "vol", "Volume fraction per cell. 3D: from BackVol3D (x→y→z). 2D: from VolumeFractions with BackgroundMesh (face order).", GH_ParamAccess.list); // 0
            pManager.AddIntegerParameter("Divisions", "divs", "Grid divisions: [nx, ny, nz] for 3D (from BackVol3D), or [nx, ny] for 2D extrusion (nz=1 is assumed)", GH_ParamAccess.list);                 // 1
            pManager.AddNumberParameter("Threshold", "t", "Cells with fraction >= threshold are assigned the inside material ID", GH_ParamAccess.item);                                                       // 2
            pManager.AddIntegerParameter("Inside Material ID", "matIn", "Material ID for cells inside the geometry", GH_ParamAccess.item);                                                                    // 3
            pManager.AddIntegerParameter("Outside Material ID", "matOut", "Material ID for cells outside the geometry", GH_ParamAccess.item);                                                                 // 4
            pManager.AddTextParameter("File Path", "path", "Full path for the output .spn file (e.g. C:\\output\\model.spn)", GH_ParamAccess.item);                                                          // 5
            pManager.AddBooleanParameter("Write", "write", "Set to true to write the file", GH_ParamAccess.item);                                                                                             // 6

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "status", "Status message after writing", GH_ParamAccess.item);           // 0
            pManager.AddTextParameter("Sculpt Command", "cmd", "Suggested sculpt.exe command line", GH_ParamAccess.item); // 1
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list); // 2
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stopwatch total_timer = Stopwatch.StartNew();
            Stopwatch phase_timer = Stopwatch.StartNew();
            List<string> timings = new List<string>();

            List<double> volume_fractions = new List<double>();
            List<int> divisions = new List<int>();
            double threshold = 0.5;
            int mat_inside = 1;
            int mat_outside = 2;
            string file_path = null;
            bool write = false;

            if (!DA.GetDataList(0, volume_fractions)) return;
            if (!DA.GetDataList(1, divisions)) return;
            if (!DA.GetData(5, ref file_path)) return;

            if (!DA.GetData(2, ref threshold)) threshold = 0.5;
            if (!DA.GetData(3, ref mat_inside)) mat_inside = 1;
            if (!DA.GetData(4, ref mat_outside)) mat_outside = 2;
            if (!DA.GetData(6, ref write)) write = false;

            if (divisions.Count != 2 && divisions.Count != 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Divisions must be [nx, ny] for 2D or [nx, ny, nz] for 3D.");
                return;
            }

            bool is_2d = divisions.Count == 2;
            int nx = divisions[0];
            int ny = divisions[1];
            int nz = is_2d ? 1 : divisions[2];
            int expected = is_2d ? nx * ny : nx * ny * nz;

            if (volume_fractions.Count != expected)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Volume fraction count ({volume_fractions.Count}) does not match grid size {nx}×{ny}{(is_2d ? "" : $"×{nz}")} = {expected}.");
                return;
            }

            string spn_name = Path.GetFileName(file_path);
            string cmd = $"sculpt.exe -j 8 -x {nx} -y {ny} -z {nz} -isp \"{spn_name}\" -p 1";

            timings.Add($"01 inputs/validation: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
            phase_timer.Restart();

            if (!write)
            {
                DA.SetData(0, $"Set Write=true to write the file. Mode: {(is_2d ? "2D planar extrusion (nz=1)" : "3D")}");
                DA.SetData(1, cmd);
                total_timer.Stop();
                timings.Add("02 SPN formatting: skipped (Write=false)");
                timings.Add("03 file writing: skipped (Write=false)");
                timings.Add($"TOTAL (excluding timing output): {total_timer.Elapsed.TotalMilliseconds:F3} ms");
                DA.SetDataList(2, timings);
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(file_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();

                if (is_2d)
                {
                    // Rhino's Mesh.CreateFromPlane indexes faces as: face[j*nx + i]
                    // where j is the y row and i is the x column (y outer, x inner).
                    // Sculpt SPN needs: for i in nx: for j in ny: for k in nz(=1)
                    // So we transpose: read vf[j*nx + i] for each (i, j).
                    for (int i = 0; i < nx; i++)
                    {
                        for (int j = 0; j < ny; j++)
                        {
                            int mat_id = volume_fractions[j * nx + i] >= threshold ? mat_inside : mat_outside;
                            sb.Append(mat_id);
                            sb.AppendLine();
                        }
                    }
                }
                else
                {
                    // 3D: BackVol3D already outputs in x→y→z order matching Sculpt's SPN ordering.
                    int idx = 0;
                    for (int i = 0; i < nx; i++)
                    {
                        for (int j = 0; j < ny; j++)
                        {
                            for (int k = 0; k < nz; k++)
                            {
                                int mat_id = volume_fractions[idx] >= threshold ? mat_inside : mat_outside;
                                sb.Append(mat_id);
                                if (k < nz - 1)
                                    sb.Append(' ');
                                idx++;
                            }
                            sb.AppendLine();
                        }
                    }
                }

                timings.Add($"02 SPN formatting ({expected:N0} cells): {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                phase_timer.Restart();
                File.WriteAllText(file_path, sb.ToString());
                timings.Add($"03 file writing: {phase_timer.Elapsed.TotalMilliseconds:F3} ms");
                DA.SetData(0, $"Written {(is_2d ? nx * ny : expected)} cells ({(is_2d ? "2D extruded to nz=1" : "3D")}) to {file_path}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to write file: {ex.Message}");
                return;
            }

            DA.SetData(1, cmd);
            total_timer.Stop();
            timings.Add($"TOTAL (excluding timing output): {total_timer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(2, timings);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A7F3C8D2-5E91-4B06-9A3D-8C2F1E047B5A");
    }
}
