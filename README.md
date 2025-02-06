# Tusqh
Tusqh (pronounced "tusk") is software for generating cubical meshes in 2D and 3D, and computing, describing and controlling the homology (topology) of these meshes using persistent homology.

It is based on a background grid, and including a grid cell in the output if its volume-fraction is above a selectable threshold. Volume-fraction means the fraction of the cell that lies inside the domain being meshed, and is estimated by sampling points within the cell and calculating whether each point lies outside or inside the domain based on its winding number. It includes anti-aliasing algorithms for mitigating the effects of the arbitrary choice of background grid orientation and scale. Tusqh depends on third party software: Rhinoceros 3D, Grasshopper, libigl, and Aleph. 

The author is Brian Shawcroft, while at Brigham Young University, and while at Sandia National Laboratories. His research partners were Prof. Kendrick Shepherd, BYU, and Scott A. Mitchell, Sandia National Labs.

[1]	B. Shawcroft, K. Shepherd, S.A. Mitchell, “Tusqh: Topological control of volume-fraction meshes near small features and dirty geometry,” paper accepted to International Meshing Roundtable (IMR), (2025).
