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

        private static void MapExcluding(MeSource source, MeDest dest, params string[] excluded) =>
            Mapper.GetInPlaceMapper(typeof(MeSource), typeof(MeDest), excluded)(source, dest);


        [Fact]
        public void AnExcludedMemberKeepsWhateverTheDestinationAlreadyHeld()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Note = "untouched" };

            MapExcluding(Sample(), dest, "Note");

            Assert.Equal(5, dest.Id);
            Assert.Equal("name", dest.Name);
            Assert.Equal("untouched", dest.Note);
        }

        [Fact]
        public void ExclusionIgnoresCase()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Note = "untouched" };

            MapExcluding(Sample(), dest, "nOtE");

            Assert.Equal("untouched", dest.Note);
        }

        [Fact]
        public void AnEmptyOrNullExclusionMapsEverything()
        {
            Mapper.ClearCache();

            var viaNull = new MeDest();
            Mapper.GetInPlaceMapper(typeof(MeSource), typeof(MeDest), null)(Sample(), viaNull);
            Assert.Equal("note", viaNull.Note);

            var viaEmpty = new MeDest();
            MapExcluding(Sample(), viaEmpty);
            Assert.Equal("note", viaEmpty.Note);
        }

        [Fact]
        public void ANameThatMatchesNothingIsNotAnError()
        {
            Mapper.ClearCache();
            var dest = new MeDest();

            MapExcluding(Sample(), dest, "NoSuchMember");

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
            MapExcluding(Sample(), excludeNote, "Note");
            Assert.Equal("name", excludeNote.Name);
            Assert.Equal("keptNote", excludeNote.Note);

            var excludeName = new MeDest { Name = "keptName", Note = "keptNote" };
            MapExcluding(Sample(), excludeName, "Name");
            Assert.Equal("keptName", excludeName.Name);
            Assert.Equal("note", excludeName.Note);
        }

        [Fact]
        public void TheSameSetInADifferentOrderReusesTheSameDelegate()
        {
            Mapper.ClearCache();

            var first = new MeDest { Name = "a", Note = "b" };
            MapExcluding(Sample(), first, "Name", "Note");

            var second = new MeDest { Name = "a", Note = "b" };
            MapExcluding(Sample(), second, "Note", "Name");

            Assert.Equal("a", second.Name);
            Assert.Equal("b", second.Note);
            Assert.Equal(5, second.Id);
        }

        [Fact]
        public void ExcludingEveryMappableMemberLeavesTheDestinationAlone()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Id = 99, Name = "a", Note = "b" };

            MapExcluding(Sample(), dest, "Id", "Name", "Note");

            Assert.Equal(99, dest.Id);
            Assert.Equal("a", dest.Name);
            Assert.Equal("b", dest.Note);
        }

        [Fact]
        public void ANullSourceLeavesTheDestinationAlone()
        {
            Mapper.ClearCache();
            var dest = new MeDest { Name = "kept" };

            // The accessor hands back a delegate; a null source is the caller's business, and the
            // public Map overload still guards it.
            ((object?)null).Map(dest);

            Assert.Equal("kept", dest.Name);
        }

        [Fact]
        public void ClearCacheDiscardsTheExcludingDelegatesToo()
        {
            // The excluding delegates live in their own cache. A cache that ClearCache does not
            // reach would keep handing out delegates after the caller asked for them to be dropped.
            Mapper.ClearCache();

            var before = new MeDest { Note = "untouched" };
            MapExcluding(Sample(), before, "Note");
            Assert.Equal("untouched", before.Note);

            Mapper.ClearCache();

            var after = new MeDest { Note = "still untouched" };
            MapExcluding(Sample(), after, "Note");
            Assert.Equal("still untouched", after.Note);
            Assert.Equal("name", after.Name);
        }
    }
}
