using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using CLEANit.Core.Rules;
using CLEANit.Core.Winapp2;
using Xunit;

namespace CLEANit.Core.Tests;

public class RuleTests
{
    [Fact]
    public void BuiltinRulesLoadWithoutErrors()
    {
        var errors = new List<string>();
        var rules = RuleCatalog.LoadBuiltin(errors);

        Assert.Empty(errors);
        Assert.True(rules.Count > 50);
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct().Count());
        Assert.Contains(rules, r => r.Area == CleanArea.System);
        Assert.Contains(rules, r => r.Area == CleanArea.Browser);
        Assert.Contains(rules, r => r.Area == CleanArea.Developer);
    }

    [Fact]
    public void OnlySafeRulesArePreselected()
    {
        Assert.All(RuleCatalog.LoadBuiltin().Where(r => r.Selected), r => Assert.Equal(RiskLevel.Safe, r.Risk));
    }

    [Fact]
    public void CookiesHistoryAndSessionsAreDeep()
    {
        var browser = RuleCatalog.LoadBuiltin().Where(r => r.Area == CleanArea.Browser).ToList();
        Assert.All(browser.Where(r => r.Id.EndsWith(".cookies") || r.Id.EndsWith(".history") || r.Id.EndsWith(".session")),
            r => Assert.Equal(RiskLevel.Deep, r.Risk));
    }

    [Fact]
    public void NoBuiltinRuleNamesProtectedFiles()
    {
        foreach (var target in RuleCatalog.LoadBuiltin().SelectMany(r => r.Targets))
        {
            foreach (var pattern in target.Patterns.Where(p => p is not "*" and not "*.*"))
            {
                Assert.DoesNotContain(pattern, PathGuard.ProtectedNames);
            }
        }
    }

    [Fact]
    public void ValidationRejectsPreselectedDeepRules()
    {
        var rule = new CleanRule
        {
            Id = "x", Name = "x", Risk = RiskLevel.Deep, Selected = true,
            Targets = [new CleanTarget { Path = "%Temp%" }]
        };

        Assert.NotNull(RuleCatalog.Validate(rule));
    }
}

public class Winapp2Tests
{
    private const string Sample = """
        ; comment
        [Registry Only *]
        LangSecRef=3021
        RegKey1=HKCU\Software\Foo

        [Example App *]
        LangSecRef=3021
        DetectFile=%ProgramFiles%\Example
        FileKey1=%LocalAppData%\Example\Cache|*|RECURSE
        FileKey2=%LocalAppData%\Example|*.log
        ExcludeKey1=FILE|%LocalAppData%\Example\|keep.log
        RegKey1=HKCU\Software\Example

        [Example Browser Saved Usernames & Passwords *]
        Section=Example Web Browser
        FileKey1=%LocalAppData%\Example\User Data\*|Login Data*

        [Example Browser Caches *]
        Section=Example Web Browser
        FileKey1=%LocalAppData%\Example\User Data\*|Login Data;*.tmp
        FileKey2=%LocalAppData%\Example\User Data\*\Cache|*|REMOVESELF

        [Google Chrome Caches *]
        Section=Google Chrome Web Browser
        FileKey1=%LocalAppData%\Google\Chrome\User Data\*\Cache|*|RECURSE

        [Warned App *]
        LangSecRef=3024
        Warning=Read this first.
        FileKey1=%AppData%\Warned|*|RECURSE
        """;

    private static IReadOnlyList<CleanRule> Parse() => Winapp2Parser.Parse(new StringReader(Sample));

    [Fact]
    public void RegistryOnlyEntriesAreDropped() => Assert.DoesNotContain(Parse(), r => r.Name == "Registry Only");

    [Fact]
    public void PasswordEntriesAreDropped() => Assert.DoesNotContain(Parse(), r => r.Name.Contains("Passwords"));

    [Fact]
    public void NativeBrowsersAreNotDuplicated() => Assert.DoesNotContain(Parse(), r => r.Name.StartsWith("Google Chrome"));

    [Fact]
    public void ProtectedMasksAreRemoved()
    {
        var rule = Assert.Single(Parse(), r => r.Name == "Example Browser Caches");
        Assert.Equal("*.tmp", rule.Targets[0].Pattern);
        Assert.True(rule.Targets[1].RemoveSelf);
        Assert.Equal(CleanArea.Browser, rule.Area);
        Assert.Equal(RiskLevel.Moderate, rule.Risk);
    }

    [Fact]
    public void EntriesAreCommunityModerateAndUnselected()
    {
        var rule = Assert.Single(Parse(), r => r.Name == "Example App");
        Assert.Equal(RuleSource.Winapp2, rule.Source);
        Assert.Equal(RiskLevel.Moderate, rule.Risk);
        Assert.False(rule.Selected);
        Assert.Equal(2, rule.Targets.Count);
        Assert.True(rule.Targets[0].Recursive);
        Assert.False(rule.Targets[1].Recursive);
        Assert.Equal(@"%LocalAppData%\Example|keep.log", Assert.Single(rule.Excludes));
        Assert.Single(rule.Detect);
    }

    [Fact]
    public void WarningsMakeEntriesDeep() =>
        Assert.Equal(RiskLevel.Deep, Assert.Single(Parse(), r => r.Name == "Warned App").Risk);

    [Fact]
    public void BundledFileParses()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Winapp2.ini");
        var rules = Winapp2Parser.ParseFile(file);

        Assert.True(rules.Count > 1000);
        Assert.DoesNotContain(rules, r => r.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.All(rules, r => Assert.False(r.Selected));
    }
}
