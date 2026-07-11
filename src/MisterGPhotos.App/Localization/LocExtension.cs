using System;
using System.Windows.Markup;
using MisterGPhotos.Core.Resources;

namespace MisterGPhotos.App.Localization;

/// <summary>
/// XAML localization extension: <c>{l:Loc Some_Key}</c> returns the translated string.
/// Resolution happens at load time (the language is fixed at startup based on the OS),
/// which is sufficient since we do not switch language at runtime.
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
