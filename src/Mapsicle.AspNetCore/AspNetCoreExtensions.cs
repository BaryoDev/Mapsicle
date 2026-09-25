using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Mapsicle.Fluent;
using Mapsicle.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

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
        /// Adds a filter that maps the request body to <typeparamref name="TDest"/> and stores it
        /// on <see cref="HttpContext.Items"/>, retrievable with <see cref="GetMappedRequest{TDest}"/>.
        /// </summary>
        /// <remarks>
        /// This used to overwrite <c>context.Arguments[i]</c> with the mapped value. Minimal API
        /// binds each handler parameter to its declared type before the filter runs, so the slot
        /// held a <typeparamref name="TSource"/>-typed reference, and assigning a
        /// <typeparamref name="TDest"/> into it threw InvalidCastException the moment the handler
        /// read the parameter, on every valid request. The handler parameter is left alone; read
        /// the mapped value through <see cref="GetMappedRequest{TDest}"/> instead.
        /// </remarks>
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
                for (int i = 0; i < context.Arguments.Count; i++)
                {
                    if (context.Arguments[i] is TSource source)
                    {
                        var mapped = MapWithAvailableMapper<TSource, TDest>(context.HttpContext, source);
                        if (mapped is not null)
                        {
                            context.HttpContext.Items[MappedRequestKey<TDest>.Instance] = mapped;
                        }
                        break;
                    }
                }

                return await next(context);
            });
        }

        /// <summary>
        /// Adds a filter that maps and validates the request body, returning 400 with the same
        /// error shape as <see cref="MapValidateAndReturn{TDest, TValidator}"/> when invalid, and
        /// otherwise stores the mapped <typeparamref name="TDest"/> on <see cref="HttpContext.Items"/>,
        /// retrievable with <see cref="GetMappedRequest{TDest}"/>.
        /// </summary>
        /// <remarks>
        /// See the remarks on <see cref="WithMappedRequest{TSource, TDest}"/> for why the handler
        /// parameter is left untouched. Validation always runs: it no longer depends on an
        /// <see cref="IMapper"/> being registered, so a request built only through
        /// Mapsicle.DependencyInjection's <c>AddMapsicle</c> (which registers
        /// <see cref="IMapperInstance"/>, not <see cref="IMapper"/>) still gets validated instead
        /// of the filter quietly calling <c>next()</c> on an invalid body.
        /// </remarks>
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
                for (int i = 0; i < context.Arguments.Count; i++)
                {
                    if (context.Arguments[i] is TSource source)
                    {
                        var result = MapAndValidateWithAvailableMapper<TSource, TDest, TValidator>(context.HttpContext, source);
                        if (!result.IsValid)
                        {
                            return Results.BadRequest(new { errors = result.ErrorsByProperty });
                        }

                        context.HttpContext.Items[MappedRequestKey<TDest>.Instance] = result.Value!;
                        break;
                    }
                }

                return await next(context);
            });
        }

        /// <summary>
        /// Gets the value a prior <see cref="WithMappedRequest{TSource, TDest}"/> or
        /// <see cref="WithValidatedMapping{TSource, TDest, TValidator}"/> filter stored for this
        /// request.
        /// </summary>
        /// <typeparam name="TDest">The mapped destination type.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>The mapped instance.</returns>
        /// <exception cref="InvalidOperationException">
        /// No filter stored a mapped <typeparamref name="TDest"/> on this request.
        /// </exception>
        public static TDest GetMappedRequest<TDest>(this HttpContext context)
            where TDest : class
        {
            if (context.TryGetMappedRequest<TDest>(out var mapped))
            {
                return mapped;
            }

            throw new InvalidOperationException(
                $"No mapped {typeof(TDest).Name} was found on this request. Add " +
                $"WithMappedRequest<TSource, {typeof(TDest).Name}>() or " +
                $"WithValidatedMapping<TSource, {typeof(TDest).Name}, TValidator>() to the endpoint " +
                $"before calling GetMappedRequest<{typeof(TDest).Name}>().");
        }

        /// <summary>
        /// Attempts to get the value a prior <see cref="WithMappedRequest{TSource, TDest}"/> or
        /// <see cref="WithValidatedMapping{TSource, TDest, TValidator}"/> filter stored for this
        /// request.
        /// </summary>
        /// <typeparam name="TDest">The mapped destination type.</typeparam>
        /// <param name="context">The current HTTP context.</param>
        /// <param name="mapped">The mapped instance, when found.</param>
        /// <returns>True if a mapped value was found.</returns>
        public static bool TryGetMappedRequest<TDest>(this HttpContext context, [NotNullWhen(true)] out TDest? mapped)
            where TDest : class
        {
            if (context.Items.TryGetValue(MappedRequestKey<TDest>.Instance, out var value) && value is TDest typed)
            {
                mapped = typed;
                return true;
            }

            mapped = null;
            return false;
        }

        // One key instance per closed TDest, so filters mapping different destination types on the
        // same request never collide in HttpContext.Items.
        private static class MappedRequestKey<TDest>
        {
            public static readonly object Instance = new object();
        }

        // IMapper needs a fluent MapperConfiguration and is registered by choice. IMapperInstance is
        // what Mapsicle.DependencyInjection's AddMapsicle registers, with no configuration required.
        // Checking only IMapper found nothing under AddMapsicle and fell through to the static
        // Mapper, silently skipping whatever conventions the caller thought they had configured.
        private static TDest? MapWithAvailableMapper<TSource, TDest>(HttpContext httpContext, TSource source)
            where TDest : class
        {
            var mapper = httpContext.RequestServices.GetService<IMapper>();
            if (mapper is not null)
            {
                return mapper.Map<TSource, TDest>(source);
            }

            var mapperInstance = httpContext.RequestServices.GetService<IMapperInstance>();
            if (mapperInstance is not null)
            {
                return mapperInstance.MapTo<TDest>(source);
            }

            return source.MapTo<TSource, TDest>();
        }

        private static MapperValidationResult<TDest> MapAndValidateWithAvailableMapper<TSource, TDest, TValidator>(
            HttpContext httpContext, TSource source)
            where TDest : class
            where TValidator : IValidator<TDest>, new()
        {
            var mapped = MapWithAvailableMapper<TSource, TDest>(httpContext, source);
            if (mapped is null)
            {
                return MapperValidationResult<TDest>.Failure(
                    default,
                    new ValidationResult(new[] { new ValidationFailure("", "Mapping returned null") }));
            }

            var validator = new TValidator();
            var validationResult = validator.Validate(mapped);
            return validationResult.IsValid
                ? MapperValidationResult<TDest>.Success(mapped)
                : MapperValidationResult<TDest>.Failure(mapped, validationResult);
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
