using System;
using System.Collections.Concurrent;
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

        private static TValidator GetOrCreateValidator<TValidator>()
            where TValidator : new()
        {
            return (TValidator)_validatorCache.GetOrAdd(typeof(TValidator), _ => new TValidator());
        }

        /// <summary>
        /// Clears the validator cache. Useful for testing scenarios.
        /// </summary>
        public static void ClearValidatorCache() => _validatorCache.Clear();
    }
}
