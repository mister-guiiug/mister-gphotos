using System.Globalization;
using System.Resources;

namespace MisterGPhotos.Core.Resources;

/// <summary>
/// Single access point for translated strings (i18n). The texts are stored in
/// Strings.resx (English, default/fallback language) and Strings.&lt;culture&gt;.resx
/// (e.g. Strings.fr.resx). The language is the one from the OS: <see cref="Culture"/> defaults
/// to <see cref="CultureInfo.CurrentUICulture"/> and can be set at startup.
///
/// To add a language: duplicate Strings.resx as Strings.&lt;code&gt;.resx and
/// translate the values. No code change is necessary.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Rm =
        new("MisterGPhotos.Core.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Culture used for resolution (default: the OS display language).</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    /// <summary>Translated string for the given key (returns the key if it is missing).</summary>
    public static string T(string key) => Rm.GetString(key, Culture) ?? key;

    /// <summary>Translated and formatted string (string.Format with the current culture).</summary>
    public static string TF(string key, params object?[] args) =>
        string.Format(Culture, T(key), args);
}
