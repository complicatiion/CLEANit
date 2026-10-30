using CLEANit.Platform.Windows;
using Xunit;

namespace CLEANit.Core.Tests;

public class PlatformParserTests
{
    [Fact]
    public void WingetTableIsParsedByColumns()
    {
        const string output = "   - \r   \\ \r\n" +
                              "Name                          Id                        Version   Verfügbar  Quelle\n" +
                              "-----------------------------------------------------------------------------------\n" +
                              "Mozilla Firefox (x64 de)      Mozilla.Firefox.de        131.0     132.0      winget\n" +
                              "Visual Studio Code            Microsoft.VisualStudioCode 1.94.0   1.95.1     winget\n" +
                              "2 Aktualisierungen verfügbar.\n";

        var updates = WinGetClient.ParseTable(output);

        Assert.Equal(2, updates.Count);
        Assert.Equal("Mozilla.Firefox.de", updates[0].Id);
        Assert.Equal("132.0", updates[0].Available);
        Assert.False(updates[0].ExplicitOnly);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe --remove", "C:\\Program Files\\App\\uninstall.exe", "--remove")]
    [InlineData("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}", "MsiExec.exe", "/I{12345678-1234-1234-1234-123456789012}")]
    public void UninstallCommandsAreSplit(string command, string file, string args)
    {
        var split = InstalledPrograms.SplitCommand(command);

        Assert.NotNull(split);
        Assert.Equal(file, split.Value.File);
        Assert.Equal(args, split.Value.Args);
    }

    [Theory]
    [InlineData("\"C:\\Tools\\a.exe\" -min", "C:\\Tools\\a.exe")]
    [InlineData("C:\\Program Files\\B\\b.exe /background", "C:\\Program Files\\B\\b.exe")]
    public void StartupExecutableIsFound(string command, string expected) =>
        Assert.Equal(expected, StartupManager.ExecutableFromCommand(command));
}
