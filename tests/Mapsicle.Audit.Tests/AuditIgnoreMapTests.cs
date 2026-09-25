using System.Linq;
using Mapsicle;
using Mapsicle.Audit;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Audit.Tests
{
    public class AuIgUser
    {
        public int Id { get; set; }
        public string Password { get; set; } = "";
        public bool IsAdmin { get; set; }
    }

    public class AuIgUserDto
    {
        public int Id { get; set; }
        [IgnoreMap] public string? Password { get; set; }
        public bool IsAdmin { get; set; }
    }

    [Collection("StaticMapperTests")]
    public class AuditIgnoreMapTests
    {
        public AuditIgnoreMapTests() => Mapper.ClearCache();

        private static AuIgUser User() => new AuIgUser { Id = 1, Password = "hunter2", IsAdmin = true };

        private static PropertyMappingInfo Entry(MappingAudit audit, string name) =>
            audit.PropertyMappings.Single(p => p.PropertyName == name);

        [Fact]
        public void Control_MapWithAudit_ReportsPlainMemberAsMapped()
        {
            var result = User().MapWithAudit<AuIgUserDto>();
            Assert.Null(result.Value!.Password);
            Assert.True(Entry(result.Audit, "Id").WasMapped);
            Assert.Equal(1, Entry(result.Audit, "Id").SourceValue);
        }

        [Fact]
        public void MapWithAudit_IgnoredMember_IsReportedAsNotMapped()
        {
            var audit = User().MapWithAudit<AuIgUserDto>().Audit;
            Assert.False(Entry(audit, "Password").WasMapped);
            Assert.Contains("Password", audit.UnmappedProperties);
        }

        [Fact]
        public void MapWithAudit_IgnoredMember_DoesNotRecordTheSourceValue()
        {
            var audit = User().MapWithAudit<AuIgUserDto>().Audit;
            Assert.DoesNotContain(audit.PropertyMappings, p => Equals(p.SourceValue, "hunter2"));
            Assert.Null(Entry(audit, "Password").SourcePropertyName);
        }

        [Fact]
        public void MapperOverload_IgnoreMapMember_DoesNotRecordTheSourceValue()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<AuIgUser, AuIgUserDto>()).CreateMapper();
            var audit = mapper.MapWithAudit<AuIgUser, AuIgUserDto>(User()).Audit;
            Assert.False(Entry(audit, "Password").WasMapped);
            Assert.DoesNotContain(audit.PropertyMappings, p => Equals(p.SourceValue, "hunter2"));
        }

        [Fact]
        public void MapperOverload_FluentIgnoredMember_IsReportedAsNotMapped()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<AuIgUser, AuIgUserDto>().ForMember(d => d.IsAdmin, o => o.Ignore())).CreateMapper();

            var result = mapper.MapWithAudit<AuIgUser, AuIgUserDto>(User());

            Assert.False(result.Value!.IsAdmin);
            Assert.False(Entry(result.Audit, "IsAdmin").WasMapped);
            Assert.Null(Entry(result.Audit, "IsAdmin").SourceValue);
        }

        [Fact]
        public void Control_MapperOverload_ReportsMembersTheConfigurationDoesNotIgnore()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<AuIgUser, AuIgUserDto>()).CreateMapper();
            var audit = mapper.MapWithAudit<AuIgUser, AuIgUserDto>(User()).Audit;
            Assert.True(Entry(audit, "IsAdmin").WasMapped);
            Assert.Equal(true, Entry(audit, "IsAdmin").SourceValue);
        }
    }
}
