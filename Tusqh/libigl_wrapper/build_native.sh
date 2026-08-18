#!/bin/bash
# Build the libigl_core native shared library on macOS or Linux.
# Run from the libigl_wrapper/ directory.
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "Configuring CMake..."
cmake -S src -B src/build_unix \
    -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_CXX_STANDARD=14

echo "Building..."
cmake --build src/build_unix --parallel

echo "Copying library to src/x64_lib/..."
mkdir -p src/x64_lib

# macOS produces .dylib, Linux produces .so
find src/build_unix -maxdepth 1 \
    \( -name "libigl_core.dylib" -o -name "libigl_core.so" \) \
    -exec cp {} src/x64_lib/ \;

echo "Done."
