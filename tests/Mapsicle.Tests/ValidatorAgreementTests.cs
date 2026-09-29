using Xunit;

namespace Mapsicle.Tests
{
    public class VaLeaf { public string Iso { get; set; } = ""; }
    public class VaMiddle { public VaLeaf? Leaf { get; set; } }
    public class VaOuter { public VaMiddle? Middle { get; set; } }
    public class VaDeepSrc { public VaOuter? Outer { get; set; } }
    public class VaDeepDst { public string OuterMiddleLeafIso { get; set; } = ""; }

    public class VaLongSrc { public long Count { get; set; } }
    public class VaIntDst { public int Count { get; set; } }
    public class VaLongDst { public long Count { get; set; } }

    /// <summary>
    /// AssertMappingValid has to agree with the mapper in both directions: it may not reject a member
    /// the mapper fills, and it may not accept one the mapper drops.
    /// </summary>
    [Collection("StaticMapperTests")]
    public class ValidatorAgreementTests
    {
        public ValidatorAgreementTests() => Mapper.ClearCache();

        [Fact]
        public void AssertMappingValid_DeepFlattenedMember_Passes()
        {
            var src = new VaDeepSrc { Outer = new VaOuter { Middle = new VaMiddle { Leaf = new VaLeaf { Iso = "PH" } } } };
            Assert.Equal("PH", src.MapTo<VaDeepDst>()!.OuterMiddleLeafIso);

            Mapper.AssertMappingValid<VaDeepSrc, VaDeepDst>();
        }

        [Fact]
        public void AssertMappingValid_NarrowingMember_Throws()
        {
            Assert.Equal(0, new VaLongSrc { Count = 5 }.MapTo<VaIntDst>()!.Count);

            var ex = Assert.Throws<System.InvalidOperationException>(() => Mapper.AssertMappingValid<VaLongSrc, VaIntDst>());
            Assert.Contains("Count", ex.Message);
        }

        [Fact]
        public void AssertMappingValid_WideningMember_Passes()
        {
            Mapper.AssertMappingValid<VaIntDst, VaLongDst>();
        }
    }
}
