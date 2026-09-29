using System.Collections.Generic;
using System.Linq;
using Mapsicle;
using Mapsicle.Audit;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Audit.Tests
{
    public class AuBdCustomer
    {
        public string Name { get; set; } = "";
    }

    public class AuBdOrder
    {
        public int Id { get; set; }
        public AuBdCustomer? Customer { get; set; }
        public string Code { get; set; } = "";
        public long Count { get; set; }
    }

    public class AuBdOrderDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        [MapFrom("Code")] public string Reference { get; set; } = "";
        public int Count { get; set; }
        public string Missing { get; set; } = "";
    }

    public class AuBdTagged
    {
        public int Id { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class AuBdTaggedDto
    {
        public int Id { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class AuBdIndexed
    {
        private readonly Dictionary<string, string> _values = new();
        public int Id { get; set; }
        public string this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : "";
            set => _values[key] = value;
        }
    }

    public class AuBdSecret
    {
        private string _pin = "";
        public int Id { get; set; }
        public string Pin { private get => _pin; set => _pin = value; }
    }

    public class AuBdFieldSource
    {
        public string Code = "";
    }

    public class AuBdFieldDto
    {
        public string Code { get; set; } = "";
    }

    public class AuBdMoney
    {
        public decimal Amount { get; set; }
    }

    public class AuBdPriced
    {
        public AuBdMoney? Price { get; set; }
    }

    public class AuBdPricedDto
    {
        public decimal Price { get; set; }
    }

    [Collection("StaticMapperTests")]
    public class AuditBindingTests
    {
        public AuditBindingTests() => Mapper.ClearCache();

        private static PropertyMappingInfo Member(AuditedMappingResult<AuBdOrderDto> result, string name) =>
            result.Audit.PropertyMappings.Single(p => p.PropertyName == name);

        private static AuditedMappingResult<AuBdOrderDto> MapOrder() =>
            new AuBdOrder { Id = 1, Customer = new AuBdCustomer { Name = "ann" }, Code = "R-9", Count = 5 }
                .MapWithAudit<AuBdOrderDto>();

        [Fact]
        public void MapWithAudit_FlattenedMember_ReportsMappedFromItsPath()
        {
            var result = MapOrder();

            Assert.Equal("ann", result.Value!.CustomerName);
            var member = Member(result, "CustomerName");
            Assert.True(member.WasMapped);
            Assert.Equal("Customer.Name", member.SourcePropertyName);
            Assert.Equal("ann", member.SourceValue);
        }

        [Fact]
        public void MapWithAudit_MapFromMember_ReportsMappedFromTheNamedSource()
        {
            var result = MapOrder();

            Assert.Equal("R-9", result.Value!.Reference);
            var member = Member(result, "Reference");
            Assert.True(member.WasMapped);
            Assert.Equal("Code", member.SourcePropertyName);
        }

        [Fact]
        public void MapWithAudit_NarrowingMember_ReportsUnmapped()
        {
            var result = MapOrder();

            Assert.Equal(0, result.Value!.Count);
            Assert.False(Member(result, "Count").WasMapped);
        }

        [Fact]
        public void MapWithAudit_Controls_DirectMappedAndMissingUnmapped()
        {
            var result = MapOrder();

            Assert.True(Member(result, "Id").WasMapped);
            Assert.False(Member(result, "Missing").WasMapped);
        }

        [Fact]
        public void WouldChangeOnMap_EqualLists_ReportsNoChange()
        {
            var existing = new AuBdTaggedDto { Id = 1, Tags = new List<string> { "x" } };

            Assert.False(new AuBdTagged { Id = 1, Tags = new List<string> { "x" } }.WouldChangeOnMap(existing));
        }

        [Fact]
        public void WouldChangeOnMap_DifferentLists_ReportsChange()
        {
            var existing = new AuBdTaggedDto { Id = 1, Tags = new List<string> { "x" } };

            Assert.True(new AuBdTagged { Id = 1, Tags = new List<string> { "y" } }.WouldChangeOnMap(existing));
        }

        [Fact]
        public void Diff_TypeWithIndexer_ComparesOrdinaryProperties()
        {
            var changes = new AuBdIndexed { Id = 1 }.Diff(new AuBdIndexed { Id = 2 });

            var change = Assert.Single(changes);
            Assert.Equal("Id", change.PropertyName);
        }

        [Fact]
        public void Diff_PrivateGetter_IsNotRead()
        {
            var changes = new AuBdSecret { Id = 1, Pin = "1111" }.Diff(new AuBdSecret { Id = 1, Pin = "2222" });

            Assert.Empty(changes);
        }

        [Fact]
        public void MapWithAudit_MemberFilledByAFluentConverter_ReportsMapped()
        {
            var mapper = new MapperConfiguration(c =>
            {
                c.CreateConverter<AuBdMoney, decimal>(m => m.Amount);
                c.CreateMap<AuBdPriced, AuBdPricedDto>();
            }).CreateMapper();

            var result = mapper.MapWithAudit<AuBdPriced, AuBdPricedDto>(new AuBdPriced { Price = new AuBdMoney { Amount = 9.5m } });

            Assert.Equal(9.5m, result.Value!.Price);
            var member = result.Audit.PropertyMappings.Single(p => p.PropertyName == "Price");
            Assert.True(member.WasMapped);
            Assert.Equal("Price", member.SourcePropertyName);
        }

        [Fact]
        public void MapWithAudit_WithoutTheConverter_ReportsTheSameMemberUnmapped()
        {
            var mapper = new MapperConfiguration(c => c.CreateMap<AuBdPriced, AuBdPricedDto>()).CreateMapper();

            var result = mapper.MapWithAudit<AuBdPriced, AuBdPricedDto>(new AuBdPriced { Price = new AuBdMoney { Amount = 9.5m } });

            Assert.Equal(0m, result.Value!.Price);
            Assert.False(result.Audit.PropertyMappings.Single(p => p.PropertyName == "Price").WasMapped);
        }

        [Fact]
        public void MapWithAudit_PropertyFilledFromASourceField_ReportsMappedWithTheValue()
        {
            var result = new AuBdFieldSource { Code = "X" }.MapWithAudit<AuBdFieldDto>();

            Assert.Equal("X", result.Value!.Code);
            var member = result.Audit.PropertyMappings.Single(p => p.PropertyName == "Code");
            Assert.True(member.WasMapped);
            Assert.Equal("Code", member.SourcePropertyName);
            Assert.Equal("X", member.SourceValue);
            Assert.Equal(typeof(string), member.SourceType);
        }
    }
}
