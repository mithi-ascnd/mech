"""
Validates the sketch-entity mirroring rules that src/TrueMirror.Core/Model/CapturedSketch.cs
implements, in particular the arc direction flip.

The failure this guards against is specific and nasty: reflecting an arc's three defining
points WITHOUT flipping its direction flag produces the complementary arc. It renders as
plausible geometry, so it survives a visual check and corrupts the part.

Run: python3 tools/sketch_mirror_reference.py
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field, replace
from typing import List, Tuple

from mirror_math_reference import Vec3, MirrorPlane, SketchFrame, mirror_frame, mirror_uv


# ---------------------------------------------------------------------------
# Entities
# ---------------------------------------------------------------------------

@dataclass
class Line:
    start: Tuple[float, float]
    end: Tuple[float, float]

    def mirrored(self) -> "Line":
        return Line(mirror_uv(*self.start), mirror_uv(*self.end))

    def sample(self, t: float) -> Tuple[float, float]:
        return (self.start[0] + (self.end[0] - self.start[0]) * t,
                self.start[1] + (self.end[1] - self.start[1]) * t)


@dataclass
class Arc:
    center: Tuple[float, float]
    start: Tuple[float, float]
    end: Tuple[float, float]
    direction: int = 1          # +1 CCW, -1 CW
    is_circle: bool = False

    def mirrored(self) -> "Arc":
        return Arc(
            center=mirror_uv(*self.center),
            start=mirror_uv(*self.start),
            end=mirror_uv(*self.end),
            # THE RULE: traversal reverses under (u, v) -> (u, -v).
            direction=self.direction if self.is_circle else -self.direction,
            is_circle=self.is_circle,
        )

    @property
    def radius(self) -> float:
        return math.hypot(self.start[0] - self.center[0], self.start[1] - self.center[1])

    def _angle(self, p) -> float:
        return math.atan2(p[1] - self.center[1], p[0] - self.center[0]) % (2 * math.pi)

    def sample(self, t: float) -> Tuple[float, float]:
        """Point at parameter t in [0,1] along the arc, respecting direction."""
        a0 = self._angle(self.start)
        a1 = self._angle(self.end)
        sweep = (a1 - a0) % (2 * math.pi)
        if self.direction < 0:
            sweep = sweep - 2 * math.pi
        a = a0 + sweep * t
        return (self.center[0] + self.radius * math.cos(a),
                self.center[1] + self.radius * math.sin(a))

    def swept_angle(self) -> float:
        a0 = self._angle(self.start)
        a1 = self._angle(self.end)
        sweep = (a1 - a0) % (2 * math.pi)
        if self.direction < 0:
            sweep = sweep - 2 * math.pi
        return sweep


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def _check(name, cond):
    if not cond:
        raise AssertionError("FAIL: " + name)
    print("  ok   " + name)


def run_tests() -> None:
    print("sketch mirror reference tests")
    print()

    X = Vec3(1, 0, 0)
    O = Vec3(0, 0, 0)
    plane = MirrorPlane.create(Vec3(0.05, 0, 0), X)
    frame = SketchFrame(origin=O, x_axis=Vec3(1, 0, 0), y_axis=Vec3(0, 1, 0))
    mframe = mirror_frame(frame, plane)

    # --- involution ---------------------------------------------------------
    print("involution:")

    line = Line((0.01, 0.02), (0.03, -0.04))
    _check("line mirrored twice is the original",
           _close2(line.mirrored().mirrored().start, line.start) and
           _close2(line.mirrored().mirrored().end, line.end))

    arc = Arc(center=(0.0, 0.0), start=(0.01, 0.0), end=(0.0, 0.01), direction=1)
    twice = arc.mirrored().mirrored()
    _check("arc mirrored twice is the original",
           _close2(twice.center, arc.center) and _close2(twice.start, arc.start)
           and _close2(twice.end, arc.end) and twice.direction == arc.direction)

    # --- the arc direction rule --------------------------------------------
    print()
    print("arc direction:")

    m = arc.mirrored()
    _check("source quarter arc sweeps CCW (+90 deg)",
           abs(math.degrees(arc.swept_angle()) - 90.0) < 1e-9)
    _check("mirrored quarter arc sweeps CW (-90 deg)",
           abs(math.degrees(m.swept_angle()) + 90.0) < 1e-9)
    _check("mirrored arc keeps its radius",
           abs(m.radius - arc.radius) < 1e-12)

    # The decisive test: every sampled point on the mirrored arc must coincide with
    # the reflection of the corresponding point on the source arc.
    worst = 0.0
    for i in range(101):
        t = i / 100.0
        want = plane.reflect_point(_to_model(frame, arc.sample(t)))
        got = _to_model(mframe, m.sample(t))
        worst = max(worst, (want - got).length())
    _check("mirrored arc traces the reflected path pointwise (err=%.2e)" % worst,
           worst < 1e-12)

    # --- and the bug it guards against -------------------------------------
    print()
    print("the bug this rule prevents:")

    naive = Arc(center=mirror_uv(*arc.center), start=mirror_uv(*arc.start),
                end=mirror_uv(*arc.end), direction=arc.direction)  # flag NOT flipped
    _check("without the flip the arc takes the 270 deg complement",
           abs(math.degrees(naive.swept_angle()) - 270.0) < 1e-9)

    naive_worst = 0.0
    for i in range(101):
        t = i / 100.0
        want = plane.reflect_point(_to_model(frame, arc.sample(t)))
        got = _to_model(mframe, naive.sample(t))
        naive_worst = max(naive_worst, (want - got).length())
    _check("naive version is geometrically wrong by %.1f mm" % (naive_worst * 1000),
           naive_worst > 1e-3)

    # --- circles are exempt -------------------------------------------------
    print()
    print("circles:")

    circle = Arc(center=(0.0, 0.0), start=(0.01, 0.0), end=(0.01, 0.0),
                 direction=1, is_circle=True)
    _check("full circle keeps its direction flag",
           circle.mirrored().direction == circle.direction)
    _check("full circle keeps its radius",
           abs(circle.mirrored().radius - circle.radius) < 1e-12)

    # --- lines --------------------------------------------------------------
    print()
    print("lines:")

    worst = 0.0
    ml = line.mirrored()
    for i in range(51):
        t = i / 50.0
        want = plane.reflect_point(_to_model(frame, line.sample(t)))
        got = _to_model(mframe, ml.sample(t))
        worst = max(worst, (want - got).length())
    _check("mirrored line traces the reflected path pointwise (err=%.2e)" % worst,
           worst < 1e-12)

    # --- a closed profile keeps its area ------------------------------------
    print()
    print("profile integrity:")

    profile = [(0.0, 0.0), (0.04, 0.0), (0.04, 0.02), (0.0, 0.02)]
    mirrored_profile = [mirror_uv(u, v) for (u, v) in profile]
    _check("closed profile area magnitude is preserved",
           abs(abs(_area(profile)) - abs(_area(mirrored_profile))) < 1e-15)
    _check("closed profile winding reverses (expected under reflection)",
           _area(profile) * _area(mirrored_profile) < 0)

    print()
    print("all tests passed")


def _to_model(frame: SketchFrame, uv) -> Vec3:
    return frame.to_model(uv[0], uv[1])


def _close2(a, b, tol=1e-12) -> bool:
    return abs(a[0] - b[0]) <= tol and abs(a[1] - b[1]) <= tol


def _area(pts: List[Tuple[float, float]]) -> float:
    """Signed polygon area by the shoelace formula."""
    total = 0.0
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        total += x0 * y1 - x1 * y0
    return total / 2.0


if __name__ == "__main__":
    run_tests()
