using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Mapsicle.Fluent;

namespace Mapsicle.DataAnnotations
{
    /// <summary>
    /// Extension methods for DataAnnotations validation integration with Mapsicle.
    /// </summary>
    public static class DataAnnotationsExtensions
    {
        #region MapAndValidate

        /// <summary>
        /// Maps the source object to the destination type and validates using DataAnnotations.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static DataAnnotationsValidationResult<TDest> MapAndValidateAnnotations<TDest>(
            this object? source)
            where TDest : class
        {
            if (source is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Source cannot be null"));
            }

            var mapped = source.MapTo<TDest>();
            if (mapped is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Mapping returned null"));
            }

            return ValidateObject(mapped);
        }

        /// <summary>
        /// Maps the source object to the destination type using IMapper and validates using DataAnnotations.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static DataAnnotationsValidationResult<TDest> MapAndValidateAnnotations<TDest>(
            this IMapper mapper,
            object? source)
            where TDest : class
        {
            if (source is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Source cannot be null"));
            }

            var mapped = mapper.Map<TDest>(source);
            if (mapped is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Mapping returned null"));
            }

            return ValidateObject(mapped);
        }

        /// <summary>
        /// Maps the source object to the destination type using IMapper and validates using DataAnnotations.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <returns>A validation result containing the mapped value and any validation errors.</returns>
        public static DataAnnotationsValidationResult<TDest> MapAndValidateAnnotations<TSource, TDest>(
            this IMapper mapper,
            TSource? source)
            where TDest : class
        {
            if (source is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Source cannot be null"));
            }

            var mapped = mapper.Map<TSource, TDest>(source);
            if (mapped is null)
            {
                return DataAnnotationsValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult("Mapping returned null"));
            }

            return ValidateObject(mapped);
        }

        #endregion

        #region Validate Only

        /// <summary>
        /// Validates an object using DataAnnotations.
        /// </summary>
        /// <typeparam name="T">The type to validate.</typeparam>
        /// <param name="value">The object to validate.</param>
        /// <returns>A validation result.</returns>
        public static DataAnnotationsValidationResult<T> ValidateAnnotations<T>(this T value)
            where T : class
        {
            return ValidateObject(value);
        }

        /// <summary>
        /// Checks if an object is valid according to DataAnnotations.
        /// </summary>
        /// <typeparam name="T">The type to validate.</typeparam>
        /// <param name="value">The object to validate.</param>
        /// <returns>True if the object is valid.</returns>
        public static bool IsValidAnnotations<T>(this T value)
            where T : class
        {
            var context = new ValidationContext(value);
            var results = new List<ValidationResult>();
            return Validator.TryValidateObject(value, context, results, validateAllProperties: true);
        }

        /// <summary>
        /// Gets all validation errors for an object.
        /// </summary>
        /// <typeparam name="T">The type to validate.</typeparam>
        /// <param name="value">The object to validate.</param>
        /// <returns>List of validation results.</returns>
        public static List<ValidationResult> GetValidationErrors<T>(this T value)
            where T : class
        {
            var context = new ValidationContext(value);
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(value, context, results, validateAllProperties: true);
            return results;
        }

        #endregion

        #region Private Helpers

        private static DataAnnotationsValidationResult<T> ValidateObject<T>(T value)
            where T : class
        {
            var context = new ValidationContext(value);
            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(value, context, results, validateAllProperties: true);

            return new DataAnnotationsValidationResult<T>(value, isValid, results);
        }

        #endregion
    }

    /// <summary>
    /// Result of a DataAnnotations validation operation.
    /// </summary>
    /// <typeparam name="T">The validated type.</typeparam>
    public class DataAnnotationsValidationResult<T>
        where T : class
    {
        /// <summary>
        /// The validated/mapped value.
        /// </summary>
        public T? Value { get; }

        /// <summary>
        /// Whether validation passed.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// List of validation errors.
        /// </summary>
        public IReadOnlyList<ValidationResult> Errors { get; }

        /// <summary>
        /// Gets errors grouped by property name.
        /// </summary>
        public IDictionary<string, string[]> ErrorsByProperty
        {
            get
            {
                var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var error in Errors)
                {
                    var memberNames = error.MemberNames.Any()
                        ? error.MemberNames
                        : new[] { string.Empty };

                    foreach (var memberName in memberNames)
                    {
                        var key = string.IsNullOrEmpty(memberName) ? "_general" : memberName;
                        if (!grouped.ContainsKey(key))
                        {
                            grouped[key] = new List<string>();
                        }
                        if (!string.IsNullOrEmpty(error.ErrorMessage))
                        {
                            grouped[key].Add(error.ErrorMessage);
                        }
                    }
                }

                return grouped.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
            }
        }

        /// <summary>
        /// Gets all error messages as a single list.
        /// </summary>
        public IEnumerable<string> ErrorMessages =>
            Errors.Where(e => !string.IsNullOrEmpty(e.ErrorMessage))
                  .Select(e => e.ErrorMessage!);

        public DataAnnotationsValidationResult(T? value, bool isValid, List<ValidationResult> errors)
        {
            Value = value;
            IsValid = isValid;
            Errors = errors.AsReadOnly();
        }

        /// <summary>
        /// Creates a failed validation result.
        /// </summary>
        public static DataAnnotationsValidationResult<T> Failure(T? value, params ValidationResult[] errors)
        {
            return new DataAnnotationsValidationResult<T>(value, false, errors.ToList());
        }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static DataAnnotationsValidationResult<T> Success(T value)
        {
            return new DataAnnotationsValidationResult<T>(value, true, new List<ValidationResult>());
        }

        /// <summary>
        /// Gets the value or throws if validation failed.
        /// </summary>
        /// <exception cref="ValidationException">Thrown if validation failed.</exception>
        public T GetValueOrThrow()
        {
            if (!IsValid || Value is null)
            {
                var message = string.Join("; ", ErrorMessages);
                throw new ValidationException(
                    string.IsNullOrEmpty(message) ? "Validation failed" : message);
            }
            return Value;
        }

        /// <summary>
        /// Executes an action if validation succeeded.
        /// </summary>
        /// <param name="action">The action to execute with the validated value.</param>
        /// <returns>This result for chaining.</returns>
        public DataAnnotationsValidationResult<T> OnSuccess(Action<T> action)
        {
            if (IsValid && Value is not null)
            {
                action(Value);
            }
            return this;
        }

        /// <summary>
        /// Executes an action if validation failed.
        /// </summary>
        /// <param name="action">The action to execute with the errors.</param>
        /// <returns>This result for chaining.</returns>
        public DataAnnotationsValidationResult<T> OnFailure(Action<IReadOnlyList<ValidationResult>> action)
        {
            if (!IsValid)
            {
                action(Errors);
            }
            return this;
        }

        /// <summary>
        /// Transforms the result to another type.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="onSuccess">Function to apply on success.</param>
        /// <param name="onFailure">Function to apply on failure.</param>
        /// <returns>The transformed result.</returns>
        public TResult Match<TResult>(
            Func<T, TResult> onSuccess,
            Func<IReadOnlyList<ValidationResult>, TResult> onFailure)
        {
            return IsValid && Value is not null
                ? onSuccess(Value)
                : onFailure(Errors);
        }
    }
}
