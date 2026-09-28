using System;
using Mapsicle.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mapsicle.DependencyInjection.Tests
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddMapsicle_RegistersAMapper_WithNoConfigurationAtAll()
        {
            // The whole point of the package. One call, no configure lambda, no CreateMap per pair.
            var provider = new ServiceCollection().AddMapsicle().BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapperInstance>();
            var dto = mapper.MapTo<UserDto>(new User { Id = 7, Name = "arnel" });

            Assert.NotNull(dto);
            Assert.Equal(7, dto!.Id);
            Assert.Equal("arnel", dto.Name);
        }

        [Fact]
        public void TheRegisteredMapper_IsASingleton()
        {
            // A mapper compiles a delegate per type pair on first use. A scoped or transient
            // registration would throw that away on every request, which is the difference between
            // a warm map and a cold one on every call.
            var provider = new ServiceCollection().AddMapsicle().BuildServiceProvider();

            Assert.Same(
                provider.GetRequiredService<IMapperInstance>(),
                provider.GetRequiredService<IMapperInstance>());
        }

        [Fact]
        public void AddMapsicle_WithOptions_AppliesThem()
        {
            var provider = new ServiceCollection()
                .AddMapsicle(o => o.MaxDepth = 2)
                .BuildServiceProvider();

            var mapper = provider.GetRequiredService<IMapperInstance>();

            // A cycle, because MaxDepth decides when the mapper starts checking for a repeat, not
            // where it stops. An acyclic chain maps whole at any setting, as it does on the static
            // mapper. With 2 the repeat is caught after four nodes; the default of 32 gives 34.
            var root = new Node { Name = "0" };
            root.Child = new Node { Name = "1", Child = root };

            var result = mapper.MapTo<NodeDto>(root);

            Assert.NotNull(result);
            Assert.NotNull(result!.Child?.Child?.Child);
            Assert.Null(result!.Child?.Child?.Child?.Child);
        }

        [Fact]
        public void AddMapsicle_WithOptions_StillMapsNormally()
        {
            // Positive control for the test above: a depth ceiling that broke ordinary mapping
            // would satisfy it just as well.
            var provider = new ServiceCollection()
                .AddMapsicle(o => o.MaxCacheSize = 50)
                .BuildServiceProvider();

            var dto = provider.GetRequiredService<IMapperInstance>()
                .MapTo<UserDto>(new User { Id = 1, Name = "x" });

            Assert.Equal("x", dto!.Name);
        }

        [Fact]
        public void AddMapsicle_RejectsANullServiceCollection()
        {
            Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddMapsicle());
        }

        [Fact]
        public void AddMapsicle_RejectsANullConfigureCallback()
        {
            Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddMapsicle(null!));
        }

        public class User { public int Id { get; set; } public string? Name { get; set; } }
        public class UserDto { public int Id { get; set; } public string? Name { get; set; } }
        public class Node { public string? Name { get; set; } public Node? Child { get; set; } }
        public class NodeDto { public string? Name { get; set; } public NodeDto? Child { get; set; } }
    }
}
