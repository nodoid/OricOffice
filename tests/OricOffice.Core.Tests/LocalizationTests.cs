using System.Text.RegularExpressions;
using OricOffice.Core.Localization;
using OricOffice.Core.Model;

namespace OricOffice.Core.Tests;

/// <summary>
/// Guards the i18n tables: every translation must cover the English master
/// key set with matching format placeholders, and every key referenced from
/// the code or XAML must actually exist in the master table.
/// </summary>
public class LocalizationTests
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        ["fr"] = TranslationsFr.Create(),
        ["it"] = TranslationsIt.Create(),
        ["de"] = TranslationsDe.Create(),
    };

    // ------------------------------------------------------------ table coverage

    [Theory]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    public void Translation_CoversEveryEnglishKey(string culture)
    {
        var missing = Translations.En.Keys.Except(Tables[culture].Keys).ToList();
        Assert.True(missing.Count == 0,
            $"{culture} is missing {missing.Count} keys: {string.Join(", ", missing.Take(12))}");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    public void Translation_HasNoUnknownKeys(string culture)
    {
        var extra = Tables[culture].Keys.Except(Translations.En.Keys).ToList();
        Assert.True(extra.Count == 0,
            $"{culture} has {extra.Count} keys English lacks: {string.Join(", ", extra.Take(12))}");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    public void Translation_ValuesAreNotEmptyAndUseTheSamePlaceholders(string culture)
    {
        var placeholders = new Regex(@"\{\d+(?:,[^}]*)?\}");

        foreach (var (key, english) in Translations.En)
        {
            var translated = Tables[culture][key];

            Assert.False(string.IsNullOrWhiteSpace(translated), $"{culture}:{key} is empty");

            var expected = placeholders.Matches(english).Select(m => m.Value).OrderBy(v => v);
            var actual = placeholders.Matches(translated).Select(m => m.Value).OrderBy(v => v);
            Assert.True(expected.SequenceEqual(actual),
                $"{culture}:{key} placeholders [{string.Join(",", actual)}] " +
                $"differ from English [{string.Join(",", expected)}]");
        }
    }

    // ------------------------------------------------------------ Loc behaviour

    [Fact]
    public void Loc_FallsBackToEnglish_ForUnknownKeys()
    {
        // The test machine's UI culture decides the active language; an unknown
        // key must resolve to the English master value (or the key itself).
        Assert.Equal(Translations.En["Menu.File"], Loc.T("Menu.File"));
        Assert.Equal("No.Such.Key.Anywhere", Loc.T("No.Such.Key.Anywhere"));
    }

    [Fact]
    public void Loc_SupportedCultures_MatchTheTables()
    {
        Assert.Equal(new[] { "en", "fr", "it", "de" }, Loc.SupportedCultures);
        foreach (var culture in Loc.SupportedCultures.Where(c => c != "en"))
            Assert.NotNull(Translations.For(culture));
    }

    // ------------------------------------------------------------ source usage

    /// <summary>Walks up from the test assembly to the repository root (the
    /// directory holding src/OricOffice.Core); null when sources are absent.</summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "OricOffice.Core")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void EveryKeyReferencedInSource_ExistsInTheMasterTable()
    {
        var root = RepoRoot();
        if (root is null) return; // sources not on this machine

        var codeKeys = new Regex(@"Loc\.(?:T|Format)\(\s*""([^""]+)""");
        var xamlKeys = new Regex(@"loc:Translate\s+([A-Za-z0-9.]+)");
        var interpolated = new Regex(@"Loc\.T\(\$""([^""]+)"""); // dynamic keys like BorderStyle.{style}

        var missing = new SortedSet<string>();

        var sourceRoot = Path.Combine(root, "src");
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                 !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in codeKeys.Matches(text))
                if (!Translations.En.ContainsKey(match.Groups[1].Value))
                    missing.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");
            foreach (Match match in xamlKeys.Matches(text))
                if (!Translations.En.ContainsKey(match.Groups[1].Value))
                    missing.Add($"{match.Groups[1].Value} ({Path.GetFileName(file)})");

            // Interpolated keys name an enum member: check each member exists.
            foreach (Match match in interpolated.Matches(text))
                foreach (var member in Enum.GetNames<BorderStyle>())
                    if (!Translations.En.ContainsKey(match.Groups[1].Value.Replace("{style}", member)))
                        missing.Add($"{match.Groups[1].Value.Replace("{style}", member)} ({Path.GetFileName(file)})");
        }

        Assert.True(missing.Count == 0,
            $"keys used in source but missing from Translations.En: {string.Join(", ", missing.Take(15))}");
    }

    [Fact]
    public void ExportSuccessStatusKeys_ExistInTheMasterTable()
    {
        // ExportAsync receives the success-status key as a string argument;
        // these are the values passed throughout the view models.
        foreach (var key in new[] { "Status.Exported", "Status.PrintedToFile" })
            Assert.True(Translations.En.ContainsKey(key), $"{key} missing");
    }
}
