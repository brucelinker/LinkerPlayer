using LinkerPlayer.Models;
using LinkerPlayer.Properties;
using LinkerPlayer.UserControls;
using LinkerPlayer.Windows;
using System;
using System.Windows;

namespace LinkerPlayer.Core;

public class ThemeManager
{
    public static ThemeColors ActiveSkin = ThemeColors.Dark;

    private void ClearStyles()
    {
        if (MainWindow.Instance == null) return;

        Application.Current.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.Resources.MergedDictionaries.Clear();
    }

    public static void AddDict(ResourceDictionary resDict)
    {
        TrackInfo.ReloadDefaultAlbumImage();

        if (MainWindow.Instance == null) return;

        Application.Current.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.Resources.MergedDictionaries.Add(resDict);
    }

    public static string GetThemeUri(ThemeColors theme)
    {
        string resourceLocator;

        switch (theme)
        {
            case ThemeColors.Slate: resourceLocator = @"Themes\Slate.xaml"; break;
            case ThemeColors.Light: resourceLocator = @"Themes\Light.xaml"; break;
            case ThemeColors.Dark: resourceLocator = @"Themes\Dark.xaml"; break;
            case ThemeColors.Gray: resourceLocator = @"Themes\Gray.xaml"; break;
            case ThemeColors.Midnight: resourceLocator = @"Themes\Midnight.xaml"; break;
            default: resourceLocator = @"Themes\White.xaml"; break;
        }

        return resourceLocator;
    }

    public static Uri GetSizeUri(FontSize size)
    {
        string resourceLocator = string.Empty;
        switch (size)
        {
            case FontSize.Big: resourceLocator = @"Styles\FontSizes\Big.xaml"; break;
            case FontSize.Bigger: resourceLocator = @"Styles\FontSizes\Bigger.xaml"; break;
            case FontSize.Biggest: resourceLocator = @"Styles\FontSizes\Biggest.xaml"; break;
            case FontSize.Huge: resourceLocator = @"Styles\FontSizes\Huge.xaml"; break;
            case FontSize.Medium: resourceLocator = @"Styles\FontSizes\Medium.xaml"; break;
            case FontSize.Normal: resourceLocator = @"Styles\FontSizes\FontSizesNormal.xaml"; break;
            case FontSize.Small: resourceLocator = @"Styles\FontSizes\Small.xaml"; break;
            case FontSize.Smaller: resourceLocator = @"Styles\FontSizes\Smaller.xaml"; break;
            case FontSize.Smallest: resourceLocator = @"Styles\FontSizes\Smallest.xaml"; break;
            case FontSize.Gonzo: resourceLocator = @"Styles\FontSizes\Gonzo.xaml"; break;
        }

        Uri langDictUri = new Uri(resourceLocator, UriKind.Relative);
        return langDictUri;
    }

    public ThemeColors ModifyTheme(ThemeColors themeColor, FontSize fontSize = FontSize.Normal)
    {
        ClearStyles();
        AddTheme(themeColor);

        const string colors = @"Styles\SolidColorBrushes.xaml";
        Uri colorsUri = new Uri(colors, UriKind.Relative);
        ResourceDictionary brushesDict = (Application.LoadComponent(colorsUri) as ResourceDictionary)!;

        AddDict(brushesDict);

        Uri sizeUri = ThemeManager.GetSizeUri(fontSize);
        ResourceDictionary sizesDict = (Application.LoadComponent(sizeUri) as ResourceDictionary)!;

        AddDict(sizesDict);

        return themeColor;
    }

    public static void AddTheme(ThemeColors skin)
    {
        string uri = GetThemeUri(skin);
        if (string.IsNullOrEmpty(uri))
            return;

        Uri? langDictUri = new Uri(uri, UriKind.Relative);

        if (langDictUri == null!)
            return;

        ResourceDictionary langDict = (Application.LoadComponent(langDictUri) as ResourceDictionary)!;

        AddDict(langDict);
    }

    private void ApplyTheme(string uri)
    {
        ClearStyles();

        Uri langDictUri = new Uri(uri, UriKind.Relative);
        ResourceDictionary langDict = (Application.LoadComponent(langDictUri) as ResourceDictionary)!;

        AddDict(langDict);
    }

    private void ApplyTheme(MainWindow main, ThemeColors skin, FontSize size)
    {
        ClearStyles();
        AddTheme(skin);

        string colors = @"Styles\SolidColorBrushes.xaml";
        Uri colorsUri = new Uri(colors, UriKind.Relative);
        ResourceDictionary brushesDict = (Application.LoadComponent(colorsUri) as ResourceDictionary)!;

        AddDict(brushesDict);

        Uri sizeUri = GetSizeUri(size);
        ResourceDictionary sizesDict = (Application.LoadComponent(sizeUri) as ResourceDictionary)!;

        AddDict(sizesDict);

        ActiveSkin = skin;
    }

    public void ApplyThemeByName(MainWindow main, string theme, string fileName = "")
    {
        FontSize currentSize = (FontSize)Settings.Default.FontSize;
        switch (theme)
        {
            case "Light": ApplyTheme(main, ThemeColors.Light, currentSize); break;
            case "Slate": ApplyTheme(main, ThemeColors.Slate, currentSize); break;
            case "Gray": ApplyTheme(main, ThemeColors.Gray, currentSize); break;
            case "Dark": ApplyTheme(main, ThemeColors.Dark, currentSize); break;
            case "URI": ApplyTheme(fileName); break;
            default: ApplyTheme(main, ThemeColors.Light, currentSize); break;
        }
    }

    public ThemeColors StringToThemeColor(string theme)
    {
        switch (theme)
        {
            case "Light": return ThemeColors.Light;
            case "Slate": return ThemeColors.Slate;
            case "Gray": return ThemeColors.Gray;
            case "Dark": return ThemeColors.Dark;
            default: return ThemeColors.Midnight;
        }
    }

    public int StringToThemeColorIndex(string theme)
    {
        switch (theme)
        {
            case "Light": return (int)ThemeColors.Light;
            case "Slate": return (int)ThemeColors.Slate;
            case "Gray": return (int)ThemeColors.Gray;
            case "Dark": return (int)ThemeColors.Dark;
            default: return (int)ThemeColors.Midnight;
        }
    }

    public string IndexToThemeColorString(int theme)
    {
        switch (theme)
        {
            case (int)ThemeColors.Light: return "Light";
            case (int)ThemeColors.Slate: return "Slate";
            case (int)ThemeColors.Gray: return "Gray";
            case (int)ThemeColors.Dark: return "Dark";
            default: return "Midnight";
        }
    }
}