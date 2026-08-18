using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    public class ExporttoAlephOpt : GH_Component
    {
        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public EdgeKey(int a, int b)
            {
                if (a <= b) { A = a; B = b; }
                else { A = b; B = a; }
            }
            public int A { get; }
            public int B { get; }
            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private readonly struct FaceKey : IEquatable<FaceKey>
        {
            public FaceKey(int a, int b, int c)
            {
                if (a > b) Swap(ref a, ref b);
                if (b > c) Swap(ref b, ref c);
                if (a > b) Swap(ref a, ref b);
                A = a; B = b; C = c;
            }
            public int A { get; }
            public int B { get; }
            public int C { get; }
            public bool Equals(FaceKey other) => A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is FaceKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B, C);
            private static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }
        }

        public ExporttoAlephOpt()
          : base("Export Mesh to Aleph Opt", "ex_aleph Opt",
              "Exports compact Tri3D Opt data using allocation-reduced simplex construction",
              "Sculpt3D", "Aleph")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "Compact vertices from Tri3D Opt", GH_ParamAccess.item);
            pManager.AddGenericParameter("Tets", "tets", "Compact tetrahedra from Tri3D Opt", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "name", "File name", GH_ParamAccess.item);
            pManager.AddTextParameter("Path", "path", "Output directory", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Filtration", "filt", "0=x, 1=y, 2=z, 3=w", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            List<string> timings = new List<string>();
            object rawVertices = null;
            object rawTets = null;
            string name = null;
            string path = null;
            int filtration = 3;

            if (!DA.GetData(0, ref rawVertices)) return;
            if (!DA.GetData(1, ref rawTets)) return;
            if (!DA.GetData(2, ref name)) return;
            if (!DA.GetData(3, ref path)) return;
            if (!DA.GetData(4, ref filtration)) return;

            CompactVertices3D compactVertices = Unwrap<CompactVertices3D>(rawVertices);
            CompactTets3D compactTets = Unwrap<CompactTets3D>(rawTets);
            if (compactVertices == null || compactTets == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect the compact verts and tets outputs from Tri3D Opt.");
                return;
            }
            if (filtration < 0 || filtration > 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Filtration must be 0, 1, 2, or 3.");
                return;
            }

            List<Point4d> vertices = compactVertices.Items;
            Tet4[] tets = compactTets.Items;
            timings.Add($"01 inputs ({vertices.Count:N0} verts, {tets.Length:N0} tets): {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            Dictionary<EdgeKey, float> edges = new Dictionary<EdgeKey, float>();
            Dictionary<FaceKey, float> faces = new Dictionary<FaceKey, float>();
            foreach (Tet4 tet in tets)
            {
                ValidateIndex(tet.A, vertices.Count);
                ValidateIndex(tet.B, vertices.Count);
                ValidateIndex(tet.C, vertices.Count);
                ValidateIndex(tet.D, vertices.Count);

                float a = (float)Math.Round(GetValue(vertices[tet.A], filtration), 4);
                float b = (float)Math.Round(GetValue(vertices[tet.B], filtration), 4);
                float c = (float)Math.Round(GetValue(vertices[tet.C], filtration), 4);
                float d = (float)Math.Round(GetValue(vertices[tet.D], filtration), 4);

                AddEdge(edges, tet.A, tet.B, Math.Min(a, b));
                AddEdge(edges, tet.A, tet.C, Math.Min(a, c));
                AddEdge(edges, tet.A, tet.D, Math.Min(a, d));
                AddEdge(edges, tet.B, tet.C, Math.Min(b, c));
                AddEdge(edges, tet.B, tet.D, Math.Min(b, d));
                AddEdge(edges, tet.C, tet.D, Math.Min(c, d));

                AddFace(faces, tet.A, tet.B, tet.C, Math.Min(a, Math.Min(b, c)));
                AddFace(faces, tet.A, tet.B, tet.D, Math.Min(a, Math.Min(b, d)));
                AddFace(faces, tet.A, tet.C, tet.D, Math.Min(a, Math.Min(c, d)));
                AddFace(faces, tet.B, tet.C, tet.D, Math.Min(b, Math.Min(c, d)));
            }

            timings.Add($"02 simplex construction ({edges.Count:N0} edges, {faces.Count:N0} faces): {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            using (StreamWriter writer = new StreamWriter(
                Path.Combine(path, name), false, new UTF8Encoding(false), bufferSize: 1 << 20))
            {
                writer.WriteLine("verts");
                foreach (Point4d vertex in vertices)
                    writer.WriteLine(Math.Round(GetValue(vertex, filtration), 4));

                writer.WriteLine("edges");
                foreach (KeyValuePair<EdgeKey, float> edge in edges)
                {
                    writer.WriteLine(edge.Key.A + 1);
                    writer.WriteLine(edge.Key.B + 1);
                    writer.WriteLine(edge.Value);
                }

                writer.WriteLine("faces");
                foreach (KeyValuePair<FaceKey, float> face in faces)
                {
                    writer.WriteLine(face.Key.A + 1);
                    writer.WriteLine(face.Key.B + 1);
                    writer.WriteLine(face.Key.C + 1);
                    writer.WriteLine(face.Value);
                }

                writer.WriteLine("tets");
                foreach (Tet4 tet in tets)
                {
                    writer.WriteLine(tet.A + 1);
                    writer.WriteLine(tet.B + 1);
                    writer.WriteLine(tet.C + 1);
                    writer.WriteLine(tet.D + 1);
                    double value = Math.Min(
                        Math.Min(GetValue(vertices[tet.A], filtration), GetValue(vertices[tet.B], filtration)),
                        Math.Min(GetValue(vertices[tet.C], filtration), GetValue(vertices[tet.D], filtration)));
                    writer.WriteLine(Math.Round(value, 4));
                }
            }

            timings.Add($"03 buffered file writing: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            totalTimer.Stop();
            timings.Add($"TOTAL (excluding timing output): {totalTimer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(0, timings);
        }

        private static T Unwrap<T>(object value) where T : class
        {
            if (value is T typed) return typed;
            if (value is GH_ObjectWrapper wrapper) return wrapper.Value as T;
            return null;
        }

        private static void ValidateIndex(int index, int count)
        {
            if (index < 0 || index >= count)
                throw new IndexOutOfRangeException($"Tetrahedron vertex {index:N0} is outside 0..{count - 1:N0}.");
        }

        private static double GetValue(Point4d point, int filtration) => filtration switch
        {
            0 => point.X,
            1 => point.Y,
            2 => point.Z,
            3 => point.W,
            _ => throw new ArgumentOutOfRangeException(nameof(filtration))
        };

        private static void AddEdge(Dictionary<EdgeKey, float> edges, int a, int b, float value) =>
            edges.TryAdd(new EdgeKey(a, b), value);

        private static void AddFace(Dictionary<FaceKey, float> faces, int a, int b, int c, float value) =>
            faces.TryAdd(new FaceKey(a, b, c), value);

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("4D5B8B3F-FB77-4A47-A96E-8F2AEF912B2B");
    }
}
