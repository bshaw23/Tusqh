@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64
if errorlevel 1 (
    echo Failed to load MSVC environment
    exit /b 1
)

cd /d "%~dp0"

echo Configuring CMake...
cmake -S src -B src\build_win -G "Ninja" -DCMAKE_CXX_STANDARD=14
if errorlevel 1 (
    echo CMake configure failed
    exit /b 1
)

echo Building...
cmake --build src\build_win
if errorlevel 1 (
    echo Build failed
    exit /b 1
)

echo Copying DLL to x64_lib...
if not exist src\x64_lib mkdir src\x64_lib
copy /Y src\build_win\libigl_core.dll src\x64_lib\

echo Done.
