using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EigenWrapper.Eigen
{
    public static class EigenDenseUtilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(ReadOnlySpan<double> firstVector, ReadOnlySpan<double> secondVector, int length)
        {
            unsafe
            {
                fixed (double* pfirst = &MemoryMarshal.GetReference(firstVector))
                {
                    fixed (double* pSecond = &MemoryMarshal.GetReference(secondVector))
                    {
                        return ThunkDenseEigen.ddot_(pfirst, pSecond, length);
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WindingNumber(ReadOnlySpan<double> vertex_positions,
                                         int n_verts,
                                         int vert_dim,
                                         ReadOnlySpan<int> triangle_indices,
                                         int n_faces,
                                         int face_dim,
                                         ReadOnlySpan<double> point_positions,
                                         int n_points,
                                         int point_dim,
                                         Span<double> winding_numbers)
        {
            unsafe
            {
                fixed (double* vertmat = &MemoryMarshal.GetReference(vertex_positions))
                {
                    fixed (int* facemat = &MemoryMarshal.GetReference(triangle_indices))
                    {
                        fixed (double* pointmat = &MemoryMarshal.GetReference(point_positions))
                        {
                            fixed (double* wind_out = &MemoryMarshal.GetReference(winding_numbers))
                            {
                                ThunkDenseEigen.windingnumber_(vertmat,n_verts,vert_dim,facemat,n_faces,face_dim,pointmat, n_points, point_dim, wind_out);
                            }
                        }
                    }
                }
            }
        }


        // 2D-only accelerated counterpart to WindingNumber: same signature
        // and same per-edge formula (results match WindingNumber's
        // 2-column/edge case exactly), but evaluated via a recursive
        // spatial hierarchy instead of summing every edge for every query
        // point. vert_dim and face_dim must both be 2 -- there is no 3D
        // variant of this entry point, since WindingNumber already gets
        // the analogous acceleration for triangle meshes automatically.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WindingNumberFast2D(ReadOnlySpan<double> vertex_positions,
                                         int n_verts,
                                         int vert_dim,
                                         ReadOnlySpan<int> edge_indices,
                                         int n_edges,
                                         int edge_dim,
                                         ReadOnlySpan<double> point_positions,
                                         int n_points,
                                         int point_dim,
                                         Span<double> winding_numbers)
        {
            unsafe
            {
                fixed (double* vertmat = &MemoryMarshal.GetReference(vertex_positions))
                {
                    fixed (int* edgemat = &MemoryMarshal.GetReference(edge_indices))
                    {
                        fixed (double* pointmat = &MemoryMarshal.GetReference(point_positions))
                        {
                            fixed (double* wind_out = &MemoryMarshal.GetReference(winding_numbers))
                            {
                                ThunkDenseEigen.windingnumberfast2d_(vertmat,n_verts,vert_dim,edgemat,n_edges,edge_dim,pointmat, n_points, point_dim, wind_out);
                            }
                        }
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double WindingNumberSlow(ReadOnlySpan<double> vertex_positions, int n_verts, int vert_dim, ReadOnlySpan<int> triangle_indices, int n_faces, int face_dim, ReadOnlySpan<double> point_pos, int point_dim)
        {
            unsafe
            {
                fixed (double* vertmat = &MemoryMarshal.GetReference(vertex_positions))
                {
                    fixed (int* facemat = &MemoryMarshal.GetReference(triangle_indices))
                    {
                        fixed(double* pointmat = &MemoryMarshal.GetReference(point_pos))
                        {
                            return ThunkDenseEigen.windingnumberslow_(vertmat,n_verts,vert_dim,facemat,n_faces,face_dim,pointmat, point_dim);
                        }
                   }
                }
            }
        }
    }
}
