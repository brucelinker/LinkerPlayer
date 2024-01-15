using System.Windows;
using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

public class EditableTabHeaderControl : ContentControl
{
    /// <summary>
    /// Dependency property to bind EditMode with XAML Trigger
    /// </summary>
    private static readonly DependencyProperty IsInEditModeProperty = DependencyProperty.Register("IsInEditMode", typeof(bool), typeof(EditableTabHeaderControl));
    private TextBox textBox;
    private string oldText;
    private DispatcherTimer timer;
    private delegate void FocusTextBox();

    /// <summary>
    /// Gets or sets a value indicating whether this instance is in edit mode.
    /// </summary>
    public bool IsInEditMode
    {
        get
        {
            return (bool)this.GetValue(IsInEditModeProperty);
        }
        set
        {
            if (string.IsNullOrEmpty(this.textBox.Text))
            {
                this.textBox.Text = this.oldText;
            }

            this.oldText = this.textBox.Text;
            this.SetValue(IsInEditModeProperty, value);
        }
    }

    /// <summary>
    /// When overridden in a derived class, is invoked whenever application code or internal processes call <see cref="M:System.Windows.FrameworkElement.ApplyTemplate"/>.
    /// </summary>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        this.textBox = this.Template.FindName("PART_TabHeader", this) as TextBox;
        if (this.textBox != null)
        {
            this.timer = new DispatcherTimer();
            this.timer.Tick += TimerTick;
            this.timer.Interval = TimeSpan.FromMilliseconds(1);
            this.LostFocus += TextBoxLostFocus;
            this.textBox.KeyDown += TextBoxKeyDown;
            this.MouseDoubleClick += EditableTabHeaderControlMouseDoubleClick;
        }
    }

    /// <summary>
    /// Sets the IsInEdit mode.
    /// </summary>
    /// <param name="value">if set to <c>true</c> [value].</param>
    public void SetEditMode(bool value)
    {
        this.IsInEditMode = value;
        this.timer.Start();
    }

    private void TimerTick(object sender, EventArgs e)
    {
        this.timer.Stop();
        this.MoveTextBoxInFocus();
    }

    private void MoveTextBoxInFocus()
    {
        if (this.textBox.CheckAccess())
        {
            if (!string.IsNullOrEmpty(this.textBox.Text))
            {
                this.textBox.CaretIndex = 0;
                this.textBox.Focus();
            }
        }
        else
        {
            this.textBox.Dispatcher.BeginInvoke(DispatcherPriority.Render, new FocusTextBox(this.MoveTextBoxInFocus));
        }
    }

    private void TextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.textBox.Text = oldText;
            this.IsInEditMode = false;
        }
        else if (e.Key == Key.Enter)
        {
            this.IsInEditMode = false;
        }
    }

    private void TextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        this.IsInEditMode = false;
    }

    private void EditableTabHeaderControlMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            this.SetEditMode(true);
        }
    }
}