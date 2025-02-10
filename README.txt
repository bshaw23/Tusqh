Summary
Tusqh (pronounced "tusk") is software for generating cubical meshes in 2D and 3D, and computing, describing and controlling the homology (topology) of these meshes using persistent homology.

Dependencies
LibIGL https://libigl.github.io/
Aleph https://github.com/Pseudomanifold/Aleph

Build Instructions

(I have a lot of work to do here. I remember almost nothing about how to get it working. Maybe start by downloading dependencies to appropriate folders?)

1. Follow instructions to download dependencies found in  Tusqh/libigl_wrapper/README
2. Build LibIGLWrapper.sln found in Tusqh/libigl_wrapper/build
3. Build libigl_wapper.sln found in Tusqh/libigl_wrapper/
If using windows
3a. Copy libigl_core.dll and libigl_core.lib found in Tusqh/libigl_wrapper/build/Debug 
3b. Paste them to Tusqh/libigl_wrapper/wrapper/bin/Debug/net7.0
4. Build Tusqh.sln found in Tusqh/Tusqh



