using System.Globalization;
using GPhotosUploader.Core.Resources;
using GPhotosUploader.Core.Services;
using Xunit;

namespace GPhotosUploader.Tests;

public class OAuthClientConfigTests
{
    private const string ValidInstalledJson = """
        {
          "installed": {
            "client_id": "123456789012-abcdefghijklmnopqrstuvwxyz123456.apps.googleusercontent.com",
            "project_id": "photos-uploader-123456",
            "auth_uri": "https://accounts.google.com/o/oauth2/auth",
            "token_uri": "https://oauth2.googleapis.com/token",
            "client_secret": "GOCSPX-AbCdEfGhIjKlMnOpQrStUvWx",
            "redirect_uris": ["http://localhost"]
          }
        }
        """;

    [Fact]
    public void ParseClientSecretJson_ReadsInstalledClient()
    {
        var creds = OAuthClientConfig.ParseClientSecretJson(ValidInstalledJson);
        Assert.EndsWith(".apps.googleusercontent.com", creds.ClientId);
        Assert.StartsWith("GOCSPX-", creds.ClientSecret);
    }

    [Fact]
    public void ParseClientSecretJson_RejectsWebClient_WithExplicitMessage()
    {
        Loc.Culture = CultureInfo.GetCultureInfo("en");
        var webJson = """{"web": {"client_id": "x.apps.googleusercontent.com", "client_secret": "secret1234"}}""";
        var ex = Assert.Throws<FormatException>(() => OAuthClientConfig.ParseClientSecretJson(webJson));
        Assert.Contains("Desktop app", ex.Message);
    }

    [Fact]
    public void ParseClientSecretJson_RejectsInvalidJson()
    {
        Assert.Throws<FormatException>(() => OAuthClientConfig.ParseClientSecretJson("pas du json"));
    }

    [Fact]
    public void ParseClientSecretJson_RejectsUnrelatedJson()
    {
        Assert.Throws<FormatException>(() => OAuthClientConfig.ParseClientSecretJson("""{"foo": 1}"""));
    }

    [Fact]
    public void ParseClientSecretJson_RejectsMissingFields()
    {
        Assert.Throws<FormatException>(() =>
            OAuthClientConfig.ParseClientSecretJson("""{"installed": {"client_id": "invalid"}}"""));
    }

    [Theory]
    [InlineData("""{"installed": null}""")]
    [InlineData("""{"installed": []}""")]
    [InlineData("""{"installed": "x"}""")]
    [InlineData("""{"installed": {"client_id": 123, "client_secret": true}}""")]
    [InlineData("""[1, 2, 3]""")]
    [InlineData("""null""")]
    public void ParseClientSecretJson_RejectsWrongJsonTypes_WithFormatException(string json)
    {
        // Never an InvalidOperationException: the contract is FormatException only.
        Assert.Throws<FormatException>(() => OAuthClientConfig.ParseClientSecretJson(json));
    }

    [Theory]
    [InlineData("123456-abc.apps.googleusercontent.com", true)]
    [InlineData("  123456-abc.apps.googleusercontent.com  ", true)]
    [InlineData("", false)]
    [InlineData("juste-un-texte", false)]
    [InlineData(".apps.googleusercontent.com", false)]
    [InlineData("with space.apps.googleusercontent.com", false)]
    public void IsValidClientId_Cases(string input, bool expected)
    {
        Assert.Equal(expected, OAuthClientConfig.IsValidClientId(input));
    }

    [Theory]
    [InlineData("GOCSPX-AbCdEfGhIjKl", true)]
    [InlineData("", false)]
    [InlineData("court", false)]
    [InlineData("with space inside!", false)]
    public void IsPlausibleClientSecret_Cases(string input, bool expected)
    {
        Assert.Equal(expected, OAuthClientConfig.IsPlausibleClientSecret(input));
    }
}
