using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<EditableTabHeaderControl> _logger;


    public EditableTabHeaderControl()
    {
        // Subscribe to MainWindow.Closing
        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.Closing += (_, _) => _isShuttingDown = true;
        }

        _logger = App.AppHost.Services.GetRequiredService<ILogger<EditableTabHeaderControl>>();
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
                _logger.LogWarning("EditableTabHeaderControl: DataContext is not PlaylistTab in SetEditMode");
            }

            PlaylistTabsViewModel? viewModel = FindAncestorViewModel(this);

            if (viewModel != null)
            {
                Tag = viewModel;
            }
            else
            {
                _logger.LogError("EditableTabHeaderControl: Could not find PlaylistTabsViewModel in SetEditMode");
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
                _textBox.SelectAll();
                //_textBox.CaretIndex = _textBox.Text.Length;
                _textBox.Focus();
            }
        }
        else
        {
            _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Render, new FocusTextBox(MoveTextBoxInFocus));
        }
    }

    private void TextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_oldText != null) _textBox!.Text = _oldText;
            IsInEditMode = false;
        }
        else if (e.Key == Key.Enter)
        {
            if (DataContext is PlaylistTab tab && Tag is PlaylistTabsViewModel viewModel)
            {
                // Store the new text value
                string newText = _textBox!.Text;

                // Update _oldText so the TextBlock shows the new value when we exit edit mode
                string? previousOldText = _oldText;
                _oldText = newText;

                IsInEditMode = false;

                // Call rename with the previous old text
                viewModel.RenamePlaylistAsync((tab, previousOldText)).GetAwaiter().GetResult();
            }
            else
            {
                IsInEditMode = false;
                _logger.LogError("EditableTabHeaderControl: Invalid DataContext or Tag in TextBoxKeyDown, DataContext: {DataType}, Tag: {TagType}",
                    DataContext?.GetType().FullName ?? "null", Tag?.GetType().FullName ?? "null");
            }
        }
    }

    private void TextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            _logger.LogInformation("EditableTabHeaderControl: Skipping TextBoxLostFocus during app shutdown");
            IsInEditMode = false;
            return;
        }

        if (DataContext is PlaylistTab tab && Tag is PlaylistTabsViewModel viewModel && _textBox!.Text != _oldText)
        {
            // Store the new text value
            string newText = _textBox.Text;
            string? previousOldText = _oldText;

            // Update _oldText so the TextBlock shows the new value when we exit edit mode
            _oldText = newText;

            IsInEditMode = false;

            // Call rename with the previous old text
            _ = viewModel.RenamePlaylistAsync((tab, previousOldText));
        }
        else
        {
            IsInEditMode = false;
            if (_textBox!.Text != _oldText)
            {
                _logger.LogError("EditableTabHeaderControl: Invalid DataContext or Tag in TextBoxLostFocus, DataContext: {DataType}, Tag: {TagType}",
                    DataContext?.GetType().FullName ?? "null", Tag?.GetType().FullName ?? "null");
            }
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
        while (obj != null!)
        {
            if (obj is FrameworkElement { DataContext: PlaylistTabsViewModel viewModel })
            {
                return viewModel;
            }

            if (obj is FrameworkElement fe)
            {
                obj = LogicalTreeHelper.GetParent(fe) ?? VisualTreeHelper.GetParent(fe)!;
            }
            else
            {
                obj = VisualTreeHelper.GetParent(obj)!;
            }
        }

        return null;
    }
}