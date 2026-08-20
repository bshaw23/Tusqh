#!/usr/bin/env python3
#
# This file is part of 'Aleph - A Library for Exploring Persistent
# Homology'. It contains code for turning the plain-text persistence
# diagram files written by Aleph's command-line tools (one file per
# dimension, named '<basename>_d<K>.txt') into a single persistence
# diagram plot, in the style commonly used in papers and reports:
# one marker shape/colour per homology dimension, a solid diagonal,
# and a dashed 'infinity' row for essential (never-dying) classes.
#
# Usage
# -----
#
#   Plot every basename found in a directory:
#
#     python plot_persistence_diagrams.py /path/to/directory
#
#   Plot a single basename only:
#
#     python plot_persistence_diagrams.py /path/to/directory -b dragon_x30_y13_z21
#
#   Write the images somewhere other than the input directory, and/or
#   pop up an interactive window instead of (or as well as) saving:
#
#     python plot_persistence_diagrams.py /path/to/directory -o /path/to/output --show
#
# Each '<basename>_d<K>.txt' file is expected to look like Aleph's
# tools actually write it, e.g.:
#
#   # dimension 0
#   # filtration superlevel
#   # birth	death
#   0	inf
#   -0	-0.046900000423192978
#   ...
#
# Lines starting with '#' are treated as comments (and are used to
# auto-detect the filtration direction; see below); everything else
# is a whitespace-separated 'birth death' pair. 'inf' / '-inf' are
# parsed as infinite persistence, i.e. a feature that never dies.

import argparse
import glob
import math
import os
import re
import sys

import matplotlib

# Pick a headless-safe backend unless the user asked to pop up an
# interactive window; switching backends after pyplot has already
# been imported is unreliable, so this has to happen first.
if "--show" not in sys.argv:
    matplotlib.use("Agg")

import matplotlib.pyplot as plt

# One (colour, marker) pair per homology dimension, chosen to match
# the conventional look of persistence diagrams (H0 = blue circles,
# H1 = red triangles, H2 = purple squares). Further dimensions fall
# back to matplotlib's default colour cycle with a repeating marker
# sequence.
DEFAULT_STYLES = [
    ("#1f3fd8", "o"),  # dimension 0
    ("#e8412c", "^"),  # dimension 1
    ("#a0299e", "s"),  # dimension 2
    ("#2ca02c", "D"),
    ("#ff7f0e", "P"),
    ("#17becf", "X"),
]

BASENAME_PATTERN = re.compile(r"^(?P<base>.+)_d(?P<dim>\d+)\.txt$")


def find_basenames(directory):
    """Returns a sorted list of basenames for which at least one
    '<basename>_d<K>.txt' file exists in the given directory."""

    bases = set()

    for path in glob.glob(os.path.join(directory, "*_d*.txt")):
        match = BASENAME_PATTERN.match(os.path.basename(path))
        if match:
            bases.add(match.group("base"))

    return sorted(bases)


def find_dimension_files(directory, basename):
    """Returns a sorted list of (dimension, path) pairs for every
    '<basename>_d<K>.txt' file that exists in the given directory."""

    files = []

    for path in glob.glob(os.path.join(directory, "%s_d*.txt" % basename)):
        match = BASENAME_PATTERN.match(os.path.basename(path))
        if match and match.group("base") == basename:
            files.append((int(match.group("dim")), path))

    files.sort(key=lambda item: item[0])
    return files


def detect_filtration(lines):
    """Inspects the comment lines of a diagram file for a
    '# filtration <sublevel|superlevel>' declaration and returns
    'sublevel', 'superlevel', or None if no such comment was found."""

    for line in lines:
        line = line.strip()
        if not line.startswith("#"):
            continue

        tokens = line.lstrip("#").split()
        if len(tokens) >= 2 and tokens[0].lower() == "filtration":
            return tokens[1].lower()

    return None


def load_diagram(path, sign):
    """Loads a single '<basename>_d<K>.txt' file and returns a list of
    (birth, death) pairs.

    Superlevel-set filtrations report thresholds directly, so a
    feature's 'birth' value (a high threshold) is numerically *larger*
    than its 'death' value (a lower threshold) -- the mirror image of
    the usual sublevel-set convention where death >= birth. To draw a
    conventional-looking diagram (points on or above the diagonal), we
    negate finite coordinates for superlevel diagrams. Infinite
    coordinates are always displayed as +infinity, regardless of sign,
    since they simply mark a feature that never dies.

    'sign' must be one of 'auto', 'flip', or 'noflip'.
    """

    with open(path) as f:
        lines = f.readlines()

    if sign == "auto":
        filtration = detect_filtration(lines)
        negate = filtration == "superlevel"
    else:
        negate = sign == "flip"

    def transform(value):
        if math.isinf(value):
            return math.inf
        return -value if negate else value

    points = []

    for line in lines:
        line = line.strip()
        if not line or line.startswith("#"):
            continue

        tokens = line.split()
        if len(tokens) < 2:
            continue

        birth, death = float(tokens[0]), float(tokens[1])
        points.append((transform(birth), transform(death)))

    return points


def plot_diagram(diagrams, title=None):
    """Creates a persistence diagram figure from a list of
    (dimension, points) pairs, where 'points' is a list of
    (birth, death) tuples. Returns the created matplotlib Figure."""

    finite_values = [
        value
        for _, points in diagrams
        for pair in points
        for value in pair
        if not math.isinf(value)
    ]

    finite_max = max(finite_values) if finite_values else 1.0
    finite_min = min(finite_values + [0.0]) if finite_values else 0.0

    span = finite_max - finite_min
    if span <= 0:
        span = 1.0

    pad         = span * 0.08
    axis_max    = finite_max + pad
    infinity_row = finite_max + span * 0.18
    top          = infinity_row + span * 0.12

    fig, ax = plt.subplots(figsize=(6, 6))

    # Diagonal death = birth.
    ax.plot([finite_min, top], [finite_min, top], color="black", linewidth=1.2, zorder=1)

    # Dashed cut-off marking the row used for essential/infinite classes.
    ax.plot(
        [finite_min, top],
        [infinity_row, infinity_row],
        color="black",
        linewidth=1.2,
        linestyle="--",
        zorder=1,
    )

    have_infinite = False

    for dimension, points in diagrams:
        if not points:
            continue

        colour, marker = DEFAULT_STYLES[dimension % len(DEFAULT_STYLES)]

        xs, ys = [], []
        for birth, death in points:
            xs.append(birth)
            ys.append(infinity_row if math.isinf(death) else death)
            if math.isinf(death):
                have_infinite = True

        ax.scatter(
            xs,
            ys,
            s=70,
            c=colour,
            marker=marker,
            edgecolors="black",
            linewidths=0.4,
            alpha=0.85,
            zorder=2,
            label=r"$B_{%d}$" % dimension,
        )

    ax.set_xlim(finite_min, top)
    ax.set_ylim(finite_min, top)

    if have_infinite:
        ticks  = [t for t in ax.get_yticks() if finite_min <= t <= axis_max]
        labels = ["%g" % t for t in ticks]

        ticks.append(infinity_row)
        labels.append(r"$\infty$")

        ax.set_yticks(ticks)
        ax.set_yticklabels(labels)
        ax.set_ylim(finite_min, top)

    ax.set_xlabel("Birth")
    ax.set_ylabel("Death")
    ax.set_aspect("equal", adjustable="box")

    if title:
        ax.set_title(title)

    ax.legend(loc="lower right", frameon=True)
    fig.tight_layout()

    return fig


def process_basename(directory, basename, output_dir, sign, dpi, show):
    dimension_files = find_dimension_files(directory, basename)

    if not dimension_files:
        print("No '*_d<K>.txt' files found for '%s' in %s" % (basename, directory), file=sys.stderr)
        return False

    diagrams = [
        (dimension, load_diagram(path, sign))
        for dimension, path in dimension_files
    ]

    fig = plot_diagram(diagrams, title=basename)

    output_path = os.path.join(output_dir, "%s_persistence_diagram.png" % basename)
    fig.savefig(output_path, dpi=dpi)
    print("Wrote %s" % output_path)

    if show:
        plt.show()

    plt.close(fig)
    return True


def main():
    parser = argparse.ArgumentParser(
        description="Plot persistence diagrams from Aleph '<basename>_d<K>.txt' output files."
    )
    parser.add_argument("directory", help="Directory containing the '<basename>_d<K>.txt' files")
    parser.add_argument(
        "-b", "--basename",
        action="append",
        dest="basenames",
        help="Only plot this basename (may be given multiple times). Default: every basename found in the directory.",
    )
    parser.add_argument(
        "-o", "--output-dir",
        default=None,
        help="Directory to write PNG files to (default: same as the input directory)",
    )
    parser.add_argument(
        "--sign",
        choices=["auto", "flip", "noflip"],
        default="auto",
        help="How to handle superlevel-set sign conventions: 'auto' reads the '# filtration' comment "
             "(default), 'flip' always negates finite coordinates, 'noflip' never does.",
    )
    parser.add_argument("--dpi", type=int, default=150, help="Resolution of the saved PNG (default: 150)")
    parser.add_argument("--show", action="store_true", help="Also display each diagram interactively")

    args = parser.parse_args()

    directory  = args.directory
    output_dir = args.output_dir or directory
    os.makedirs(output_dir, exist_ok=True)

    basenames = args.basenames or find_basenames(directory)

    if not basenames:
        print("No '*_d<K>.txt' files found in %s" % directory, file=sys.stderr)
        sys.exit(1)

    ok = True
    for basename in basenames:
        ok = process_basename(directory, basename, output_dir, args.sign, args.dpi, args.show) and ok

    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
