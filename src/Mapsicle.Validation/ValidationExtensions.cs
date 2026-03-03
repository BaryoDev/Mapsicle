using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Mapsicle.Fluent;

namespace Mapsicle.Validation
{
    /// <summary>
    /// Extension methods for adding validation to Mapsicle mappings.
    /// </summary>
    public static class ValidationExtensions
    {
        private static readonly ConcurrentDictionary<Type, object> _validatorCache = new();

        /// <summary>
        /// Maps the source object to the destination type and validates the result.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static MapperValidationResult<TDest> MapAndValidate<TDest, TValidator>(
            this IMapper mapper,
            object? source)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            var mapped = mapper.Map<TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = validator.Validate(mapped);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Maps the source object to the destination type and validates the result.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static MapperValidationResult<TDest> MapAndValidate<TSource, TDest, TValidator>(
            this IMapper mapper,
            TSource? source)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            var mapped = mapper.Map<TSource, TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = validator.Validate(mapped);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Maps the source object to the destination type and validates using a provided validator instance.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <param name="validator">The validator instance to use.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static MapperValidationResult<TDest> MapAndValidate<TDest>(
            this IMapper mapper,
            object? source,
            IValidator<TDest> validator)
            where TDest : class
        {
            var mapped = mapper.Map<TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validationResult = validator.Validate(mapped);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Maps the source object to the destination type and validates using a provided validator instance.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <param name="validator">The validator instance to use.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static MapperValidationResult<TDest> MapAndValidate<TSource, TDest>(
            this IMapper mapper,
            TSource? source,
            IValidator<TDest> validator)
            where TDest : class
        {
            var mapped = mapper.Map<TSource, TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validationResult = validator.Validate(mapped);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Validates an already-mapped object using the specified validator type.
        /// </summary>
        /// <typeparam name="T">The type to validate.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <returns>A validation result.</returns>
        public static MapperValidationResult<T> Validate<T, TValidator>(this T value)
            where T : class
            where TValidator : IValidator<T>, new()
        {
            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = validator.Validate(value);
            return new MapperValidationResult<T>(value, validationResult);
        }

        /// <summary>
        /// Gets or creates a cached validator instance. Note: validators created this way are
        /// process-wide singletons. For DI-registered validators, use the overloads accepting
        /// IValidator&lt;TDest&gt; directly instead.
        /// </summary>
        private static TValidator GetOrCreateValidator<TValidator>()
            where TValidator : new()
        {
            return (TValidator)_validatorCache.GetOrAdd(typeof(TValidator), _ => new TValidator());
        }

        /// <summary>
        /// Clears the validator cache. Useful for testing scenarios.
        /// </summary>
        public static void ClearValidatorCache() => _validatorCache.Clear();

        #region Async Methods

        /// <summary>
        /// Maps the source object to the destination type and validates the result asynchronously.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static async Task<MapperValidationResult<TDest>> MapAndValidateAsync<TDest, TValidator>(
            this IMapper mapper,
            object? source,
            CancellationToken cancellationToken = default)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            var mapped = mapper.Map<TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = await validator.ValidateAsync(mapped, cancellationToken);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Maps the source object to the destination type and validates the result asynchronously.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static async Task<MapperValidationResult<TDest>> MapAndValidateAsync<TSource, TDest, TValidator>(
            this IMapper mapper,
            TSource? source,
            CancellationToken cancellationToken = default)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            var mapped = mapper.Map<TSource, TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = await validator.ValidateAsync(mapped, cancellationToken);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Maps the source object to the destination type and validates using a provided validator instance asynchronously.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object to map.</param>
        /// <param name="validator">The validator instance to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static async Task<MapperValidationResult<TDest>> MapAndValidateAsync<TDest>(
            this IMapper mapper,
            object? source,
            IValidator<TDest> validator,
            CancellationToken cancellationToken = default)
            where TDest : class
        {
            var mapped = mapper.Map<TDest>(source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validationResult = await validator.ValidateAsync(mapped, cancellationToken);
            return new MapperValidationResult<TDest>(mapped, validationResult);
        }

        /// <summary>
        /// Validates an already-mapped object using the specified validator type asynchronously.
        /// </summary>
        /// <typeparam name="T">The type to validate.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="value">The value to validate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A validation result.</returns>
        public static async Task<MapperValidationResult<T>> ValidateAsync<T, TValidator>(
            this T value,
            CancellationToken cancellationToken = default)
            where T : class
            where TValidator : IValidator<T>, new()
        {
            var validator = GetOrCreateValidator<TValidator>();
            var validationResult = await validator.ValidateAsync(value, cancellationToken);
            return new MapperValidationResult<T>(value, validationResult);
        }

        #endregion

        #region Collection Validation

        /// <summary>
        /// Maps and validates a collection of objects.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source collection.</param>
        /// <returns>A collection validation result.</returns>
        public static CollectionValidationResult<TDest> MapAndValidateAll<TSource, TDest, TValidator>(
            this IMapper mapper,
            IEnumerable<TSource>? source)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            if (source is null)
            {
                return new CollectionValidationResult<TDest>(
                    new List<TDest>(),
                    new List<MapperValidationResult<TDest>>(),
                    true);
            }

            var results = new List<MapperValidationResult<TDest>>();
            var validItems = new List<TDest>();
            var isAllValid = true;

            foreach (var item in source)
            {
                var result = mapper.MapAndValidate<TSource, TDest, TValidator>(item);
                results.Add(result);
                if (result.IsValid && result.Value is not null)
                {
                    validItems.Add(result.Value);
                }
                else
                {
                    isAllValid = false;
                }
            }

            return new CollectionValidationResult<TDest>(validItems, results, isAllValid);
        }

        /// <summary>
        /// Maps and validates a collection of objects asynchronously.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source collection.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A collection validation result.</returns>
        public static async Task<CollectionValidationResult<TDest>> MapAndValidateAllAsync<TSource, TDest, TValidator>(
            this IMapper mapper,
            IEnumerable<TSource>? source,
            CancellationToken cancellationToken = default)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            if (source is null)
            {
                return new CollectionValidationResult<TDest>(
                    new List<TDest>(),
                    new List<MapperValidationResult<TDest>>(),
                    true);
            }

            var results = new List<MapperValidationResult<TDest>>();
            var validItems = new List<TDest>();
            var isAllValid = true;

            foreach (var item in source)
            {
                var result = await mapper.MapAndValidateAsync<TSource, TDest, TValidator>(item, cancellationToken);
                results.Add(result);
                if (result.IsValid && result.Value is not null)
                {
                    validItems.Add(result.Value);
                }
                else
                {
                    isAllValid = false;
                }
            }

            return new CollectionValidationResult<TDest>(validItems, results, isAllValid);
        }

        #endregion
    }

    /// <summary>
    /// Result of validating a collection of objects.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    public class CollectionValidationResult<T>
        where T : class
    {
        /// <summary>
        /// Successfully validated items.
        /// </summary>
        public IReadOnlyList<T> ValidItems { get; }

        /// <summary>
        /// Individual validation results for each item.
        /// </summary>
        public IReadOnlyList<MapperValidationResult<T>> Results { get; }

        /// <summary>
        /// Whether all items passed validation.
        /// </summary>
        public bool IsAllValid { get; }

        /// <summary>
        /// Count of valid items.
        /// </summary>
        public int ValidCount => ValidItems.Count;

        /// <summary>
        /// Count of invalid items.
        /// </summary>
        public int InvalidCount => Results.Count - ValidItems.Count;

        public CollectionValidationResult(
            List<T> validItems,
            List<MapperValidationResult<T>> results,
            bool isAllValid)
        {
            ValidItems = validItems.AsReadOnly();
            Results = results.AsReadOnly();
            IsAllValid = isAllValid;
        }

        /// <summary>
        /// Gets only the failed validation results.
        /// </summary>
        public IEnumerable<MapperValidationResult<T>> GetFailedResults()
        {
            foreach (var result in Results)
            {
                if (!result.IsValid)
                {
                    yield return result;
                }
            }
        }
    }
}
