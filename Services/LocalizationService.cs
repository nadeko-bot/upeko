using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace upeko.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        ["en-US"] = "English",
        ["fr"] = "Français",
        ["de"] = "Deutsch",
        ["es"] = "Español",
        ["pl"] = "Polski",
        ["ru"] = "Русский",
        ["zh-Hans"] = "中文",
    };

    private string _currentLanguage = "en-US";
    private IResourceDictionary? _currentDictionary;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    public static IReadOnlyDictionary<string, string> Languages => SupportedLanguages;

    public string this[string key]
    {
        get
        {
            if (Application.Current is not null
                && Application.Current.TryFindResource(key, out var value)
                && value is string str)
            {
                return str;
            }
            return $"[{key}]";
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (!SupportedLanguages.ContainsKey(languageCode))
            languageCode = "en-US";

        var app = Application.Current;
        if (app is null) return;

        if (_currentDictionary is not null)
            app.Resources.MergedDictionaries.Remove(_currentDictionary);

        var uri = new Uri($"avares://upeko/Assets/Lang/Strings.{languageCode}.axaml");
        try
        {
#pragma warning disable IL2026
            var dict = (IResourceDictionary)AvaloniaXamlLoader.Load(uri);
#pragma warning restore IL2026
            app.Resources.MergedDictionaries.Add(dict);
            _currentDictionary = dict;
        }
        catch
        {
            if (languageCode != "en-US")
            {
                SetLanguage("en-US");
                return;
            }
        }

        CurrentLanguage = languageCode;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }
}
