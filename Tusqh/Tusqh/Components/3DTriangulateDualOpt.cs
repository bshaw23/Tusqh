using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Sculpt2D.Components
{
    /// <summary>
    /// Compact, contiguous storage for weighted vertices. This is passed through
    /// Grasshopper as one generic item to avoid wrapping millions of values.
    /// </summary>
    public sealed class CompactVertices3D
    {
        public CompactVertices3D(List<Point4d> items) => Items = items;
        public List<Point4d> Items { get; }
        public int Count => Items.Count;
        public override string ToString() => $"CompactVertices3D ({Count:N0} vertices)";
    }

    public readonly struct Tet4
    {
        public Tet4(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public int this[int index] => index switch
        {
            0 => A,
            1 => B,
            2 => C,
            3 => D,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    /// <summary>
    /// Stores all tetrahedra in one value-type array rather than one List object
    /// and one backing array per tetrahedron.
    /// </summary>
    public sealed class CompactTets3D
    {
        public CompactTets3D(Tet4[] items) => Items = items;
        public Tet4[] Items { get; }
        public int Count => Items.Length;
        public override string ToString() => $"CompactTets3D ({Count:N0} tetrahedra)";
    }

    internal readonly struct QuadFaceKey : IEquatable<QuadFaceKey>
    {
        public QuadFaceKey(int a, int b, int c, int d)
        {
            if (a > b) Swap(ref a, ref b);
            if (c > d) Swap(ref c, ref d);
            if (a > c) Swap(ref a, ref c);
            if (b > d) Swap(ref b, ref d);
            if (b > c) Swap(ref b, ref c);
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }

        public bool Equals(QuadFaceKey other) => A == other.A && B == other.B && C == other.C && D == other.D;
        public override bool Equals(object obj) => obj is QuadFaceKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A, B, C, D);

        private static void Swap(ref int a, ref int b)
        {
            int temp = a;
            a = b;
            b = temp;
        }
    }

    internal readonly struct TriangleKey : IEquatable<TriangleKey>
    {
        public TriangleKey(int a, int b, int c)
        {
            if (a > b) Swap(ref a, ref b);
            if (b > c) Swap(ref b, ref c);
            if (a > b) Swap(ref a, ref b);
            A = a;
            B = b;
            C = c;
        }

        public int A { get; }
        public int B { get; }
        public int C { get; }

        public bool Equals(TriangleKey other) => A == other.A && B == other.B && C == other.C;
        public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A, B, C);

        private static void Swap(ref int a, ref int b)
        {
            int temp = a;
            a = b;
            b = temp;
        }
    }

    public class TriangulateDual3DOpt : GH_Component
    {
        private static readonly int[,] FaceCorners =
        {
            { 0, 1, 5, 4 },
            { 1, 2, 6, 5 },
            { 2, 3, 7, 6 },
            { 0, 4, 7, 3 },
            { 0, 3, 2, 1 },
            { 4, 5, 6, 7 }
        };

        public TriangulateDual3DOpt()
          : base("Triangulate Dual 3D Opt", "Tri3D Opt",
              "Optimized 24-tetrahedron subdivision using structured-grid lookup and compact storage",
              "Sculpt3d", "Aleph")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Vertex List", "verts", "Vertices of Dual Mesh", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Hexes", "hexes", "Vertex indices of dual hexes, flattened -- every 8 consecutive entries are one hex", GH_ParamAccess.list);
            pManager.AddNumberParameter("Volume Fractions", "vols", "Volume fractions in x-y-z structured-grid order", GH_ParamAccess.list);
            pManager.AddPointParameter("Centroids", "cents", "Centroids of the original structured hex mesh", GH_ParamAccess.list);
            pManager.AddNumberParameter("x distance", "xdist", "Original hex length in x", GH_ParamAccess.item);
            pManager.AddNumberParameter("y distance", "ydist", "Original hex length in y", GH_ParamAccess.item);
            pManager.AddNumberParameter("z distance", "zdist", "Original hex length in z", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Negative", "neg", "Negate the volume-fraction filtration values", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Minimum", "cm", "Use minimum rather than maximum values for new centroids", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Visualize Mesh", "viz", "Build a triangle visualization; keep false for large grids", GH_ParamAccess.item);
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Vertices", "verts", "One compact weighted-vertex collection; connect to Export Aleph Opt", GH_ParamAccess.item);
            pManager.AddGenericParameter("Tets", "tets", "One compact 24-tet collection; connect to Export Aleph Opt", GH_ParamAccess.item);
            pManager.AddMeshParameter("Visualization", "viz", "Optional visualization", GH_ParamAccess.list);
            pManager.AddTextParameter("Timings", "time", "Per-phase wall-clock timings in milliseconds", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            List<string> timings = new List<string>();

            List<Point3d> vertices = new List<Point3d>();
            List<int> hexes = new List<int>();
            List<double> volumeFractions = new List<double>();
            List<Point3d> centroids = new List<Point3d>();
            double xDist = 0;
            double yDist = 0;
            double zDist = 0;
            bool negative = true;
            bool useMin = true;
            bool visualize = false;

            if (!DA.GetDataList(0, vertices)) return;
            if (!DA.GetDataList(1, hexes)) return;
            if (!DA.GetDataList(2, volumeFractions)) return;
            if (!DA.GetDataList(3, centroids)) return;
            if (!DA.GetData(4, ref xDist)) return;
            if (!DA.GetData(5, ref yDist)) return;
            if (!DA.GetData(6, ref zDist)) return;
            if (!DA.GetData(7, ref negative)) negative = true;
            if (!DA.GetData(8, ref useMin)) useMin = true;
            if (!DA.GetData(9, ref visualize)) visualize = false;

            if (centroids.Count == 0 || xDist <= 0 || yDist <= 0 || zDist <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Centroids must be nonempty and cell distances must be positive.");
                return;
            }

            if (volumeFractions.Count != centroids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Volume/centroid mismatch: {volumeFractions.Count:N0} volumes and {centroids.Count:N0} centroids.");
                return;
            }

            timings.Add($"01 inputs/validation: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            double minZ = double.PositiveInfinity;
            double maxZ = double.NegativeInfinity;
            foreach (Point3d centroid in centroids)
            {
                minX = Math.Min(minX, centroid.X);
                maxX = Math.Max(maxX, centroid.X);
                minY = Math.Min(minY, centroid.Y);
                maxY = Math.Max(maxY, centroid.Y);
                minZ = Math.Min(minZ, centroid.Z);
                maxZ = Math.Max(maxZ, centroid.Z);
            }

            int nx = (int)Math.Round((maxX - minX) / xDist) + 1;
            int ny = (int)Math.Round((maxY - minY) / yDist) + 1;
            int nz = (int)Math.Round((maxZ - minZ) / zDist) + 1;
            long expectedCentroids = (long)nx * ny * nz;
            if (expectedCentroids != centroids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Structured-grid inference produced {nx}x{ny}x{nz}={expectedCentroids:N0}, but received {centroids.Count:N0} centroids.");
                return;
            }

            if (hexes.Count % 8 != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Hexes must be a flattened list with 8 entries per hex; received {hexes.Count:N0}, not a multiple of 8.");
                return;
            }
            int hexCount = hexes.Count / 8;

            long dualNx = (long)nx + 1;
            long dualNy = (long)ny + 1;
            long dualNz = (long)nz + 1;
            long expectedHexes = dualNx * dualNy * dualNz;
            if (expectedHexes != hexCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Structured dual-grid inference expected {expectedHexes:N0} hexes, but received {hexCount:N0}.");
                return;
            }

            long uniqueFaceCount =
                (dualNx + 1) * dualNy * dualNz +
                dualNx * (dualNy + 1) * dualNz +
                dualNx * dualNy * (dualNz + 1);
            int faceCapacity = checked((int)uniqueFaceCount);
            int estimatedPointCount = checked(vertices.Count + hexCount + faceCapacity);
            List<Point4d> points = new List<Point4d>(estimatedPointCount);
            for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
            {
                Point3d vertex = vertices[vertexIndex];
                int ix = Math.Clamp((int)Math.Round((vertex.X - minX) / xDist), 0, nx - 1);
                int iy = Math.Clamp((int)Math.Round((vertex.Y - minY) / yDist), 0, ny - 1);
                int iz = Math.Clamp((int)Math.Round((vertex.Z - minZ) / zDist), 0, nz - 1);
                int volumeIndex = checked((ix * ny + iy) * nz + iz);
                points.Add(new Point4d(vertex.X, vertex.Y, vertex.Z, volumeFractions[volumeIndex]));
            }

            timings.Add($"02 direct volume mapping ({vertices.Count:N0} vertices): {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            int tetCount = checked(hexCount * 24);
            Tet4[] tets = new Tet4[tetCount];
            int tetWriteIndex = 0;
            Dictionary<QuadFaceKey, int> faceCentroidIndices =
                new Dictionary<QuadFaceKey, int>(faceCapacity);

            for (int hexIndex = 0; hexIndex < hexCount; hexIndex++)
            {
                int hexBase = hexIndex * 8;

                for (int corner = 0; corner < 8; corner++)
                {
                    int cornerValue = hexes[hexBase + corner];
                    if (cornerValue < 0 || cornerValue >= vertices.Count)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"Hex {hexIndex:N0}, corner {corner}, references vertex {cornerValue:N0}; valid range is 0..{vertices.Count - 1:N0}.");
                        return;
                    }
                }

                double hexValue = useMin ? double.PositiveInfinity : double.NegativeInfinity;
                Point3d hexCentroid = Point3d.Origin;
                for (int corner = 0; corner < 8; corner++)
                {
                    int index = hexes[hexBase + corner];
                    hexCentroid += vertices[index];
                    hexValue = Combine(hexValue, points[index].W, useMin);
                }
                hexCentroid /= 8.0;
                int hexCentroidIndex = points.Count;
                points.Add(new Point4d(hexCentroid.X, hexCentroid.Y, hexCentroid.Z, hexValue));

                for (int faceIndex = 0; faceIndex < 6; faceIndex++)
                {
                    int a = hexes[hexBase + FaceCorners[faceIndex, 0]];
                    int b = hexes[hexBase + FaceCorners[faceIndex, 1]];
                    int c = hexes[hexBase + FaceCorners[faceIndex, 2]];
                    int d = hexes[hexBase + FaceCorners[faceIndex, 3]];
                    QuadFaceKey key = new QuadFaceKey(a, b, c, d);

                    if (!faceCentroidIndices.TryGetValue(key, out int faceCentroidIndex))
                    {
                        Point3d faceCentroid = (vertices[a] + vertices[b] + vertices[c] + vertices[d]) / 4.0;
                        double faceValue = Combine(points[a].W, points[b].W, useMin);
                        faceValue = Combine(faceValue, points[c].W, useMin);
                        faceValue = Combine(faceValue, points[d].W, useMin);
                        faceCentroidIndex = points.Count;
                        points.Add(new Point4d(faceCentroid.X, faceCentroid.Y, faceCentroid.Z, faceValue));
                        faceCentroidIndices.Add(key, faceCentroidIndex);
                    }

                    tets[tetWriteIndex++] = new Tet4(a, faceCentroidIndex, b, hexCentroidIndex);
                    tets[tetWriteIndex++] = new Tet4(b, faceCentroidIndex, c, hexCentroidIndex);
                    tets[tetWriteIndex++] = new Tet4(c, faceCentroidIndex, d, hexCentroidIndex);
                    tets[tetWriteIndex++] = new Tet4(d, faceCentroidIndex, a, hexCentroidIndex);
                }
            }

            if (tetWriteIndex != tets.Length)
                throw new InvalidOperationException($"Expected {tets.Length:N0} tetrahedra but wrote {tetWriteIndex:N0}.");

            timings.Add($"03 compact 24-tet construction ({tets.Length:N0} tets): {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            if (negative)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Point4d point = points[i];
                    points[i] = new Point4d(point.X, point.Y, point.Z, -point.W);
                }
            }

            List<Mesh> visualization = visualize ? BuildVisualization(points, tets) : new List<Mesh>();
            timings.Add($"04 weighting/visualization: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            phaseTimer.Restart();

            DA.SetData(0, new CompactVertices3D(points));
            DA.SetData(1, new CompactTets3D(tets));
            DA.SetDataList(2, visualization);
            timings.Add($"05 compact output publication: {phaseTimer.Elapsed.TotalMilliseconds:F3} ms");
            totalTimer.Stop();
            timings.Add($"TOTAL (excluding timing output): {totalTimer.Elapsed.TotalMilliseconds:F3} ms");
            DA.SetDataList(3, timings);
        }

        private static double Combine(double first, double second, bool useMin) =>
            useMin ? Math.Min(first, second) : Math.Max(first, second);

        private static List<Mesh> BuildVisualization(List<Point4d> points, Tet4[] tets)
        {
            List<Mesh> meshes = new List<Mesh>();
            HashSet<TriangleKey> faces = new HashSet<TriangleKey>();
            foreach (Tet4 tet in tets)
            {
                AddTriangle(tet.A, tet.B, tet.D, points, faces, meshes);
                AddTriangle(tet.B, tet.C, tet.D, points, faces, meshes);
                AddTriangle(tet.A, tet.D, tet.C, points, faces, meshes);
                AddTriangle(tet.A, tet.C, tet.B, points, faces, meshes);
            }
            return meshes;
        }

        private static void AddTriangle(
            int a,
            int b,
            int c,
            List<Point4d> points,
            HashSet<TriangleKey> faces,
            List<Mesh> meshes)
        {
            if (!faces.Add(new TriangleKey(a, b, c))) return;
            Mesh mesh = new Mesh();
            mesh.Vertices.Add(points[a].X, points[a].Y, points[a].Z);
            mesh.Vertices.Add(points[b].X, points[b].Y, points[b].Z);
            mesh.Vertices.Add(points[c].X, points[c].Y, points[c].Z);
            mesh.Faces.AddFace(0, 1, 2);
            meshes.Add(mesh);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("E6B794A2-3D7C-4CF2-9C74-36FBAFCD11E8");
    }
}
