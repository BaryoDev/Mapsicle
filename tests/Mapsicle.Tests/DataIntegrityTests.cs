using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Properties that have to hold regardless of where the process runs or what the caller does
    /// with the source afterwards.
    /// </summary>
    /// <remarks>
    /// A mapper sold on being trustworthy has to produce the same bytes in every region and has to
    /// be honest about what it shares with the object it mapped from. Neither was pinned before.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class DataIntegrityTests
    {
        // ---- Culture invariance ----------------------------------------------------------------
        // Number and date conversions read the ambient thread culture, so 1234.5m became "1234,5"
        // under de-DE and "1234.5" under en-US. A mapper feeding a serialisation or persistence
        // boundary wrote a value another region read back as a different number.

        [Fact]
        public void DecimalToString_IsInvariant_UnderAForeignCulture()
        {
            WithCulture("de-DE", () =>
            {
                // Positive control: this environment really does format differently under de-DE.
                // Without it, a globalization-invariant runtime would pass the assertion below
                // while proving nothing at all.
                Assert.Equal("1234,5", 1234.5m.ToString());

                Mapper.ClearCache();
                var dto = new CultureSource { Amount = 1234.5m }.MapTo<CultureSource, CultureDest>();

                Assert.NotNull(dto);
                Assert.Equal("1234.5", dto!.Amount);
            });
        }

        [Fact]
        public void DoubleToString_IsInvariant_UnderAForeignCulture()
        {
            WithCulture("de-DE", () =>
            {
                Mapper.ClearCache();
                var dto = ((object)new CultureDoubleSource { Amount = 1234.5d }).MapTo<CultureDoubleDest>();

                Assert.NotNull(dto);
                Assert.Equal("1234.5", dto!.Amount);
            });
        }

        [Fact]
        public void NullableDecimalToString_IsInvariant_AndEmptyWhenAbsent()
        {
            WithCulture("de-DE", () =>
            {
                Mapper.ClearCache();
                var withValue = ((object)new CultureNullableSource { Amount = 9.5m }).MapTo<CultureNullableDest>();
                Assert.Equal("9.5", withValue!.Amount);

                Mapper.ClearCache();
                var without = ((object)new CultureNullableSource { Amount = null }).MapTo<CultureNullableDest>();
                Assert.Equal(string.Empty, without!.Amount);
            });
        }

        [Fact]
        public void DictionaryCoercion_ParsesInvariantly_WhenOptedInto()
        {
            var previous = Mapper.CoerceDictionaryValues;
            Mapper.CoerceDictionaryValues = true;
            try
            {
                WithCulture("de-DE", () =>
                {
                    // "1.5" is one and a half everywhere, not fifteen. Under de-DE the old code read
                    // the dot as a thousands separator and produced 15.
                    var dict = new Dictionary<string, object?> { ["Amount"] = "1.5" };

                    var result = dict.MapTo<CultureParseTarget>();

                    Assert.NotNull(result);
                    Assert.Equal(1.5d, result!.Amount);
                });
            }
            finally
            {
                Mapper.CoerceDictionaryValues = previous;
            }
        }

        [Fact]
        public void StringSourceIsUntouched_ByInvariantFormatting()
        {
            // Positive control: formatting changes only apply to values that format themselves. A
            // string passes through as it was written, culture or not.
            WithCulture("de-DE", () =>
            {
                Mapper.ClearCache();
                var dto = ((object)new CultureTextSource { Amount = "1234,5" }).MapTo<CultureDest>();

                Assert.Equal("1234,5", dto!.Amount);
            });
        }

        [Fact]
        public void AnObjectDeclaredMember_HoldingANumber_StillFormatsInvariantly()
        {
            // The static check can only see the declared type. A member declared as object holding a
            // boxed decimal fell past it to a bare ToString() and produced "1234,5" under de-DE,
            // while the same value in a decimal member produced "1234.5". The culture bug survived
            // in the one place the compile-time check cannot reach.
            WithCulture("de-DE", () =>
            {
                Mapper.ClearCache();
                var dto = ((object)new BoxedSource { Amount = 1234.5m }).MapTo<CultureDest>();

                Assert.Equal("1234.5", dto!.Amount);
            });
        }

        [Fact]
        public void AnObjectDeclaredMember_HoldingAString_IsUnchanged()
        {
            // Positive control: only values that format themselves are reformatted.
            WithCulture("de-DE", () =>
            {
                Mapper.ClearCache();
                var dto = ((object)new BoxedSource { Amount = "1234,5 as written" }).MapTo<CultureDest>();

                Assert.Equal("1234,5 as written", dto!.Amount);
            });
        }

        [Fact]
        public void AnObjectDeclaredMember_HoldingNull_YieldsNull()
        {
            Mapper.ClearCache();
            var dto = ((object)new BoxedSource { Amount = null }).MapTo<CultureDest>();

            Assert.Null(dto!.Amount);
        }

        private static void WithCulture(string name, Action body)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            var target = new CultureInfo(name);
            CultureInfo.CurrentCulture = target;
            CultureInfo.CurrentUICulture = target;
            try
            {
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
                Mapper.ClearCache();
            }
        }

        // ---- The shallow-copy contract for in-place Map ------------------------------------------
        // Map assigns a directly-assignable reference rather than copying it, so source and
        // destination share one mutable instance afterwards. That is a deliberate choice, the same
        // one AutoMapper makes, and copying instead would change the allocation profile that this
        // project treats as a correctness property. It was undocumented and unpinned, which is the
        // part that was wrong: a caller pointing Map at a long-lived entity had no way to know that
        // later mutation of the source reaches into it.

        [Fact]
        public void InPlaceMap_SharesReferenceTypedProperties_WithTheSource()
        {
            Mapper.ClearCache();

            var source = new AliasSource { Items = new List<string> { "a" } };
            var destination = source.Map(new AliasDest());

            source.Items.Add("b");

            Assert.Same(source.Items, destination.Items);
            Assert.Equal(2, destination.Items!.Count);
        }

        [Fact]
        public void MapTo_AlsoSharesReferenceTypedProperties_WithTheSource()
        {
            // MapTo behaves the same way, for the same reason: a directly-assignable property is
            // assigned, not rebuilt. The contract is therefore one rule for every entry point
            // rather than a quirk of Map, which is what makes it documentable.
            Mapper.ClearCache();

            var source = new AliasSource { Items = new List<string> { "a" } };
            var destination = ((object)source).MapTo<AliasDest>();

            source.Items.Add("b");

            Assert.Same(source.Items, destination!.Items);
        }

        [Fact]
        public void ADifferentlyTypedNestedObject_IsBuiltFresh_NotShared()
        {
            // The boundary of the rule. Sharing happens only where the destination member can hold
            // the source instance as it is. A nested object of a different type has to be built, so
            // there is nothing to share and mutating the source cannot reach the destination.
            Mapper.ClearCache();

            var inner = new AliasInner { Name = "before" };
            var source = new AliasNestedSource { Item = inner };

            var destination = ((object)source).MapTo<AliasNestedDest>();
            inner.Name = "after";

            Assert.NotNull(destination!.Item);
            Assert.Equal("before", destination.Item!.Name);
        }

        // ---- Types, unique to this file ----------------------------------------------------------

        public class CultureSource { public decimal Amount { get; set; } }
        public class BoxedSource { public object? Amount { get; set; } }
        public class CultureTextSource { public string? Amount { get; set; } }
        public class CultureDest { public string? Amount { get; set; } }
        public class CultureDoubleSource { public double Amount { get; set; } }
        public class CultureDoubleDest { public string? Amount { get; set; } }
        public class CultureNullableSource { public decimal? Amount { get; set; } }
        public class CultureNullableDest { public string? Amount { get; set; } }
        public class CultureParseTarget { public double Amount { get; set; } }

        public class AliasSource { public List<string>? Items { get; set; } }
        public class AliasInner { public string? Name { get; set; } }
        public class AliasInnerDto { public string? Name { get; set; } }
        public class AliasNestedSource { public AliasInner? Item { get; set; } }
        public class AliasNestedDest { public AliasInnerDto? Item { get; set; } }
        public class AliasDest { public List<string>? Items { get; set; } }
    }
}
