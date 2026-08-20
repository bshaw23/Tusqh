// Hierarchical (divide & conquer) *exact* winding-number evaluator for a 2D
// edge soup (one or more open/closed polylines). This mirrors the algorithm
// igl::WindingNumberAABB uses for 3D triangle meshes -- see
// libigl/include/igl/WindingNumberAABB.h and WindingNumberTree.h -- adapted
// to 2D:
//
//   - The bounding volume is a 2D axis-aligned box instead of a 3D one.
//   - The per-primitive contribution is igl::signed_angle over an edge
//     instead of igl::solid_angle over a triangle (same formula libigl's
//     own brute-force igl::winding_number uses for the F.cols()==2 case,
//     so results match exactly).
//   - The "cap" that a node presents to points outside its bounding box is
//     simpler than 3D's boundary-fan triangulation: since the input is
//     already an edge chain rather than a triangle soup, the cap of any
//     contiguous open sub-chain is just the single segment connecting its
//     two endpoints. This is the same "replace a path with the direct
//     segment between its endpoints" identity that makes 3D's boundary
//     capping valid, just without needing exterior-edge extraction or fan
//     triangulation first. A sub-chain that closes into a full loop within
//     one node (rare -- only when an entire original loop lands undivided
//     in a spatial split) can't be reduced further and is kept as-is.
//
// Recursion: whenever a query point lies inside a node's bounding box, we
// must recurse (or, at a leaf, sum exactly). Whenever it lies outside, the
// node's (possibly much smaller) cap gives the exact same contribution as
// summing every edge in the subtree -- no approximation, just an
// algorithmic speedup.

#ifndef WINDING_NUMBER_AABB_2D_H
#define WINDING_NUMBER_AABB_2D_H

#include <Eigen/Dense>
#include <algorithm>
#include <limits>
#include <memory>
#include <tuple>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "libigl/include/igl/signed_angle.h"

class WindingNumberAABB2D
{
public:
  // V: #V by 2 vertex positions. E: #E by 2 zero-based vertex indices per
  // edge. Both must outlive this tree and every query made against it.
  WindingNumberAABB2D(const Eigen::MatrixXd& V, const Eigen::MatrixXi& E);

  // Recursively builds the hierarchy. Call once after construction.
  void grow();

  // Exact winding number of point p with respect to the full edge set.
  double winding_number(const Eigen::RowVector2d& p) const;

private:
  // Minimum number of edges in a hierarchy leaf. Mirrors
  // WindingNumberAABB_MIN_F (100) from the 3D implementation.
  static constexpr size_t MIN_EDGES = 50;

  WindingNumberAABB2D(const Eigen::MatrixXd& V, const Eigen::MatrixXi& E, std::vector<int> edge_ids);

  bool inside(const Eigen::RowVector2d& p) const;
  double sum_edges(const std::vector<int>& ids, const Eigen::RowVector2d& p) const;
  double sum_cap(const Eigen::RowVector2d& p) const;
  void compute_bounds();
  void compute_cap();

  const Eigen::MatrixXd& V;
  const Eigen::MatrixXi& E_all;

  std::vector<int> edge_ids;                // indices into E_all belonging to this node
  std::vector<std::pair<int, int>> cap;     // reduced boundary: (vertex, vertex) pairs,
                                             // not necessarily indices into E_all

  Eigen::RowVector2d min_corner, max_corner;

  std::unique_ptr<WindingNumberAABB2D> left, right;
};

inline WindingNumberAABB2D::WindingNumberAABB2D(const Eigen::MatrixXd& V_, const Eigen::MatrixXi& E_)
  : V(V_), E_all(E_)
{
  edge_ids.resize(static_cast<size_t>(E_.rows()));
  for (int i = 0; i < E_.rows(); ++i)
    edge_ids[static_cast<size_t>(i)] = i;
  compute_bounds();
  compute_cap();
}

inline WindingNumberAABB2D::WindingNumberAABB2D(const Eigen::MatrixXd& V_, const Eigen::MatrixXi& E_, std::vector<int> ids)
  : V(V_), E_all(E_), edge_ids(std::move(ids))
{
  compute_bounds();
  compute_cap();
}

inline void WindingNumberAABB2D::compute_bounds()
{
  min_corner = Eigen::RowVector2d(
    std::numeric_limits<double>::infinity(), std::numeric_limits<double>::infinity());
  max_corner = Eigen::RowVector2d(
    -std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity());

  for (int e : edge_ids)
  {
    for (int k = 0; k < 2; ++k)
    {
      int vtx = E_all(e, k);
      // Parenthesized to dodge the min/max macros pulled in by <intrin.h>
      // on Windows.
      min_corner.x() = (std::min)(min_corner.x(), V(vtx, 0));
      min_corner.y() = (std::min)(min_corner.y(), V(vtx, 1));
      max_corner.x() = (std::max)(max_corner.x(), V(vtx, 0));
      max_corner.y() = (std::max)(max_corner.y(), V(vtx, 1));
    }
  }
}

inline void WindingNumberAABB2D::compute_cap()
{
  cap.clear();

  // Adjacency within this node only: vertex -> [(other vertex, edge id, is this vertex the edge's source?), ...]
  std::unordered_map<int, std::vector<std::tuple<int, int, bool>>> adj;
  adj.reserve(edge_ids.size() * 2);
  for (int e : edge_ids)
  {
    int u = E_all(e, 0);
    int v = E_all(e, 1);
    // Track whether this vertex is the edge's source (E_all(e,0), i.e. the
    // *tail* of the original polyline direction at this edge) or its
    // target (E_all(e,1), the *head*). igl::signed_angle is orientation
    // sensitive -- the whole-loop sum only comes out as the correct
    // signed integer because every edge is walked tail-to-head in a
    // single consistent direction -- so the cap segment replacing a
    // chain has to preserve that same direction, not an arbitrary one.
    adj[u].emplace_back(v, e, true);   // u is this edge's source
    adj[v].emplace_back(u, e, false);  // v is this edge's target
  }

  std::unordered_set<int> visited;
  visited.reserve(edge_ids.size());

  // Every vertex belongs to exactly one original polyline (ProcessPolylines
  // gives each curve its own fresh vertex range), so within this subset any
  // vertex has degree at most 2: at most one "in" edge and one "out" edge.
  // A degree-1 vertex here is a genuine chain endpoint -- either an open
  // polyline's real end, or a point where a spatial split cut the chain.
  // Walk each such chain to its other end and replace it with one segment,
  // oriented the same way the original edges were.
  for (auto& kv : adj)
  {
    int start = kv.first;
    if (kv.second.size() != 1)
      continue;

    // For a clean contiguous run of originally-sequential edges, exactly
    // one of its two degree-1 endpoints is the source of its lone edge
    // (the chain's forward-orientation start) and the other is the
    // target (the chain's forward-orientation end). Skip target-role
    // starts here; they get emitted (in the correct direction) when the
    // loop reaches their chain's source-role endpoint instead.
    bool start_is_source = std::get<2>(kv.second[0]);
    if (!start_is_source)
      continue;

    int cur = start;
    int prev_edge = -1;
    while (true)
    {
      int next_vertex = -1;
      int next_edge = -1;
      for (auto& nb : adj[cur])
      {
        if (std::get<1>(nb) != prev_edge && visited.find(std::get<1>(nb)) == visited.end())
        {
          next_vertex = std::get<0>(nb);
          next_edge = std::get<1>(nb);
          break;
        }
      }
      if (next_edge == -1)
        break;

      visited.insert(next_edge);
      prev_edge = next_edge;
      cur = next_vertex;
    }

    if (cur != start)
      cap.emplace_back(start, cur);
  }

  // Anything left unvisited belongs to a fully closed loop that landed
  // entirely inside this node (no degree-1 vertex to start a walk from --
  // i.e. it hasn't been cut by any split yet). A closed curve has no
  // boundary, so -- exactly like a watertight surface in the 3D version,
  // whose exterior_edges() is empty -- any point strictly outside its
  // bounding box has winding number *exactly* zero with respect to it
  // (the point sits in the curve's unbounded exterior component). So
  // these edges contribute nothing to the cap; omitting them here is what
  // lets an untouched closed loop still be recognized as worth splitting
  // (empty cap < edge count) instead of grow() bailing out immediately.
}

inline void WindingNumberAABB2D::grow()
{
  if (edge_ids.size() <= MIN_EDGES || cap.size() >= edge_ids.size())
    return;

  double dx = max_corner.x() - min_corner.x();
  double dy = max_corner.y() - min_corner.y();
  int axis = (dx >= dy) ? 0 : 1;

  std::vector<double> mids(edge_ids.size());
  for (size_t i = 0; i < edge_ids.size(); ++i)
  {
    int e = edge_ids[i];
    int u = E_all(e, 0);
    int v = E_all(e, 1);
    mids[i] = 0.5 * (V(u, axis) + V(v, axis));
  }

  std::vector<double> sorted_mids = mids;
  size_t mid_idx = sorted_mids.size() / 2;
  std::nth_element(sorted_mids.begin(), sorted_mids.begin() + static_cast<long>(mid_idx), sorted_mids.end());
  double median = sorted_mids[mid_idx];

  std::vector<int> left_ids, right_ids;
  for (size_t i = 0; i < edge_ids.size(); ++i)
    (mids[i] <= median ? left_ids : right_ids).push_back(edge_ids[i]);

  if (left_ids.empty() || right_ids.empty())
    return;

  left.reset(new WindingNumberAABB2D(V, E_all, std::move(left_ids)));
  right.reset(new WindingNumberAABB2D(V, E_all, std::move(right_ids)));
  left->grow();
  right->grow();
}

inline bool WindingNumberAABB2D::inside(const Eigen::RowVector2d& p) const
{
  return p.x() >= min_corner.x() && p.x() <= max_corner.x() &&
         p.y() >= min_corner.y() && p.y() <= max_corner.y();
}

inline double WindingNumberAABB2D::sum_edges(const std::vector<int>& ids, const Eigen::RowVector2d& p) const
{
  double w = 0;
  for (int e : ids)
    w += igl::signed_angle(V.row(E_all(e, 0)), V.row(E_all(e, 1)), p);
  return w;
}

inline double WindingNumberAABB2D::sum_cap(const Eigen::RowVector2d& p) const
{
  double w = 0;
  for (auto& seg : cap)
    w += igl::signed_angle(V.row(seg.first), V.row(seg.second), p);
  return w;
}

inline double WindingNumberAABB2D::winding_number(const Eigen::RowVector2d& p) const
{
  if (inside(p))
  {
    if (left && right)
      return left->winding_number(p) + right->winding_number(p);
    return sum_edges(edge_ids, p);
  }

  if (cap.size() < edge_ids.size())
    return sum_cap(p);
  return sum_edges(edge_ids, p);
}

#endif
