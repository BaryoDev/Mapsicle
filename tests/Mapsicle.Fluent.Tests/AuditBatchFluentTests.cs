using System;
using System.Collections.Generic;
using Mapsicle.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    public class AfCustomer { public string Name { get; set; } = ""; }
    public class AfOrder { public AfCustomer? Customer { get; set; } }
    public class AfOrderDto { public string CustomerName { get; set; } = ""; }
    public class AfItem { public int Id { get; set; } }
    public class AfItemDto { public int Id { get; set; } }
    public class AfPoint { public int X { get; set; } public int Y { get; set; } }
    public struct AfPointStruct { public int X { get; set; } public int Y { get; set; } }

    public class AfChild { public string Name { get; set; } = ""; }
    public class AfChildDto { public string Name { get; set; } = ""; }
    public class AfInSrc { public int Count { get; set; } public AfChild? Child { get; set; } public string Note { get; set; } = ""; }
    public class AfInDst { public long Count { get; set; } public AfChildDto? Child { get; set; } public string Note { get; set; } = "kept"; }

    public class AfAnimal { public string Name { get; set; } = ""; }
    public class AfDog : AfAnimal { public int Barks { get; set; } }
    public class AfAnimalDto { public string Name { get; set; } = ""; }
    public class AfDogDto : AfAnimalDto { public int Barks { get; set; } public string Tag { get; set; } = ""; }

    public class AfMoney { public decimal Amount { get; set; } }
    public class AfPriced { public AfMoney? Price { get; set; } public string Label { get; set; } = ""; }
    public class AfPricedDto { public decimal Price { get; set; } = -1; public string Label { get; set; } = ""; }
    public class AfPricedText { public AfMoney? Price { get; set; } }
    public class AfPricedTextDto { public string Price { get; set; } = ""; }

    public class AfUser { public string Name { get; set; } = ""; public string Password { get; set; } = ""; }
    public class AfUserDto { public string Name { get; set; } = ""; public string Password { get; set; } = ""; }

    public class AuditBatchFluentTests
    {
        private static IMapper Empty() => new MapperConfiguration(_ => { }).CreateMapper();

        // ---- #78 an unconfigured pair maps what the static mapper maps ----------------------------

        [Fact]
        public void AnUnconfiguredBoxedPrimitiveMaps()
        {
            var m = Empty();
            var id = Guid.NewGuid();

            Assert.Equal(5, m.Map<int>((object)5));
            Assert.Equal(42L, m.Map<long>((object)42));
            Assert.Equal(id, m.Map<Guid>((object)id));
            Assert.Equal(5, m.Map<int?>((object)5));
        }

        [Fact]
        public void AnUnconfiguredPairFlattens() =>
            Assert.Equal("Ada", Empty().Map<AfOrderDto>(new AfOrder { Customer = new AfCustomer { Name = "Ada" } })!.CustomerName);

        [Fact]
        public void AnUnconfiguredHashSetFills()
        {
            var set = Empty().Map<HashSet<AfItemDto>>(new List<AfItem> { new() { Id = 1 }, new() { Id = 2 } })!;

            Assert.Equal(new[] { 1, 2 }, set.Select(i => i.Id).OrderBy(i => i).ToArray());
        }

        [Fact]
        public void AnUnconfiguredDictionaryFills()
        {
            var map = Empty().Map<Dictionary<string, AfItemDto>>(new Dictionary<string, AfItem> { ["a"] = new() { Id = 7 } })!;

            Assert.Equal(7, map["a"].Id);
        }

        [Fact]
        public void AnUnconfiguredStructFills()
        {
            var p = Empty().Map<AfPointStruct>(new AfPoint { X = 3, Y = 4 });

            Assert.Equal(3, p.X);
            Assert.Equal(4, p.Y);
        }

        [Fact]
        public void AListOfWideningElementsMaps() =>
            Assert.Equal(new long[] { 1, 2 }, Empty().Map<List<long>>(new List<int> { 1, 2 })!);

        [Fact]
        public void AnIgnoreStillHoldsInsideAHashSet()
        {
            // The control for routing unconfigured shapes to the core mapper: a configured element
            // map must still be the one applied, or the ignore stops protecting the password.
            var m = new MapperConfiguration(c => c.CreateMap<AfUser, AfUserDto>().ForMember(d => d.Password, o => o.Ignore())).CreateMapper();

            var set = m.Map<HashSet<AfUserDto>>(new List<AfUser> { new() { Name = "a", Password = "p" } })!;

            var only = Assert.Single(set);
            Assert.Equal("a", only.Name);
            Assert.Null(only.Password);
        }

        [Fact]
        public void AnIgnoreStillHoldsInsideADictionary()
        {
            var m = new MapperConfiguration(c => c.CreateMap<AfUser, AfUserDto>().ForMember(d => d.Password, o => o.Ignore())).CreateMapper();

            var map = m.Map<Dictionary<string, AfUserDto>>(new Dictionary<string, AfUser> { ["k"] = new() { Name = "a", Password = "p" } })!;

            Assert.Equal("a", map["k"].Name);
            Assert.Null(map["k"].Password);
        }

        // ---- #79 in-place Map converts like the constructing path ---------------------------------

        [Fact]
        public void InPlaceMapWidensAndMapsNestedObjects()
        {
            var m = new MapperConfiguration(c => c.CreateMap<AfInSrc, AfInDst>()).CreateMapper();
            var dest = new AfInDst();

            m.Map(new AfInSrc { Count = 5, Child = new AfChild { Name = "c" }, Note = "n" }, dest);

            Assert.Equal(5L, dest.Count);
            Assert.Equal("c", dest.Child!.Name);
            Assert.Equal("n", dest.Note);
        }

        [Fact]
        public void InPlaceMapLeavesAnIgnoredMemberAlone()
        {
            var m = new MapperConfiguration(c => c.CreateMap<AfInSrc, AfInDst>().ForMember(d => d.Note, o => o.Ignore())).CreateMapper();
            var dest = new AfInDst();

            m.Map(new AfInSrc { Count = 5, Note = "n" }, dest);

            Assert.Equal(5L, dest.Count);
            Assert.Equal("kept", dest.Note);
        }

        [Fact]
        public void InPlaceMapLeavesAFailedConditionAloneAndMapsAPassingOne()
        {
            var m = new MapperConfiguration(c => c.CreateMap<AfInSrc, AfInDst>()
                .ForMember(d => d.Note, o => o.Condition(s => s.Count > 10))
                .ForMember(d => d.Count, o => o.Condition(s => s.Count > 1))).CreateMapper();

            var dest = new AfInDst();
            m.Map(new AfInSrc { Count = 5, Note = "n" }, dest);

            Assert.Equal("kept", dest.Note);
            Assert.Equal(5L, dest.Count);
        }

        [Fact]
        public void InPlaceMapAppliesACustomMapping()
        {
            var m = new MapperConfiguration(c => c.CreateMap<AfInSrc, AfInDst>()
                .ForMember(d => d.Note, o => o.MapFrom(s => s.Note + "!"))).CreateMapper();

            var dest = new AfInDst();
            m.Map(new AfInSrc { Count = 5, Note = "n" }, dest);

            Assert.Equal("n!", dest.Note);
            Assert.Equal(5L, dest.Count);
        }

        // ---- #80 Include returns the derived destination ------------------------------------------

        [Fact]
        public void IncludeReturnsTheDerivedDestination()
        {
            var m = new MapperConfiguration(c =>
            {
                c.CreateMap<AfAnimal, AfAnimalDto>().Include<AfDog, AfDogDto>();
                c.CreateMap<AfDog, AfDogDto>();
            }).CreateMapper();

            AfAnimal src = new AfDog { Name = "rex", Barks = 3 };
            var dto = m.Map<AfAnimalDto>(src);

            var dog = Assert.IsType<AfDogDto>(dto);
            Assert.Equal("rex", dog.Name);
            Assert.Equal(3, dog.Barks);
        }

        [Fact]
        public void IncludeRunsTheDerivedAfterMap()
        {
            var m = new MapperConfiguration(c =>
            {
                c.CreateMap<AfAnimal, AfAnimalDto>().Include<AfDog, AfDogDto>();
                c.CreateMap<AfDog, AfDogDto>().AfterMap((s, d) => d.Tag = "dog");
            }).CreateMapper();

            var dto = m.Map<AfAnimalDto>((AfAnimal)new AfDog { Name = "rex" });

            Assert.Equal("dog", Assert.IsType<AfDogDto>(dto).Tag);
        }

        [Fact]
        public void IncludeDispatchesEachElementOfAList()
        {
            var m = new MapperConfiguration(c =>
            {
                c.CreateMap<AfAnimal, AfAnimalDto>().Include<AfDog, AfDogDto>();
                c.CreateMap<AfDog, AfDogDto>();
            }).CreateMapper();

            var list = m.Map<List<AfAnimalDto>>(new List<AfAnimal> { new AfDog { Name = "rex", Barks = 2 }, new() { Name = "cat" } })!;

            Assert.Equal(2, Assert.IsType<AfDogDto>(list[0]).Barks);
            Assert.Equal(typeof(AfAnimalDto), list[1].GetType());
            Assert.Equal("cat", list[1].Name);
        }

        // ---- #81 CreateConverter applies to members -----------------------------------------------

        [Fact]
        public void AConverterAppliesToAMember()
        {
            var m = new MapperConfiguration(c =>
            {
                c.CreateConverter<AfMoney, decimal>(x => x.Amount);
                c.CreateMap<AfPriced, AfPricedDto>();
            }).CreateMapper();

            var dto = m.Map<AfPricedDto>(new AfPriced { Price = new AfMoney { Amount = 9.5m }, Label = "l" })!;

            Assert.Equal(9.5m, dto.Price);
            Assert.Equal("l", dto.Label);
        }

        [Fact]
        public void AConverterIntoAStringBeatsToString()
        {
            var m = new MapperConfiguration(c => c.CreateConverter<AfMoney, string>(x => "PHP " + x.Amount)).CreateMapper();

            Assert.Equal("PHP 9.5", m.Map<AfPricedTextDto>(new AfPricedText { Price = new AfMoney { Amount = 9.5m } })!.Price);
        }

        [Fact]
        public void AConverterOnANullMemberLeavesTheDefault()
        {
            var m = new MapperConfiguration(c => c.CreateConverter<AfMoney, decimal>(x => x.Amount)).CreateMapper();

            Assert.Equal(0m, m.Map<AfPricedDto>(new AfPriced { Price = null })!.Price);
        }

        [Fact]
        public void AConverterAppliesInPlace()
        {
            var m = new MapperConfiguration(c => c.CreateConverter<AfMoney, decimal>(x => x.Amount)).CreateMapper();
            var dest = new AfPricedDto();

            m.Map(new AfPriced { Price = new AfMoney { Amount = 2m } }, dest);

            Assert.Equal(2m, dest.Price);
        }

        [Fact]
        public void AnIgnoreBeatsAConverter()
        {
            var m = new MapperConfiguration(c =>
            {
                c.CreateConverter<AfMoney, decimal>(x => x.Amount);
                c.CreateMap<AfPriced, AfPricedDto>().ForMember(d => d.Price, o => o.Ignore());
            }).CreateMapper();

            Assert.Equal(0m, m.Map<AfPricedDto>(new AfPriced { Price = new AfMoney { Amount = 9.5m } })!.Price);
        }

        // ---- #82 a second AddMapsicle call keeps the first ----------------------------------------

        [Fact]
        public void ASecondAddMapsicleKeepsTheFirstCallsIgnore()
        {
            var services = new ServiceCollection();
            services.AddMapsicle(c => c.CreateMap<AfUser, AfUserDto>().ForMember(d => d.Password, o => o.Ignore()));
            services.AddMapsicle(c => c.CreateMap<AfItem, AfItemDto>());

            using var provider = services.BuildServiceProvider();
            var mapper = provider.GetRequiredService<IMapper>();

            Assert.Null(mapper.Map<AfUser, AfUserDto>(new AfUser { Name = "a", Password = "p" })!.Password);
            Assert.NotNull(provider.GetRequiredService<MapperConfiguration>().GetTypeMap(typeof(AfItem), typeof(AfItemDto)));
            Assert.NotNull(provider.GetRequiredService<MapperConfiguration>().GetTypeMap(typeof(AfUser), typeof(AfUserDto)));
            Assert.Single(services, d => d.ServiceType == typeof(IMapper));
            Assert.Single(services, d => d.ServiceType == typeof(MapperConfiguration));
        }

        [Fact]
        public void ValidationOnALaterCallCoversTheEarlierMaps()
        {
            var services = new ServiceCollection();
            services.AddMapsicle(c => c.CreateMap<AfItem, AfUserDto>());

            var ex = Assert.Throws<InvalidOperationException>(
                () => services.AddMapsicle(c => c.CreateMap<AfItem, AfItemDto>(), validateConfiguration: true));
            Assert.Contains("'Name' on 'AfUserDto'", ex.Message);
        }

        [Fact]
        public void ASingleAddMapsicleStillWorks()
        {
            using var provider = new ServiceCollection()
                .AddMapsicle(c => c.CreateMap<AfItem, AfItemDto>(), validateConfiguration: true)
                .BuildServiceProvider();

            Assert.Equal(4, provider.GetRequiredService<IMapper>().Map<AfItemDto>(new AfItem { Id = 4 })!.Id);
        }
    }
}
