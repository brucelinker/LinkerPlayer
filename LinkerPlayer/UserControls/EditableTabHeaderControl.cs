using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

public class EditableTabHeaderControl : ContentControl
{
    private static readonly DependencyProperty IsInEditModeProperty =
        DependencyProperty.Register(nameof(IsInEditMode), typeof(bool), typeof(EditableTabHeaderControl));
    private TextBox? _textBox;
    private string? _oldText;
    private DispatcherTimer? _timer;
    private bool _isShuttingDown;
    private delegate void FocusTextBox();

    public EditableTabHeaderControl()
    {
        // Subscribe to MainWindow.Closing
        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.Closing += (s, e) => _isShuttingDown = true;
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textBox = Template.FindName("PART_TabHeader", this) as TextBox;

        if (_textBox != null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += TimerTick!;
            _timer.Interval = TimeSpan.FromMilliseconds(1);
            LostFocus += TextBoxLostFocus;
            _textBox.KeyDown += TextBoxKeyDown;
            MouseDoubleClick += EditableTabHeaderControlMouseDoubleClick;
            if (DataContext is PlaylistTab tab)
            {
                _oldText = tab.Name;
            }
        }
    }

    public bool IsInEditMode
    {
        get => (bool)GetValue(IsInEditModeProperty);
        set
        {
            if (_textBox != null && string.IsNullOrEmpty(_textBox.Text))
            {
                if (_oldText != null) _textBox.Text = _oldText;
            }
            SetValue(IsInEditModeProperty, value);
        }
    }

    public void SetEditMode(bool value)
    {
        if (value)
        {
            if (DataContext is PlaylistTab tab)
            {
                _oldText = tab.Name;
            }
            else
            {
                Log.Warning("EditableTabHeaderControl: DataContext is not PlaylistTab in SetEditMode");
            }
            var viewModel = FindAncestorViewModel(this);
            if (viewModel != null)
            {
                Tag = viewModel;
            }
            else
            {
                Log.Error("EditableTabHeaderControl: Could not find PlaylistTabsViewModel in SetEditMode");
            }
        }
        IsInEditMode = value;
        if (value && _timer != null)
        {
            _timer.Start();
        }
    }

    private void TimerTick(object sender, EventArgs e)
    {
        _timer!.Stop();
        MoveTextBoxInFocus();
    }

    private void MoveTextBoxInFocus()
    {
        if (_textBox!.CheckAccess())
        {
            if (!string.IsNullOrEmpty(_textBox.Text))
            {
                _textBox.CaretIndex = _textBox.Text.Length;
                _textBox.Focus();
            }
        }
        else
        {
            _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Render, new FocusTextBox(MoveTextBoxInFocus));
        }
    }

    private async void TextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_oldText != null) _textBox!.Text = _oldText;
            IsInEditMode = false;
        }
        else if (e.Key == Key.Enter)
        {
            IsInEditMode = false;
            if (DataContext is PlaylistTab tab && Tag is PlaylistTabsViewModel viewModel)
            {
                await viewModel.RenamePlaylistAsync((tab, _oldText));
            }
            else
            {
                Log.Error("EditableTabHeaderControl: Invalid DataContext or Tag in TextBoxKeyDown, DataContext: {DataType}, Tag: {TagType}",
                    DataContext?.GetType()?.FullName ?? "null", Tag?.GetType()?.FullName ?? "null");
            }
        }
    }

    private void TextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            Log.Information("EditableTabHeaderControl: Skipping TextBoxLostFocus during app shutdown");
            IsInEditMode = false;
            return;
        }

        IsInEditMode = false;
        if (DataContext is PlaylistTab tab && Tag is PlaylistTabsViewModel viewModel && _textBox!.Text != _oldText)
        {
            _ = viewModel.RenamePlaylistAsync((tab, _oldText));
        }
        else if (_textBox!.Text != _oldText)
        {
            Log.Error("EditableTabHeaderControl: Invalid DataContext or Tag in TextBoxLostFocus, DataContext: {DataType}, Tag: {TagType}",
                DataContext?.GetType()?.FullName ?? "null", Tag?.GetType()?.FullName ?? "null");
        }
    }

    private void EditableTabHeaderControlMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            SetEditMode(true);
        }
    }

    private PlaylistTabsViewModel? FindAncestorViewModel(DependencyObject obj)
    {
        while (obj != null)
        {
            if (obj is FrameworkElement element && element.DataContext is PlaylistTabsViewModel viewModel)
            {
                return viewModel;
            }
            if (obj is FrameworkElement fe)
            {
                obj = LogicalTreeHelper.GetParent(fe) ?? VisualTreeHelper.GetParent(fe);
            }
            else
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
        }
        return null;
    }
}