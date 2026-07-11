using System;
using System.Windows.Markup;
using GPhotosUploader.Core.Resources;

namespace GPhotosUploader.App.Localization;

/// <summary>
/// Extension XAML de localisation : <c>{l:Loc Some_Key}</c> renvoie la chaîne traduite.
/// La résolution a lieu au chargement (la langue est fixée au démarrage selon l'OS),
/// ce qui est suffisant puisqu'on ne change pas de langue à chaud.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
