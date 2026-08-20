using System.Runtime.InteropServices;
using System.Security;

namespace EigenWrapper.Eigen
{
    internal static unsafe class ThunkDenseEigen
    {
        internal const string NativeThunkLibIGLPath = "libigl_core";

        [DllImport(NativeThunkLibIGLPath), SuppressUnmanagedCodeSecurity]
        public static extern double ddot_([In] double* firstVector, [In] double* secondVector, int length);

        [DllImport(NativeThunkLibIGLPath), SuppressUnmanagedCodeSecurity]
        public static extern double windingnumberslow_([In] double* v, int row_v, int col_v, [In] int* f, int row_f, int col_f, [In] double* o, int col_o);

        [DllImport(NativeThunkLibIGLPath), SuppressUnmanagedCodeSecurity]
        public static extern void windingnumber_([In] double* v, int row_v, int col_v, [In] int* f, int row_f, int col_f, [In] double* o, int row_o, int col_o, [Out] double* wind);

        [DllImport(NativeThunkLibIGLPath), SuppressUnmanagedCodeSecurity]
        public static extern void windingnumberfast2d_([In] double* v, int row_v, int col_v, [In] int* e, int row_e, int col_e, [In] double* o, int row_o, int col_o, [Out] double* wind);
    }

}
