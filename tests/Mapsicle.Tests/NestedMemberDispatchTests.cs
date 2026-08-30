using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// The nested member call resolves once per call site and remembers. These are the cases where
    /// remembering could be wrong.
    /// </summary>
    /// <remarks>
    /// Each nested reference used to cost more than the whole of the rest of a map, because it went
    /// back through the public entry point per member per item and paid two dictionary lookups
    /// doing it. Caching the answer at the call site removes that, and introduces exactly three
    /// ways to be wrong: a member whose runtime type changes, a cache that was cleared underneath
    /// it, and a cycle that must still be stopped. One test each.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class NestedMemberDispatchTests
    {
        [Fact]
        public void ANestedMemberHoldingADerivedType_IsMappedAsThatType()
        {
            Mapper.ClearCache();

            var withBase = ((object)new NdHolder { Item = new NdBase { Name = "base" } }).MapTo<NdHolderDto>();
            Assert.Equal("base", withBase!.Item?.Name);

            // Same call site, different runtime type. The holder remembered NdBase; this must not
            // be mapped through NdBase's delegate, whose first instruction is a cast.
            var withDerived = ((object)new NdHolder { Item = new NdDerived { Name = "derived", Extra = "x" } }).MapTo<NdHolderDto>();
            Assert.Equal("derived", withDerived!.Item?.Name);
        }

        [Fact]
        public void ANestedMemberAlternatingBetweenTypes_StaysCorrect()
        {
            // The pathological shape for a one-entry cache: it never gets to keep an answer.
            // Correctness must not depend on the cache hitting.
            Mapper.ClearCache();

            for (int i = 0; i < 20; i++)
            {
                var item = i % 2 == 0
                    ? (NdBase)new NdBase { Name = "even" }
                    : new NdDerived { Name = "odd", Extra = "e" };

                var dto = ((object)new NdHolder { Item = item }).MapTo<NdHolderDto>();
                Assert.Equal(i % 2 == 0 ? "even" : "odd", dto!.Item?.Name);
            }
        }

        [Fact]
        public void AParentDelegateOutlivingClearCache_DoesNotKeepMappingThroughTheOldChild()
        {
            // Going through the public API cannot show this. ClearCache drops every parent
            // delegate, so the next call compiles a new parent with a new holder in it and the
            // stale one is unreachable. The version of this test that mapped, cleared and mapped
            // again passed with the invalidation removed.
            //
            // What the holder actually promises is narrower: it must not answer from a generation
            // that has been cleared. To see that, hold a parent delegate across the clear the way
            // a caller who cached one would, then change what the child pair resolves to. A holder
            // that re-resolves picks up the change. One that does not keeps returning the old map.
            Mapper.ClearCache();

            var source = new NdClearHolder { Item = new NdClearInner { Name = "original" } };
            Assert.Equal("original", ((object)source).MapTo<NdClearHolderDto>()!.Item?.Name);

            var cache = MapToCache();
            var parent = (Func<object, NdClearHolderDto>)cache[(typeof(NdClearHolder), typeof(NdClearHolderDto))];

            Mapper.ClearCache();

            Func<object, NdClearInnerDto> poisoned = _ => new NdClearInnerDto { Name = "POISONED" };
            cache[(typeof(NdClearInner), typeof(NdClearInnerDto))] = poisoned;

            Assert.Equal("POISONED", parent(source).Item?.Name);
        }

        private static System.Collections.Concurrent.ConcurrentDictionary<(Type, Type), Delegate> MapToCache()
        {
            var field = typeof(Mapper).GetField("_mapToCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            return (System.Collections.Concurrent.ConcurrentDictionary<(Type, Type), Delegate>)field!.GetValue(null)!;
        }

        [Fact]
        public void ACycleThroughANestedMember_StillStopsAtMaxDepth()
        {
            // Depth tracking is now decided from the remembered answer rather than looked up each
            // time. If that answer were wrong, this would not return.
            Mapper.ClearCache();

            var a = new NdNode { Name = "a" };
            var b = new NdNode { Name = "b" };
            a.Next = b;
            b.Next = a;

            var first = ((object)a).MapTo<NdNodeDto>();
            var second = ((object)a).MapTo<NdNodeDto>();

            Assert.Equal("a", first!.Name);
            Assert.Equal("a", second!.Name);
        }

        [Fact]
        public void UnderTheBoundedCache_NestedMembersStillMap()
        {
            // The holder deliberately does not cache when UseLruCache is on, because that mode
            // exists to bound memory. The mapping still has to be correct with the cache off.
            var previous = Mapper.UseLruCache;
            Mapper.UseLruCache = true;
            try
            {
                Mapper.ClearCache();
                var dto = ((object)new NdLruHolder { Item = new NdLruInner { Name = "bounded" } }).MapTo<NdLruHolderDto>();
                Assert.Equal("bounded", dto!.Item?.Name);
            }
            finally
            {
                Mapper.UseLruCache = previous;
                Mapper.ClearCache();
            }
        }

        [Fact]
        public void ManyThreadsResolvingTheSameCallSite_AllGetTheRightAnswer()
        {
            // Two threads racing to resolve compute the same answer, so the loser overwriting the
            // winner is harmless. That is only true if neither can observe a half-written pair.
            Mapper.ClearCache();

            var sources = Enumerable.Range(0, 200)
                .Select(i => new NdRaceHolder { Item = new NdRaceInner { Name = "n" + i } })
                .ToArray();

            Parallel.For(0, sources.Length, i =>
            {
                var dto = ((object)sources[i]).MapTo<NdRaceHolderDto>();
                Assert.Equal("n" + i, dto!.Item?.Name);
            });
        }

        [Fact]
        public void ANullNestedMember_StaysNull()
        {
            Mapper.ClearCache();
            var dto = ((object)new NdHolder { Item = null }).MapTo<NdHolderDto>();
            Assert.Null(dto!.Item);
        }

        public class NdBase { public string? Name { get; set; } }
        public class NdDerived : NdBase { public string? Extra { get; set; } }
        public class NdHolder { public NdBase? Item { get; set; } }
        public class NdItemDto { public string? Name { get; set; } }
        public class NdHolderDto { public NdItemDto? Item { get; set; } }

        public class NdClearInner { public string? Name { get; set; } }
        public class NdClearInnerDto { public string? Name { get; set; } }
        public class NdClearHolder { public NdClearInner? Item { get; set; } }
        public class NdClearHolderDto { public NdClearInnerDto? Item { get; set; } }

        public class NdNode { public string? Name { get; set; } public NdNode? Next { get; set; } }
        public class NdNodeDto { public string? Name { get; set; } public NdNodeDto? Next { get; set; } }

        public class NdLruInner { public string? Name { get; set; } }
        public class NdLruInnerDto { public string? Name { get; set; } }
        public class NdLruHolder { public NdLruInner? Item { get; set; } }
        public class NdLruHolderDto { public NdLruInnerDto? Item { get; set; } }

        public class NdRaceInner { public string? Name { get; set; } }
        public class NdRaceInnerDto { public string? Name { get; set; } }
        public class NdRaceHolder { public NdRaceInner? Item { get; set; } }
        public class NdRaceHolderDto { public NdRaceInnerDto? Item { get; set; } }
    }
}
