using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    /// <summary>
    /// Writes the minimal data needed to reconstruct a filtered tetrahedral
    /// complex: one scalar per vertex and four zero-based indices per tet.
    /// Edges, faces, and higher-simplex values are deliberately omitted because
    /// Aleph reconstructs them from this data.
    /// </summary>
    public sealed class ExporttoAlephCompact : GH_Component
    {
        private static readonly byte[] Magic =
        {
            (byte)'T', (byte)'Q', (byte)'A', (byte)'L',
            (byte)'P', (byte)'H', (byte)'1', 0
        };

        private const uint FormatVersion = 1;
        private const int HeaderSize = 8 + sizeof(uint) + sizeof(uint) + sizeof(uint) + sizeof(ulong);

        public ExporttoAlephCompact()
          : base("Export Mesh to Aleph Compact", "ex_aleph Compact",
              "Writes compact binary vertex filtration values and tetrahedron connectivity for Aleph",
              "Sculpt3D", "Aleph")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "Compact vertices from Tri3D Opt", GH_ParamAccess.item);
            pManager.AddGenericParameter("Tets", "tets", "Compact tetrahedra from Tri3D Opt", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "name", "Output file name", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "path", "Existing output directory", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Filtration", "filt", "0=x, 1=y, 2=z, 3=w", GH_ParamAccess.item, 3);
            pManager.AddBooleanParameter("Superlevel", "super", "True for a descending superlevel filtration", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Write", "write", "Write the compact file", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File", "file", "Full compact output path", GH_ParamAccess.item);
            pManager.AddTextParameter("Statistics", "stats", "Dataset and estimated file-size information", GH_ParamAccess.list);
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            var timings = new List<string>();

            object rawVertices = null;
            object rawTets = null;
            string name = null;
            string path = null;
            int filtration = 3;
            bool superlevel = true;
            bool write = false;

            if (!DA.GetData(0, ref rawVertices)) return;
            if (!DA.GetData(1, ref rawTets)) return;
            if (!DA.GetData(2, ref name)) return;
            if (!DA.GetData(3, ref path)) return;
            if (!DA.GetData(4, ref filtration)) return;
            if (!DA.GetData(5, ref superlevel)) return;
            if (!DA.GetData(6, ref write)) return;

            CompactVertices3D compactVertices = Unwrap<CompactVertices3D>(rawVertices);
            CompactTets3D compactTets = Unwrap<CompactTets3D>(rawTets);
            if (compactVertices == null || compactTets == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Connect the compact verts and tets outputs from Tri3D Opt.");
                return;
            }

            if (filtration < 0 || filtration > 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Filtration must be 0, 1, 2, or 3.");
                return;
            }

            if (!BitConverter.IsLittleEndian)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "The compact Aleph format currently requires a little-endian platform.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Name and path must not be empty.");
                return;
            }

            string outputPath;
            try
            {
                outputPath = Path.GetFullPath(Path.Combine(path, name));
            }
            catch (Exception exception)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Invalid output path: {exception.Message}");
                return;
            }

            List<Point4d> vertices = compactVertices.Items;
            Tet4[] tets = compactTets.Items;
            long expectedBytes = checked((long)HeaderSize + 4L * vertices.Count + 16L * tets.LongLength);

            timings.Add($"01 inputs ({vertices.Count:N0} verts, {tets.Length:N0} tets): {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            var stats = new List<string>
            {
                $"Vertices: {vertices.Count:N0}",
                $"Tetrahedra: {tets.Length:N0}",
                $"Expected file size: {expectedBytes:N0} bytes ({expectedBytes / 1048576.0:N2} MiB)",
                $"Filtration: {(superlevel ? "superlevel" : "sublevel")} coordinate {filtration}",
                "Scalar storage: float32 after rounding to 4 decimal places"
            };

            DA.SetData(0, outputPath);
            DA.SetDataList(1, stats);

            if (!write)
            {
                Message = "Ready";
                timings.Add("Write is false; no file was created.");
                timings.Add($"TOTAL: {totalTimer.Elapsed.TotalMilliseconds:F3} ms");
                DA.SetDataList(2, timings);
                return;
            }

            if (!Directory.Exists(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The output directory does not exist.");
                return;
            }

            // Validate before opening the output so an invalid input cannot leave
            // a partially overwritten file.
            for (int i = 0; i < vertices.Count; ++i)
            {
                float value = GetStoredValue(vertices[i], filtration);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Vertex {i:N0} has a filtration value that cannot be stored as float32.");
                    return;
                }
            }

            for (int i = 0; i < tets.Length; ++i)
            {
                Tet4 tet = tets[i];
                if (!IsValidIndex(tet.A, vertices.Count) ||
                    !IsValidIndex(tet.B, vertices.Count) ||
                    !IsValidIndex(tet.C, vertices.Count) ||
                    !IsValidIndex(tet.D, vertices.Count))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Tetrahedron {i:N0} contains an index outside 0..{vertices.Count - 1:N0}.");
                    return;
                }

                if (tet.A == tet.B || tet.A == tet.C || tet.A == tet.D ||
                    tet.B == tet.C || tet.B == tet.D || tet.C == tet.D)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Tetrahedron {i:N0} contains duplicate vertex indices.");
                    return;
                }
            }

            timings.Add($"02 validation: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            uint flags = (superlevel ? 1u : 0u) | ((uint)filtration << 8);

            try
            {
                using (var stream = new FileStream(
                    outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    1 << 20, FileOptions.SequentialScan))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(flags);
                    writer.Write(checked((uint)vertices.Count));
                    writer.Write(checked((ulong)tets.LongLength));

                    foreach (Point4d vertex in vertices)
                        writer.Write(GetStoredValue(vertex, filtration));

                    foreach (Tet4 tet in tets)
                    {
                        writer.Write(checked((uint)tet.A));
                        writer.Write(checked((uint)tet.B));
                        writer.Write(checked((uint)tet.C));
                        writer.Write(checked((uint)tet.D));
                    }
                }
            }
            catch (Exception exception)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unable to write compact Aleph file: {exception.Message}");
                return;
            }

            timings.Add($"03 compact binary writing: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            totalTimer.Stop();
            timings.Add($"TOTAL: {totalTimer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(2, timings);
            Message = $"{expectedBytes / 1048576.0:N1} MiB";
        }

        private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;

        private static T Unwrap<T>(object value) where T : class
        {
            if (value is T typed) return typed;
            if (value is GH_ObjectWrapper wrapper) return wrapper.Value as T;
            return null;
        }

        private static double GetValue(Point4d point, int filtration) => filtration switch
        {
            0 => point.X,
            1 => point.Y,
            2 => point.Z,
            3 => point.W,
            _ => throw new ArgumentOutOfRangeException(nameof(filtration))
        };

        private static float GetStoredValue(Point4d point, int filtration) =>
            (float)Math.Round(GetValue(point, filtration), 4);

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A8C0A983-22F0-4F8E-87D7-933254A9D301");
    }
}
