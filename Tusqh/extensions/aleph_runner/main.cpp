#include <aleph/persistenceDiagrams/Calculation.hh>
#include <aleph/persistentHomology/Calculation.hh>
#include <aleph/topology/BoundaryMatrix.hh>
#include <aleph/topology/Conversions.hh>
#include <aleph/topology/Simplex.hh>
#include <aleph/topology/SimplicialComplex.hh>
#include <aleph/topology/filtrations/Data.hh>

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
#include <functional>
#include <iomanip>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <type_traits>
#include <utility>
#include <vector>

#ifdef _OPENMP
  #include <omp.h>
#endif

namespace
{

using Clock      = std::chrono::steady_clock;
using DataType   = double;
using VertexType = std::uint32_t;
using Simplex    = aleph::topology::Simplex<DataType, VertexType>;
using Complex    = aleph::topology::SimplicialComplex<Simplex>;
using Matrix     = aleph::topology::BoundaryMatrix<aleph::defaults::Representation>;

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
  bool dualize = true;
  bool includeTop = false;
  bool keepDiagonal = false;
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
    throw std::runtime_error( "Vertex count exceeds Aleph's 32-bit index capacity" );

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
    << "INPUT may be a compact binary v1 file or a legacy verts/edges/faces/tets text export.\n"
    << "Compact files use the filtration direction stored in their header unless overridden below.\n\n"
    << "Options:\n"
    << "  --superlevel       Override input metadata: descending filtration and minimum values.\n"
    << "  --sublevel         Override input metadata: ascending filtration and maximum values.\n"
    << "  --no-dualize       Reduce the ordinary boundary matrix instead of its dual.\n"
    << "  --include-top      Include unpaired creators in the top dimension.\n"
    << "  --keep-diagonal    Retain zero-persistence points.\n"
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
    else if( argument == "--no-dualize" )
      options.dualize = false;
    else if( argument == "--include-top" )
      options.includeTop = true;
    else if( argument == "--keep-diagonal" )
      options.keepDiagonal = true;
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
                   const std::array<std::size_t, 4>& simplexCounts,
                   std::size_t totalSimplices,
                   std::size_t pairingCount,
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
  output << "simplices_total\t" << totalSimplices << "\tcount\n";
  for( std::size_t dimension = 0; dimension < simplexCounts.size(); ++dimension )
    output << "simplices_d" << dimension << "\t" << simplexCounts[dimension] << "\tcount\n";
  output << "persistence_pairs\t" << pairingCount << "\tcount\n";
#ifdef _OPENMP
  output << "openmp_max_threads\t" << omp_get_max_threads() << "\tcount\n";
#else
  output << "openmp_max_threads\t1\tcount\n";
#endif
  for( auto&& timing : timings )
    output << timing.name << "\t" << timing.milliseconds << "\tms\n";
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
    timings.push_back( { "read_input", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    Complex complex;
    constexpr std::size_t seedBatchSize = 262144;
    std::vector<Simplex> seeds;
    auto inputSimplexCount = input.vertexCount > std::numeric_limits<std::size_t>::max() - input.tetrahedronCount
                           ? std::numeric_limits<std::size_t>::max()
                           : input.vertexCount + input.tetrahedronCount;
    seeds.reserve( std::min( seedBatchSize, inputSimplexCount ) );

    auto flushSeeds = [&] ()
    {
      if( seeds.empty() )
        return;
      complex.insert( seeds.begin(), seeds.end() );
      seeds.clear();
    };

    for( std::size_t i = 0; i < input.values.size(); ++i )
    {
      seeds.emplace_back( static_cast<VertexType>( i ), input.values[i] );
      if( seeds.size() == seedBatchSize )
        flushSeeds();
    }

    for( auto&& tet : input.tets )
    {
      auto value = input.values.at( tet.vertices[0] );
      for( std::size_t i = 1; i < tet.vertices.size(); ++i )
      {
        auto vertexValue = input.values.at( tet.vertices[i] );
        value = options.superlevel ? std::min( value, vertexValue )
                                   : std::max( value, vertexValue );
      }
      seeds.emplace_back( tet.vertices.begin(), tet.vertices.end(), value );
      if( seeds.size() == seedBatchSize )
        flushSeeds();
    }

    flushSeeds();
    std::vector<Simplex>().swap( seeds );
    std::vector<DataType>().swap( input.values );
    std::vector<Tet>().swap( input.tets );
    timings.push_back( { "seed_complex", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    complex.createMissingFaces();
    complex.recalculateWeights( !options.superlevel );
    timings.push_back( { "restore_faces_and_weights", elapsedMilliseconds( stageStart, Clock::now() ) } );

    if( complex.size() > std::numeric_limits<aleph::defaults::Index>::max() )
      throw std::runtime_error( "Total simplex count exceeds Aleph's 32-bit boundary-matrix capacity" );

    std::array<std::size_t, 4> simplexCounts = {{ 0, 0, 0, 0 }};
    for( auto&& simplex : complex )
    {
      if( simplex.dimension() >= simplexCounts.size() )
        throw std::runtime_error( "Input produced a simplex above dimension 3" );
      ++simplexCounts[simplex.dimension()];
    }

    stageStart = Clock::now();
    if( options.superlevel )
      complex.sort( aleph::topology::filtrations::Data<Simplex, std::greater<DataType>>() );
    else
      complex.sort( aleph::topology::filtrations::Data<Simplex, std::less<DataType>>() );
    timings.push_back( { "sort_filtration", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    Matrix boundaryMatrix = aleph::topology::makeBoundaryMatrix( complex );
    timings.push_back( { "build_boundary_matrix", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    Matrix reductionMatrix = options.dualize ? boundaryMatrix.dualize()
                                             : std::move( boundaryMatrix );
    if( options.dualize )
      boundaryMatrix = Matrix();
    timings.push_back( { "dualize_boundary_matrix", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    auto pairing = aleph::calculatePersistencePairing( reductionMatrix, options.includeTop );
    reductionMatrix = Matrix();
    timings.push_back( { "reduce_and_pair", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    auto diagrams = aleph::makePersistenceDiagrams( pairing, complex );
    if( !options.keepDiagonal )
      for( auto&& diagram : diagrams )
        diagram.removeDiagonal();
    timings.push_back( { "construct_diagrams", elapsedMilliseconds( stageStart, Clock::now() ) } );

    stageStart = Clock::now();
    for( auto&& diagram : diagrams )
    {
      auto filename = options.outputPrefix + "_d" + std::to_string( diagram.dimension() ) + ".txt";
      std::ofstream output( filename );
      if( !output )
        throw std::runtime_error( "Unable to write persistence diagram: " + filename );

      output << std::setprecision( 17 );
      output << "# dimension " << diagram.dimension() << "\n";
      output << "# filtration " << ( options.superlevel ? "superlevel" : "sublevel" ) << "\n";
      output << "# birth\tdeath\n";
      output << diagram;
    }
    timings.push_back( { "write_diagrams", elapsedMilliseconds( stageStart, Clock::now() ) } );
    timings.push_back( { "total", elapsedMilliseconds( totalStart, Clock::now() ) } );

    writeTimings( options, input, simplexCounts, complex.size(), pairing.size(), timings );

    std::cout << std::fixed << std::setprecision( 3 );
    std::cout << "Aleph run complete\n";
    std::cout << "  input format: " << input.format << "\n";
    std::cout << "  vertices: " << input.vertexCount << "\n";
    std::cout << "  tetrahedra: " << input.tetrahedronCount << "\n";
    std::cout << "  simplices: " << complex.size() << "\n";
    std::cout << "  persistence pairs: " << pairing.size() << "\n";
    for( auto&& timing : timings )
      std::cout << "  " << timing.name << ": " << timing.milliseconds << " ms\n";
    std::cout << "  timing file: " << options.outputPrefix << "_timings.tsv\n";
    std::cout << "Note: Aleph's boundary-matrix reduction is serial; OpenMP does not parallelize reduce_and_pair.\n";
    return 0;
  }
  catch( const std::exception& error )
  {
    std::cerr << "Error: " << error.what() << "\n";
    return 1;
  }
}
