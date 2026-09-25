using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentValidation;
using Mapsicle.Fluent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mapsicle.AspNetCore.Tests;

/// <summary>
/// Covers the endpoint filters against a real TestServer, since the previous behaviour (replacing
/// the handler's TSource argument with a TDest, and calling next() unvalidated when only
/// IMapperInstance was registered) only broke once ASP.NET Core actually bound and invoked a
/// minimal API handler. Unit tests calling the filter delegate directly never exercised binding.
/// </summary>
[Collection("StaticMapperTests")]
public class EndpointFilterTests
{
    public class EfRequest
    {
        public string Name { get; set; } = "";
    }

    public class EfCommand
    {
        public string Name { get; set; } = "";
    }

    public class EfCommandValidator : AbstractValidator<EfCommand>
    {
        public EfCommandValidator() => RuleFor(c => c.Name).NotEmpty();
    }

    private enum MapperRegistration
    {
        None,
        FluentIMapper,
        MapperInstance,
        MarkerInstance,
    }

    // Returns a value the static Mapper never would, so a test can tell which mapper ran.
    private sealed class MarkerMapperInstance : IMapperInstance
    {
        public T? MapTo<T>(object? source) => (T)(object)new EfCommand { Name = "from-instance" };
        public System.Collections.Generic.List<T> MapTo<T>(System.Collections.IEnumerable? source) => new();
        public TDest Map<TDest>(object? source, TDest destination) => destination;
        public void ClearCache() { }
        public MapperCacheInfo CacheInfo() => default!;
        public void Dispose() { }
    }

    private static async Task<(WebApplication App, HttpClient Client)> Start(
        MapperRegistration registration,
        Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        if (registration == MapperRegistration.FluentIMapper)
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<EfRequest, EfCommand>());
            builder.Services.AddSingleton<IMapper>(config.CreateMapper());
        }
        else if (registration == MapperRegistration.MapperInstance)
        {
            builder.Services.AddSingleton<IMapperInstance>(_ => MapperFactory.Create());
        }
        else if (registration == MapperRegistration.MarkerInstance)
        {
            builder.Services.AddSingleton<IMapperInstance>(new MarkerMapperInstance());
        }

        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static void MapMappedEndpoint(WebApplication app)
        => app.MapPost("/mapped", (EfRequest req, HttpContext ctx) =>
            Results.Ok(ctx.GetMappedRequest<EfCommand>().Name))
            .WithMappedRequest<EfRequest, EfCommand>();

    private static void MapValidatedEndpoint(WebApplication app)
        => app.MapPost("/validated", (EfRequest req, HttpContext ctx) =>
            Results.Ok(ctx.GetMappedRequest<EfCommand>().Name))
            .WithValidatedMapping<EfRequest, EfCommand, EfCommandValidator>();

    [Fact]
    public async Task WithMappedRequest_ValidBody_Returns200AndHandlerSeesMappedValue()
    {
        var (app, client) = await Start(MapperRegistration.FluentIMapper, MapMappedEndpoint);
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/mapped", new EfRequest { Name = "ok" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithValidatedMapping_ValidBody_Returns200AndHandlerSeesMappedValue()
    {
        var (app, client) = await Start(MapperRegistration.FluentIMapper, MapValidatedEndpoint);
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/validated", new EfRequest { Name = "ok" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithValidatedMapping_InvalidBody_ReturnsExactly400WithErrors()
    {
        var (app, client) = await Start(MapperRegistration.FluentIMapper, MapValidatedEndpoint);
        await using var appScope = app;

        var response = await client.PostAsJsonAsync("/validated", new EfRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task WithValidatedMapping_NoIMapper_IMapperInstanceRegistered_StillValidates()
    {
        var (app, client) = await Start(MapperRegistration.MapperInstance, MapValidatedEndpoint);
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/validated", new EfRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithValidatedMapping_NothingRegistered_StillValidatesViaStaticMapper()
    {
        var (app, client) = await Start(MapperRegistration.None, MapValidatedEndpoint);
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/validated", new EfRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithMappedRequest_NoIMapper_UsesRegisteredIMapperInstance()
    {
        var (app, client) = await Start(MapperRegistration.MarkerInstance, MapMappedEndpoint);
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/mapped", new EfRequest { Name = "from-body" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("from-instance", await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public void GetMappedRequest_WithoutFilter_ThrowsInvalidOperationException()
    {
        var context = new DefaultHttpContext();

        Assert.Throws<InvalidOperationException>(() => context.GetMappedRequest<EfCommand>());
    }
}
