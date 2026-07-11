using System.Globalization;
using GPhotosUploader.Core.Resources;
using Xunit;

namespace GPhotosUploader.Tests;

[Collection("Localization")]
public class LocalizationTests
{
    private static string WithCulture(string cultureName, Func<string> f)
    {
        var previous = Loc.Culture;
        try
        {
            Loc.Culture = CultureInfo.GetCultureInfo(cultureName);
            return f();
        }
        finally
        {
            Loc.Culture = previous;
        }
    }

    [Fact]
    public void English_IsTheDefaultFallback()
    {
        // An untranslated culture (e.g. German) should fall back to neutral English.
        Assert.Equal("Scan the folder", WithCulture("de-DE", () => Loc.T("Main_ScanButton")));
    }

    [Fact]
    public void French_ResolvesFrenchStrings()
    {
        Assert.Equal("Scanner le dossier", WithCulture("fr-FR", () => Loc.T("Main_ScanButton")));
    }

    [Fact]
    public void Format_UsesCultureAndArguments()
    {
        var en = WithCulture("en-US", () => Loc.TF("Account_Connected", "me@example.com"));
        Assert.Equal("Connected: me@example.com", en);

        var fr = WithCulture("fr-FR", () => Loc.TF("Account_Connected", "me@example.com"));
        Assert.Equal("Connecté : me@example.com", fr);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself()
    {
        Assert.Equal("This_Key_Does_Not_Exist", Loc.T("This_Key_Does_Not_Exist"));
    }

    [Fact]
    public void Disclaimer_IsTranslatedInBothLanguages()
    {
        Assert.Contains("entire library", WithCulture("en-US", () => Loc.T("Disclaimer_Duplicates")));
        Assert.Contains("toute votre bibliothèque", WithCulture("fr-FR", () => Loc.T("Disclaimer_Duplicates")));
    }
}
