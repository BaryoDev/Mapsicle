using System;
using System.Collections.Generic;
using Mapsicle;

[assembly: MapsicleGenerate(typeof(AotOrder), typeof(AotOrderDto))]

// Published with NativeAOT in CI and run. Under JIT every one of these passed while the published
// binary threw on a declared list and returned null for an undeclared pair, so only the binary counts.
var failures = 0;

void Check(string name, Func<string?> actual, string expected)
{
    string? got;
    try { got = actual(); }
    catch (Exception e) { got = $"{e.GetType().Name}: {e.Message}"; }
    var ok = got == expected;
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {got}{(ok ? "" : $" (expected {expected})")}");
}

void Refused(string name, Func<object?> map, params string[] messageMustContain)
{
    string got;
    try { got = $"returned {map() ?? "null"}"; }
    catch (NotSupportedException e)
    {
        var missing = Array.Find(messageMustContain, part => !e.Message.Contains(part));
        got = missing is null ? "NotSupportedException" : $"NotSupportedException without \"{missing}\": {e.Message}";
    }
    catch (Exception e) { got = e.GetType().Name; }
    var ok = got == "NotSupportedException";
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {got}");
}

var order = new AotOrder { Id = 7, Name = "Ana" };

Check("declared typed", () => order.MapTo<AotOrder, AotOrderDto>()?.Name, "Ana");
Check("declared untyped", () => ((object)order).MapTo<AotOrderDto>()?.Name, "Ana");
Check("declared list", () => new List<AotOrder> { order, order }.MapTo<AotOrderDto>()[1].Name, "Ana");
Check("declared array", () => new[] { order }.MapTo<AotOrderDto>()[0].Name, "Ana");
Check("scalar widening", () => ((object)5).MapTo<long>().ToString(), "5");
Check("typed scalar widening", () => 5.MapTo<int, long>().ToString(), "5");
Check("factory scalar widening", () => { using var f = MapperFactory.Create(); return f.MapTo<long>(5).ToString(); }, "5");

Refused("undeclared MapTo", () => new AotPet { Name = "Rex" }.MapTo<AotPetDto>(),
    "AotPet", "AotPetDto", "[assembly: MapsicleGenerate(typeof(AotPet), typeof(AotPetDto))]");
Refused("undeclared Map(existing)", () => new AotPet { Name = "Rex" }.Map(new AotPetDto()));
Refused("declared Map(existing)", () => order.Map(new AotOrderDto()));
Refused("MapperFactory", () => { using var f = MapperFactory.Create(); return f.MapTo<AotOrderDto>(order); });
Refused("declared list as object", () => ((object)new List<AotOrder> { order }).MapTo<List<AotOrderDto>>());
Refused("dictionary", () => new Dictionary<string, object?> { ["Name"] = "Rex" }.MapTo<AotPetDto>(),
    "has no generated form");

Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
return failures == 0 ? 0 : 1;

public class AotOrder { public int Id { get; set; } public string? Name { get; set; } }
public class AotOrderDto { public int Id { get; set; } public string? Name { get; set; } }
public class AotPet { public string? Name { get; set; } }
public class AotPetDto { public string? Name { get; set; } }
