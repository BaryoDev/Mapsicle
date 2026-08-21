using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Mapsicle.Performance.Tests
{
    /// <summary>
    /// Allocation budgets for warm mapping paths.
    ///
    /// These are the gate behind the word "high-performance" in the package description. They fail
    /// when a change puts reflection, boxing, a closure or a LINQ operator on a per-call path. They
    /// deliberately measure bytes rather than time: allocation is deterministic, wall-clock on a
    /// hosted runner is not.
    ///
    /// Budgets are the measured value plus headroom for a boxed field or two, not a round number
    /// pulled out of the air. If your change moves one, move it in the same commit and say why.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class AllocationBudgetTests
    {
        public class Source
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public bool IsActive { get; set; }
        }

        public class Dest
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public bool IsActive { get; set; }
        }

        public class Nested
        {
            public int Id { get; set; }
            public Address? Address { get; set; }
        }

        public class Address
        {
            public string City { get; set; } = "";
        }

        public class FlatDest
        {
            public int Id { get; set; }
            public string AddressCity { get; set; } = "";
        }

        private static Source NewSource() => new()
        {
            Id = 1,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            IsActive = true
        };

        /// <summary>
        /// Runs the action once to warm every cache, then measures allocation across
        /// <paramref name="iterations"/> calls on this thread only.
        /// </summary>
        private static long BytesPerCall(Action action, int iterations = 10_000)
        {
            action();
            action();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }
            var after = GC.GetAllocatedBytesForCurrentThread();

            return (after - before) / iterations;
        }

        [Fact]
        public void TypedMapTo_AllocatesOnlyTheDestination()
        {
            var source = NewSource();

            var bytes = BytesPerCall(() => source.MapTo<Source, Dest>());

            // Dest is 5 fields: object header + 3 references + int + bool, padded. Measured 48 B.
            // Anything above this budget means something other than the destination is being
            // allocated per call: a boxed value, a closure, a params array, a LINQ enumerator.
            Assert.True(bytes <= 64, $"MapTo<TSource,TDest> allocated {bytes} B/call, budget 64 B");
        }

        [Fact]
        public void UntypedMapTo_AllocatesOnlyTheDestination()
        {
            var source = NewSource();

            var bytes = BytesPerCall(() => source.MapTo<Dest>());

            Assert.True(bytes <= 64, $"MapTo<T>(object) allocated {bytes} B/call, budget 64 B");
        }

        [Fact]
        public void MapIntoExistingDestination_AllocatesNothing()
        {
            var source = NewSource();
            var destination = new Dest();

            var bytes = BytesPerCall(() => source.Map(destination));

            // Nothing is constructed here, so the only correct answer is zero.
            Assert.True(bytes == 0, $"Map(destination) allocated {bytes} B/call, budget 0 B");
        }

        [Fact]
        public void Flattening_AllocatesOnlyTheDestination()
        {
            var source = new Nested { Id = 1, Address = new Address { City = "Cebu" } };

            var bytes = BytesPerCall(() => source.MapTo<Nested, FlatDest>());

            // Measured 32 B. Flattening is compiled into the expression tree, so the null check on
            // the parent must not cost an allocation.
            Assert.True(bytes <= 48, $"Flattened MapTo allocated {bytes} B/call, budget 48 B");
        }

        [Fact]
        public void CollectionMapping_AllocatesListPlusItems()
        {
            var sources = Enumerable.Range(0, 100).Select(_ => NewSource()).ToList();

            var bytes = BytesPerCall(() => sources.MapTo<Source, Dest>(), iterations: 200);

            // 100 destinations plus one pre-sized List<Dest>. Measured 5,696 B.
            // A budget here catches a per-item closure or a re-created mapper delegate, both of
            // which would multiply this number rather than nudge it.
            Assert.True(bytes <= 7_000, $"Collection of 100 allocated {bytes} B/call, budget 7,000 B");
        }

        [Fact]
        public void WarmMapping_DoesNotGrowTheCache()
        {
            Mapper.ClearCache();
            var source = NewSource();

            _ = source.MapTo<Dest>();
            var afterFirst = Mapper.CacheInfo().Total;

            for (var i = 0; i < 1_000; i++)
            {
                _ = source.MapTo<Dest>();
            }

            // A change that keys the cache on something per-call (a new tuple, a boxed key, the
            // instance rather than the type) shows up here as unbounded growth.
            Assert.Equal(afterFirst, Mapper.CacheInfo().Total);
        }
        /// <summary>
        /// Mapsicle.Fluent's in-place map, which used to reflect on every call.
        /// </summary>
        /// <remarks>
        /// Measured at 616 B/call and roughly 33x the core before the property pairing was resolved
        /// once per type pair and the assignment compiled. Zero is now the correct number, so the
        /// budget is zero: this path writes into an object the caller already owns and has nothing
        /// legitimate to allocate. Any allocation here means the reflection came back.
        /// </remarks>
        [Fact]
        public void FluentInPlaceMap_AllocatesNothing()
        {
            var config = new Mapsicle.Fluent.MapperConfiguration(c => c.CreateMap<Source, Dest>());
            var mapper = config.CreateMapper();
            var source = NewSource();
            var destination = new Dest();

            var bytes = BytesPerCall(() => mapper.Map(source, destination));

            Assert.True(bytes == 0, $"Fluent Map(source, destination) allocated {bytes} B/call, budget 0 B");
        }

        /// <summary>
        /// The same pairing is resolved once, not once per call.
        /// </summary>
        [Fact]
        public void FluentInPlaceMap_ProducesTheSameResultOnEveryCall()
        {
            var config = new Mapsicle.Fluent.MapperConfiguration(c => c.CreateMap<Source, Dest>());
            var mapper = config.CreateMapper();
            var source = NewSource();

            var first = mapper.Map(source, new Dest());
            var hundredth = new Dest();
            for (var i = 0; i < 100; i++)
            {
                hundredth = mapper.Map(source, new Dest());
            }

            Assert.Equal(first.FirstName, hundredth.FirstName);
            Assert.Equal(first.Email, hundredth.Email);
            Assert.Equal(first.IsActive, hundredth.IsActive);
        }
    }
}
