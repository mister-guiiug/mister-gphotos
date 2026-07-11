using System.Globalization;
using System.Resources;

namespace GPhotosUploader.Core.Resources;

/// <summary>
/// Point d'accès unique aux chaînes traduites (i18n). Les textes sont stockés dans
/// Strings.resx (anglais, langue par défaut/repli) et Strings.&lt;culture&gt;.resx
/// (ex. Strings.fr.resx). La langue est celle de l'OS : <see cref="Culture"/> vaut
/// par défaut <see cref="CultureInfo.CurrentUICulture"/> et peut être fixée au démarrage.
///
/// Pour ajouter une langue : dupliquer Strings.resx en Strings.&lt;code&gt;.resx et
/// traduire les valeurs. Aucune modification de code n'est nécessaire.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Rm =
        new("GPhotosUploader.Core.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>Culture utilisée pour la résolution (par défaut : langue d'affichage de l'OS).</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    /// <summary>Chaîne traduite pour la clé donnée (renvoie la clé si elle est absente).</summary>
    public static string T(string key) => Rm.GetString(key, Culture) ?? key;

    /// <summary>Chaîne traduite et formatée (string.Format avec la culture courante).</summary>
    public static string TF(string key, params object?[] args) =>
        string.Format(Culture, T(key), args);
}
