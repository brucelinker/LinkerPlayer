using LinkerPlayer.Windows;
using System.Windows;
using System;
using LinkerPlayer.Models;
using LinkerPlayer.Properties;

namespace LinkerPlayer.Core;

public class ThemeManager
{
    public static ThemeColors ActiveSkin = ThemeColors.White;

    public static void ClearStyles()
    {
        Application.Current.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.TrackList.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.FunctionButtons.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.PlayerControls.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.TrackList.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.PlaylistList.Resources.MergedDictionaries.Clear();
        //MainWindow.Instance.PlaylistTabs.Resources.MergedDictionaries.Clear();
        MainWindow.Instance.TitlebarButtons.Resources.MergedDictionaries.Clear();
        //MainWindow.Instance.Favourites.Resources.MergedDictionaries.Clear();
        //MainWindow.Instance.AlbumDetails.Resources.MergedDictionaries.Clear();
        //MainWindow.Instance.Playlist.PlaylistStatus.Resources.MergedDictionaries.Clear();
        //MainWindow.Instance.SmartPlayer.Spectrum.Resources.MergedDictionaries.Clear();
    }

    public static void AddDict(ResourceDictionary resDict)
    {
        DefaultAlbumImage.Reload();

        Application.Current.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.TrackList.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.FunctionButtons.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.PlayerControls.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.PlaylistList.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.PlaylistTabs.Resources.MergedDictionaries.Add(resDict);
        MainWindow.Instance.TitlebarButtons.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.setctrl.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.TrackTable.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.Favourites.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.AlbumDetails.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.Playlist.PlaylistStatus.Resources.MergedDictionaries.Add(resDict);
        //MainWindow.Instance.SmartPlayer.OnThemeChanged();
    }

    public static string GetThemeUri(ThemeColors theme)
    {
        string resourceLocator;

        switch (theme)
        {
            case ThemeColors.Blue: resourceLocator = @"Themes\Blue.xaml"; break;
            case ThemeColors.White: resourceLocator = @"Themes\White.xaml"; break;
            case ThemeColors.Dark: resourceLocator = @"Themes\Dark.xaml"; break;
            case ThemeColors.Gray: resourceLocator = @"Themes\Gray.xaml"; break;
            case ThemeColors.BlackSmooth: resourceLocator = @"Themes\BlackSmooth.xaml"; break;
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

    public static void ApplyTheme(string uri)
    {
        ClearStyles();

        Uri langDictUri = new Uri(uri, UriKind.Relative);
        ResourceDictionary langDict = (Application.LoadComponent(langDictUri) as ResourceDictionary)!;

        AddDict(langDict);
    }

    public static void ApplyTheme(MainWindow main, ThemeColors skin, FontSize size)
    {
        ResourceDictionary origDict = Application.Current.Resources;

        ClearStyles();
        AddTheme(skin);

        string colors = @"Styles\SolidColorBrushes.xaml";
        Uri colorsUri = new Uri(colors, UriKind.Relative);
        ResourceDictionary brushesDict = (Application.LoadComponent(colorsUri) as ResourceDictionary)!;

        AddDict(brushesDict);

        Uri size_uri = GetSizeUri(size);
        ResourceDictionary sizesDict = (Application.LoadComponent(size_uri) as ResourceDictionary)!;

        AddDict(sizesDict);

        ActiveSkin = skin;

        //Application.Current.Resources.MergedDictionaries[0].Values.ToString();
    }

    public static void ApplyThemeByName(MainWindow main, string theme, string fileName = "")
    {
        FontSize currentSize = (FontSize)Settings.Default.FontSize;
        switch (theme)
        {
            case "White": ApplyTheme(main, ThemeColors.White, currentSize); break;
            case "Blue": ApplyTheme(main, ThemeColors.Blue, currentSize); break;
            case "Dark": ApplyTheme(main, ThemeColors.Dark, currentSize); break;
            case "Custom": ApplySkinFromFile(main, fileName); break;
            case "URI": ApplyTheme(fileName); break;
            default: ApplyTheme(main, ThemeColors.White, currentSize); break;
        }
    }

    public static void ApplySkinFromFile(MainWindow main, string skinFile)
    {
    }

    public static void ApplyFontSize(MainWindow main, FontSize size)
    {
        ApplyTheme(main, (ThemeColors)Settings.Default.SelectedTheme, size);
        //MainWindow.Instance.TrackTable.UpdateMargín(size);
        //MainWindow.Instance.Favorites.UpdateMargín(size);
    }

    public static void ApplyPadding(MainWindow main, PaddingType type)
    {
        ResourceDictionary origDict = Application.Current.Resources;

        string dictStr = @"Styles\Padding\Padding" + type.ToString() + ".xaml";
        Uri paddingUri = new Uri(dictStr, UriKind.Relative);
        ResourceDictionary paddingDict = (Application.LoadComponent(paddingUri) as ResourceDictionary)!;
        AddDict(paddingDict);
    }

}