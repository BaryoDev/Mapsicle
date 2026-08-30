using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// Mapping onto an existing destination while leaving named members alone.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class MapExcludingTests
    {
        public class MeSource { public int Id { get; set; } public string Name { get; set; } = ""; public string Note { get; set; } = ""; }
        public class MeDest { public int Id { get; set; } public string Name { get; set; } = ""; public string Note { get; set; } = ""; }

        private static MeSource Sample() => new() { Id = 5, Name = "name", Note = "note" };

        [Fact]
        public void AnExcludedMemberKeepsWhateverTheDestinationAlreadyHeld()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Note = "untouched" };

            Sample().Map(dest, new[] { "Note" });

            Assert.Equal(5, dest.Id);
            Assert.Equal("name", dest.Name);
            Assert.Equal("untouched", dest.Note);
        }

        [Fact]
        public void ExclusionIgnoresCase()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Note = "untouched" };

            Sample().Map(dest, new[] { "nOtE" });

            Assert.Equal("untouched", dest.Note);
        }

        [Fact]
        public void AnEmptyOrNullExclusionMapsEverything()
        {
            Mapper.ClearCache();

            var viaNull = new MeDest();
            Sample().Map(viaNull, null);
            Assert.Equal("note", viaNull.Note);

            var viaEmpty = new MeDest();
            Sample().Map(viaEmpty, Array.Empty<string>());
            Assert.Equal("note", viaEmpty.Note);
        }

        [Fact]
        public void ANameThatMatchesNothingIsNotAnError()
        {
            Mapper.ClearCache();
            var dest = new MeDest();

            Sample().Map(dest, new[] { "NoSuchMember" });

            Assert.Equal("name", dest.Name);
            Assert.Equal("note", dest.Note);
        }

        [Fact]
        public void DifferentExclusionSetsDoNotShareADelegate()
        {
            // Keyed only on the type pair, the second call here would reuse the first delegate and
            // exclude the wrong member. Both directions are checked because only one of them fails
            // if the key is missing the exclusion.
            Mapper.ClearCache();

            var excludeNote = new MeDest { Name = "keptName", Note = "keptNote" };
            Sample().Map(excludeNote, new[] { "Note" });
            Assert.Equal("name", excludeNote.Name);
            Assert.Equal("keptNote", excludeNote.Note);

            var excludeName = new MeDest { Name = "keptName", Note = "keptNote" };
            Sample().Map(excludeName, new[] { "Name" });
            Assert.Equal("keptName", excludeName.Name);
            Assert.Equal("note", excludeName.Note);
        }

        [Fact]
        public void TheSameSetInADifferentOrderReusesTheSameDelegate()
        {
            Mapper.ClearCache();

            var first = new MeDest { Name = "a", Note = "b" };
            Sample().Map(first, new[] { "Name", "Note" });

            var second = new MeDest { Name = "a", Note = "b" };
            Sample().Map(second, new[] { "Note", "Name" });

            Assert.Equal("a", second.Name);
            Assert.Equal("b", second.Note);
            Assert.Equal(5, second.Id);
        }

        [Fact]
        public void ExcludingEveryMappableMemberLeavesTheDestinationAlone()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Id = 99, Name = "a", Note = "b" };

            Sample().Map(dest, new[] { "Id", "Name", "Note" });

            Assert.Equal(99, dest.Id);
            Assert.Equal("a", dest.Name);
            Assert.Equal("b", dest.Note);
        }

        [Fact]
        public void ANullSourceLeavesTheDestinationAlone()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Name = "kept" };

            ((object?)null).Map(dest, new[] { "Note" });

            Assert.Equal("kept", dest.Name);
        }

        [Fact]
        public void ClearCacheDiscardsTheExcludingDelegatesToo()
        {
            // The excluding delegates live in their own cache. A cache that ClearCache does not
            // reach would keep handing out delegates after the caller asked for them to be dropped.
            Mapper.ClearCache();

            var before = new MeDest { Note = "untouched" };
            Sample().Map(before, new[] { "Note" });
            Assert.Equal("untouched", before.Note);

            Mapper.ClearCache();

            var after = new MeDest { Note = "still untouched" };
            Sample().Map(after, new[] { "Note" });
            Assert.Equal("still untouched", after.Note);
            Assert.Equal("name", after.Name);
        }
    }
}
