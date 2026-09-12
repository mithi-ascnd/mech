using TrueMirror.Core.Geometry;
using TrueMirror.Core.Resolver;
using Xunit;

namespace TrueMirror.Tests
{
    /// <summary>
    /// The resolver is the part that replaces SOLIDWORKS persistent reference IDs, which
    /// cannot cross a document boundary. Its most important property is NOT that it finds
    /// matches - it is that it refuses to guess when a match is ambiguous.
    ///
    /// On a symmetric part two faces can be genuinely indistinguishable. Picking one
    /// produces a model that is wrong in a way nobody notices until it is machined.
    /// </summary>
    public class EntityResolverTests
    {
        private static readonly MirrorPlane Yz = new MirrorPlane(Vec3.Zero, Vec3.UnitX);

        private static EntityResolver Resolver => new EntityResolver(Yz);

        private static FaceSignature Source() => new FaceSignature
        {
            Kind = SurfaceKind.Cylinder,
            Centroid = new Vec3(10, 5, 0),
            Measure = 0.004,
            Radius = 0.003
        };

        [Fact]
        public void UniqueMatchResolves()
        {
            var source = Source();
            var expected = source.Reflected(Yz);

            var decoys = new[]
            {
                expected,
                new FaceSignature { Kind = SurfaceKind.Cylinder, Centroid = new Vec3(-10, 5, 9), Measure = 0.004, Radius = 0.003 },
                new FaceSignature { Kind = SurfaceKind.Plane,    Centroid = new Vec3(-10, 5, 0), Measure = 0.004, Radius = 0.003 },
                new FaceSignature { Kind = SurfaceKind.Cylinder, Centroid = new Vec3(-10, 5, 0), Measure = 0.004, Radius = 0.005 }
            };

            var result = Resolver.Resolve(source, decoys);

            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.Same(expected, result.Match);
        }

        [Fact]
        public void WrongKindDoesNotMatchEvenAtTheRightPlace()
        {
            var candidates = new[]
            {
                new FaceSignature { Kind = SurfaceKind.Plane, Centroid = new Vec3(-10, 5, 0), Measure = 0.004, Radius = 0.003 }
            };

            Assert.Equal(ResolutionStatus.NotFound, Resolver.Resolve(Source(), candidates).Status);
        }

        [Fact]
        public void WrongRadiusDoesNotMatch()
        {
            var candidates = new[]
            {
                new FaceSignature { Kind = SurfaceKind.Cylinder, Centroid = new Vec3(-10, 5, 0), Measure = 0.004, Radius = 0.005 }
            };

            Assert.Equal(ResolutionStatus.NotFound, Resolver.Resolve(Source(), candidates).Status);
        }

        [Fact]
        public void IndistinguishableTwinsReportAmbiguousRatherThanGuessing()
        {
            var source = Source();

            var twins = new[]
            {
                source.Reflected(Yz),
                new FaceSignature { Kind = SurfaceKind.Cylinder, Centroid = new Vec3(-10, 5, 0), Measure = 0.004, Radius = 0.003 }
            };

            var result = Resolver.Resolve(source, twins);

            Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
            Assert.Null(result.Match);
            Assert.Equal(2, result.CandidateCount);
        }

        [Fact]
        public void EmptyCandidateSetReportsNotFound()
            => Assert.Equal(ResolutionStatus.NotFound,
                Resolver.Resolve(Source(), new FaceSignature[0]).Status);

        [Fact]
        public void NullCandidateSetReportsNotFoundRatherThanThrowing()
            => Assert.Equal(ResolutionStatus.NotFound, Resolver.Resolve(Source(), null).Status);

        [Fact]
        public void ReflectionPreservesMeasureAndRadius()
        {
            var source = Source();
            var reflected = source.Reflected(Yz);

            Assert.Equal(source.Measure, reflected.Measure, 12);
            Assert.Equal(source.Radius, reflected.Radius, 12);
            Assert.Equal(source.Kind, reflected.Kind);
        }

        [Fact]
        public void TryResolveAllIsAllOrNothing()
        {
            // A partially resolved fillet is not a fillet, it is a different feature.
            var a = Source();
            var b = new FaceSignature
            {
                Kind = SurfaceKind.Plane,
                Centroid = new Vec3(1, 1, 1),
                Measure = 0.002,
                Radius = 0
            };

            var candidates = new[] { a.Reflected(Yz) };   // b has no counterpart

            var ok = Resolver.TryResolveAll(new[] { a, b }, candidates,
                out var matches, out var reason);

            Assert.False(ok);
            Assert.Null(matches);
            Assert.Contains("2 of 2", reason);
        }

        [Fact]
        public void TryResolveAllSucceedsWhenEveryReferenceResolves()
        {
            var a = Source();
            var b = new FaceSignature
            {
                Kind = SurfaceKind.Plane,
                Centroid = new Vec3(1, 1, 1),
                Measure = 0.002,
                Radius = 0
            };

            var candidates = new[] { a.Reflected(Yz), b.Reflected(Yz) };

            var ok = Resolver.TryResolveAll(new[] { a, b }, candidates,
                out var matches, out _);

            Assert.True(ok);
            Assert.Equal(2, matches.Count);
        }
    }
}
