using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Minimal dark dialog that asks for a single line of text (preset names).</summary>
internal sealed class TextPromptWindow : Window
{
    private readonly TextBox _input;
    public string? Result { get; private set; }

    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        Title = title; Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.ToolWindow; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(18, 16, 24)); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");
        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        root.Children.Add(new TextBlock { Text = prompt, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(200, 190, 215)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        _input = new TextBox { Text = initialValue, FontSize = 12, MaxLength = 40, Height = 32 };
        root.Children.Add(_input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "OK", Width = 84, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 84, Height = 30, IsCancel = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
    }

    private void Accept()
    {
        // Enter can reach here twice (text box handler + default button); DialogResult may only be set once.
        if (Result is not null) return;
        var text = _input.Text.Trim();
        if (text.Length == 0) { _input.Focus(); return; }
        Result = text; DialogResult = true;
    }
}
