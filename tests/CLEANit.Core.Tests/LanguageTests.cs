using CLEANit.Core.Localization;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.7: language on the first start.</summary>
public class LanguageTests
{
    [Fact]
    public void German_DisplayLanguage_Region_Or_Keyboard()
    {
        Assert.Equal("de", LanguageDetection.Choose("de-DE", "de-DE", []));
        Assert.Equal("de", LanguageDetection.Choose("en-US", "de-AT", []));
        Assert.Equal("de", LanguageDetection.Choose("en-US", "en-US", [0x0409, 0x0407]));
        Assert.Equal("de", LanguageDetection.Choose("en-US", "en-US", [0x0807]));
    }

    [Fact]
    public void English_Otherwise()
    {
        Assert.Equal("en", LanguageDetection.Choose("en-US", "en-US", [0x0409]));
        Assert.Equal("en", LanguageDetection.Choose("fr-FR", "fr-FR", [0x040C]));
        Assert.Equal("en", LanguageDetection.Choose(null, null, []));
        Assert.Equal("en", LanguageDetection.Choose("fr-CH", "fr-CH", [0x100C]));
        Assert.Equal("en", LanguageDetection.Choose("en-GB", "en-DE-x", []));
    }
}
