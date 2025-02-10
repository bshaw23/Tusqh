# CMakeList.txt : Top-level CMake project file, do global configuration

cmake_minimum_required (VERSION 3.8)

project ("LibIGLWrapper")

set(CMAKE_BUILD_TYPE "Release" CACHE STRING "Choose the type of build, options are: Debug Release RelWithDebInfo MinSizeRel." FORCE)

set(EIGEN_DIRECTORY ../eigen/ CACHE STRING "Eigen directory.  The directory specified should have a subdirectory 'Eigen'.")
include_directories(${EIGEN_DIRECTORY})
set(LIBIGL_DIRECTORY . CACHE STRING "LibIGL directory.")
include_directories(${LIBIGL_DIRECTORY})

set(CMAKE_SHARED_LIBRARY_PREFIX "")

add_library(libigl_core SHARED LibIGLWrapper.cpp)
