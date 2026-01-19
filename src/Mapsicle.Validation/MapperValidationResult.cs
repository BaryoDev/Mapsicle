using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;

namespace Mapsicle.Validation
{
    /// <summary>
    /// Represents the result of a map-and-validate operation.
    /// </summary>
    /// <typeparam name="T">The type of the mapped destination object.</typeparam>
    public class MapperValidationResult<T>
    {
        /// <summary>
        /// The mapped destination object. May be default if mapping failed.
        /// </summary>
        public T? Value { get; }

        /// <summary>
        /// The FluentValidation result containing any validation errors.
        /// </summary>
        public ValidationResult ValidationResult { get; }

        /// <summary>
        /// Returns true if validation passed with no errors.
        /// </summary>
        public bool IsValid => ValidationResult.IsValid;

        /// <summary>
        /// Returns the list of validation errors.
        /// </summary>
        public IList<ValidationFailure> Errors => ValidationResult.Errors;

        /// <summary>
        /// Returns a dictionary of property names to their error messages.
        /// </summary>
        public IDictionary<string, string[]> ErrorsByProperty =>
            Errors.GroupBy(e => e.PropertyName)
                  .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        internal MapperValidationResult(T? value, ValidationResult validationResult)
        {
            Value = value;
            ValidationResult = validationResult;
        }

        /// <summary>
        /// Creates a successful result with the mapped value.
        /// </summary>
        public static MapperValidationResult<T> Success(T value) =>
            new MapperValidationResult<T>(value, new ValidationResult());

        /// <summary>
        /// Creates a failed result with validation errors.
        /// </summary>
        public static MapperValidationResult<T> Failure(T? value, ValidationResult validationResult) =>
            new MapperValidationResult<T>(value, validationResult);

        /// <summary>
        /// Gets the value if valid, otherwise throws an exception with validation errors.
        /// </summary>
        public T GetValueOrThrow()
        {
            if (!IsValid)
            {
                var errors = string.Join("; ", Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
                throw new ValidationException($"Validation failed: {errors}", Errors);
            }
            return Value!;
        }
    }

    /// <summary>
    /// Exception thrown when validation fails and GetValueOrThrow is called.
    /// </summary>
    public class ValidationException : Exception
    {
        /// <summary>
        /// The validation failures that caused this exception.
        /// </summary>
        public IList<ValidationFailure> Errors { get; }

        public ValidationException(string message, IList<ValidationFailure> errors)
            : base(message)
        {
            Errors = errors;
        }
    }
}
