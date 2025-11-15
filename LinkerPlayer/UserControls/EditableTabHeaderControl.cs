using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<EditableTabHeaderControl> _logger;

    public EditableTabHeaderControl()
    {
        _logger = App.AppHost.Services.GetRequiredService<ILogger<EditableTabHeaderControl>>();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox != null)
        {
            _textBox.KeyDown -= TextBoxKeyDown;
        }

        _textBox = Template.FindName("PART_TabHeader", this) as TextBox;

        if (_textBox != null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += TimerTick!;
            _timer.Interval = TimeSpan.FromMilliseconds(1);
            _textBox.KeyDown += TextBoxKeyDown;
            if (DataContext is PlaylistTab tab)
            {
                _oldText = tab.Name;
            }
        }

        _logger.LogDebug("EditableTabHeaderControl: Template applied. DataContext={DataType}", DataContext?.GetType().FullName ?? "null");
    }

    public bool IsInEditMode
    {
        get => (bool)GetValue(IsInEditModeProperty);
        set
        {
            if (_textBox != null && string.IsNullOrEmpty(_textBox.Text))
            {
                if (_oldText != null)
                    _textBox.Text = _oldText;
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

            PlaylistTabsViewModel? viewModel = FindAncestorViewModel(this);
            if (viewModel != null)
            {
                Tag = viewModel;
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
                _textBox.Focus();
            }
        }
        else
        {
            _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)MoveTextBoxInFocus);
        }
    }

    private void TextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_oldText != null)
                _textBox!.Text = _oldText;
            IsInEditMode = false;
        }
        else if (e.Key == Key.Enter)
        {
            if (DataContext is PlaylistTab tab && Tag is PlaylistTabsViewModel viewModel)
            {
                string newText = _textBox!.Text;
                string? previousOldText = _oldText;
                _oldText = newText;
                IsInEditMode = false;
                viewModel.RenamePlaylistAsync((tab, previousOldText)).GetAwaiter().GetResult();
            }
            else
            {
                IsInEditMode = false;
            }
        }
    }

    private static PlaylistTabsViewModel? FindAncestorViewModel(DependencyObject obj)
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
