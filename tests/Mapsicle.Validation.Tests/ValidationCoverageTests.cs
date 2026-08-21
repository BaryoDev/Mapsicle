using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Mapsicle.Fluent;
using Mapsicle.Validation;
using Xunit;

namespace Mapsicle.Validation.Tests;

/// <summary>
/// The entry points the original thirteen tests never reached.
/// </summary>
/// <remarks>
/// Mapsicle.Validation measured 12.7% line coverage, and coverage alone would be arguable if the
/// gap had not been demonstrated by mutation: inverting every <c>if (source is null)</c> guard in
/// the package changed nothing, all thirteen tests still passed. A guard nothing exercises is a
/// guard that is not there.
///
/// So these cover, deliberately: the null and empty inputs where that mutant lived, all six async
/// entry points, both collection entry points, and the arithmetic on
/// <see cref="CollectionValidationResult{T}"/> that reports how much of a batch survived. A caller
/// deciding what to persist from a partially valid batch is relying on those counts being right.
/// </remarks>
public class ValidationCoverageTests
{
    private static IMapper NewMapper() =>
        new MapperConfiguration(c => c.CreateMap<CoverageSource, CoverageDto>()).CreateMapper();

    // ---- The guards where the mutant survived ------------------------------------------------

    [Fact]
    public void MapAndValidate_WithANullSource_ReportsFailureRatherThanThrowing()
    {
        var result = NewMapper().MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(null);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void MapAndValidateAll_WithANullCollection_ReturnsAnEmptyValidResult()
    {
        var result = NewMapper().MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(null);

        Assert.True(result.IsAllValid);
        Assert.Empty(result.ValidItems);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.ValidCount);
        Assert.Equal(0, result.InvalidCount);
    }

    [Fact]
    public void MapAndValidateAll_WithAnEmptyCollection_ReturnsAnEmptyValidResult()
    {
        var result = NewMapper()
            .MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(Array.Empty<CoverageSource>());

        Assert.True(result.IsAllValid);
        Assert.Equal(0, result.ValidCount);
    }

    // ---- Collections, including the partially valid batch --------------------------------------

    [Fact]
    public void MapAndValidateAll_WithEveryItemValid_ReportsAllValid()
    {
        var source = new[]
        {
            new CoverageSource { Id = 1, Name = "Ada", Email = "ada@example.com" },
            new CoverageSource { Id = 2, Name = "Grace", Email = "grace@example.com" },
        };

        var result = NewMapper().MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.True(result.IsAllValid);
        Assert.Equal(2, result.ValidCount);
        Assert.Equal(0, result.InvalidCount);
        Assert.Empty(result.GetFailedResults());
    }

    /// <summary>
    /// The case a caller actually has to reason about: some of the batch is usable.
    /// </summary>
    [Fact]
    public void MapAndValidateAll_WithAPartiallyValidBatch_SeparatesTheUsableItems()
    {
        var source = new[]
        {
            new CoverageSource { Id = 1, Name = "Ada", Email = "ada@example.com" },
            new CoverageSource { Id = 2, Name = "", Email = "not-an-email" },
            new CoverageSource { Id = 3, Name = "Grace", Email = "grace@example.com" },
        };

        var result = NewMapper().MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.False(result.IsAllValid);
        Assert.Equal(2, result.ValidCount);
        Assert.Equal(1, result.InvalidCount);
        Assert.Equal(3, result.Results.Count);

        // ValidItems must hold the items that passed, not merely the right number of them.
        Assert.Equal(new[] { "Ada", "Grace" }, result.ValidItems.Select(i => i.Name));

        var failed = result.GetFailedResults().ToList();
        Assert.Single(failed);
        Assert.NotEmpty(failed[0].Errors);
    }

    /// <summary>
    /// ValidCount and InvalidCount are derived differently, so they can disagree.
    /// </summary>
    /// <remarks>
    /// ValidCount reads ValidItems.Count while InvalidCount is Results.Count - ValidItems.Count.
    /// Nothing forces them to sum to the batch size, so it is asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void CollectionResult_CountsAccountForEveryItem()
    {
        var source = Enumerable.Range(0, 10)
            .Select(i => new CoverageSource
            {
                Id = i,
                Name = i % 3 == 0 ? "" : $"n{i}",
                Email = $"n{i}@example.com",
            })
            .ToArray();

        var result = NewMapper().MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.Equal(10, result.ValidCount + result.InvalidCount);
        Assert.Equal(10, result.Results.Count);
        Assert.Equal(result.InvalidCount, result.GetFailedResults().Count());
    }

    // ---- The async surface ---------------------------------------------------------------------

    [Fact]
    public async Task MapAndValidateAsync_AgreesWithTheSynchronousResult()
    {
        var mapper = NewMapper();
        var source = new CoverageSource { Id = 1, Name = "Ada", Email = "ada@example.com" };

        var sync = mapper.MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(source);
        var async = await mapper.MapAndValidateAsync<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.Equal(sync.IsValid, async.IsValid);
        Assert.Equal(sync.Value!.Name, async.Value!.Name);
    }

    [Fact]
    public async Task MapAndValidateAsync_ReportsTheSameErrorsAsTheSynchronousPath()
    {
        var mapper = NewMapper();
        var source = new CoverageSource { Id = 1, Name = "", Email = "nope" };

        var sync = mapper.MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(source);
        var async = await mapper.MapAndValidateAsync<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.False(async.IsValid);
        Assert.Equal(
            sync.Errors.Select(e => e.PropertyName).OrderBy(n => n),
            async.Errors.Select(e => e.PropertyName).OrderBy(n => n));
    }

    [Fact]
    public async Task MapAndValidateAllAsync_AgreesWithTheSynchronousCollectionResult()
    {
        var mapper = NewMapper();
        var source = new[]
        {
            new CoverageSource { Id = 1, Name = "Ada", Email = "ada@example.com" },
            new CoverageSource { Id = 2, Name = "", Email = "bad" },
        };

        var sync = mapper.MapAndValidateAll<CoverageSource, CoverageDto, CoverageValidator>(source);
        var async = await mapper.MapAndValidateAllAsync<CoverageSource, CoverageDto, CoverageValidator>(source);

        Assert.Equal(sync.ValidCount, async.ValidCount);
        Assert.Equal(sync.InvalidCount, async.InvalidCount);
        Assert.Equal(sync.IsAllValid, async.IsAllValid);
    }

    [Fact]
    public async Task ValidateAsync_ValidatesAnExistingObject()
    {
        var valid = await new CoverageDto { Id = 1, Name = "Ada", Email = "ada@example.com" }
            .ValidateAsync<CoverageDto, CoverageValidator>();
        var invalid = await new CoverageDto { Id = 2, Name = "", Email = "nope" }
            .ValidateAsync<CoverageDto, CoverageValidator>();

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public async Task MapAndValidateAsync_WithANullSource_ReportsFailure()
    {
        var result = await NewMapper()
            .MapAndValidateAsync<CoverageSource, CoverageDto, CoverageValidator>(null);

        Assert.False(result.IsValid);
    }

    // ---- The result type itself -----------------------------------------------------------------

    [Fact]
    public void GetValueOrThrow_CarriesTheFailuresIntoTheException()
    {
        var result = NewMapper()
            .MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(
                new CoverageSource { Id = 1, Name = "", Email = "nope" });

        var ex = Assert.Throws<ValidationException>(() => result.GetValueOrThrow());

        // An exception that says only "validation failed" makes a caller re-run the validation to
        // find out what happened.
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void ErrorsByProperty_GroupsEveryFailure()
    {
        var result = NewMapper()
            .MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(
                new CoverageSource { Id = 1, Name = "", Email = "nope" });

        var grouped = result.ErrorsByProperty;

        Assert.Equal(
            result.Errors.Count,
            grouped.Sum(g => g.Value.Count()));
    }

    [Fact]
    public void ClearValidatorCache_DoesNotBreakLaterValidation()
    {
        var mapper = NewMapper();
        _ = mapper.MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(
            new CoverageSource { Id = 1, Name = "Ada", Email = "ada@example.com" });

        ValidationExtensions.ClearValidatorCache();

        var after = mapper.MapAndValidate<CoverageSource, CoverageDto, CoverageValidator>(
            new CoverageSource { Id = 2, Name = "Grace", Email = "grace@example.com" });

        Assert.True(after.IsValid);
        Assert.Equal("Grace", after.Value!.Name);
    }

    #region Types

    public class CoverageSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class CoverageDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class CoverageValidator : AbstractValidator<CoverageDto>
    {
        public CoverageValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Email).EmailAddress();
        }
    }

    #endregion
}
