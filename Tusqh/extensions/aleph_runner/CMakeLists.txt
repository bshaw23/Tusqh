cmake_minimum_required(VERSION 3.20)

project(tusqh_aleph_runner LANGUAGES CXX)

set(ALEPH_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../aleph" CACHE PATH
    "Path to the Aleph source checkout")
set(BOOST_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../_install/boost_1_91_0" CACHE PATH
    "Path to the Boost source/include root")

if(NOT EXISTS "${ALEPH_SOURCE_DIR}/include/aleph/topology/Simplex.hh")
  message(FATAL_ERROR "Aleph headers were not found under ALEPH_SOURCE_DIR=${ALEPH_SOURCE_DIR}")
endif()

find_path(BOOST_INCLUDE_DIR
  NAMES boost/version.hpp
  HINTS "${BOOST_ROOT}"
)

if(NOT BOOST_INCLUDE_DIR)
  message(FATAL_ERROR "Boost headers were not found. Set BOOST_ROOT to the Boost directory.")
endif()

add_executable(tusqh_aleph_runner main.cpp)

target_compile_features(tusqh_aleph_runner PRIVATE cxx_std_14)
target_include_directories(tusqh_aleph_runner PRIVATE
  "${ALEPH_SOURCE_DIR}"
  "${ALEPH_SOURCE_DIR}/include"
  "${BOOST_INCLUDE_DIR}"
)

if(MSVC)
  target_compile_definitions(tusqh_aleph_runner PRIVATE
    _USE_MATH_DEFINES
    __PRETTY_FUNCTION__=__FUNCSIG__
  )
  target_compile_options(tusqh_aleph_runner PRIVATE /W4)
else()
  target_compile_options(tusqh_aleph_runner PRIVATE -Wall -Wextra -Wpedantic)
endif()

find_package(OpenMP QUIET)
if(OpenMP_CXX_FOUND)
  target_link_libraries(tusqh_aleph_runner PRIVATE OpenMP::OpenMP_CXX)
endif()

