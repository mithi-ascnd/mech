"""
Reference implementation of TrueMirror's mirror math, in Python, so the algorithm
can be executed and tested on any machine before it is ported to C#.

This is NOT shipped with the add-in. It exists because the C# lives on Windows
behind a SOLIDWORKS licence, and the geometry is the part most likely to be
subtly, silently wrong. Getting a reflected arc's direction backwards produces a
part that looks right and is wrong.

The C# port lives in src/TrueMirror.Core/Geometry/ and must stay in step with this.
Run: python3 tools/mirror_math_reference.py
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Tuple

TOL = 1e-12


# ---------------------------------------------------------------------------
# Vectors
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class Vec3:
    x: float
    y: float
    z: float

    def __add__(self, o: "Vec3") -> "Vec3":
        return Vec3(self.x + o.x, self.y + o.y, self.z + o.z)

    def __sub__(self, o: "Vec3") -> "Vec3":
        return Vec3(self.x - o.x, self.y - o.y, self.z - o.z)

    def scale(self, k: float) -> "Vec3":
        return Vec3(self.x * k, self.y * k, self.z * k)

    def dot(self, o: "Vec3") -> float:
        return self.x * o.x + self.y * o.y + self.z * o.z

    def cross(self, o: "Vec3") -> "Vec3":
        return Vec3(
            self.y * o.z - self.z * o.y,
            self.z * o.x - self.x * o.z,
            self.x * o.y - self.y * o.x,
        )

    def length(self) -> float:
        return math.sqrt(self.dot(self))

    def normalized(self) -> "Vec3":
        n = self.length()
        if n < TOL:
            raise ValueError("cannot normalize a zero-length vector")
        return self.scale(1.0 / n)

    def close(self, o: "Vec3", tol: float = 1e-9) -> bool:
        return (self - o).length() <= tol


# ---------------------------------------------------------------------------
# Mirror plane
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class MirrorPlane:
    """A plane defined by a point on it and a unit normal."""
    point: Vec3
    normal: Vec3

    @staticmethod
    def create(point: Vec3, normal: Vec3) -> "MirrorPlane":
        return MirrorPlane(point, normal.normalized())

    def reflect_point(self, p: Vec3) -> Vec3:
        """p' = p - 2((p - P) . n) n"""
        d = (p - self.point).dot(self.normal)
        return p - self.normal.scale(2.0 * d)

    def reflect_direction(self, v: Vec3) -> Vec3:
        """Directions ignore the plane's offset: v' = v - 2(v . n) n"""
        return v - self.normal.scale(2.0 * v.dot(self.normal))

    def signed_distance(self, p: Vec3) -> float:
        return (p - self.point).dot(self.normal)


# ---------------------------------------------------------------------------
# Sketch frames
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class SketchFrame:
    """
    A sketch's local coordinate system in model space.

    SOLIDWORKS sketch frames are right-handed: x_axis cross y_axis == normal.
    """
    origin: Vec3
    x_axis: Vec3
    y_axis: Vec3

    @property
    def normal(self) -> Vec3:
        return self.x_axis.cross(self.y_axis)

    def is_right_handed(self, tol: float = 1e-9) -> bool:
        return self.normal.dot(self.x_axis.cross(self.y_axis)) > 0 and \
            abs(self.normal.length() - 1.0) < tol

    def to_model(self, u: float, v: float) -> Vec3:
        return self.origin + self.x_axis.scale(u) + self.y_axis.scale(v)

    def to_sketch(self, p: Vec3) -> Tuple[float, float]:
        d = p - self.origin
        return (d.dot(self.x_axis), d.dot(self.y_axis))


# ---------------------------------------------------------------------------
# THE LOAD-BEARING RESULT
# ---------------------------------------------------------------------------

def mirror_frame(frame: SketchFrame, plane: MirrorPlane) -> SketchFrame:
    """
    Reflect a sketch frame and return a RIGHT-HANDED frame on the mirrored plane.

    This is the crux of the whole tool, so the reasoning is spelled out:

    Reflection has determinant -1. If we naively reflect all three axes we get
        (R(x), R(y), R(n))
    and because R(x) cross R(y) == -R(x cross y) == -R(n), that frame is
    LEFT-handed. SOLIDWORKS will not accept it.

    To restore right-handedness we negate exactly one in-plane axis. We choose y.
    The consequence is not cosmetic and must be propagated everywhere: a sketch
    point at local (u, v) in the source lands at local (u, -v) in the mirror.

    That single sign flip is why arcs reverse sweep direction, why angles negate,
    and why draft angles change sign. Everything downstream derives from here.
    """
    return SketchFrame(
        origin=plane.reflect_point(frame.origin),
        x_axis=plane.reflect_direction(frame.x_axis),
        y_axis=plane.reflect_direction(frame.y_axis).scale(-1.0),
    )


def mirror_uv(u: float, v: float) -> Tuple[float, float]:
    """
    Map a sketch-local coordinate into the mirrored frame produced by
    mirror_frame(). Corollary of the y-negation above.
    """
    return (u, -v)


def mirror_sketch_angle(angle_rad: float) -> float:
    """
    A direction angle measured CCW from local +x becomes -angle under (u, v) ->
    (u, -v). Normalised to [0, 2pi).
    """
    return (-angle_rad) % (2.0 * math.pi)


# ---------------------------------------------------------------------------
# Entity signatures (the v0.2 resolver)
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class FaceSignature:
    """
    A mirror-invariant fingerprint for matching a source face to its counterpart
    in the rebuilt body. Persistent reference IDs are per-document and cannot
    cross, so matching must be geometric.
    """
    kind: str          # "plane", "cylinder", "cone", "sphere", "other"
    centroid: Vec3
    area: float
    radius: float      # 0.0 when not applicable

    def reflected(self, plane: MirrorPlane) -> "FaceSignature":
        # Area and radius are invariant under reflection; only position moves.
        return FaceSignature(
            kind=self.kind,
            centroid=plane.reflect_point(self.centroid),
            area=self.area,
            radius=self.radius,
        )

    def matches(self, other: "FaceSignature", lin_tol: float, rel_tol: float) -> bool:
        if self.kind != other.kind:
            return False
        if not self.centroid.close(other.centroid, lin_tol):
            return False
        if not _rel_close(self.area, other.area, rel_tol):
            return False
        if not _rel_close(self.radius, other.radius, rel_tol):
            return False
        return True


def _rel_close(a: float, b: float, rel_tol: float) -> bool:
    scale = max(abs(a), abs(b), 1e-12)
    return abs(a - b) / scale <= rel_tol


def resolve_face(source: FaceSignature, candidates, plane: MirrorPlane,
                 lin_tol: float = 1e-7, rel_tol: float = 1e-6):
    """
    Returns (match, status) where status is one of "resolved", "not-found",
    "ambiguous".

    Ambiguity is NOT an error to paper over. On a symmetric part two faces can be
    genuinely indistinguishable, and guessing produces a silently wrong model.
    The caller falls back instead.
    """
    target = source.reflected(plane)
    hits = [c for c in candidates if target.matches(c, lin_tol, rel_tol)]

    if len(hits) == 1:
        return hits[0], "resolved"
    if not hits:
        return None, "not-found"
    return None, "ambiguous"


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def _check(name, cond):
    if not cond:
        raise AssertionError("FAIL: " + name)
    print("  ok   " + name)


def run_tests() -> None:
    print("mirror math reference tests")
    print()

    X = Vec3(1, 0, 0)
    Y = Vec3(0, 1, 0)
    Z = Vec3(0, 0, 1)
    O = Vec3(0, 0, 0)

    # --- reflection basics -------------------------------------------------
    print("reflection:")
    right = MirrorPlane.create(O, X)  # the YZ plane

    _check("point reflects across YZ",
           right.reflect_point(Vec3(3, 2, 1)).close(Vec3(-3, 2, 1)))
    _check("point on plane is fixed",
           right.reflect_point(Vec3(0, 5, 7)).close(Vec3(0, 5, 7)))
    _check("reflection is an involution",
           right.reflect_point(right.reflect_point(Vec3(3, 2, 1))).close(Vec3(3, 2, 1)))
    _check("direction ignores offset",
           MirrorPlane.create(Vec3(10, 0, 0), X).reflect_direction(Vec3(1, 2, 3))
           .close(Vec3(-1, 2, 3)))

    offset = MirrorPlane.create(Vec3(5, 0, 0), X)
    _check("offset plane reflects about x=5",
           offset.reflect_point(Vec3(7, 1, 1)).close(Vec3(3, 1, 1)))

    _check("distance is preserved",
           abs((right.reflect_point(Vec3(1, 2, 3)) - right.reflect_point(Vec3(4, 6, 3))).length()
               - (Vec3(1, 2, 3) - Vec3(4, 6, 3)).length()) < 1e-12)

    # --- the handedness result --------------------------------------------
    print()
    print("handedness:")

    front = SketchFrame(origin=O, x_axis=X, y_axis=Y)   # normal = +Z
    _check("source frame is right-handed", front.normal.close(Z))

    naive_y = right.reflect_direction(front.y_axis)
    naive_n = right.reflect_direction(front.x_axis).cross(naive_y)
    _check("naive reflection of all axes is LEFT-handed (the trap)",
           naive_n.close(right.reflect_direction(front.normal).scale(-1.0)))

    m = mirror_frame(front, right)
    _check("mirror_frame restores right-handedness",
           m.normal.close(m.x_axis.cross(m.y_axis)))
    _check("mirrored normal is the reflected normal",
           m.normal.close(right.reflect_direction(front.normal)))

    # --- uv mapping consistency -------------------------------------------
    print()
    print("uv mapping:")

    tilted = SketchFrame(
        origin=Vec3(1, 2, 3),
        x_axis=Vec3(1, 1, 0).normalized(),
        y_axis=Vec3(-1, 1, 0).normalized(),
    )
    _check("tilted test frame is right-handed", tilted.normal.close(Z))

    plane2 = MirrorPlane.create(Vec3(2, 0, 0), X)
    tm = mirror_frame(tilted, plane2)

    worst = 0.0
    for (u, v) in [(0, 0), (1, 0), (0, 1), (3, -4), (-2.5, 7.25), (100, -0.001)]:
        want = plane2.reflect_point(tilted.to_model(u, v))
        mu, mv = mirror_uv(u, v)
        got = tm.to_model(mu, mv)
        worst = max(worst, (want - got).length())
    _check("mirror_uv agrees with reflecting through model space (err=%.2e)" % worst,
           worst < 1e-12)

    # --- arc direction ------------------------------------------------------
    print()
    print("arc direction:")

    start, end = 0.0, math.pi / 2          # CCW quarter arc in the source
    ms, me = mirror_sketch_angle(start), mirror_sketch_angle(end)
    _check("mirrored arc sweeps the other way",
           _sweep_ccw(start, end) > 0 and _sweep_ccw(ms, me) < 0)

    _check("angle mirroring is an involution",
           abs(mirror_sketch_angle(mirror_sketch_angle(1.234)) - 1.234) < 1e-12)

    # --- entity resolution --------------------------------------------------
    print()
    print("entity resolver:")

    src = FaceSignature("cylinder", Vec3(10, 5, 0), area=0.004, radius=0.003)
    good = src.reflected(right)
    decoy_far = FaceSignature("cylinder", Vec3(-10, 5, 9), area=0.004, radius=0.003)
    decoy_kind = FaceSignature("plane", Vec3(-10, 5, 0), area=0.004, radius=0.003)
    decoy_rad = FaceSignature("cylinder", Vec3(-10, 5, 0), area=0.004, radius=0.005)

    hit, status = resolve_face(src, [good, decoy_far, decoy_kind, decoy_rad], right)
    _check("unique match resolves", status == "resolved" and hit is good)

    _, status = resolve_face(src, [decoy_far, decoy_kind], right)
    _check("no match reports not-found", status == "not-found")

    twin = FaceSignature("cylinder", Vec3(-10, 5, 0), area=0.004, radius=0.003)
    _, status = resolve_face(src, [good, twin], right)
    _check("indistinguishable twins report ambiguous, never a guess",
           status == "ambiguous")

    _, status = resolve_face(src, [], right)
    _check("empty candidate set reports not-found", status == "not-found")

    print()
    print("all tests passed")


def _sweep_ccw(a: float, b: float) -> float:
    """Signed shortest sweep from a to b, positive CCW."""
    d = (b - a) % (2.0 * math.pi)
    return d if d <= math.pi else d - 2.0 * math.pi


if __name__ == "__main__":
    run_tests()
