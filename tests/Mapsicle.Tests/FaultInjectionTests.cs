using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// How the mapper behaves when something inside a mapping fails.
    /// </summary>
    /// <remarks>
    /// "It works" is easy to demonstrate. What a team needs before putting a library on a request
    /// path is what happens when it does not: which exception surfaces, whether it is wrapped,
    /// whether the destination is left half-written, and whether a failure poisons the cache for
    /// every later call.
    ///
    /// These pin the answers. Two of them are worth reading before use:
    ///
    ///   - exceptions from your property accessors propagate unwrapped, so a catch block written
    ///     for <c>InvalidOperationException</c> catches an <c>InvalidOperationException</c> and not
    ///     a <c>TargetInvocationException</c> wrapping one;
    ///   - <c>Map(destination)</c> is not atomic. A setter that throws part-way leaves the earlier
    ///     properties already written. Map into a fresh instance if you need all-or-nothing.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class FaultInjectionTests
    {
        [Fact]
        public void AThrowingSourceGetter_PropagatesUnwrapped()
        {
            Mapper.ClearCache();

            var ex = Assert.Throws<InvalidOperationException>(
                () => new FaultThrowingGetterSource().MapTo<FaultPlainDest>());

            Assert.Equal("getter exploded", ex.Message);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void AThrowingDestinationSetter_PropagatesUnwrapped()
        {
            Mapper.ClearCache();

            var ex = Assert.Throws<InvalidOperationException>(
                () => new FaultPlainSource { Ok = 1, Bad = "x" }.MapTo<FaultThrowingSetterDest>());

            Assert.Equal("setter exploded", ex.Message);
        }

        [Fact]
        public void AThrowingConstructor_PropagatesUnwrapped()
        {
            Mapper.ClearCache();

            var ex = Assert.Throws<InvalidOperationException>(
                () => new FaultPlainSource { Ok = 1 }.MapTo<FaultThrowingCtorDest>());

            Assert.Equal("ctor exploded", ex.Message);
        }

        /// <summary>
        /// In-place mapping is not transactional, and that is worth knowing rather than discovering.
        /// </summary>
        [Fact]
        public void InPlaceMap_LeavesPartialStateWhenASetterThrows()
        {
            Mapper.ClearCache();
            var destination = new FaultThrowingSetterDest();

            Assert.Throws<InvalidOperationException>(
                () => new FaultPlainSource { Ok = 7, Bad = "x" }.Map(destination));

            // Ok was written before Bad threw. Nothing rolls it back.
            Assert.Equal(7, destination.OkValue);
        }

        /// <summary>
        /// A failure must not poison the cache: the next call with good data has to succeed.
        /// </summary>
        [Fact]
        public void AFailedMapping_DoesNotBreakLaterMappingsOfTheSamePair()
        {
            Mapper.ClearCache();

            Assert.Throws<InvalidOperationException>(
                () => new FaultPlainSource { Ok = 1, Bad = "boom" }.MapTo<FaultThrowingSetterDest>());

            // Same type pair, and the delegate compiled during the failed call is now cached.
            var ex = Assert.Throws<InvalidOperationException>(
                () => new FaultPlainSource { Ok = 2, Bad = "boom" }.MapTo<FaultThrowingSetterDest>());
            Assert.Equal("setter exploded", ex.Message);

            // An unrelated pair still maps, so the cache is not left in a broken state.
            var ok = new FaultPlainSource { Ok = 5, Bad = "fine" }.MapTo<FaultPlainDest>();
            Assert.Equal(5, ok!.Ok);
        }

        [Fact]
        public void AThrowingItem_DoesNotSilentlyTruncateACollection()
        {
            Mapper.ClearCache();

            var items = new List<FaultMaybeThrows>
            {
                new FaultMaybeThrows { Explode = false },
                new FaultMaybeThrows { Explode = true },
                new FaultMaybeThrows { Explode = false },
            };

            // The whole call fails rather than returning two of three items. A partial collection
            // presented as complete would be the worse outcome.
            Assert.Throws<InvalidOperationException>(() => items.MapTo<FaultPlainDest>());
        }

        [Fact]
        public void ADisposedMapperInstance_ThrowsObjectDisposedException()
        {
            var mapper = MapperFactory.Create();
            mapper.Dispose();

            Assert.Throws<ObjectDisposedException>(() => mapper.MapTo<FaultPlainDest>(new FaultPlainSource()));
            Assert.Throws<ObjectDisposedException>(() => mapper.ClearCache());
            Assert.Throws<ObjectDisposedException>(() => mapper.CacheInfo());
        }

        [Fact]
        public void DisposingTwice_IsNotAnError()
        {
            var mapper = MapperFactory.Create();
            mapper.Dispose();

            var second = Record.Exception(() => mapper.Dispose());

            Assert.Null(second);
        }

        /// <summary>
        /// Clearing the cache while other threads are mapping is a real production shape: a
        /// configuration reload on one thread, traffic on the others. It must not throw or return
        /// wrong data.
        /// </summary>
        [Fact]
        public async Task ClearingTheCacheDuringConcurrentMapping_StaysCorrect()
        {
            Mapper.ClearCache();

            var failures = new List<string>();
            var stop = false;

            var clearer = Task.Run(() =>
            {
                while (!Volatile.Read(ref stop))
                {
                    Mapper.ClearCache();
                    Thread.Sleep(1);
                }
            });

            var mappers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 2_000; i++)
                {
                    var dest = new FaultPlainSource { Ok = i, Bad = "v" }.MapTo<FaultPlainDest>();
                    if (dest is null || dest.Ok != i)
                    {
                        lock (failures) failures.Add($"expected {i}, got {dest?.Ok.ToString() ?? "null"}");
                        return;
                    }
                }
            })).ToArray();

            try
            {
                await Task.WhenAll(mappers);
            }
            finally
            {
                // In a finally because Task.WhenAll rethrows. Without it, a throwing mapper task
                // leaves the clearer looping past the end of this test, calling Mapper.ClearCache()
                // underneath whatever runs next. A background task that outlives its test is a
                // source of failures nobody can attribute.
                Volatile.Write(ref stop, true);
                await clearer;
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures.Take(5)));
        }

        #region Types

        public class FaultPlainSource
        {
            public int Ok { get; set; }
            public string Bad { get; set; } = "";
        }

        public class FaultPlainDest
        {
            public int Ok { get; set; }
            public string Bad { get; set; } = "";
        }

        public class FaultThrowingGetterSource
        {
            public int Ok { get; set; } = 3;
            public string Bad => throw new InvalidOperationException("getter exploded");
        }

        public class FaultThrowingSetterDest
        {
            private int _ok;

            /// <summary>Reads what was written without going through the throwing setter.</summary>
            public int OkValue => _ok;

            public int Ok
            {
                get => _ok;
                set => _ok = value;
            }

            public string Bad
            {
                get => "";
                set => throw new InvalidOperationException("setter exploded");
            }
        }

        public class FaultThrowingCtorDest
        {
            public FaultThrowingCtorDest() => throw new InvalidOperationException("ctor exploded");

            public int Ok { get; set; }
        }

        public class FaultMaybeThrows
        {
            public bool Explode { get; set; }
            public int Ok => Explode ? throw new InvalidOperationException("item exploded") : 1;
            public string Bad { get; set; } = "";
        }

        #endregion
    }
}
