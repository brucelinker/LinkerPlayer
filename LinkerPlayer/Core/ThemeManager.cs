using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using System;
using System.Windows;

namespace LinkerPlayer.Core;

public class ThemeManager
{
    private void ClearStyles()
    {
        if (MainWindow.Instance == null) return;

        Application.Current.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.Resources.MergedDictionaries.Clear();
    }

    public static void AddDict(ResourceDictionary resDict)
    {
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
            default: resourceLocator = @"Themes\Slate.xaml"; break;
        }

        return resourceLocator;
    }

    public void ModifyTheme(ThemeColors themeColor)
    {
        ClearStyles();
        
        // Add theme colors first
        AddTheme(themeColor);

        // Add the solid color brushes
        const string colors = @"Styles\SolidColorBrushes.xaml";
        Uri colorsUri = new(colors, UriKind.Relative);
        ResourceDictionary brushesDict = (Application.LoadComponent(colorsUri) as ResourceDictionary)!;
        AddDict(brushesDict);

        // Re-add the essential style dictionaries that were cleared
        const string stylesRepo = @"Styles\StylesRepository.xaml";
        Uri stylesUri = new(stylesRepo, UriKind.Relative);
        ResourceDictionary stylesDict = (Application.LoadComponent(stylesUri) as ResourceDictionary)!;
        AddDict(stylesDict);

        const string horizontalSlider = @"Styles\HorizontalSlider.xaml";
        Uri sliderUri = new(horizontalSlider, UriKind.Relative);
        ResourceDictionary sliderDict = (Application.LoadComponent(sliderUri) as ResourceDictionary)!;
        AddDict(sliderDict);

        const string rectangleButton = @"Styles\RectangleButton.xaml";
        Uri buttonUri = new(rectangleButton, UriKind.Relative);
        ResourceDictionary buttonDict = (Application.LoadComponent(buttonUri) as ResourceDictionary)!;
        AddDict(buttonDict);

        const string circularButton = @"Styles\CircularButton.xaml";
        Uri circularUri = new(circularButton, UriKind.Relative);
        ResourceDictionary circularDict = (Application.LoadComponent(circularUri) as ResourceDictionary)!;
        AddDict(circularDict);
    }

    public static void AddTheme(ThemeColors skin)
    {
        string uri = GetThemeUri(skin);
        if (string.IsNullOrEmpty(uri))
            return;

        Uri? langDictUri = new(uri, UriKind.Relative);

        if (langDictUri == null!)
            return;

        ResourceDictionary langDict = (Application.LoadComponent(langDictUri) as ResourceDictionary)!;

        AddDict(langDict);
    }

    public ThemeColors StringToThemeColor(string theme)
    {
        switch (theme)
        {
            case "Light": return ThemeColors.Light;
            case "Gray": return ThemeColors.Gray;
            case "Dark": return ThemeColors.Dark;
            case "Midnight": return ThemeColors.Midnight;
            default: return ThemeColors.Slate;
        }
    }

    public int StringToThemeColorIndex(string theme)
    {
        switch (theme)
        {
            case "Light": return (int)ThemeColors.Light;
            case "Gray": return (int)ThemeColors.Gray;
            case "Dark": return (int)ThemeColors.Dark;
            case "Midnight": return (int)ThemeColors.Midnight;
            default: return (int)ThemeColors.Slate;
        }
    }

    public string IndexToThemeColorString(int theme)
    {
        switch (theme)
        {
            case (int)ThemeColors.Light: return "Light";
            case (int)ThemeColors.Gray: return "Gray";
            case (int)ThemeColors.Dark: return "Dark";
            case (int)ThemeColors.Midnight: return "Midnight";
            default: return "Slate";
        }
    }
}