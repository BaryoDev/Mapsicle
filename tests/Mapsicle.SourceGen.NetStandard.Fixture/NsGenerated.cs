using System;
using System.Collections.Generic;
using Mapsicle;

[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.NetStandard.Fixture.NsGenOrder), typeof(Mapsicle.SourceGen.NetStandard.Fixture.NsGenOrderDto))]

namespace Mapsicle.SourceGen.NetStandard.Fixture
{
    public enum NsGenStatus { Draft = 0, Paid = 1 }
    public enum NsGenStatusDto { Paid = 5, Draft = 6 }

    public sealed class NsGenCountry { public string Iso { get; set; } = ""; }
    public sealed class NsGenCountryDto { public string Iso { get; set; } = ""; }

    public class NsGenLine { public string Sku { get; set; } = ""; public int Qty { get; set; } }
    public class NsGenLineDto { public string Sku { get; set; } = ""; public long Qty { get; set; } }

    public class NsGenOrder
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public NsGenStatus Status { get; set; }
        public NsGenStatus Label { get; set; }
        public DateTime PlacedAt { get; set; }
        public NsGenCountry Country { get; set; } = new NsGenCountry();
        public List<NsGenLine> Lines { get; set; } = new List<NsGenLine>();
        public NsGenLine[] Extras { get; set; } = new NsGenLine[0];
    }

    public class NsGenOrderDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public NsGenStatusDto Status { get; set; }
        public string Label { get; set; } = "";
        public DateTimeOffset PlacedAt { get; set; }
        public NsGenCountryDto Country { get; set; } = new NsGenCountryDto();
        public string CountryIso { get; set; } = "";
        public List<NsGenLineDto> Lines { get; set; } = new List<NsGenLineDto>();
        public List<NsGenLineDto> Extras { get; set; } = new List<NsGenLineDto>();
    }

    /// <summary>The calls a consumer on this target would write, compiled on this target.</summary>
    public static class NsGenCalls
    {
        public static NsGenOrderDto Typed(NsGenOrder order) => order.MapTo<NsGenOrderDto>();

        public static NsGenOrderDto Untyped(NsGenOrder order) => ((object)order).MapTo<NsGenOrderDto>();
    }
}
