using System;
using System.Collections.Generic;
using System.Linq;
using TrueMirror.Core.Geometry;

namespace TrueMirror.Core.Resolver
{
    public enum SurfaceKind
    {
        Unknown = 0,
        Plane,
        Cylinder,
        Cone,
        Sphere,
        Torus,
        Bspline
    }

    /// <summary>
    /// A mirror-invariant fingerprint used to match a source face or edge to its counterpart
    /// in the part being rebuilt.
    ///
    /// This exists because SOLIDWORKS persistent reference IDs
    /// (IModelDocExtension::GetPersistReference3) identify an object within ONE model
    /// document. A byte array taken from the source part is meaningless in a newly created
    /// one, so references such as "fillet these four edges" or "cut up to this face" have to
    /// be re-established geometrically.
    ///
    /// Area and radius are invariant under reflection; only position moves.
    /// </summary>
    public sealed class FaceSignature
    {
        public SurfaceKind Kind { get; set; }

        public Vec3 Centroid { get; set; }

        /// <summary>Face area in m^2, or edge length in m for an edge signature.</summary>
        public double Measure { get; set; }

        /// <summary>Radius in m for cylinders, cones and spheres. Zero when not applicable.</summary>
        public double Radius { get; set; }

        /// <summary>Opaque handle back to the live SOLIDWORKS entity this describes.</summary>
        public object Tag { get; set; }

        public FaceSignature Reflected(MirrorPlane plane) => new FaceSignature
        {
            Kind = Kind,
            Centroid = plane.ReflectPoint(Centroid),
            Measure = Measure,
            Radius = Radius,
            Tag = Tag
        };

        public bool Matches(FaceSignature other, double linearTolerance, double relativeTolerance)
        {
            if (other == null) return false;
            if (Kind != other.Kind) return false;
            if (!Centroid.IsClose(other.Centroid, linearTolerance)) return false;
            if (!RelativelyClose(Measure, other.Measure, relativeTolerance)) return false;
            if (!RelativelyClose(Radius, other.Radius, relativeTolerance)) return false;
            return true;
        }

        private static bool RelativelyClose(double a, double b, double relativeTolerance)
        {
            var scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-12);
            return Math.Abs(a - b) / scale <= relativeTolerance;
        }

        public override string ToString()
            => $"{Kind} at {Centroid} measure={Measure:G6} r={Radius:G6}";
    }

    public enum ResolutionStatus
    {
        Resolved,
        NotFound,

        /// <summary>
        /// More than one candidate matched. On a symmetric part two faces can be genuinely
        /// indistinguishable. Guessing yields a silently wrong model, so this is a
        /// first-class outcome that triggers fallback, not an error to smooth over.
        /// </summary>
        Ambiguous
    }

    public sealed class ResolutionResult
    {
        public ResolutionStatus Status { get; }
        public FaceSignature Match { get; }
        public int CandidateCount { get; }

        private ResolutionResult(ResolutionStatus status, FaceSignature match, int candidateCount)
        {
            Status = status;
            Match = match;
            CandidateCount = candidateCount;
        }

        public bool IsResolved => Status == ResolutionStatus.Resolved;

        public static ResolutionResult Resolved(FaceSignature match)
            => new ResolutionResult(ResolutionStatus.Resolved, match, 1);

        public static ResolutionResult NotFound()
            => new ResolutionResult(ResolutionStatus.NotFound, null, 0);

        public static ResolutionResult Ambiguous(int count)
            => new ResolutionResult(ResolutionStatus.Ambiguous, null, count);

        public string Describe()
        {
            switch (Status)
            {
                case ResolutionStatus.Resolved: return "resolved";
                case ResolutionStatus.NotFound: return "no matching entity in the rebuilt body";
                default: return $"ambiguous - {CandidateCount} indistinguishable candidates";
            }
        }
    }

    /// <summary>
    /// Matches source entities onto the in-progress mirrored body.
    ///
    /// Relies on an invariant maintained by the transcriber: before rebuilding feature N,
    /// the target body is exactly the mirror image of the source body at state N-1. If that
    /// invariant is broken - by rebuilding out of order, or by skipping a feature without
    /// recording it - resolution silently degrades.
    /// </summary>
    public sealed class EntityResolver
    {
        private readonly MirrorPlane m_Plane;
        private readonly double m_LinearTolerance;
        private readonly double m_RelativeTolerance;

        public EntityResolver(MirrorPlane plane,
            double linearTolerance = 1e-7,
            double relativeTolerance = 1e-6)
        {
            m_Plane = plane ?? throw new ArgumentNullException(nameof(plane));
            m_LinearTolerance = linearTolerance;
            m_RelativeTolerance = relativeTolerance;
        }

        public ResolutionResult Resolve(FaceSignature source, IReadOnlyList<FaceSignature> candidates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (candidates == null || candidates.Count == 0)
            {
                return ResolutionResult.NotFound();
            }

            var target = source.Reflected(m_Plane);

            var hits = candidates
                .Where(c => target.Matches(c, m_LinearTolerance, m_RelativeTolerance))
                .ToList();

            if (hits.Count == 1) return ResolutionResult.Resolved(hits[0]);
            if (hits.Count == 0) return ResolutionResult.NotFound();
            return ResolutionResult.Ambiguous(hits.Count);
        }

        /// <summary>
        /// Resolves a set of references, e.g. the edge list of a fillet. All-or-nothing:
        /// a partially resolved fillet is not a fillet, it is a different feature.
        /// </summary>
        public bool TryResolveAll(
            IReadOnlyList<FaceSignature> sources,
            IReadOnlyList<FaceSignature> candidates,
            out List<FaceSignature> matches,
            out string failureReason)
        {
            matches = new List<FaceSignature>();
            failureReason = null;

            for (var i = 0; i < sources.Count; i++)
            {
                var result = Resolve(sources[i], candidates);
                if (!result.IsResolved)
                {
                    failureReason =
                        $"reference {i + 1} of {sources.Count} ({sources[i].Kind}): {result.Describe()}";
                    matches = null;
                    return false;
                }

                matches.Add(result.Match);
            }

            return true;
        }
    }
}
