using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MinecraftHost.Views;

public partial class SyntaxHighlightingEditor : UserControl
{
    private bool _updating;
    private readonly DispatcherTimer _debounce;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SyntaxHighlightingEditor),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty FileExtensionProperty =
        DependencyProperty.Register(nameof(FileExtension), typeof(string), typeof(SyntaxHighlightingEditor),
            new PropertyMetadata(string.Empty, OnFileExtensionChanged));

    public string FileExtension
    {
        get => (string)GetValue(FileExtensionProperty);
        set => SetValue(FileExtensionProperty, value);
    }

    public SyntaxHighlightingEditor()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            FlushEditorToText();
        };

        Editor.TextChanged += OnEditorTextChanged;
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SyntaxHighlightingEditor self)
        {
            if (!self._updating)
                self.LoadFromText((string)e.NewValue ?? string.Empty);
        }
    }

    private static void OnFileExtensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SyntaxHighlightingEditor self && !string.IsNullOrEmpty(self.Text))
            self.LoadFromText(self.Text);
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void LoadFromText(string text)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            RebuildDocument(text);
        }
        finally
        {
            _updating = false;
        }
    }

    private void FlushEditorToText()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var verticalOffset = Editor.VerticalOffset;
            var horizontalOffset = Editor.HorizontalOffset;
            var caretOffset = GetCaretOffset();

            var raw = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            var text = raw.EndsWith("\r\n") ? raw[..^2] : raw.EndsWith('\n') ? raw[..^1] : raw;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            SetCurrentValue(TextProperty, text);
            RebuildDocument(text);

            RestoreCaretOffset(caretOffset);
            Editor.ScrollToVerticalOffset(verticalOffset);
            Editor.ScrollToHorizontalOffset(horizontalOffset);
        }
        finally
        {
            _updating = false;
        }
    }

    private void RebuildDocument(string text)
    {
        var doc = new FlowDocument { PageWidth = 10000 };
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var ext = (FileExtension ?? string.Empty).ToLowerInvariant();

        foreach (var line in lines)
        {
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 19 };
            switch (ext)
            {
                case ".properties":
                    TokenizeProperties(para, line);
                    break;
                case ".json":
                    TokenizeJson(para, line);
                    break;
                case ".yml":
                case ".yaml":
                    TokenizeYaml(para, line);
                    break;
                default:
                    para.Inlines.Add(Cr(line, 0xD4, 0xD4, 0xD4));
                    break;
            }
            doc.Blocks.Add(para);
        }

        Editor.Document = doc;
    }

    private static void TokenizeProperties(Paragraph para, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        if (line.TrimStart().StartsWith('#'))
        {
            para.Inlines.Add(Cr(line, 0x6A, 0x91, 0x53));
            return;
        }
        var eq = line.IndexOf('=');
        if (eq >= 0)
        {
            para.Inlines.Add(Cr(line[..eq], 0x9C, 0xDC, 0xFE));
            para.Inlines.Add(Cr("=", 0xD4, 0xD4, 0xD4));
            para.Inlines.Add(Cr(line[(eq + 1)..], 0xCE, 0x91, 0x78));
        }
        else
        {
            para.Inlines.Add(Cr(line, 0xD4, 0xD4, 0xD4));
        }
    }

    private static void TokenizeJson(Paragraph para, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        var match = Regex.Match(line, @"^(\s*)(""(?:[^""\\]|\\.)*"")(\s*:\s*)(.*)$");
        if (match.Success)
        {
            if (match.Groups[1].Length > 0)
                para.Inlines.Add(Cr(match.Groups[1].Value, 0xD4, 0xD4, 0xD4));
            para.Inlines.Add(Cr(match.Groups[2].Value, 0x9C, 0xDC, 0xFE));
            para.Inlines.Add(Cr(match.Groups[3].Value, 0xD4, 0xD4, 0xD4));
            AppendJsonValue(para, match.Groups[4].Value);
            return;
        }

        AppendJsonValue(para, line);
    }

    private static void AppendJsonValue(Paragraph para, string val)
    {
        if (string.IsNullOrEmpty(val)) return;

        var match = Regex.Match(val, @"^(.*?)([,\]\}]*\s*)$");
        var core = match.Groups[1].Value;
        var trail = match.Groups[2].Value;

        var trimCore = core.Trim();
        if (Regex.IsMatch(trimCore, @"^(true|false|null)$"))
            para.Inlines.Add(Cr(core, 0x56, 0x9C, 0xD6));
        else if (Regex.IsMatch(trimCore, @"^-?\d+(\.\d+)?([eE][+-]?\d+)?$"))
            para.Inlines.Add(Cr(core, 0xB5, 0xCE, 0xA8));
        else if (trimCore.StartsWith('"'))
            para.Inlines.Add(Cr(core, 0xCE, 0x91, 0x78));
        else
            para.Inlines.Add(Cr(core, 0xD4, 0xD4, 0xD4));

        if (trail.Length > 0)
            para.Inlines.Add(Cr(trail, 0xD4, 0xD4, 0xD4));
    }

    private static void TokenizeYaml(Paragraph para, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        if (line.TrimStart().StartsWith('#'))
        {
            para.Inlines.Add(Cr(line, 0x6A, 0x91, 0x53));
            return;
        }

        var match = Regex.Match(line, @"^(\s*)(-\s+)?([^:]+)(:\s*)(.*)$");
        if (match.Success)
        {
            if (match.Groups[1].Length > 0)
                para.Inlines.Add(Cr(match.Groups[1].Value, 0xD4, 0xD4, 0xD4));
            if (match.Groups[2].Length > 0)
                para.Inlines.Add(Cr(match.Groups[2].Value, 0xD4, 0xD4, 0xD4));

            para.Inlines.Add(Cr(match.Groups[3].Value, 0x9C, 0xDC, 0xFE));
            para.Inlines.Add(Cr(match.Groups[4].Value, 0xD4, 0xD4, 0xD4));

            var val = match.Groups[5].Value;
            if (val.Length > 0)
            {
                var trimVal = val.Trim();
                if (Regex.IsMatch(trimVal, @"^(true|false|null|~|yes|no|on|off)$", RegexOptions.IgnoreCase))
                    para.Inlines.Add(Cr(val, 0x56, 0x9C, 0xD6));
                else if (Regex.IsMatch(trimVal, @"^-?\d+(\.\d+)?$"))
                    para.Inlines.Add(Cr(val, 0xB5, 0xCE, 0xA8));
                else
                    para.Inlines.Add(Cr(val, 0xCE, 0x91, 0x78));
            }
            return;
        }

        var listMatch = Regex.Match(line, @"^(\s*-\s+)(.*)$");
        if (listMatch.Success)
        {
            para.Inlines.Add(Cr(listMatch.Groups[1].Value, 0xD4, 0xD4, 0xD4));
            para.Inlines.Add(Cr(listMatch.Groups[2].Value, 0xCE, 0x91, 0x78));
            return;
        }

        para.Inlines.Add(Cr(line, 0xD4, 0xD4, 0xD4));
    }

    private static Run Cr(string text, byte r, byte g, byte b) =>
        new(text) { Foreground = new SolidColorBrush(Color.FromRgb(r, g, b)) };

    private int GetCaretOffset()
    {
        var start = Editor.Document.ContentStart;
        var current = Editor.CaretPosition;
        var offset = 0;

        var pointer = start;
        while (pointer != null && pointer.CompareTo(current) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                var runEnd = pointer.GetPositionAtOffset(runText.Length);

                if (runEnd != null && runEnd.CompareTo(current) <= 0)
                {
                    offset += runText.Length;
                    pointer = runEnd;
                }
                else
                {
                    offset += pointer.GetOffsetToPosition(current);
                    break;
                }
            }
            else if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart)
            {
                if (pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph)
                {
                    if (pointer.CompareTo(start) > 0)
                    {
                        offset += Environment.NewLine.Length;
                    }
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        return offset;
    }

    private void RestoreCaretOffset(int targetOffset)
    {
        var pointer = Editor.Document.ContentStart;
        var currentOffset = 0;

        while (pointer != null && currentOffset < targetOffset)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = pointer.GetTextInRun(LogicalDirection.Forward);
                if (currentOffset + runText.Length <= targetOffset)
                {
                    currentOffset += runText.Length;
                    pointer = pointer.GetPositionAtOffset(runText.Length);
                }
                else
                {
                    pointer = pointer.GetPositionAtOffset(targetOffset - currentOffset);
                    break;
                }
            }
            else if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart)
            {
                if (pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph)
                {
                    if (pointer.CompareTo(Editor.Document.ContentStart) > 0)
                    {
                        if (currentOffset + Environment.NewLine.Length <= targetOffset)
                        {
                            currentOffset += Environment.NewLine.Length;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        if (pointer != null)
        {
            Editor.CaretPosition = pointer;
        }
    }
}