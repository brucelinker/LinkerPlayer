using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using System;
using System.Windows;

namespace LinkerPlayer.Core;

public class ThemeManager
{
    // Cache font styles before clearing so we can restore them
    private Style? _cachedWindowFontStyle;
    private Style? _cachedUserControlFontStyle;

    private void ClearStyles()
    {
        if (MainWindow.Instance == null) return;

        // IMPORTANT: Cache font styles BEFORE clearing!
        CacheFontStyles();

        Application.Current.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.Resources.MergedDictionaries.Clear();
    }

    private void CacheFontStyles()
    {
        // Save font styles before they get cleared
        var appResources = Application.Current.Resources;

        if (appResources.Contains(typeof(Window)))
        {
            _cachedWindowFontStyle = appResources[typeof(Window)] as Style;
        }

        if (appResources.Contains(typeof(System.Windows.Controls.UserControl)))
        {
            _cachedUserControlFontStyle = appResources[typeof(System.Windows.Controls.UserControl)] as Style;
        }
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

        // Re-add MahApps IconPacks that were cleared
        ReaddIconPacks();

        // IMPORTANT: Re-apply font styles and other App.xaml resources
        ReapplyAppResources();
    }

    private void ReaddIconPacks()
    {
        // Re-add MahApps.Metro.IconPacks dictionaries
        try
        {
            var octiconsDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MahApps.Metro.IconPacks.Octicons;component/Themes/packiconocticons.xaml", UriKind.Absolute)
            };
            AddDict(octiconsDict);

            var materialDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MahApps.Metro.IconPacks.Material;component/Themes/packiconmaterial.xaml", UriKind.Absolute)
            };
            AddDict(materialDict);

            var entypoDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MahApps.Metro.IconPacks.Entypo;component/Themes/packiconentypo.xaml", UriKind.Absolute)
            };
            AddDict(entypoDict);
        }
        catch (Exception)
        {
            // IconPacks might not be critical, continue if they fail to load
        }
    }

    private void ReapplyAppResources()
    {
        // Get the original App.xaml resources before they were cleared
        // We need to re-add font styles and other application-level resources
        var app = Application.Current;

        // Re-add AppIcon if it exists in original resources
        if (!app.Resources.Contains("AppIcon"))
        {
            try
            {
                var appIcon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/LinkerPlayer;component/Images/app64.ico", UriKind.Absolute));
                app.Resources["AppIcon"] = appIcon;
            }
            catch
            {
                // Icon is not critical
            }
        }

        // Re-apply font styles from App.xaml
        // These need to be re-added after theme change to prevent font reverting to system default
        ReapplyFontStyles();
    }

    private void ReapplyFontStyles()
    {
        // Re-apply the cached font styles that were saved before clearing
        if (_cachedWindowFontStyle != null && MainWindow.Instance != null)
        {
            Application.Current.Resources[typeof(Window)] = _cachedWindowFontStyle;
            MainWindow.Instance.Resources[typeof(Window)] = _cachedWindowFontStyle;
        }

        if (_cachedUserControlFontStyle != null && MainWindow.Instance != null)
        {
            Application.Current.Resources[typeof(System.Windows.Controls.UserControl)] = _cachedUserControlFontStyle;
            MainWindow.Instance.Resources[typeof(System.Windows.Controls.UserControl)] = _cachedUserControlFontStyle;
        }
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