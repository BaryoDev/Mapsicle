using System;
using System.Collections.Generic;
using System.Linq;
using Mapsicle;

// The declaration under test. Everything below exists to answer one question: does the code the
// generator emits for a realistic graph cost what the same projection written by hand costs.
[assembly: MapsicleGenerate(typeof(Mapsicle.Benchmarks.AgOrder), typeof(Mapsicle.Benchmarks.AgOrderDto))]

namespace Mapsicle.Benchmarks
{
    // One aggregate carrying every shape the emitter has a rule for: three levels of nesting, a
    // collection, a collection inside that collection, a widening, an enum into a string, an enum
    // into a different enum, a DateTime into an offset, a nullable, and two flattened paths.
    public enum AgState { Draft, Shipped }
    public enum AgChannelSource { Web, Mobile }
    public enum AgChannelDest { Web, Mobile }

    public class AgCountry { public string Iso { get; set; } = ""; }
    public class AgCountryDto { public string Iso { get; set; } = ""; }

    public class AgAddress { public string City { get; set; } = ""; public AgCountry Country { get; set; } = new(); }
    public class AgAddressDto { public string City { get; set; } = ""; public AgCountryDto Country { get; set; } = new(); }

    public class AgCustomer { public string FullName { get; set; } = ""; public AgAddress Address { get; set; } = new(); }
    public class AgCustomerDto { public string FullName { get; set; } = ""; public AgAddressDto Address { get; set; } = new(); }

    public class AgDiscount { public string Code { get; set; } = ""; public decimal Percent { get; set; } }
    public class AgDiscountDto { public string Code { get; set; } = ""; public decimal Percent { get; set; } }

    public class AgLine
    {
        public int Quantity { get; set; }
        public string Sku { get; set; } = "";
        public List<AgDiscount> Discounts { get; set; } = new();
    }

    public class AgLineDto
    {
        public int Quantity { get; set; }
        public string Sku { get; set; } = "";
        public List<AgDiscountDto> Discounts { get; set; } = new();
    }

    public class AgOrder
    {
        public int Id { get; set; }
        public string Reference { get; set; } = "";
        public AgState State { get; set; }
        public AgChannelSource Channel { get; set; }
        public AgCustomer Customer { get; set; } = new();
        public List<AgLine> Lines { get; set; } = new();
        public decimal Total { get; set; }
        public DateTime PlacedOn { get; set; }
        public DateTime? ShippedOn { get; set; }
    }

    public class AgOrderDto
    {
        public long Id { get; set; }                          // widening
        public string Reference { get; set; } = "";
        public string State { get; set; } = "";               // enum into a string
        public AgChannelDest Channel { get; set; }            // enum into a different enum
        public AgCustomerDto Customer { get; set; } = new();
        public List<AgLineDto> Lines { get; set; } = new();
        public decimal Total { get; set; }
        public DateTimeOffset PlacedOn { get; set; }          // DateTime into an offset
        public DateTime? ShippedOn { get; set; }
        public string CustomerFullName { get; set; } = "";    // flattened, two levels
        public string CustomerAddressCity { get; set; } = ""; // flattened, three levels
    }

    /// <summary>The same projection written out, which is the only baseline worth measuring against.</summary>
    /// <remarks>
    /// Written the obvious way rather than the clever way: allocate, assign, and loop the
    /// collections with an indexed for over a pre-sized destination. Nothing here is anything a
    /// generator could not also emit, because the question is what generated code should cost, not
    /// whether a person can beat it with tricks the emitter has no rule for.
    ///
    /// If this ever gets optimised past what the emitter can produce, the gate stops measuring the
    /// emitter and starts measuring the gap between two hand written styles.
    /// </remarks>
    public static class AgHandwritten
    {
        public static AgOrderDto Map(AgOrder o)
        {
            var lines = new List<AgLineDto>(o.Lines.Count);
            for (var i = 0; i < o.Lines.Count; i++)
            {
                lines.Add(Line(o.Lines[i]));
            }

            return new AgOrderDto
            {
                Id = o.Id,
                Reference = o.Reference,
                State = o.State.ToString(),
                Channel = o.Channel == AgChannelSource.Mobile ? AgChannelDest.Mobile : AgChannelDest.Web,
                Customer = Customer(o.Customer),
                Lines = lines,
                Total = o.Total,
                PlacedOn = o.PlacedOn,
                ShippedOn = o.ShippedOn,
                CustomerFullName = o.Customer.FullName,
                CustomerAddressCity = o.Customer.Address.City,
            };
        }

        private static AgCustomerDto Customer(AgCustomer c) => new()
        {
            FullName = c.FullName,
            Address = new AgAddressDto
            {
                City = c.Address.City,
                Country = new AgCountryDto { Iso = c.Address.Country.Iso },
            },
        };

        private static AgLineDto Line(AgLine l)
        {
            var discounts = new List<AgDiscountDto>(l.Discounts.Count);
            for (var i = 0; i < l.Discounts.Count; i++)
            {
                discounts.Add(new AgDiscountDto { Code = l.Discounts[i].Code, Percent = l.Discounts[i].Percent });
            }

            return new AgLineDto { Quantity = l.Quantity, Sku = l.Sku, Discounts = discounts };
        }

        /// <summary>Fails before a single timing is taken if the two rows map differently.</summary>
        /// <remarks>
        /// Warming both lanes is not checking them. A generated mapper that drops a member, or
        /// converts one differently, is faster than one that does not, and both the time bound and
        /// the allocation bound would have reported that as a pass. Every member of the graph is
        /// compared, including inside both collections, because the members most likely to be
        /// dropped are the ones furthest from the root.
        /// </remarks>
        public static void AssertLanesAgree(AgOrder order)
        {
            // First, that there is a generated lane at all. Without this the whole gate passes with
            // the analyzer removed: MapTo binds to the engine instead, the engine agrees with itself,
            // both allocate the same, and the ratio sits comfortably inside the band while measuring
            // the thing the gate exists to compare against. The test suite has an equivalent check
            // and the benchmark did not.
            var registry = typeof(global::Mapsicle.Mapper)
                .GetField("_generatedPairs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.GetValue(null)
                ?? throw new InvalidOperationException("could not read the generated registry");

            var keys = ((System.Collections.IEnumerable)registry.GetType().GetProperty("Keys")!.GetValue(registry)!)
                .Cast<object>()
                .Select(k => k.ToString() ?? "")
                .ToList();

            if (!keys.Any(k => k.Contains(nameof(AgOrder), StringComparison.Ordinal)
                            && k.Contains(nameof(AgOrderDto), StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "AgOrder into AgOrderDto was not generated, so this benchmark is timing the engine "
                    + "against hand written code and calling the result a generator result. Check the "
                    + "build for MSG001 and that the analyzer reference is present.");
            }

            var hand = Map(order);
            var generated = order.MapTo<AgOrderDto>()
                ?? throw new InvalidOperationException("the generated lane returned null");

            var differences = new List<string>();

            void Same(string name, object? a, object? b)
            {
                if (!Equals(a, b)) differences.Add($"{name}: hand written={a ?? "null"} generated={b ?? "null"}");
            }

            Same(nameof(hand.Id), hand.Id, generated.Id);
            Same(nameof(hand.Reference), hand.Reference, generated.Reference);
            Same(nameof(hand.State), hand.State, generated.State);
            Same(nameof(hand.Channel), hand.Channel, generated.Channel);
            Same(nameof(hand.Total), hand.Total, generated.Total);
            Same(nameof(hand.PlacedOn), hand.PlacedOn, generated.PlacedOn);
            Same(nameof(hand.ShippedOn), hand.ShippedOn, generated.ShippedOn);
            Same(nameof(hand.CustomerFullName), hand.CustomerFullName, generated.CustomerFullName);
            Same(nameof(hand.CustomerAddressCity), hand.CustomerAddressCity, generated.CustomerAddressCity);
            Same("Customer.FullName", hand.Customer.FullName, generated.Customer.FullName);
            Same("Customer.Address.City", hand.Customer.Address.City, generated.Customer.Address.City);
            Same("Customer.Address.Country.Iso", hand.Customer.Address.Country.Iso, generated.Customer.Address.Country.Iso);
            Same("Lines.Count", hand.Lines.Count, generated.Lines.Count);

            for (var i = 0; i < Math.Min(hand.Lines.Count, generated.Lines.Count); i++)
            {
                Same($"Lines[{i}].Quantity", hand.Lines[i].Quantity, generated.Lines[i].Quantity);
                Same($"Lines[{i}].Sku", hand.Lines[i].Sku, generated.Lines[i].Sku);
                Same($"Lines[{i}].Discounts.Count", hand.Lines[i].Discounts.Count, generated.Lines[i].Discounts.Count);

                for (var j = 0; j < Math.Min(hand.Lines[i].Discounts.Count, generated.Lines[i].Discounts.Count); j++)
                {
                    Same($"Lines[{i}].Discounts[{j}].Code", hand.Lines[i].Discounts[j].Code, generated.Lines[i].Discounts[j].Code);
                    Same($"Lines[{i}].Discounts[{j}].Percent", hand.Lines[i].Discounts[j].Percent, generated.Lines[i].Discounts[j].Percent);
                }
            }

            if (differences.Count > 0)
            {
                throw new InvalidOperationException(
                    "the generated lane and the hand written baseline map differently, so timing them "
                    + "would compare different work:\n  " + string.Join("\n  ", differences));
            }
        }

        public static AgOrder Build() => new()
        {
            Id = 1001,
            Reference = "SO-1001",
            State = AgState.Shipped,
            Channel = AgChannelSource.Mobile,
            Customer = new AgCustomer
            {
                FullName = "Ada Lovelace",
                Address = new AgAddress { City = "Cebu", Country = new AgCountry { Iso = "PH" } },
            },
            Total = 24998.50m,
            PlacedOn = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Utc),
            ShippedOn = new DateTime(2026, 8, 31, 14, 0, 0, DateTimeKind.Utc),
            Lines =
            {
                new AgLine
                {
                    Quantity = 2, Sku = "PH-1",
                    Discounts = { new AgDiscount { Code = "LAUNCH", Percent = 10m } },
                },
                new AgLine { Quantity = 1, Sku = "AC-9" },
            },
        };
    }
}
