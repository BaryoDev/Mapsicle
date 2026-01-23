using System;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Mapsicle.Fluent;
using Mapsicle.Validation;

namespace Mapsicle.AspNetCore
{
    /// <summary>
    /// ASP.NET Core integration extensions for Mapsicle.
    /// </summary>
    public static class AspNetCoreExtensions
    {
        #region IResult Extensions

        /// <summary>
        /// Maps the value to the destination type and returns an Ok result.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <returns>IResult with the mapped object.</returns>
        public static IResult MapToOk<TDest>(this object? source)
        {
            if (source is null) return Results.NotFound();

            var mapped = source.MapTo<TDest>();
            return mapped is null ? Results.NotFound() : Results.Ok(mapped);
        }

        /// <summary>
        /// Maps the value to the destination type and returns an Ok result using IMapper.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <returns>IResult with the mapped object.</returns>
        public static IResult MapToOk<TDest>(this IMapper mapper, object? source)
        {
            if (source is null) return Results.NotFound();

            var mapped = mapper.Map<TDest>(source);
            return mapped is null ? Results.NotFound() : Results.Ok(mapped);
        }

        /// <summary>
        /// Maps the value and returns a Created result at the specified URI.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="uri">The URI of the created resource.</param>
        /// <returns>IResult with Created status and the mapped object.</returns>
        public static IResult MapToCreated<TDest>(this object? source, string uri)
        {
            if (source is null) return Results.BadRequest();

            var mapped = source.MapTo<TDest>();
            return mapped is null ? Results.BadRequest() : Results.Created(uri, mapped);
        }

        /// <summary>
        /// Maps the value and returns a Created result at the specified URI using IMapper.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="uri">The URI of the created resource.</param>
        /// <returns>IResult with Created status and the mapped object.</returns>
        public static IResult MapToCreated<TDest>(this IMapper mapper, object? source, string uri)
        {
            if (source is null) return Results.BadRequest();

            var mapped = mapper.Map<TDest>(source);
            return mapped is null ? Results.BadRequest() : Results.Created(uri, mapped);
        }

        /// <summary>
        /// Maps the value and returns an Accepted result.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source object.</param>
        /// <param name="uri">Optional URI to check status.</param>
        /// <returns>IResult with Accepted status and the mapped object.</returns>
        public static IResult MapToAccepted<TDest>(this object? source, string? uri = null)
        {
            if (source is null) return Results.BadRequest();

            var mapped = source.MapTo<TDest>();
            return mapped is null ? Results.BadRequest() : Results.Accepted(uri, mapped);
        }

        #endregion

        #region Validation Result Extensions

        /// <summary>
        /// Maps, validates, and returns appropriate IResult.
        /// Returns Ok with mapped value if valid, BadRequest with errors if invalid.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <returns>IResult based on mapping and validation outcome.</returns>
        public static IResult MapValidateAndReturn<TDest, TValidator>(
            this IMapper mapper,
            object? source)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            if (source is null) return Results.BadRequest(new { error = "Source is null" });

            var result = mapper.MapAndValidate<TDest, TValidator>(source);

            if (result.IsValid)
            {
                return Results.Ok(result.Value);
            }

            return Results.BadRequest(new
            {
                errors = result.ErrorsByProperty
            });
        }

        /// <summary>
        /// Maps, validates, and returns Created result if valid.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="uri">The URI of the created resource.</param>
        /// <returns>IResult based on mapping and validation outcome.</returns>
        public static IResult MapValidateAndCreate<TDest, TValidator>(
            this IMapper mapper,
            object? source,
            string uri)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            if (source is null) return Results.BadRequest(new { error = "Source is null" });

            var result = mapper.MapAndValidate<TDest, TValidator>(source);

            if (result.IsValid)
            {
                return Results.Created(uri, result.Value);
            }

            return Results.BadRequest(new
            {
                errors = result.ErrorsByProperty
            });
        }

        /// <summary>
        /// Maps, validates, and returns Created result with generated URI.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source object.</param>
        /// <param name="uriGenerator">Function to generate URI from mapped object.</param>
        /// <returns>IResult based on mapping and validation outcome.</returns>
        public static IResult MapValidateAndCreate<TDest, TValidator>(
            this IMapper mapper,
            object? source,
            Func<TDest, string> uriGenerator)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            if (source is null) return Results.BadRequest(new { error = "Source is null" });

            var result = mapper.MapAndValidate<TDest, TValidator>(source);

            if (result.IsValid && result.Value is not null)
            {
                var uri = uriGenerator(result.Value);
                return Results.Created(uri, result.Value);
            }

            return Results.BadRequest(new
            {
                errors = result.ErrorsByProperty
            });
        }

        #endregion

        #region Collection Mapping Extensions

        /// <summary>
        /// Maps a collection and returns Ok result with the list.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The source collection.</param>
        /// <returns>IResult with the mapped collection.</returns>
        public static IResult MapCollectionToOk<TDest>(this System.Collections.IEnumerable? source)
        {
            if (source is null) return Results.Ok(Array.Empty<TDest>());

            var mapped = source.MapTo<TDest>();
            return Results.Ok(mapped);
        }

        /// <summary>
        /// Maps a collection using IMapper and returns Ok result.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="source">The source collection.</param>
        /// <returns>IResult with the mapped collection.</returns>
        public static IResult MapCollectionToOk<TSource, TDest>(
            this IMapper mapper,
            IEnumerable<TSource>? source)
        {
            if (source is null) return Results.Ok(Array.Empty<TDest>());

            var mapped = new List<TDest>();
            foreach (var item in source)
            {
                var mappedItem = mapper.Map<TSource, TDest>(item);
                if (mappedItem is not null)
                {
                    mapped.Add(mappedItem);
                }
            }
            return Results.Ok(mapped);
        }

        #endregion

        #region Problem Details Extensions

        /// <summary>
        /// Creates a ProblemDetails result from validation errors.
        /// </summary>
        /// <typeparam name="T">The type that was validated.</typeparam>
        /// <param name="result">The validation result.</param>
        /// <param name="title">Optional problem title.</param>
        /// <param name="instance">Optional instance URI.</param>
        /// <returns>IResult with ProblemDetails.</returns>
        public static IResult ToProblemDetails<T>(
            this MapperValidationResult<T> result,
            string? title = null,
            string? instance = null)
            where T : class
        {
            if (result.IsValid)
            {
                return Results.Ok(result.Value);
            }

            var problemDetails = new ValidationProblemDetails(
                result.ErrorsByProperty.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value))
            {
                Title = title ?? "Validation failed",
                Status = StatusCodes.Status400BadRequest,
                Instance = instance
            };

            return Results.BadRequest(problemDetails);
        }

        #endregion

        #region Endpoint Filter Extensions

        /// <summary>
        /// Adds a mapping filter that maps request body to specified type.
        /// </summary>
        /// <typeparam name="TSource">The source request type.</typeparam>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <param name="builder">The endpoint convention builder.</param>
        /// <returns>The builder for chaining.</returns>
        public static RouteHandlerBuilder WithMappedRequest<TSource, TDest>(
            this RouteHandlerBuilder builder)
            where TDest : class
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var mapper = context.HttpContext.RequestServices.GetService<IMapper>();
                if (mapper is null)
                {
                    return await next(context);
                }

                // Find the source argument and replace with mapped version
                for (int i = 0; i < context.Arguments.Count; i++)
                {
                    if (context.Arguments[i] is TSource source)
                    {
                        var mapped = mapper.Map<TSource, TDest>(source);
                        if (mapped is not null)
                        {
                            context.Arguments[i] = mapped;
                        }
                    }
                }

                return await next(context);
            });
        }

        /// <summary>
        /// Adds a validation filter that validates and maps request body.
        /// </summary>
        /// <typeparam name="TSource">The source request type.</typeparam>
        /// <typeparam name="TDest">The destination type to map to.</typeparam>
        /// <typeparam name="TValidator">The FluentValidation validator type.</typeparam>
        /// <param name="builder">The endpoint convention builder.</param>
        /// <returns>The builder for chaining.</returns>
        public static RouteHandlerBuilder WithValidatedMapping<TSource, TDest, TValidator>(
            this RouteHandlerBuilder builder)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var mapper = context.HttpContext.RequestServices.GetService<IMapper>();
                if (mapper is null)
                {
                    return await next(context);
                }

                // Find the source argument
                for (int i = 0; i < context.Arguments.Count; i++)
                {
                    if (context.Arguments[i] is TSource source)
                    {
                        var result = mapper.MapAndValidate<TSource, TDest, TValidator>(source);
                        if (!result.IsValid)
                        {
                            return Results.BadRequest(new { errors = result.ErrorsByProperty });
                        }
                        context.Arguments[i] = result.Value!;
                    }
                }

                return await next(context);
            });
        }

        #endregion
    }

    /// <summary>
    /// Response wrapper for mapped API responses.
    /// </summary>
    /// <typeparam name="T">The type of the data.</typeparam>
    public class MappedResponse<T> where T : class
    {
        /// <summary>
        /// The mapped data.
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// Whether the operation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error messages if any.
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Creates a successful response.
        /// </summary>
        public static MappedResponse<T> Ok(T data) => new()
        {
            Data = data,
            Success = true
        };

        /// <summary>
        /// Creates a failed response.
        /// </summary>
        public static MappedResponse<T> Fail(params string[] errors) => new()
        {
            Success = false,
            Errors = errors.ToList()
        };

        /// <summary>
        /// Creates a failed response from validation result.
        /// </summary>
        public static MappedResponse<T> FromValidation(MapperValidationResult<T> result)
        {
            if (result.IsValid)
            {
                return Ok(result.Value!);
            }

            return new MappedResponse<T>
            {
                Success = false,
                Data = result.Value,
                Errors = result.Errors.Select(e => e.ErrorMessage).ToList()
            };
        }
    }
}
