using System.Threading;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    /// <summary>
    /// The fluent lane stops a deep acyclic chain short of the end of the stack, like the static one.
    /// </summary>
    /// <remarks>
    /// A 2,200 deep chain overflowed a default thread through the fluent mapper. On regression this
    /// file aborts the test host rather than failing an assertion.
    /// </remarks>
    public class FluentStackHeadroomTests
    {
        public class FshNode { public int Value { get; set; } public FshNode? Next { get; set; } }
        public class FshNodeDto { public int Value { get; set; } public FshNodeDto? Next { get; set; } }

        private static int MappedLength(int chainLength)
        {
            FshNode? head = null;
            for (var i = chainLength - 1; i >= 0; i--) head = new FshNode { Value = i, Next = head };

            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var length = 0;
            var thread = new Thread(() =>
            {
                for (var at = mapper.Map<FshNodeDto>(head); at != null; at = at.Next) length++;
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            return length;
        }

        [Fact]
        public void AChainDeeperThanTheStackStopsInsteadOfOverflowing() =>
            Assert.InRange(MappedLength(20_000), 1, 19_999);

        [Fact]
        public void AChainTheStackCanHoldStillMapsWhole() =>
            Assert.Equal(500, MappedLength(500));
    }
}
