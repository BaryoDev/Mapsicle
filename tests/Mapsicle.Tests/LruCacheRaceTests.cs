using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Xunit;

namespace Mapsicle.Tests
{
    public class LruRaceSrcA { public int Id { get; set; } }
    public class LruRaceDstA { public int Id { get; set; } }
    public class LruRaceSrcB { public int Id { get; set; } }
    public class LruRaceDstB { public int Id { get; set; } }
    public class LruRaceSrcC { public int Id { get; set; } }
    public class LruRaceDstC { public int Id { get; set; } }
    public class LruRaceSrcD { public int Id { get; set; } }
    public class LruRaceDstD { public int Id { get; set; } }
    public class LruRaceToggleSrc { public int Id { get; set; } }
    public class LruRaceToggleDst { public int Id { get; set; } }

    /// <summary>
    /// Mapping while the bounded cache is trimmed, toggled or inspected from other threads.
    /// </summary>
    /// <remarks>
    /// These are races, so each test hammers for a bounded time and stops at the first exception.
    /// Each threw NullReferenceException well inside its budget before the fix.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class LruCacheRaceTests : IDisposable
    {
        private static readonly int Workers = Math.Max(8, Environment.ProcessorCount * 2);

        public LruCacheRaceTests()
        {
            Mapper.UseLruCache = false;
            Mapper.MaxCacheSize = 1000;
            Mapper.ClearCache();
        }

        public void Dispose()
        {
            Mapper.UseLruCache = false;
            Mapper.MaxCacheSize = 1000;
            Mapper.ClearCache();
        }

        [Fact]
        public void TypedCollectionMapSurvivesTheTypedCacheBeingTrimmed()
        {
            Mapper.UseLruCache = true;
            Mapper.MaxCacheSize = 2;

            var failure = Hammer(TimeSpan.FromSeconds(2), worker =>
            {
                var count = (worker % 4) switch
                {
                    0 => new List<LruRaceSrcA> { new() { Id = 1 }, new() { Id = 2 } }.MapTo<LruRaceSrcA, LruRaceDstA>().Count,
                    1 => new List<LruRaceSrcB> { new() { Id = 1 }, new() { Id = 2 } }.MapTo<LruRaceSrcB, LruRaceDstB>().Count,
                    2 => new List<LruRaceSrcC> { new() { Id = 1 }, new() { Id = 2 } }.MapTo<LruRaceSrcC, LruRaceDstC>().Count,
                    _ => new List<LruRaceSrcD> { new() { Id = 1 }, new() { Id = 2 } }.MapTo<LruRaceSrcD, LruRaceDstD>().Count,
                };
                if (count != 2) throw new InvalidOperationException($"mapped {count} items, expected 2");
            }, chaos: null);

            Assert.True(failure is null, failure?.ToString());
        }

        [Fact]
        public void UntypedMapSurvivesUseLruCacheBeingToggled()
        {
            var failure = HammerFreshCopies(copy =>
            {
                var mapTo = copy.Method<Func<object, LruRaceToggleDst>>("MapTo", typeof(object));
                var map = copy.Method<Func<object, LruRaceToggleDst, LruRaceToggleDst>>("Map", typeof(object), null);
                var on = false;

                void Work(int worker)
                {
                    var source = new LruRaceToggleSrc { Id = 7 };
                    var mapped = worker % 2 == 0 ? mapTo(source) : map(source, new LruRaceToggleDst());
                    if (mapped.Id != 7) throw new InvalidOperationException($"mapped Id {mapped.Id}, expected 7");
                }

                return (Work, () => copy.SetUseLruCache(on = !on));
            });

            Assert.True(failure is null, failure?.ToString());
        }

        [Fact]
        public void CacheInfoSurvivesUseLruCacheBeingToggled()
        {
            var failure = HammerFreshCopies(copy =>
            {
                var on = false;
                return (_ => copy.CacheInfo(), () => copy.SetUseLruCache(on = !on));
            });

            Assert.True(failure is null, failure?.ToString());
        }

        /// <summary>
        /// Hammers a freshly loaded copy of the core several times over, stopping at the first failure.
        /// </summary>
        /// <remarks>
        /// A fresh copy because these windows are widest the first time each branch runs, while
        /// methods are still being compiled. In a full suite run earlier tests had already warmed the
        /// shared copy, and a hammer on it passed against the broken code every time. A fresh copy
        /// also keeps the toggling away from the statics other tests use. The context is not
        /// collectible because code in a collectible context skips tiered compilation and is
        /// optimized straight away, which closes the window again; ten small copies are left loaded.
        /// </remarks>
        private static Exception? HammerFreshCopies(Func<FreshMapsicle, (Action<int> Work, Action Chaos)> setup)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var (work, chaos) = setup(new FreshMapsicle());
                var failure = Hammer(TimeSpan.FromMilliseconds(300), work, chaos);
                if (failure != null) return failure;
            }
            return null;
        }

        private sealed class FreshMapsicle
        {
            private readonly Type _mapper;

            public FreshMapsicle()
            {
                var assembly = new AssemblyLoadContext("lru-race").LoadFromAssemblyPath(typeof(Mapper).Assembly.Location);
                _mapper = assembly.GetType(typeof(Mapper).FullName!, throwOnError: true)!;
                SetUseLruCache = Method<Action<bool>>("set_UseLruCache", typeof(bool));

                var cacheInfo = _mapper.GetMethod(nameof(Mapper.CacheInfo))!;
                CacheInfo = Expression.Lambda<Func<object>>(
                    Expression.Convert(Expression.Call(cacheInfo), typeof(object))).Compile();
            }

            public Action<bool> SetUseLruCache { get; }

            public Func<object> CacheInfo { get; }

            /// <summary>A static method by name and parameters, where null stands for the generic parameter.</summary>
            public TDelegate Method<TDelegate>(string name, params Type?[] parameters) where TDelegate : Delegate
            {
                var method = _mapper.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m =>
                    m.Name == name
                    && m.GetParameters().Select(p => p.ParameterType.IsGenericParameter ? null : p.ParameterType)
                        .SequenceEqual(parameters));
                if (method.IsGenericMethodDefinition)
                {
                    method = method.MakeGenericMethod(typeof(TDelegate).GetMethod("Invoke")!.ReturnType);
                }
                return (TDelegate)method.CreateDelegate(typeof(TDelegate));
            }
        }

        /// <summary>Runs the action on many threads until the budget runs out or one throws.</summary>
        private static Exception? Hammer(TimeSpan budget, Action<int> work, Action? chaos)
        {
            Exception? first = null;
            var stop = 0;
            var clock = Stopwatch.StartNew();

            void Loop(Action body)
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    try
                    {
                        body();
                    }
                    catch (Exception e)
                    {
                        Interlocked.CompareExchange(ref first, e, null);
                        Volatile.Write(ref stop, 1);
                    }
                    if (clock.Elapsed > budget) Volatile.Write(ref stop, 1);
                }
            }

            var threads = new List<Thread>();
            for (var i = 0; i < Workers; i++)
            {
                var worker = i;
                threads.Add(new Thread(() => Loop(() => work(worker))) { IsBackground = true });
            }
            if (chaos != null) threads.Add(new Thread(() => Loop(chaos)) { IsBackground = true });

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            return first;
        }
    }
}
