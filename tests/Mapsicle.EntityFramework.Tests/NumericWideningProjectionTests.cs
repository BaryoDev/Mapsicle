using System;
using System.Linq;
using Mapsicle;
using Mapsicle.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Mapsicle.EntityFramework.Tests
{
    public class NwStock
    {
        public int Id { get; set; }
        public int Qty { get; set; }
        public int? Reserved { get; set; }
        public int? Missing { get; set; }
        public float Weight { get; set; }
        public long Big { get; set; }
    }

    public class NwStockDto
    {
        public long Qty { get; set; }
        public long? Reserved { get; set; }
        public long Missing { get; set; } = -1;
        public double Weight { get; set; }
        public int Big { get; set; }
    }

    public class NwContext : DbContext
    {
        public NwContext(DbContextOptions<NwContext> options) : base(options) { }
        public DbSet<NwStock> Stock => Set<NwStock>();
    }

    [Collection("StaticMapperTests")]
    public class NumericWideningProjectionTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly NwContext _db;

        public NumericWideningProjectionTests()
        {
            Mapper.ClearCache();
            QueryableExtensions.ClearProjectionCache();
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _db = new NwContext(new DbContextOptionsBuilder<NwContext>().UseSqlite(_connection).Options);
            _db.Database.EnsureCreated();
            _db.Stock.Add(new NwStock { Qty = 5, Reserved = 2, Missing = null, Weight = 1.5f, Big = 7 });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        [Fact]
        public void ProjectTo_WidensIntToLong_LikeMapTo()
        {
            var projected = _db.Stock.ProjectTo<NwStockDto>().Single();
            var mapped = _db.Stock.AsNoTracking().Single().MapTo<NwStockDto>()!;

            Assert.Equal(5, projected.Qty);
            Assert.Equal(mapped.Qty, projected.Qty);
        }

        [Fact]
        public void ProjectTo_WidensNullableAndFloat_LikeMapTo()
        {
            var projected = _db.Stock.ProjectTo<NwStockDto>().Single();
            var mapped = _db.Stock.AsNoTracking().Single().MapTo<NwStockDto>()!;

            Assert.Equal(2L, projected.Reserved);
            Assert.Equal(1.5, projected.Weight);
            Assert.Equal(mapped.Reserved, projected.Reserved);
            Assert.Equal(mapped.Weight, projected.Weight);
            Assert.Equal(mapped.Missing, projected.Missing);
        }

        [Fact]
        public void ProjectTo_LeavesNarrowingUnmapped_LikeMapTo()
        {
            var projected = _db.Stock.ProjectTo<NwStockDto>().Single();

            Assert.Equal(0, projected.Big);
        }
    }
}
