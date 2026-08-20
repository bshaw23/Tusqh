// LibIGLWrapper.cpp : Defines the entry point for the application.

#include "LibIGLWrapper.h"
#include <Eigen/Core>
#include <Eigen/Dense>

#include "libigl/include/igl/winding_number.h"
#include "libigl/include/igl/WindingNumberAABB.h"
#include "libigl/include/igl/WindingNumberMethod.h"
#include "libigl/include/igl/WindingNumberTree.h"
#include "libigl/include/igl/parallel_for.h"

#include "WindingNumberAABB2D.h"

using namespace std;
using namespace Eigen;

EXPORT_API(double) ddot_(_In_  double* v1, _In_  double* v2, int length1)
{
	Map<const VectorXd> first(v1, length1);
	Map<const VectorXd> second(v2, length1);
	return first.dot(second);
}

EXPORT_API(void) windingnumber_(_In_ double* v,const int row_v, const int col_v, _In_ int* f, const int row_f, const int col_f, _In_ double* o, const int row_o, const int col_o, _Out_ double* wind)
{
    
//    cout << " Got here" << endl;
    Map<const MatrixXd> vertsmap(v, row_v, col_v);
    Map<const MatrixXi> facesmap(f, row_f, col_f);
    Map<const MatrixXd> ordinatesmap(o, row_o, col_o);
    
    MatrixXd verts = vertsmap;
    MatrixXi faces = facesmap;
    MatrixXd ordinates = ordinatesmap;
    
    VectorXd winding_number(row_o);
//    MatrixXd winding_number = MatrixXd::Zero(row_o,1);  
  
//    cout << "Hi" << endl;
//    cout << verts.rows() << verts.cols() << endl;
//    cout << verts << endl;
//    cout << "end verts" << endl;
//    cout << faces.rows() << faces.cols() << endl;
//    cout << faces << endl;
//    cout << "end faces" << endl;
//    cout << ordinates.rows() << ordinates.cols() << endl;
//    cout << ordinates << endl;
//    cout << "end ordinates" << endl;
//    cout << winding_number.rows() << winding_number.cols() << endl;
//    cout << winding_number << endl;
//    cout << "end winding number init"<< endl;

    igl::winding_number(verts,faces,ordinates,winding_number);

//    cout << winding_number << endl;

//    cout << "copy data init" << endl;
//    cout << wind << endl;
    //wind = winding_number.data();

//    cout << "copy data end" << endl;
//    cout << wind << endl;
    Map<VectorXd> result(wind, row_o);
    for ( int i = 0; i < row_o; ++i )
    {
        result[i] = winding_number[i];
    }
}



// 2D-accelerated counterpart to windingnumber_: same signature and same
// per-edge igl::signed_angle formula (so results match windingnumber_'s
// F.cols()==2 brute-force path exactly), but evaluated via a recursive
// spatial hierarchy (WindingNumberAABB2D) instead of summing every edge
// for every query point. Only valid for 2D edge input (col_v == 2,
// col_f == 2); use windingnumber_ for triangle meshes, which already gets
// the analogous 3D acceleration (WindingNumberAABB) for free.
EXPORT_API(void) windingnumberfast2d_(_In_ double* v, const int row_v, const int col_v, _In_ int* e, const int row_e, const int col_e, _In_ double* o, const int row_o, const int col_o, _Out_ double* wind)
{
    Map<const MatrixXd> vertsmap(v, row_v, col_v);
    Map<const MatrixXi> edgesmap(e, row_e, col_e);
    Map<const MatrixXd> ptsmap(o, row_o, col_o);

    MatrixXd verts = vertsmap;
    MatrixXi edges = edgesmap;
    MatrixXd pts = ptsmap;

    WindingNumberAABB2D tree(verts, edges);
    tree.grow();

    Map<VectorXd> result(wind, row_o);
    igl::parallel_for(row_o, [&](const int i)
    {
        result(i) = tree.winding_number(pts.row(i));
    }, 10000);
}

EXPORT_API(double) windingnumberslow_(_In_ double* v,const int row_v, const int col_v, _In_ int* f, const int row_f, const int col_f, _In_ double* o, const int col_o)
{
    /*
    MatrixXd verts(10,2);
    MatrixXd faces(10,2);
    MatrixXd ordinates(1,2);
    */
    
    Map<const MatrixXd> verts(v, row_v, col_v);
    Map<const MatrixXi> faces(f, row_f, col_f);
    Map<const MatrixXd> ordinate(o, 1, col_o);
    
    
    return igl::winding_number(verts,faces,ordinate);
}
