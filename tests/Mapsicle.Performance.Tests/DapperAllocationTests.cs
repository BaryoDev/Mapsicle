using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Mapsicle.Dapper;
using Mapsicle.Fluent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mapsicle.Performance.Tests
{
    /// <summary>
    /// The Dapper extensions measured against the same query and loop written by hand.
    /// </summary>
    /// <remarks>
    /// The database allocates far more per query than the mapping does, so an absolute budget here
    /// would be a budget on SQLite. Each test runs the hand written equivalent against the same
    /// connection and asserts the extension costs nothing on top of it.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public sealed class DapperAllocationTests : IDisposable
    {
        private const string Sql = "SELECT Id, Name, Score FROM DapperAllocRows";

        // Integer division over the iteration count leaves a few bytes of rounding either way.
        private const long Tolerance = 8;

        private readonly SqliteConnection _connection;

        public class DapperAllocRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public long Score { get; set; }
        }

        public class DapperAllocRowDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public long Score { get; set; }
        }

        public DapperAllocationTests()
        {
            Mapper.ClearCache();
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.Execute(@"
                CREATE TABLE DapperAllocRows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Score INTEGER NOT NULL);
                INSERT INTO DapperAllocRows (Id, Name, Score) VALUES (1, 'a', 10), (2, 'b', 20), (3, 'c', 30), (4, 'd', 40), (5, 'e', 50);");
        }

        public void Dispose()
        {
            _connection.Dispose();
            Mapper.ClearCache();
        }

        private static MapperConfiguration NewConfiguration() =>
            new(cfg => cfg.CreateMap<DapperAllocRow, DapperAllocRowDto>());

        private static long BytesPerCall(Action action, int iterations = 2_000)
        {
            action();
            action();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }
            var after = GC.GetAllocatedBytesForCurrentThread();

            return (after - before) / iterations;
        }

        private List<DapperAllocRowDto> HandWritten()
        {
            var rows = _connection.Query<DapperAllocRow>(Sql).AsList();
            var target = new List<DapperAllocRowDto>(rows.Count);
            for (var i = 0; i < rows.Count; i++) target.Add(rows[i].MapTo<DapperAllocRowDto>()!);
            return target;
        }

        private List<DapperAllocRowDto> HandWritten(IMapper mapper)
        {
            var rows = _connection.Query<DapperAllocRow>(Sql).AsList();
            var target = new List<DapperAllocRowDto>(rows.Count);
            for (var i = 0; i < rows.Count; i++) target.Add(mapper.Map<DapperAllocRow, DapperAllocRowDto>(rows[i])!);
            return target;
        }

        private void AssertNoOverhead(string path, long extension, long handWritten)
        {
            Assert.True(
                extension - handWritten <= Tolerance,
                $"{path} allocated {extension} B/call against {handWritten} B/call for the same query and loop written by hand");
        }

        [Fact]
        public void HandWrittenBaseline_MapsEveryRow()
        {
            var result = HandWritten();

            Assert.Equal(5, result.Count);
            Assert.Equal("e", result[4].Name);
            Assert.Equal(50, result[4].Score);
        }

        [Fact]
        public void QueryAndMap_AllocatesNoMoreThanTheHandWrittenLoop()
        {
            var extension = BytesPerCall(() => _connection.QueryAndMap<DapperAllocRow, DapperAllocRowDto>(Sql));
            var handWritten = BytesPerCall(() => HandWritten());

            AssertNoOverhead("QueryAndMap", extension, handWritten);
        }

        [Fact]
        public void QueryAndMap_WithMapper_AllocatesNoMoreThanTheHandWrittenLoop()
        {
            var mapper = NewConfiguration().CreateMapper();

            var extension = BytesPerCall(() => _connection.QueryAndMap<DapperAllocRow, DapperAllocRowDto>(Sql, mapper));
            var handWritten = BytesPerCall(() => HandWritten(mapper));

            AssertNoOverhead("QueryAndMap(IMapper)", extension, handWritten);
        }

        [Fact]
        public void QueryAndMap_WithConfiguration_DoesNotRebuildTheMapperPerCall()
        {
            var config = NewConfiguration();
            var mapper = config.CreateMapper();

            var extension = BytesPerCall(() => _connection.QueryAndMap<DapperAllocRow, DapperAllocRowDto>(Sql, config));
            var handWritten = BytesPerCall(() => HandWritten(mapper));

            AssertNoOverhead("QueryAndMap(MapperConfiguration)", extension, handWritten);
        }

        [Fact]
        public void QueryAndMapAsync_WithMapper_AllocatesNoMoreThanTheHandWrittenLoop()
        {
            var mapper = NewConfiguration().CreateMapper();

            var extension = BytesPerCall(() => _connection.QueryAndMapAsync<DapperAllocRow, DapperAllocRowDto>(Sql, mapper).GetAwaiter().GetResult());
            var handWritten = BytesPerCall(() => HandWrittenAsync(mapper).GetAwaiter().GetResult());

            AssertNoOverhead("QueryAndMapAsync(IMapper)", extension, handWritten);
        }

        private async Task<List<DapperAllocRowDto>> HandWrittenAsync(IMapper mapper)
        {
            var rows = (await _connection.QueryAsync<DapperAllocRow>(Sql)).AsList();
            var target = new List<DapperAllocRowDto>(rows.Count);
            for (var i = 0; i < rows.Count; i++) target.Add(mapper.Map<DapperAllocRow, DapperAllocRowDto>(rows[i])!);
            return target;
        }

        [Fact]
        public void MapTo_WithMapper_AllocatesOnlyTheListAndItems()
        {
            var mapper = NewConfiguration().CreateMapper();
            var rows = _connection.Query<DapperAllocRow>(Sql).AsList();

            var extension = BytesPerCall(() => rows.MapTo<DapperAllocRow, DapperAllocRowDto>(mapper));
            var handWritten = BytesPerCall(() =>
            {
                var target = new List<DapperAllocRowDto>(rows.Count);
                for (var i = 0; i < rows.Count; i++) target.Add(mapper.Map<DapperAllocRow, DapperAllocRowDto>(rows[i])!);
            });

            AssertNoOverhead("MapTo(IMapper)", extension, handWritten);
        }
    }
}
