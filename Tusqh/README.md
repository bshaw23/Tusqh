# Tusqh — Sculpt2D Grasshopper Plugin

A Grasshopper 8 plugin for 2D/3D mesh sculpting, winding-number computation,
and related geometry operations. Depends on a native C++ library
([libigl](https://libigl.github.io/)) via a managed P/Invoke wrapper.

---

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| Visual Studio Build Tools (C++ workload) | 2019 or later | `winget install Microsoft.VisualStudio.2022.BuildTools` |
| CMake | 3.10+ | `winget install Kitware.CMake` |
| Ninja | (bundled with VS C++ workload) | — |
| .NET SDK | 7.0+ | `winget install Microsoft.DotNet.SDK.7` |
| Rhinoceros 3D | 8.x | https://www.rhino3d.com |

---

## Build steps

### Step 1 — Build the native C++ library (first time only)

The native library (`libigl_core.dll`) wraps libigl's winding-number routines.
It only needs to be rebuilt if you change `libigl_wrapper/src/LibIGLWrapper.cpp`.

```bat
cd libigl_wrapper
build_native.bat
```

Output: `libigl_wrapper/src/x64_lib/libigl_core.dll`

> If the script cannot find `vcvarsall.bat`, edit the path at the top of
> `build_native.bat` to match your Visual Studio installation.

### Step 2 — Build the C# wrapper (first time only)

```bat
dotnet build libigl_wrapper/wrapper/wrapper.csproj -c Release
```

Output: `libigl_wrapper/wrapper/bin/Release/net7.0/wrapper.dll`

### Step 3 — Build the Grasshopper plugin

```bat
cd Tusqh
dotnet build Tusqh.csproj -c Release
```

Output: `Tusqh/bin/Release/net7.0/Tusqh.gha`

The post-build step in `Tusqh.csproj` automatically copies
`libigl_core.dll` into the same output directory.

---

## Installing into Grasshopper

1. Open Rhino 8 and run the `GrasshopperFolders` command (or go to
   **Grasshopper → File → Special Folders → Components Folder**).
2. Copy the following files from `Tusqh/bin/Release/net7.0/` into that folder:
   - `Tusqh.gha`
   - `wrapper.dll`
   - `libigl_core.dll`
3. Restart Rhino. The **Sculpt2D** tab will appear in the Grasshopper toolbar.

---

## Project layout

```
Tusqh/                          Main Grasshopper plugin (C#, .NET 7)
  Components/                   31 Grasshopper component source files
  Tusqh.csproj                  MSBuild project — outputs Tusqh.gha
  Tusqh.sln                     Visual Studio solution

libigl_wrapper/                 Native geometry library integration
  src/
    LibIGLWrapper.cpp/.h        Exported C functions (winding number, dot)
    CMakeLists.txt              CMake build for libigl_core.dll
    x64_lib/libigl_core.dll     Built native library (Windows)
  wrapper/
    core/EigenBasic.cs          [DllImport] P/Invoke declarations
    core/EigenMethods.cs        Managed API (EigenDenseUtilities)
    wrapper.csproj              .NET 7 class library — outputs wrapper.dll
  eigen/                        Eigen 3.4.0 header library
  src/libigl/                   libigl header library
  build_native.bat              One-step native build script (Windows)
  build_native.sh               One-step native build script (macOS/Linux)
  README                        Detailed libigl_wrapper build reference
```

---

## Rebuilding from scratch

```bat
:: Delete CMake cache and rebuild native lib
rmdir /s /q libigl_wrapper\src\build_win
cd libigl_wrapper && build_native.bat && cd ..

:: Rebuild C# wrapper
dotnet build libigl_wrapper/wrapper/wrapper.csproj -c Release

:: Rebuild plugin
cd Tusqh && dotnet build Tusqh.csproj -c Release
```

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `DllNotFoundException: libigl_core` at runtime | Ensure `libigl_core.dll` is in the same folder as `Tusqh.gha` and `wrapper.dll`. |
| `vcvarsall.bat` not found | Edit the path in `build_native.bat` to your VS installation. |
| CMake can't find Ninja | Repair the "Desktop development with C++" workload in VS Installer, or `winget install Ninja-build.Ninja`. |
| Plugin tab missing after install | Unblock the `.gha` and `.dll` files (right-click → Properties → Unblock) and restart Rhino. |
