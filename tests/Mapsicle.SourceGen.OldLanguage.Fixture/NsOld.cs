using Mapsicle;

[assembly: MapsicleGenerate(typeof(Mapsicle.SourceGen.OldLanguage.Fixture.NsOldBox), typeof(Mapsicle.SourceGen.OldLanguage.Fixture.NsOldBoxDto))]

namespace Mapsicle.SourceGen.OldLanguage.Fixture
{
    public class NsOldBox { public int X { get; set; } public string Name { get; set; } }
    public class NsOldBoxDto { public long X { get; set; } public string Name { get; set; } }

    /// <summary>The call a consumer on this target would write, compiled with C# 7.3.</summary>
    public static class NsOldCalls
    {
        public static NsOldBoxDto Typed(NsOldBox box) => box.MapTo<NsOldBoxDto>();
    }
}
