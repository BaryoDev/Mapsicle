using System;
using Mapsicle.Fluent;
using Xunit;

namespace Mapsicle.Fluent.Tests
{
    public class VafSrc { public int Id { get; set; } public long Count { get; set; } public VafInner? Inner { get; set; } }
    public class VafInner { public string Name { get; set; } = ""; }
    public class VafPrefixDst { public int Id { get; set; } public string IdentityNumber { get; set; } = ""; }
    public class VafNarrowDst { public int Id { get; set; } public int Count { get; set; } }
    public class VafFlatDst { public int Id { get; set; } public string InnerName { get; set; } = ""; }

    [Collection("StaticMapperTests")]
    public class ValidatorAgreementFluentTests
    {
        public ValidatorAgreementFluentTests() => Mapper.ClearCache();

        [Fact]
        public void AssertConfigurationIsValid_MemberSharingOnlyAPrefix_Throws()
        {
            var config = new MapperConfiguration(c => c.CreateMap<VafSrc, VafPrefixDst>());

            var ex = Assert.Throws<InvalidOperationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains("IdentityNumber", ex.Message);
        }

        [Fact]
        public void AssertConfigurationIsValid_NarrowingMember_Throws()
        {
            var config = new MapperConfiguration(c => c.CreateMap<VafSrc, VafNarrowDst>());

            var ex = Assert.Throws<InvalidOperationException>(() => config.AssertConfigurationIsValid());
            Assert.Contains("'Count'", ex.Message);
        }

        [Fact]
        public void AssertConfigurationIsValid_FlattenedMember_Passes()
        {
            var config = new MapperConfiguration(c => c.CreateMap<VafSrc, VafFlatDst>());

            config.AssertConfigurationIsValid();
            Assert.Equal("n", config.CreateMapper().Map<VafFlatDst>(new VafSrc { Inner = new VafInner { Name = "n" } })!.InnerName);
        }

        [Fact]
        public void AssertConfigurationIsValid_CustomMappingOrIgnore_Passes()
        {
            var config = new MapperConfiguration(c =>
            {
                c.CreateMap<VafSrc, VafPrefixDst>().ForMember(d => d.IdentityNumber, o => o.MapFrom(s => "x" + s.Id));
                c.CreateMap<VafSrc, VafNarrowDst>().ForMember(d => d.Count, o => o.Ignore());
            });

            config.AssertConfigurationIsValid();
        }
    }
}
