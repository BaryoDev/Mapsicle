using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Mapsicle.Docs.Tests
{
    /// <summary>
    /// README.md is packed into every NuGet package, and NuGet renders it under stricter rules
    /// than GitHub does.
    /// </summary>
    /// <remarks>
    /// The logo was an <c>img</c> tag with a relative source. GitHub rendered it. NuGet does not
    /// allow raw HTML, so the tag itself appeared as text at the top of the package page, and the
    /// relative path would not have resolved there in any case. Nothing caught it because nothing
    /// looks at the README the way NuGet does, and it is only visible after publishing.
    /// </remarks>
    public class PackedReadmeTests
    {
        private static string Readme()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mapsicle.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            var path = Path.Combine(dir!.FullName, "README.md");
            Assert.True(File.Exists(path), $"README.md not found from {AppContext.BaseDirectory}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void TheReadmeCarriesNoRawHtml()
        {
            // Deliberately narrow: block-level and inline tags NuGet drops, rather than anything
            // between angle brackets, because prose legitimately contains things like List<T>.
            var tags = Regex.Matches(Readme(), @"</?(img|div|span|br|p|table|tr|td|th|h[1-6]|a|b|i|center|font)\b[^>]*>",
                RegexOptions.IgnoreCase);

            Assert.True(tags.Count == 0,
                "NuGet strips raw HTML and shows the tag as text. Found: "
                + string.Join(", ", System.Linq.Enumerable.Select(tags, m => m.Value)));
        }

        [Fact]
        public void EveryImageUsesAnAbsoluteUrl()
        {
            // A relative path resolves on GitHub and resolves to nothing on nuget.org.
            foreach (Match image in Regex.Matches(Readme(), @"!\[[^\]]*\]\(([^)]+)\)"))
            {
                var url = image.Groups[1].Value;
                Assert.True(url.StartsWith("https://", StringComparison.Ordinal),
                    $"Image '{url}' is not absolute, so it will not render on nuget.org.");
            }
        }

        [Fact]
        public void TheReadmeReferencesAtLeastOneImage()
        {
            // A positive control. Without it both tests above pass on a README with no images,
            // which is exactly the state a careless fix would leave behind.
            Assert.Matches(@"!\[[^\]]*\]\(https://", Readme());
        }
        /// <summary>Every package that ships is listed in the README, and installable from it.</summary>
        /// <remarks>
        /// 2.2.0 shipped Mapsicle.SourceGen, the whole point of the release, and it appeared in
        /// neither the package table nor the install list. Someone reading the README top to bottom
        /// would not have learned the package existed. Mapsicle.DependencyInjection had been in the
        /// table without an install line for longer than that.
        ///
        /// Read from the projects rather than from a list kept by hand, because a list kept by hand
        /// is what was already wrong.
        /// </remarks>
        [Fact]
        public void EveryPackableProjectIsInTheReadme()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Mapsicle.sln")))
            {
                root = root.Parent;
            }

            Assert.NotNull(root);

            var readme = Readme();
            var missing = new System.Collections.Generic.List<string>();

            foreach (var project in new DirectoryInfo(Path.Combine(root!.FullName, "src")).GetDirectories())
            {
                var csproj = Path.Combine(project.FullName, project.Name + ".csproj");
                if (!File.Exists(csproj)) continue;
                if (File.ReadAllText(csproj).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal)) continue;

                if (!readme.Contains($"| **{project.Name}**", StringComparison.Ordinal))
                {
                    missing.Add($"{project.Name} is not in the package table");
                }

                if (!Regex.IsMatch(readme, @"^dotnet add package " + Regex.Escape(project.Name) + @"\s*$", RegexOptions.Multiline))
                {
                    missing.Add($"{project.Name} has no install line");
                }
            }

            Assert.True(missing.Count == 0,
                "these packages ship and the README does not tell anyone:\n  " + string.Join("\n  ", missing));
        }

    }
}
