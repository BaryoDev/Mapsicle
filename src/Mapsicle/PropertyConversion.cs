using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Mapsicle
{
    /// <summary>
    /// The single decision about how one property converts to another.
    /// </summary>
    /// <remarks>
    /// This logic used to be written out three times: <c>Mapper.CreatePropertyBinding</c>,
    /// <c>Mapper.CreateTypedPropertyBinding</c> and <c>MapperInstance.CreatePropertyBinding</c>. The
    /// copies drifted, and the drift shipped. Two bugs existed in all three (an unguarded
    /// <c>ToString()</c>, and widening numeric pairs falling through to "unmapped"), while a third
    /// existed in one copy only: 1.2.3 records that mappers from <c>MapperFactory.Create()</c> silently
    /// dropped nested objects.
    ///
    /// Every call site now routes here, so a conversion rule is stated once and every entry point
    /// agrees by construction rather than by review.
    ///
    /// Everything returned is a pure expression tree. Nothing here runs per call: it runs once when a
    /// mapper is compiled, so the warm path cost of these rules is whatever IL the conversion emits,
    /// which for primitives is a single conversion opcode.
    /// </remarks>
    internal static class PropertyConversion
    {
        /// <summary>
        /// Builds the value expression to assign to <paramref name="targetType"/>, or null when the
        /// pair is not mappable and the destination should keep its default.
        /// </summary>
        /// <param name="propExp">Expression yielding the source property value.</param>
        /// <param name="srcType">Declared type of the source property.</param>
        /// <param name="targetType">Declared type of the destination property.</param>
        /// <param name="buildNestedMap">
        /// Builds the recursive map call for a nested complex object. Supplied by the caller because the
        /// static <c>Mapper</c> and an instance <c>MapperInstance</c> recurse into different mappers.
        /// </param>
        internal static Expression? TryBuild(
            Expression propExp,
            Type srcType,
            Type targetType,
            Func<Expression, Type, Expression> buildNestedMap)
        {
            // Identical or reference-compatible, including int -> int? which Nullable special-cases.
            if (targetType.IsAssignableFrom(srcType))
            {
                return srcType == targetType ? propExp : Expression.Convert(propExp, targetType);
            }

            // Nested complex object. string is excluded on both sides: it is a class, but mapping into
            // or out of it means ToString or parsing, not member-by-member mapping.
            //
            // The source side accepts an interface as well as a class. It used to test IsClass alone,
            // and an interface is not a class, so a source member typed as an abstraction was never
            // treated as mappable: IThing into a concrete Thing was dropped and read downstream as a
            // field the source did not have. The recursive map resolves the runtime type, so the
            // declared type only has to be something that can hold a mappable instance.
            if (IsMappableSource(srcType) && targetType.IsClass && targetType != typeof(string))
            {
                return buildNestedMap(propExp, targetType);
            }

            if (targetType == typeof(string))
            {
                return BuildToString(propExp, srcType);
            }

            if (srcType.IsEnum && (targetType == typeof(int) || targetType == typeof(long)))
            {
                return Expression.Convert(propExp, targetType);
            }

            // Widening numeric. The CLR type system has no notion of the implicit numeric conversions
            // the C# language defines, so IsAssignableFrom above says false for int -> long and every
            // widening pair used to fall out of the cascade entirely, leaving the destination at its
            // default. A caller mapping int 42 into a long got 0, silently.
            var numeric = TryBuildNumericWidening(propExp, srcType, targetType);
            if (numeric is not null)
            {
                return numeric;
            }

            // Nullable source to its non-nullable counterpart: null becomes the destination default.
            var underlyingSource = Nullable.GetUnderlyingType(srcType);
            if (underlyingSource != null && targetType.IsAssignableFrom(underlyingSource))
            {
                return Expression.Coalesce(propExp, Expression.Default(targetType));
            }

            return null;
        }

        /// <summary>
        /// Whether a source member of this declared type can hold something worth mapping member by
        /// member: a class or an interface, and not a string.
        /// </summary>
        private static bool IsMappableSource(Type srcType) =>
            (srcType.IsClass || srcType.IsInterface) && srcType != typeof(string);

        /// <summary>
        /// Whether <paramref name="destProp"/> can be filled by flattening <paramref name="sourceProp"/>,
        /// for example <c>AddressCity</c> from <c>Address.City</c>.
        /// </summary>
        /// <remarks>
        /// This is the rule the mapper applies, and it lives here so the validator cannot answer
        /// differently. It used to: <c>AssertMappingValid&lt;Source, Dest&gt;()</c> reported
        /// <c>NameLength</c> as mapped from a <c>string Name</c>, because <c>Name</c> is a prefix of
        /// <c>NameLength</c> and <c>string</c> has a <c>Length</c> property. The mapper skips
        /// <c>string</c> sources outright, so the property was never populated and the validator's
        /// pass was false assurance, which is worse than having no validator.
        ///
        /// Two conditions the validator was missing, both enforced here: the source property must be
        /// a class and not a <c>string</c>, and the nested property's type must be assignable to the
        /// destination property's type.
        /// </remarks>
        /// <summary>
        /// How many levels a flattened name may descend before the search gives up.
        /// </summary>
        /// <remarks>
        /// A ceiling rather than a preference. A type holding itself, like a category with a parent
        /// category, gives the search an infinite supply of candidate paths, and this runs while the
        /// delegate is being built rather than while it runs, so a runaway hangs the compile instead
        /// of overflowing a stack. Four covers the shapes people actually write: an aggregate root,
        /// an entity it owns, something that entity owns, and a field on it.
        /// </remarks>
        internal const int MaxFlattenDepth = 4;

        /// <summary>
        /// Resolves a flattened destination name into the chain of source properties that produces it.
        /// </summary>
        /// <remarks>
        /// <c>CustomerAddressCity</c> becomes Customer, then Address, then City. The search takes the
        /// longest prefix that matches a readable property and recurses on the remainder, so a name
        /// spelling more than one real path resolves to the one whose first step is longest, and the
        /// result does not depend on the order properties are enumerated in.
        ///
        /// This replaced a single level lookup that could form <c>CustomerAddress</c> and never
        /// descended into <c>Address</c>, so a three level name silently left its member at the
        /// default. Both delegate builders call this, because there were two copies of the old one
        /// and copies of this logic are what CONTRIBUTING exists to prevent.
        /// </remarks>
        internal static bool TryFindFlattenedPath(
            PropertyInfo destProp,
            PropertyInfo[] sourceProps,
            Func<Type, PropertyInfo[]> readableProperties,
            out List<PropertyInfo> path)
        {
            path = new List<PropertyInfo>();
            return Descend(destProp.Name, destProp.PropertyType, sourceProps, readableProperties, path, 0);
        }

        private static bool Descend(
            string remainingName,
            Type destType,
            PropertyInfo[] candidates,
            Func<Type, PropertyInfo[]> readableProperties,
            List<PropertyInfo> path,
            int depth)
        {
            if (depth >= MaxFlattenDepth) return false;

            // Longest prefix first, so Address wins over A when both are properties and the name is
            // AddressCity. Without this the answer depends on enumeration order.
            foreach (var candidate in candidates
                         .Where(p => remainingName.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(p => p.Name.Length))
            {
                var remainder = remainingName.Substring(candidate.Name.Length);

                if (remainder.Length == 0)
                {
                    // The whole name is consumed. Only useful below the first level, because at the
                    // first level this is an ordinary member match rather than flattening.
                    if (depth == 0) continue;
                    if (!IsAssignableForFlattening(candidate.PropertyType, destType)) continue;

                    path.Add(candidate);
                    return true;
                }

                if (!CanDescendInto(candidate.PropertyType)) continue;

                path.Add(candidate);
                if (Descend(remainder, destType, readableProperties(candidate.PropertyType),
                            readableProperties, path, depth + 1))
                {
                    return true;
                }
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        private static bool CanDescendInto(Type type) =>
            type.IsClass && type != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

        /// <summary>Whether the leaf of a flattened path can be assigned to the destination.</summary>
        private static bool IsAssignableForFlattening(Type from, Type to) =>
            to.IsAssignableFrom(from) || IsLosslessNumericWidening(from, to);

        internal static bool TryFindFlattenedSource(
            PropertyInfo destProp,
            PropertyInfo sourceProp,
            PropertyInfo[] nestedProps,
            out PropertyInfo? nestedProp)
        {
            nestedProp = null;

            if (!sourceProp.PropertyType.IsClass || sourceProp.PropertyType == typeof(string))
            {
                return false;
            }

            var destName = destProp.Name;
            if (!destName.StartsWith(sourceProp.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = destName.Substring(sourceProp.Name.Length);
            if (string.IsNullOrEmpty(remainder))
            {
                return false;
            }

            for (var i = 0; i < nestedProps.Length; i++)
            {
                if (nestedProps[i].Name.Equals(remainder, StringComparison.OrdinalIgnoreCase))
                {
                    nestedProp = nestedProps[i];
                    break;
                }
            }

            if (nestedProp is null || !destProp.PropertyType.IsAssignableFrom(nestedProp.PropertyType))
            {
                nestedProp = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// <c>ToString()</c> on the source value, guarded when the source can be null.
        /// </summary>
        /// <remarks>
        /// A reference-typed source compiles to a virtual call on the instance, so a null source threw
        /// <c>NullReferenceException</c> from inside the compiled delegate, with a stack trace pointing
        /// at <c>lambda_method</c> and nothing naming the property. A null source now yields null,
        /// which is what the rest of the mapper does with a value it cannot produce, and what
        /// AutoMapper does.
        ///
        /// Value types need no guard: <see cref="Expression.Call(Expression, MethodInfo)"/> on a value
        /// type receiver boxes rather than dereferences. That includes <c>Nullable&lt;T&gt;</c>, whose
        /// own <c>ToString()</c> returns an empty string when it has no value.
        /// </remarks>
        internal static Expression BuildToString(Expression propExp, Type srcType)
        {
            var underlying = Nullable.GetUnderlyingType(srcType);

            // A value that knows how to format itself is formatted invariantly. A bare ToString()
            // reads the ambient thread culture, so the same decimal produced "1234.5" on one machine
            // and "1234,5" on another, and a mapper feeding a serialisation or persistence boundary
            // wrote a number the other region read back as a different one.
            if (underlying is not null && typeof(IFormattable).IsAssignableFrom(underlying))
            {
                // Nullable<T> does not itself implement IFormattable. Its own ToString() yields an
                // empty string when it has no value, which is the behaviour kept here.
                return Expression.Condition(
                    Expression.Property(propExp, "HasValue"),
                    FormatInvariant(Expression.Property(propExp, "Value")),
                    Expression.Constant(string.Empty, typeof(string)));
            }

            if (typeof(IFormattable).IsAssignableFrom(srcType))
            {
                if (srcType.IsValueType)
                {
                    return FormatInvariant(propExp);
                }

                return Expression.Condition(
                    Expression.Equal(propExp, Expression.Constant(null, srcType)),
                    Expression.Constant(null, typeof(string)),
                    FormatInvariant(propExp));
            }

            if (srcType.IsValueType)
            {
                return Expression.Call(propExp, ObjectToString);
            }

            // A member declared as object, or as a base type, can still hold something that formats
            // itself. The declared type is all this method can see, so the runtime type is tested
            // instead: a boxed decimal in an object property produced "1234,5" under de-DE while the
            // same value in a decimal property produced "1234.5", which is the culture bug surviving
            // in the one place the static check cannot reach. The conversion is a reference cast and
            // allocates nothing.
            return Expression.Call(ToInvariantString_, Expression.Convert(propExp, typeof(object)));
        }

        /// <summary>
        /// <c>ToString()</c> on a value whose type is only known at run time, invariant where it can be.
        /// </summary>
        internal static string? ToInvariantString(object? value) =>
            value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

        private static readonly MethodInfo ToInvariantString_ =
            typeof(PropertyConversion).GetMethod(nameof(ToInvariantString), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static Expression FormatInvariant(Expression value) =>
            Expression.Call(
                value,
                FormattableToString,
                Expression.Constant(null, typeof(string)),
                InvariantCulture);

        private static readonly Expression InvariantCulture =
            Expression.Constant(CultureInfo.InvariantCulture, typeof(IFormatProvider));

        private static readonly MethodInfo FormattableToString =
            typeof(IFormattable).GetMethod(nameof(ToString), new[] { typeof(string), typeof(IFormatProvider) })!;

        private static readonly MethodInfo ObjectToString =
            typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)!;

        /// <summary>
        /// Whether every value of <paramref name="source"/> survives conversion to
        /// <paramref name="target"/>, asked by call sites that convert a runtime value rather than
        /// emitting an expression.
        /// </summary>
        /// <remarks>
        /// The dictionary entry point converts boxed values with <c>Convert.ChangeType</c> and cannot
        /// use the expression cascade, but it must not answer differently about which pairs are
        /// allowed. It asks here so the table stays stated once.
        /// </remarks>
        internal static bool IsLosslessNumericWidening(Type source, Type target) => IsWidening(source, target);

        /// <summary>
        /// Emits a conversion for a widening numeric pair, including the nullable forms, or null.
        /// </summary>
        private static Expression? TryBuildNumericWidening(Expression propExp, Type srcType, Type targetType)
        {
            var sourceUnderlying = Nullable.GetUnderlyingType(srcType) ?? srcType;
            var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (!IsWidening(sourceUnderlying, targetUnderlying))
            {
                return null;
            }

            var sourceIsNullable = Nullable.GetUnderlyingType(srcType) != null;
            var targetIsNullable = Nullable.GetUnderlyingType(targetType) != null;

            // int? -> long?: a single lifted conversion, which carries null through as null. Doing
            // this the way the branch below does, by converting to the underlying type first, throws
            // InvalidOperationException on a null source, because Expression.Convert on an empty
            // Nullable<T> has no value to convert.
            if (sourceIsNullable && targetIsNullable)
            {
                return Expression.Convert(propExp, targetType);
            }

            // int? -> long: convert only when there is a value, otherwise leave the destination default.
            // The HasValue test has to happen before the conversion rather than being folded into it,
            // for the same reason.
            if (sourceIsNullable)
            {
                return Expression.Condition(
                    Expression.Property(propExp, "HasValue"),
                    Expression.Convert(Expression.Property(propExp, "Value"), targetType),
                    Expression.Default(targetType));
            }

            // int -> long, and int -> long?. Converting to the underlying type first keeps the emitted
            // conversion a single numeric opcode; the lift to Nullable<T> is then a constructor call.
            var converted = Expression.Convert(propExp, targetUnderlying);
            return targetUnderlying == targetType ? converted : Expression.Convert(converted, targetType);
        }

        /// <summary>
        /// The pairs where every source value survives the conversion.
        /// </summary>
        /// <remarks>
        /// This is the C# implicit numeric conversion table, which is lossless and cannot throw, plus
        /// one deliberate addition: <c>decimal</c> to <c>double</c>.
        ///
        /// Narrowing is deliberately absent. <c>long</c> to <c>int</c> and signed/unsigned
        /// reinterpretation lose or corrupt values, so those pairs stay unmapped and the destination
        /// keeps its default, which is the existing documented behaviour.
        ///
        /// <c>decimal</c> to <c>double</c> is the one pair here that is not lossless: it can lose
        /// precision past roughly 15 significant digits. It is included because the alternative is what
        /// shipped, which was a silent <c>0</c>, and because it cannot throw. <c>double</c> to
        /// <c>decimal</c> is deliberately NOT included: it throws <see cref="OverflowException"/> for
        /// values outside decimal's range, and a mapper turning a data difference into an exception at
        /// map time is worse than leaving the property unmapped.
        /// </remarks>
        private static bool IsWidening(Type source, Type target)
        {
            if (source == target)
            {
                return false;
            }

            return WideningTargets.TryGetValue(source, out var targets) && Array.IndexOf(targets, target) >= 0;
        }

        private static readonly Dictionary<Type, Type[]> WideningTargets = new()
        {
            [typeof(sbyte)] = new[] { typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(byte)] = new[] { typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(short)] = new[] { typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(ushort)] = new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(int)] = new[] { typeof(long), typeof(float), typeof(double), typeof(decimal) },
            [typeof(uint)] = new[] { typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(long)] = new[] { typeof(float), typeof(double), typeof(decimal) },
            [typeof(ulong)] = new[] { typeof(float), typeof(double), typeof(decimal) },
            [typeof(char)] = new[] { typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) },
            [typeof(float)] = new[] { typeof(double) },
            [typeof(decimal)] = new[] { typeof(double) },
        };
    }
}
