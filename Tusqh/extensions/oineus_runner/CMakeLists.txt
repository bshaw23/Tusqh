cmake_minimum_required(VERSION 3.20)

project(tusqh_oineus_runner LANGUAGES CXX)

set(OINEUS_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../oineus" CACHE PATH
    "Path to the Oineus source checkout")
set(VCPKG_INSTALLED_DIR "C:/vcpkg/installed/x64-windows" CACHE PATH
    "Path to the vcpkg x64-windows install tree providing Boost")

if(NOT EXISTS "${OINEUS_SOURCE_DIR}/include/oineus/oineus.h")
  message(FATAL_ERROR "Oineus headers were not found under OINEUS_SOURCE_DIR=${OINEUS_SOURCE_DIR}")
endif()

find_path(BOOST_INCLUDE_DIR
  NAMES boost/version.hpp
  HINTS "${VCPKG_INSTALLED_DIR}/include"
)

if(NOT BOOST_INCLUDE_DIR)
  message(FATAL_ERROR "Boost headers were not found. Set VCPKG_INSTALLED_DIR to the vcpkg x64-windows install tree "
                       "(boost-multiprecision, boost-iterator, boost-array, boost-range, boost-serialization, "
                       "boost-random, boost-foreach, boost-heap, boost-smart-ptr, boost-static-assert, "
                       "boost-type-traits must be installed there -- see extensions/oineus_runner/README.md).")
endif()

add_executable(tusqh_oineus_runner main.cpp)

target_compile_features(tusqh_oineus_runner PRIVATE cxx_std_20)
target_include_directories(tusqh_oineus_runner PRIVATE
  "${OINEUS_SOURCE_DIR}/include"
  "${OINEUS_SOURCE_DIR}/extern"
  "${BOOST_INCLUDE_DIR}"
)

if(MSVC)
  target_compile_options(tusqh_oineus_runner PRIVATE /W4)
else()
  target_compile_options(tusqh_oineus_runner PRIVATE -Wall -Wextra -Wpedantic)
endif()

find_package(Threads REQUIRED)
target_link_libraries(tusqh_oineus_runner PRIVATE Threads::Threads)
