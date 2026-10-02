using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Mapsicle.NamingConventions
{
    /// <summary>
    /// Represents a naming convention for property name transformation.
    /// </summary>
    public abstract class NamingConvention
    {
        // The capitalised-word alternative used to come first and accept a lone capital, so the
        // acronym alternative never ran and "HTTPServerID" split into H, T, T, P, Server, I, D. The
        // classes were ASCII only, so "StraßeName" lost its ß and split around the gap.
        /// <summary>Splits an identifier into words at case changes and separators.</summary>
        protected static readonly Regex WordBoundaryRegex = new(
                @"[\p{Lu}\p{Lt}]+(?>\p{Nd}*)(?![\p{Ll}\p{Lo}\p{Lm}\p{M}])"
                + @"|[\p{Lu}\p{Lt}][\p{Ll}\p{Lo}\p{Lm}\p{M}\p{Nd}]*"
                + @"|[\p{Ll}\p{Lo}\p{Lm}\p{M}\p{Nd}]+",
                RegexOptions.Compiled);

        /// <summary>
        /// PascalCase naming convention (e.g., UserName, FirstName).
        /// </summary>
        public static NamingConvention PascalCase { get; } = new PascalCaseConvention();

        /// <summary>
        /// camelCase naming convention (e.g., userName, firstName).
        /// </summary>
        public static NamingConvention CamelCase { get; } = new CamelCaseConvention();

        /// <summary>
        /// snake_case naming convention (e.g., user_name, first_name).
        /// </summary>
        public static NamingConvention SnakeCase { get; } = new SnakeCaseConvention();

        /// <summary>
        /// kebab-case naming convention (e.g., user-name, first-name).
        /// </summary>
        public static NamingConvention KebabCase { get; } = new KebabCaseConvention();

        /// <summary>
        /// SCREAMING_SNAKE_CASE naming convention (e.g., USER_NAME, FIRST_NAME).
        /// Common for constants and environment variables.
        /// </summary>
        public static NamingConvention ScreamingSnakeCase { get; } = new ScreamingSnakeCaseConvention();

        /// <summary>
        /// The name of this naming convention.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Converts a property name from this convention to a normalized form (PascalCase words).
        /// </summary>
        public abstract string[] ToWords(string name);

        /// <summary>
        /// Converts normalized words to this convention's format.
        /// </summary>
        public abstract string FromWords(string[] words);

        internal static string[] SplitAtCaseChanges(string name)
        {
            var matches = WordBoundaryRegex.Matches(name);
            var words = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                words[i] = matches[i].Value;
            }
            return words;
        }

        /// <summary>
        /// Converts a name from one convention to another.
        /// </summary>
        public static string Convert(string name, NamingConvention from, NamingConvention to)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var words = from.ToWords(name);
            return to.FromWords(words);
        }

        /// <summary>
        /// Checks if two names match when conventions are applied.
        /// </summary>
        public static bool NamesMatch(string sourceName, NamingConvention sourceConvention,
                                       string destName, NamingConvention destConvention)
        {
            if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(destName))
                return false;

            // Compared word by word, this refused address_line_1 against AddressLine1, because one
            // side has three words and the other two. Where a word ends is a property of the
            // convention, not of the name, so only the letters are compared.
            return string.Equals(
                string.Concat(sourceConvention.ToWords(sourceName)),
                string.Concat(destConvention.ToWords(destName)),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal class PascalCaseConvention : NamingConvention
    {
        public override string Name => "PascalCase";

        public override string[] ToWords(string name)
        {
            return SplitAtCaseChanges(name);
        }

        public override string FromWords(string[] words)
        {
            var sb = new StringBuilder();
            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(word)) continue;
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                    sb.Append(word.Substring(1).ToLowerInvariant());
            }
            return sb.ToString();
        }
    }

    internal class CamelCaseConvention : NamingConvention
    {
        public override string Name => "camelCase";

        public override string[] ToWords(string name)
        {
            return SplitAtCaseChanges(name);
        }

        public override string FromWords(string[] words)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (string.IsNullOrEmpty(word)) continue;

                if (i == 0)
                {
                    sb.Append(word.ToLowerInvariant());
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(word[0]));
                    if (word.Length > 1)
                        sb.Append(word.Substring(1).ToLowerInvariant());
                }
            }
            return sb.ToString();
        }
    }

    internal class SnakeCaseConvention : NamingConvention
    {
        public override string Name => "snake_case";

        public override string[] ToWords(string name)
        {
            return name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public override string FromWords(string[] words)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append('_');
                sb.Append(words[i].ToLowerInvariant());
            }
            return sb.ToString();
        }
    }

    internal class KebabCaseConvention : NamingConvention
    {
        public override string Name => "kebab-case";

        public override string[] ToWords(string name)
        {
            return name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public override string FromWords(string[] words)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(words[i].ToLowerInvariant());
            }
            return sb.ToString();
        }
    }

    internal class ScreamingSnakeCaseConvention : NamingConvention
    {
        public override string Name => "SCREAMING_SNAKE_CASE";

        public override string[] ToWords(string name)
        {
            return name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public override string FromWords(string[] words)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append('_');
                sb.Append(words[i].ToUpperInvariant());
            }
            return sb.ToString();
        }
    }
}
