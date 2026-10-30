using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Software;
using CLEANit.Core.Tweaks;
using CLEANit.Platform.Windows;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.3: package bundles, winget parsers, folder tree, Windows settings engine.</summary>
public class SoftwareTests
{
    [Fact]
    public void BundleRoundTripsInWingetFormat()
    {
        var bundle = new PackageBundle();
        Assert.True(bundle.Add(new BundlePackage("GodotEngine.GodotEngine")));
        Assert.True(bundle.Add(new BundlePackage("Git.Git", "2.47.0")));
        Assert.False(bundle.Add(new BundlePackage("git.git")));
        Assert.True(bundle.Add(new BundlePackage("9NBLGGH4NNS1", null, "msstore")));

        var json = bundle.ToJson();
        Assert.Contains(PackageBundle.SchemaUrl, json);
        Assert.Contains("\"PackageIdentifier\": \"Git.Git\"", json);

        var back = PackageBundle.Parse(json);
        Assert.Equal(3, back.Packages.Count);
        Assert.Equal("2.47.0", Assert.Single(back.Packages, p => p.Id == "Git.Git").Version);
        Assert.Equal("msstore", Assert.Single(back.Packages, p => p.Id == "9NBLGGH4NNS1").Source);
    }

    [Fact]
    public void WingetExportFileIsRead()
    {
        const string json = """
            { "$schema": "https://aka.ms/winget-packages.schema.2.0.json", "CreationDate": "2026-10-29T10:00:00.000+01:00",
              "Sources": [ { "Packages": [ { "PackageIdentifier": "7zip.7zip" }, { "PackageIdentifier": "Mozilla.Firefox", "Version": "131.0" } ],
                             "SourceDetails": { "Argument": "https://cdn.winget.microsoft.com/cache", "Identifier": "Microsoft.Winget.Source_8wekyb3d8bbwe",
                                                "Name": "winget", "Type": "Microsoft.PreIndexed.Package" } } ],
              "WinGetVersion": "1.9.25200" }
            """;
        var bundle = PackageBundle.Parse(json);
        Assert.Equal(2, bundle.Packages.Count);
        Assert.All(bundle.Packages, p => Assert.Equal("winget", p.Source));
    }

    [Fact]
    public void SearchTableIsParsed()
    {
        const string output = "Name                Id                       Version  Match\n" +
                              "-------------------------------------------------------------\n" +
                              "Godot Engine        GodotEngine.GodotEngine  4.3      \n" +
                              "Godot Engine - Mono GodotEngine.GodotEngine.Mono 4.3  Tag: godot\n";
        var packages = WinGetClient.ParseSearch(output, "winget");
        Assert.Equal(2, packages.Count);
        Assert.Equal("GodotEngine.GodotEngine", packages[0].Id);
        Assert.Equal("4.3", packages[0].Version);
        Assert.Null(packages[0].Match);
    }

    [Fact]
    public void ListTableMarksLocalEntries()
    {
        const string output = "Name          Id                               Version   Verfügbar Quelle\n" +
                              "----------------------------------------------------------------------------\n" +
                              "7-Zip         7zip.7zip                        24.08     24.09     winget\n" +
                              "Old Tool      ARP\\Machine\\X64\\OldTool          1.0\n";
        var list = WinGetClient.ParseList(output);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsKnown);
        Assert.Equal("24.09", list[0].Available);
        Assert.False(list[1].IsKnown);
    }

    [Fact]
    public void ShowOutputIsParsed()
    {
        const string output = "Gefunden Godot Engine [GodotEngine.GodotEngine]\n" +
                              "Version: 4.3\n" +
                              "Herausgeber: Godot Engine\n" +
                              "Beschreibung: Multi-platform 2D and 3D game engine\n" +
                              "  with a dedicated editor.\n" +
                              "Startseite: https://godotengine.org\n" +
                              "Lizenz: MIT\n" +
                              "Lizenz-URL: https://godotengine.org/license\n";
        var details = WinGetClient.ParseShow(output);
        Assert.NotNull(details);
        Assert.Equal("GodotEngine.GodotEngine", details.Id);
        Assert.Equal("Godot Engine", details.Name);
        Assert.Equal("MIT", details.License);
        Assert.Equal("https://godotengine.org", details.Homepage);
        Assert.Contains("dedicated editor", details.Description);
    }

    [Fact]
    public void ChocolateyLimitOutputIsParsed()
    {
        var list = ChocolateyClient.ParseLimitOutput("godot|4.3.0\r\ngodot-mono|4.3.0\r\nChocolatey v2.3.0\r\n");
        Assert.Equal(2, list.Count);
        Assert.Equal(("godot", "4.3.0"), list[0]);
    }

    [Fact]
    public void ProgramNamesAreNormalizedForShortcuts()
    {
        Assert.Equal(AppLauncher.Normalize("Godot Engine"), AppLauncher.Normalize("Godot Engine 4.3 (x64)"));
    }

    [Fact]
    public async Task FolderTreeSumsSizes()
    {
        var dir = Directory.CreateTempSubdirectory("cleanit-tree-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "a", "deep"));
            Directory.CreateDirectory(Path.Combine(dir.FullName, "b"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "root.bin"), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "a", "x.bin"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "a", "deep", "y.bin"), new byte[1000]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "b", "z.bin"), new byte[50]);

            var tree = await FolderTree.ScanAsync(dir.FullName, null, CancellationToken.None);

            Assert.Equal(1160, tree.Bytes);
            Assert.Equal(4, tree.Files);
            Assert.Equal(3, tree.Folders);
            Assert.Equal("a", tree.Children[0].Name);
            Assert.Equal(1100, tree.Children[0].Bytes);
            Assert.Equal(Path.Combine(dir.FullName, "a", "deep"), tree.Children[0].Children[0].Path);
            Assert.Equal("root.bin", Assert.Single(tree.LargestFiles).Name);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void TweaksAreBackedUpAndReverted()
    {
        var store = new MemoryRegistry();
        store.Write(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
        var dir = Directory.CreateTempSubdirectory("cleanit-tweaks-");
        try
        {
            var engine = new TweakEngine(store, enterprise: false, dir.FullName);
            var transparency = TweakCatalog.All.Single(t => t.Id == "appearance.transparency");
            var edge = TweakCatalog.All.Single(t => t.Id == "edge.firstrun");
            Assert.False(engine.IsApplied(transparency));

            var (backup, skipped) = engine.Apply([transparency, edge], "test");
            Assert.NotNull(backup);
            Assert.Empty(skipped);
            Assert.True(engine.IsApplied(transparency));
            Assert.True(engine.IsApplied(edge));

            engine.Revert([transparency, edge]);
            Assert.Equal(1, store.Read(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"));
            Assert.False(store.Exists(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void TelemetryUsesEditionValue()
    {
        var telemetry = TweakCatalog.All.Single(t => t.Id == "privacy.telemetry");
        Assert.Equal(1, telemetry.ValuesFor(enterprise: false)[0].Value);
        Assert.Equal(0, telemetry.ValuesFor(enterprise: true)[0].Value);
    }

    [Fact]
    public void CatalogHasDocumentationAndModerateIsExpertOnly()
    {
        Assert.All(TweakCatalog.All, t => Assert.StartsWith("https://", t.DocUrl));
        Assert.All(TweakCatalog.All.Where(t => t.Recommended), t => Assert.Equal(RiskLevel.Safe, t.Risk));
        Assert.DoesNotContain(TweakCatalog.For(22631, expert: false), t => t.Risk != RiskLevel.Safe);
        Assert.DoesNotContain(TweakCatalog.For(22631, expert: true), t => t.Id == "taskbar.feeds");
        Assert.DoesNotContain(TweakCatalog.For(19045, expert: true), t => t.Id == "taskbar.widgets");
    }

    private sealed class MemoryRegistry : IRegistryStore
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.OrdinalIgnoreCase);

        private static string K(RegHive h, string key, string name) => $"{h}\\{key}\\{name}";

        public int? Read(RegHive hive, string key, string name) => _values.TryGetValue(K(hive, key, name), out var v) ? v : null;

        public bool Exists(RegHive hive, string key, string name) => _values.ContainsKey(K(hive, key, name));

        public void Write(RegHive hive, string key, string name, int value) => _values[K(hive, key, name)] = value;

        public void Delete(RegHive hive, string key, string name) => _values.Remove(K(hive, key, name));
    }
}
