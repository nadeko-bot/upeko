using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace upeko.Views;

/// <summary>
/// Selectable text that shows `backtick` spans as code.
/// </summary>
public class InlineCodeText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<InlineCodeText, string?>(nameof(Markdown));

    public static readonly StyledProperty<string> CodeBackgroundKeyProperty =
        AvaloniaProperty.Register<InlineCodeText, string>(nameof(CodeBackgroundKey), "CodeBackground");

    private static readonly FontFamily CodeFont = new("JetBrains Mono,Cascadia Mono,Consolas,Menlo,monospace");

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// The resource key of the brush behind code spans.
    /// </summary>
    public string CodeBackgroundKey
    {
        get => GetValue(CodeBackgroundKeyProperty);
        set => SetValue(CodeBackgroundKeyProperty, value);
    }

    protected override System.Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == CodeBackgroundKeyProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var parts = (Markdown ?? string.Empty).Split('`');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            // Odd parts sit between backticks; an unmatched last backtick stays plain text.
            var isCode = i % 2 == 1 && i < parts.Length - 1;
            var run = new Run(parts[i]);
            if (isCode)
            {
                run.FontFamily = CodeFont;
                run.FontSize = FontSize * 0.92;
                run[!Run.BackgroundProperty] = this.GetResourceObservable(CodeBackgroundKey).ToBinding();
                run[!Run.ForegroundProperty] = this.GetResourceObservable("TextPrimary").ToBinding();
            }
            else if (i % 2 == 1)
            {
                run.Text = "`" + parts[i];
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
