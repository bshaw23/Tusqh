#include <oineus/oineus.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cctype>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

namespace
{

using Clock      = std::chrono::steady_clock;
using DataType    = double;
using VertexType  = std::uint32_t;   // matches the compact binary / legacy text format
using Int         = int;             // matches the Oineus core validated by the port's own
                                      // Catch2 test suite (VRUDecomposition<int>, Simplex<int>)
using Real        = double;
using OSimplex    = oineus::Simplex<Int>;
using OFiltration = oineus::Filtration<OSimplex, Real>;

struct Tet
{
  std::array<VertexType, 4> vertices;
};

struct InputData
{
  std::vector<DataType> values;
  std::vector<Tet> tets;
  std::size_t vertexCount = 0;
  std::size_t tetrahedronCount = 0;
  std::size_t skippedEdges = 0;
  std::size_t skippedFaces = 0;
  std::string format = "legacy_text";
  bool hasFiltrationMetadata = false;
  bool superlevel = true;
  std::uint32_t filtrationCoordinate = 3;
};

struct Options
{
  std::string input;
  std::string outputPrefix;
  bool superlevel = true;
  bool filtrationExplicit = false;
  bool keepDiagonal = false;
  // Oineus also supports dualizing the boundary matrix before reduction, same
  // idea as Aleph's --no-dualize. Unlike Aleph, this port's own validation
  // (the smoke test and the Catch2 reduction suite) only ever exercised
  // dualize=false, so that is the default here; --dualize is opt-in and not
  // covered by that validation.
  bool dualize = false;
  unsigned threads = 0; // 0 = auto (hardware_concurrency)
};

struct Timing
{
  std::string name;
  double milliseconds;
};

double elapsedMilliseconds( Clock::time_point start, Clock::time_point end )
{
  return std::chrono::duration<double, std::milli>( end - start ).count();
}

std::string trim( std::string text )
{
  auto isSpace = [] ( unsigned char c ) { return std::isspace( c ) != 0; };

  text.erase( text.begin(),
              std::find_if( text.begin(), text.end(),
                            [&] ( char c ) { return !isSpace( static_cast<unsigned char>( c ) ); } ) );
  text.erase( std::find_if( text.rbegin(), text.rend(),
                            [&] ( char c ) { return !isSpace( static_cast<unsigned char>( c ) ); } ).base(),
              text.end() );
  return text;
}

DataType parseData( const std::string& text, std::size_t lineNumber )
{
  std::size_t consumed = 0;
  DataType value = 0.0;

  try
  {
    value = std::stod( text, &consumed );
  }
  catch( const std::exception& )
  {
    throw std::runtime_error( "Invalid floating-point value at line " + std::to_string( lineNumber ) );
  }

  if( consumed != text.size() || !std::isfinite( value ) )
    throw std::runtime_error( "Invalid finite floating-point value at line " + std::to_string( lineNumber ) );

  return value;
}

std::uint64_t parseIndex( const std::string& text, std::size_t lineNumber )
{
  if( text.empty() || text.front() == '-' )
    throw std::runtime_error( "Invalid tetrahedron index at line " + std::to_string( lineNumber ) );

  std::size_t consumed = 0;
  std::uint64_t value = 0;

  try
  {
    value = std::stoull( text, &consumed );
  }
  catch( const std::exception& )
  {
    throw std::runtime_error( "Invalid tetrahedron index at line " + std::to_string( lineNumber ) );
  }

  if( consumed != text.size() )
    throw std::runtime_error( "Invalid tetrahedron index at line " + std::to_string( lineNumber ) );

  return value;
}

template <class UInt> UInt readLittleEndian( std::istream& input, const char* field )
{
  static_assert( std::is_unsigned<UInt>::value, "UInt must be unsigned" );

  std::array<unsigned char, sizeof( UInt )> bytes;
  input.read( reinterpret_cast<char*>( bytes.data() ), bytes.size() );
  if( !input )
    throw std::runtime_error( std::string( "Unexpected end of compact input while reading " ) + field );

  UInt value = 0;
  for( std::size_t i = 0; i < bytes.size(); ++i )
    value |= static_cast<UInt>( bytes[i] ) << ( 8 * i );
  return value;
}

float readLittleEndianFloat( std::istream& input, const char* field )
{
  auto bits = readLittleEndian<std::uint32_t>( input, field );
  float value = 0.0f;
  static_assert( sizeof( value ) == sizeof( bits ), "Unexpected float32 representation" );
  std::memcpy( &value, &bits, sizeof( value ) );
  return value;
}

// Reads the same "compact binary v1" (TQALPH1) format aleph_runner reads --
// see extensions/aleph_runner/README.md for the exact byte layout. Kept in
// lockstep with aleph_runner's reader so the Grasshopper export components
// (which produce this format) do not need to change.
InputData readCompactFile( const std::string& filename )
{
  static const std::array<unsigned char, 8> expectedMagic =
    {{ 'T', 'Q', 'A', 'L', 'P', 'H', '1', 0 }};
  static const std::uint64_t headerSize = 28;

  std::ifstream input( filename, std::ios::binary );
  if( !input )
    throw std::runtime_error( "Unable to open input file: " + filename );

  std::array<unsigned char, 8> magic;
  input.read( reinterpret_cast<char*>( magic.data() ), magic.size() );
  if( !input || magic != expectedMagic )
    throw std::runtime_error( "Invalid compact Aleph file signature" );

  auto version = readLittleEndian<std::uint32_t>( input, "format version" );
  auto flags = readLittleEndian<std::uint32_t>( input, "flags" );
  auto vertexCount = readLittleEndian<std::uint32_t>( input, "vertex count" );
  auto tetrahedronCount = readLittleEndian<std::uint64_t>( input, "tetrahedron count" );

  if( version != 1 )
    throw std::runtime_error( "Unsupported compact Aleph format version: " + std::to_string( version ) );
  if( ( flags & ~std::uint32_t( 0x301 ) ) != 0 )
    throw std::runtime_error( "Compact Aleph input contains unsupported flags" );

  auto filtrationCoordinate = ( flags >> 8 ) & 0x3;
  if( vertexCount == 0 )
    throw std::runtime_error( "Compact input contains no vertices" );
  if( tetrahedronCount == 0 )
    throw std::runtime_error( "Compact input contains no tetrahedra" );
  if( tetrahedronCount > std::numeric_limits<std::size_t>::max() )
    throw std::runtime_error( "Tetrahedron count exceeds this platform's addressable range" );

  if( tetrahedronCount > ( std::numeric_limits<std::uint64_t>::max() - headerSize -
                           4ull * vertexCount ) / 16ull )
    throw std::runtime_error( "Compact input size overflows uint64" );
  auto expectedSize = headerSize + 4ull * vertexCount + 16ull * tetrahedronCount;

  input.seekg( 0, std::ios::end );
  auto fileEnd = input.tellg();
  if( fileEnd < 0 || static_cast<std::uint64_t>( fileEnd ) != expectedSize )
    throw std::runtime_error( "Compact input size does not match its header" );
  input.seekg( headerSize, std::ios::beg );

  InputData result;
  result.format = "compact_binary_v1";
  result.hasFiltrationMetadata = true;
  result.superlevel = ( flags & 1u ) != 0;
  result.filtrationCoordinate = filtrationCoordinate;
  result.vertexCount = vertexCount;
  result.tetrahedronCount = static_cast<std::size_t>( tetrahedronCount );
  result.values.reserve( result.vertexCount );
  result.tets.reserve( result.tetrahedronCount );

  for( std::size_t i = 0; i < result.vertexCount; ++i )
  {
    auto value = readLittleEndianFloat( input, "vertex filtration value" );
    if( !std::isfinite( value ) )
      throw std::runtime_error( "Non-finite compact vertex value at index " + std::to_string( i ) );
    result.values.push_back( static_cast<DataType>( value ) );
  }

  for( std::size_t i = 0; i < result.tetrahedronCount; ++i )
  {
    Tet tet;
    for( auto& vertex : tet.vertices )
    {
      vertex = readLittleEndian<std::uint32_t>( input, "tetrahedron index" );
      if( vertex >= result.vertexCount )
        throw std::runtime_error( "Compact tetrahedron index outside 0.." +
                                  std::to_string( result.vertexCount - 1 ) +
                                  " in tetrahedron " + std::to_string( i ) );
    }

    auto sorted = tet.vertices;
    std::sort( sorted.begin(), sorted.end() );
    if( std::adjacent_find( sorted.begin(), sorted.end() ) != sorted.end() )
      throw std::runtime_error( "Degenerate compact tetrahedron at index " + std::to_string( i ) );
    result.tets.push_back( tet );
  }

  return result;
}

// Reads the same legacy verts/edges/faces/tets text export aleph_runner
// reads. The edges/faces sections are redundant here too (Oineus derives
// them itself, same as Aleph's createMissingFaces) so they are just skipped.
InputData readGrasshopperFile( const std::string& filename )
{
  std::ifstream input( filename );
  if( !input )
    throw std::runtime_error( "Unable to open input file: " + filename );

  enum class Section { None, Vertices, Edges, Faces, Tets };

  Section section = Section::None;
  bool sawVertices = false;
  bool sawEdges = false;
  bool sawFaces = false;
  bool sawTets = false;

  InputData result;
  std::size_t edgeLines = 0;
  std::size_t faceLines = 0;
  std::size_t lineNumber = 0;
  std::array<std::string, 5> tetFields;
  std::array<std::size_t, 5> tetFieldLines;
  std::size_t tetFieldCount = 0;

  std::string line;
  while( std::getline( input, line ) )
  {
    ++lineNumber;
    line = trim( line );

    if( line.empty() )
      continue;

    if( line == "verts" || line == "edges" || line == "faces" || line == "tets" )
    {
      if( tetFieldCount != 0 )
        throw std::runtime_error( "Incomplete tetrahedron record before line " + std::to_string( lineNumber ) );

      if( line == "verts" )
      {
        section = Section::Vertices;
        sawVertices = true;
      }
      else if( line == "edges" )
      {
        section = Section::Edges;
        sawEdges = true;
      }
      else if( line == "faces" )
      {
        section = Section::Faces;
        sawFaces = true;
      }
      else
      {
        section = Section::Tets;
        sawTets = true;
      }

      continue;
    }

    switch( section )
    {
      case Section::Vertices:
        result.values.push_back( parseData( line, lineNumber ) );
        break;

      case Section::Edges:
        ++edgeLines;
        break;

      case Section::Faces:
        ++faceLines;
        break;

      case Section::Tets:
      {
        tetFields[tetFieldCount] = line;
        tetFieldLines[tetFieldCount] = lineNumber;
        ++tetFieldCount;

        if( tetFieldCount == tetFields.size() )
        {
          Tet tet;
          for( std::size_t i = 0; i < 4; ++i )
          {
            auto oneBased = parseIndex( tetFields[i], tetFieldLines[i] );
            if( oneBased == 0 || oneBased > result.values.size() )
              throw std::runtime_error( "Tetrahedron index outside 1.." +
                                        std::to_string( result.values.size() ) +
                                        " at line " + std::to_string( tetFieldLines[i] ) );

            tet.vertices[i] = static_cast<VertexType>( oneBased - 1 );
          }

          auto sorted = tet.vertices;
          std::sort( sorted.begin(), sorted.end() );
          if( std::adjacent_find( sorted.begin(), sorted.end() ) != sorted.end() )
            throw std::runtime_error( "Degenerate tetrahedron ending at line " + std::to_string( lineNumber ) );

          // Validate the exported value, but recompute it from vertex values later.
          parseData( tetFields[4], tetFieldLines[4] );
          result.tets.push_back( tet );
          tetFieldCount = 0;
        }
        break;
      }

      case Section::None:
        throw std::runtime_error( "Expected the 'verts' marker before line " + std::to_string( lineNumber ) );
    }
  }

  if( !sawVertices || !sawEdges || !sawFaces || !sawTets )
    throw std::runtime_error( "Input must contain verts, edges, faces, and tets sections" );
  if( tetFieldCount != 0 )
    throw std::runtime_error( "Incomplete tetrahedron record at end of input" );
  if( edgeLines % 3 != 0 )
    throw std::runtime_error( "The edges section does not contain complete three-line records" );
  if( faceLines % 4 != 0 )
    throw std::runtime_error( "The faces section does not contain complete four-line records" );
  if( result.values.empty() )
    throw std::runtime_error( "Input contains no vertices" );
  if( result.tets.empty() )
    throw std::runtime_error( "Input contains no tetrahedra" );
  if( result.values.size() > std::numeric_limits<VertexType>::max() )
    throw std::runtime_error( "Vertex count exceeds this format's 32-bit index capacity" );

  result.skippedEdges = edgeLines / 3;
  result.skippedFaces = faceLines / 4;
  result.vertexCount = result.values.size();
  result.tetrahedronCount = result.tets.size();
  return result;
}

InputData readInputFile( const std::string& filename )
{
  static const std::array<unsigned char, 8> compactMagic =
    {{ 'T', 'Q', 'A', 'L', 'P', 'H', '1', 0 }};

  std::ifstream probe( filename, std::ios::binary );
  if( !probe )
    throw std::runtime_error( "Unable to open input file: " + filename );

  std::array<unsigned char, 8> prefix = {{ 0 }};
  probe.read( reinterpret_cast<char*>( prefix.data() ), prefix.size() );
  if( probe.gcount() == static_cast<std::streamsize>( prefix.size() ) && prefix == compactMagic )
    return readCompactFile( filename );

  return readGrasshopperFile( filename );
}

void printUsage( std::ostream& output, const char* executable )
{
  output
    << "Usage:\n"
    << "  " << executable << " INPUT OUTPUT_PREFIX [options]\n\n"
    << "INPUT may be a compact binary v1 file or a legacy verts/edges/faces/tets text export\n"
    << "-- the same files aleph_runner reads. Compact files use the filtration direction\n"
    << "stored in their header unless overridden below.\n\n"
    << "Options:\n"
    << "  --superlevel       Override input metadata: descending filtration, cell value = min of its vertices.\n"
    << "  --sublevel         Override input metadata: ascending filtration, cell value = max of its vertices.\n"
    << "  --dualize          Dualize the boundary matrix before reduction. NOT covered by this\n"
    << "                     port's own validation (Catch2 suite only exercised dualize=false).\n"
    << "  --keep-diagonal    Retain zero-persistence points.\n"
    << "  --threads N        Reduction thread count (default: hardware concurrency).\n"
    << "  -h, --help         Show this help.\n";
}

Options parseOptions( int argc, char** argv )
{
  if( argc == 2 && ( std::string( argv[1] ) == "-h" || std::string( argv[1] ) == "--help" ) )
  {
    printUsage( std::cout, argv[0] );
    std::exit( 0 );
  }

  if( argc < 3 )
  {
    printUsage( std::cerr, argv[0] );
    throw std::runtime_error( "INPUT and OUTPUT_PREFIX are required" );
  }

  Options options;
  options.input = argv[1];
  options.outputPrefix = argv[2];

  for( int i = 3; i < argc; ++i )
  {
    std::string argument = argv[i];
    if( argument == "--superlevel" )
    {
      options.superlevel = true;
      options.filtrationExplicit = true;
    }
    else if( argument == "--sublevel" )
    {
      options.superlevel = false;
      options.filtrationExplicit = true;
    }
    else if( argument == "--dualize" )
      options.dualize = true;
    else if( argument == "--keep-diagonal" )
      options.keepDiagonal = true;
    else if( argument == "--threads" )
    {
      if( i + 1 >= argc )
        throw std::runtime_error( "--threads requires a value" );
      auto value = parseIndex( argv[++i], 0 );
      if( value == 0 || value > std::numeric_limits<unsigned>::max() )
        throw std::runtime_error( "--threads value out of range" );
      options.threads = static_cast<unsigned>( value );
    }
    else if( argument == "-h" || argument == "--help" )
    {
      printUsage( std::cout, argv[0] );
      std::exit( 0 );
    }
    else
      throw std::runtime_error( "Unknown option: " + argument );
  }

  return options;
}

void writeTimings( const Options& options,
                   const InputData& input,
                   const std::array<std::size_t, 4>& cellCounts,
                   std::size_t totalCells,
                   std::size_t pairingCount,
                   unsigned threadsUsed,
                   const std::vector<Timing>& timings )
{
  std::ofstream output( options.outputPrefix + "_timings.tsv" );
  if( !output )
    throw std::runtime_error( "Unable to write timing file" );

  output << std::setprecision( 17 );
  output << "metric\tvalue\tunit\n";
  output << "input_format\t" << input.format << "\ttext\n";
  output << "filtration\t" << ( options.superlevel ? "superlevel" : "sublevel" ) << "\ttext\n";
  if( input.hasFiltrationMetadata )
    output << "filtration_coordinate\t" << input.filtrationCoordinate << "\tindex\n";
  output << "dualized\t" << ( options.dualize ? 1 : 0 ) << "\tbool\n";
  output << "input_vertices\t" << input.vertexCount << "\tcount\n";
  output << "input_tetrahedra\t" << input.tetrahedronCount << "\tcount\n";
  output << "ignored_exported_edges\t" << input.skippedEdges << "\tcount\n";
  output << "ignored_exported_faces\t" << input.skippedFaces << "\tcount\n";
  output << "cells_total\t" << totalCells << "\tcount\n";
  for( std::size_t dimension = 0; dimension < cellCounts.size(); ++dimension )
    output << "cells_d" << dimension << "\t" << cellCounts[dimension] << "\tcount\n";
  output << "persistence_pairs\t" << pairingCount << "\tcount\n";
  output << "reduction_threads\t" << threadsUsed << "\tcount\n";
  for( auto&& timing : timings )
    output << timing.name << "\t" << timing.milliseconds << "\tms\n";
}

// Value assigned to a derived cell (edge/triangle/tetrahedron) from its
// vertices' filtration values. Verified directly against grid.h's
// simplex_value_and_vertex (the Freudenthal-grid path already validated end
// to end by this port's smoke test and Catch2 suite): negate=false (Aleph's
// "sublevel") takes the MAX of the vertex values with ascending filtration
// order; negate=true (Aleph's "superlevel") takes the MIN with descending
// order -- the same convention aleph_runner uses
// (`options.superlevel ? std::min(...) : std::max(...)`), so oineus_runner's
// `negate` is exactly aleph_runner's `superlevel`, not its logical opposite.
template <class VertexRange>
Real lowerStarValue( const VertexRange& vertices, const std::vector<Real>& vertexValues, bool negate )
{
  auto it = vertices.begin();
  Real value = vertexValues[ *it ];
  for( ++it; it != vertices.end(); ++it )
  {
    auto v = vertexValues[ *it ];
    value = negate ? std::min( value, v ) : std::max( value, v );
  }
  return value;
}

// Oineus's own formatting (DgmPoint::is_minus_inf / is_plus_inf, used by its
// to_string_possible_inf) prints a signed "-inf"/"inf" matching the actual
// sweep direction -- e.g. a superlevel (negate=true) essential class's death
// is genuinely -infinity, since the sweep parameter decreases. Aleph always
// prints unsigned "inf" for essential classes regardless of direction (see
// tetra_aleph_d0.txt vs tetra_oineus_d0.txt in this port's own validation:
// both agree on birth=4, only the death sentinel's sign differs). Normalize
// to Aleph's unsigned convention here so the two tools' text output is
// directly diffable; nothing about the underlying value changes internally.
std::string formatValue( Real value )
{
  if( oineus::DgmPoint<Real>::is_minus_inf( value ) || oineus::DgmPoint<Real>::is_plus_inf( value ) )
    return "inf";
  std::ostringstream stream;
  stream << std::setprecision( 17 ) << value;
  return stream.str();
}

} // namespace

int main( int argc, char** argv )
{
  try
  {
    auto options = parseOptions( argc, argv );
    auto totalStart = Clock::now();
    std::vector<Timing> timings;

    auto stageStart = Clock::now();
    auto input = readInputFile( options.input );
    if( input.hasFiltrationMetadata && !options.filtrationExplicit )
      options.superlevel = input.superlevel;
    bool negate = options.superlevel; // see lowerStarValue's comment
    timings.push_back( { "read_input", elapsedMilliseconds( stageStart, Clock::now() ) } );

    if( input.vertexCount > static_cast<std::size_t>( std::numeric_limits<Int>::max() ) )
      throw std::runtime_error( "Vertex count exceeds this runner's 32-bit index capacity" );

    unsigned threads = options.threads;
    if( threads == 0 )
      threads = std::max( 1u, std::thread::hardware_concurrency() );

    // Oineus has no equivalent of Aleph's createMissingFaces() /
    // recalculateWeights() -- building the full closed complex (every
    // vertex, edge, triangle, and tetrahedron, each with its lower-star
    // value, deduplicated across shared faces) is this runner's job instead
    // of the library's.
    stageStart = Clock::now();
    OFiltration::CellVector cells;
    cells.reserve( input.vertexCount + input.tetrahedronCount * 11 );
    std::unordered_map<OSimplex::Uid, std::size_t, OSimplex::UidHasher> uidToIndex;
    uidToIndex.reserve( input.vertexCount + input.tetrahedronCount * 11 );

    auto addCell = [&]( std::initializer_list<Int> vertexList )
    {
      OSimplex::IdxVector verts( vertexList.begin(), vertexList.end() );
      OSimplex simplex( verts );
      auto uid = simplex.get_uid();
      if( uidToIndex.find( uid ) != uidToIndex.end() )
        return;
      Real value = lowerStarValue( vertexList, input.values, negate );
      uidToIndex.emplace( uid, cells.size() );
      cells.emplace_back( std::move( simplex ), value );
    };

    for( std::size_t v = 0; v < input.vertexCount; ++v )
      addCell( { static_cast<Int>( v ) } );

    for( auto&& tet : input.tets )
    {
      Int a = static_cast<Int>( tet.vertices[0] );
      Int b = static_cast<Int>( tet.vertices[1] );
      Int c = static_cast<Int>( tet.vertices[2] );
      Int d = static_cast<Int>( tet.vertices[3] );

      // 6 edges
      addCell( { a, b } ); addCell( { a, c } ); addCell( { a, d } );
      addCell( { b, c } ); addCell( { b, d } ); addCell( { c, d } );
      // 4 triangles
      addCell( { a, b, c } ); addCell( { a, b, d } );
      addCell( { a, c, d } ); addCell( { b, c, d } );
      // the tetrahedron itself
      addCell( { a, b, c, d } );
    }

    std::array<std::size_t, 4> cellCounts = {{ 0, 0, 0, 0 }};
    for( auto&& cell : cells )
    {
      auto dim = cell.get_cell().dim();
      if( dim >= static_cast<decltype(dim)>( cellCounts.size() ) )
        throw std::runtime_error( "Input produced a cell above dimension 3" );
      ++cellCounts[ dim ];
    }
    timings.push_back( { "build_cells", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    OFiltration fil( std::move( cells ), negate, static_cast<int>( threads ) );
    timings.push_back( { "build_filtration", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    oineus::VRUDecomposition<Int> decomposition( fil, options.dualize, static_cast<int>( threads ) );
    oineus::ReductionParams params;
    params.n_threads = static_cast<int>( threads );
    params.compute_v = true;
    decomposition.reduce( params );
    timings.push_back( { "reduce", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    auto diagrams = decomposition.diagram( fil, /* include_inf_points = */ true, static_cast<int>( threads ) );
    timings.push_back( { "construct_diagrams", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    std::size_t pairingCount = 0;
    for( std::size_t dimension = 0; dimension < diagrams.n_dims(); ++dimension )
    {
      auto&& diagram = diagrams.get_diagram_in_dimension( dimension );

      auto filename = options.outputPrefix + "_d" + std::to_string( dimension ) + ".txt";
      std::ofstream output( filename );
      if( !output )
        throw std::runtime_error( "Unable to write persistence diagram: " + filename );

      output << std::setprecision( 17 );
      output << "# dimension " << dimension << "\n";
      output << "# filtration " << ( options.superlevel ? "superlevel" : "sublevel" ) << "\n";
      output << "# birth\tdeath\n";
      for( auto&& point : diagram )
      {
        if( !options.keepDiagonal && point.is_diagonal() )
          continue;
        output << formatValue( point.birth ) << "\t" << formatValue( point.death ) << "\n";
        ++pairingCount;
      }
    }
    timings.push_back( { "write_diagrams", elapsedMilliseconds( stageStart, Clock::now() ) } );
    timings.push_back( { "total", elapsedMilliseconds( totalStart, Clock::now() ) } );

    std::size_t totalCells = cellCounts[0] + cellCounts[1] + cellCounts[2] + cellCounts[3];
    writeTimings( options, input, cellCounts, totalCells, pairingCount, threads, timings );

    std::cout << std::fixed << std::setprecision( 3 );
    std::cout << "Oineus run complete\n";
    std::cout << "  input format: " << input.format << "\n";
    std::cout << "  vertices: " << input.vertexCount << "\n";
    std::cout << "  tetrahedra: " << input.tetrahedronCount << "\n";
    std::cout << "  cells: " << totalCells << "\n";
    std::cout << "  persistence pairs written: " << pairingCount << "\n";
    std::cout << "  reduction threads: " << threads << "\n";
    for( auto&& timing : timings )
      std::cout << "  " << timing.name << ": " << timing.milliseconds << " ms\n";
    std::cout << "  timing file: " << options.outputPrefix << "_timings.tsv\n";
    if( options.dualize )
      std::cout << "Note: --dualize was requested; this path is not covered by this port's own validation.\n";
    return 0;
  }
  catch( const std::exception& error )
  {
    std::cerr << "Error: " << error.what() << "\n";
    return 1;
  }
}
