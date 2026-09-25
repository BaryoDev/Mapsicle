using System.Threading;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A deep acyclic chain stops short of the end of the stack instead of killing the process.
    /// </summary>
    /// <remarks>
    /// The only guard was a fixed depth of 10,000, and a 1 MB thread overflowed at about 2,200. A
    /// StackOverflowException cannot be caught, so on regression this file aborts the test host rather
    /// than failing an assertion. That is the failure it exists to catch.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class StackHeadroomTests
    {
        public class ShNode { public int Value { get; set; } public ShNode? Next { get; set; } }
        public class ShNodeDto { public int Value { get; set; } public ShNodeDto? Next { get; set; } }

        private static ShNode Chain(int length)
        {
            ShNode? head = null;
            for (var i = length - 1; i >= 0; i--) head = new ShNode { Value = i, Next = head };
            return head!;
        }

        private static int MappedLength(ShNode source, int stackBytes)
        {
            var length = 0;
            var thread = new Thread(() =>
            {
                for (var at = source.MapTo<ShNodeDto>(); at != null; at = at.Next) length++;
            }, stackBytes);
            thread.Start();
            thread.Join();
            return length;
        }

        [Fact]
        public void AChainDeeperThanTheStackStopsInsteadOfOverflowing()
        {
            Mapper.ClearCache();

            var length = MappedLength(Chain(20_000), 1024 * 1024);

            Assert.InRange(length, Mapper.MaxDepth + 1, 19_999);
        }

        [Fact]
        public void AChainTheStackCanHoldStillMapsWhole()
        {
            Mapper.ClearCache();

            Assert.Equal(500, MappedLength(Chain(500), 1024 * 1024));
        }
    }
}
