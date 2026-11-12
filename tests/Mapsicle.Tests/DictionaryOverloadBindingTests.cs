using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// A dictionary mapped into a type the old <c>where T : new()</c> constraint excluded.
    /// </summary>
    /// <remarks>
    /// C# checks a constraint when it picks an overload, so a positional record or a type with
    /// required members bound to the IEnumerable overload and the call returned a List of T. The
    /// declared type of the variable below is a List-free T on purpose: with <c>var</c> the wrong
    /// overload compiled and ran, so only the runtime type of the result shows it.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class DictionaryOverloadBindingTests
    {
        public class DbPlain { public int A { get; set; } public string? B { get; set; } }

        public class DbRequired { public required int A { get; set; } public string? B { get; set; } }

        public record DbPositional(int A, string B);

        public record DbMixed(int A) { public string? B { get; set; } }

        public struct DbStruct { public int A { get; set; } public string? B { get; set; } }

        public class DbTied
        {
            public DbTied(int a, string b) { Used = "ab"; A = a; }
            public DbTied(int a, long c) { Used = "ac"; A = a; }
            public DbTied(string z, int y) { Used = "zy"; }
            public string Used { get; }
            public int A { get; }
        }

        private static Dictionary<string, object?> Row() => new() { ["a"] = 5, ["B"] = "x" };

        [Fact]
        public void APlainClassStillMaps()
        {
            var result = Row().MapTo<DbPlain>();

            Assert.IsType<DbPlain>(result);
            Assert.Equal(5, result!.A);
            Assert.Equal("x", result.B);
        }

        [Fact]
        public void ARequiredMemberTypeMapsAndIsNotAList()
        {
            object? result = Row().MapTo<DbRequired>();

            var typed = Assert.IsType<DbRequired>(result);
            Assert.Equal(5, typed.A);
            Assert.Equal("x", typed.B);
        }

        [Fact]
        public void APositionalRecordMapsThroughItsConstructor()
        {
            object? result = Row().MapTo<DbPositional>();

            var typed = Assert.IsType<DbPositional>(result);
            Assert.Equal(5, typed.A);
            Assert.Equal("x", typed.B);
        }

        [Fact]
        public void ARecordWithAnExtraSetterFillsBoth()
        {
            var dict = new Dictionary<string, object?> { ["A"] = 3, ["B"] = "y" };

            var result = dict.MapTo<DbMixed>();

            Assert.Equal(3, result!.A);
            Assert.Equal("y", result.B);
        }

        [Fact]
        public void AMissingConstructorKeyGivesTheDefault()
        {
            var result = new Dictionary<string, object?>().MapTo<DbPositional>();

            Assert.Equal(0, result!.A);
            Assert.Null(result.B);
        }

        [Fact]
        public void AStructMaps()
        {
            var result = Row().MapTo<DbStruct>();

            Assert.Equal(5, result.A);
            Assert.Equal("x", result.B);
        }

        [Fact]
        public void TheConstructorWithTheMostMatchingKeysWinsAmongTies()
        {
            Assert.Equal("ab", new Dictionary<string, object?> { ["a"] = 4, ["b"] = "x" }.MapTo<DbTied>()!.Used);
            Assert.Equal("ac", new Dictionary<string, object?> { ["a"] = 4, ["c"] = 9L }.MapTo<DbTied>()!.Used);
            Assert.Equal("zy", new Dictionary<string, object?> { ["z"] = "q", ["y"] = 1 }.MapTo<DbTied>()!.Used);
        }

        [Fact]
        public void WithNoMatchingKeysTheParameterTypeNamesDecide()
        {
            // (int, long) sorts before (int, string) and (string, int), on every run.
            for (var i = 0; i < 20; i++)
            {
                Assert.Equal("ac", new Dictionary<string, object?>().MapTo<DbTied>()!.Used);
            }
        }

        [Fact]
        public void ANullDictionaryGivesNull()
        {
            Dictionary<string, object?>? none = null;

            Assert.Null(none.MapTo<DbPositional>());
        }

        [Fact]
        public void AnIDictionaryReceiverBindsToo()
        {
            IDictionary<string, object?> row = Row();

            Assert.IsType<DbPositional>(row.MapTo<DbPositional>());
        }
    }
}
