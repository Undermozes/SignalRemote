using Remotely.Shared.Entities;
using SkiaSharp;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Remotely.Server.Services.ClaudeAssistant;

/// <summary>
/// Renders one Claude session from the history as an A4 PDF report: the questions, Claude's
/// answers (light Markdown), the commands with approval and output, and the screenshots.
/// Laid out by hand with SkiaSharp, which the server already ships.
/// </summary>
public static partial class ClaudeConversationPdf
{
    private const float PageWidth = 595f;   // A4 in points
    private const float PageHeight = 842f;
    private const float Margin = 44f;
    private const float ContentWidth = PageWidth - 2 * Margin;
    private const float FooterSpace = 28f;
    private const int MaxOutputLines = 250;

    private static readonly SKColor Fg = new(0x11, 0x18, 0x27);
    private static readonly SKColor Muted = new(0x6b, 0x72, 0x80);
    private static readonly SKColor Border = new(0xe5, 0xe7, 0xeb);
    private static readonly SKColor Sunken = new(0xf3, 0xf4, 0xf6);
    private static readonly SKColor Brand = new(0x25, 0x63, 0xeb);
    private static readonly SKColor BrandSoft = new(0xe8, 0xf0, 0xfe);
    private static readonly SKColor Success = new(0x15, 0x80, 0x3d);
    private static readonly SKColor Danger = new(0xb9, 0x1c, 0x1c);
    private static readonly SKColor Warning = new(0x92, 0x40, 0x0e);

    // Fonts from the fonts-dejavu-core package (installed in the Docker image).
    private const string FontDir = "/usr/share/fonts/truetype/dejavu";

    /// <param name="events">The session's events in order.</param>
    /// <param name="screenshots">JPEG bytes per screenshot event id.</param>
    public static byte[] Render(
        IReadOnlyList<ClaudeAssistantEvent> events,
        IReadOnlyDictionary<Guid, byte[]> screenshots,
        TimeZoneInfo timeZone)
    {
        if (events.Count == 0)
        {
            throw new ArgumentException("A session needs at least one event.", nameof(events));
        }

        using var fonts = Fonts.Load();
        using var stream = new MemoryStream();
        var first = events[0];
        var metadata = new SKDocumentPdfMetadata
        {
            Title = $"Claude conversation - {first.DeviceName ?? first.DeviceId}",
            Creator = "SignalRemote",
            Producer = "SignalRemote",
            Creation = DateTime.Now,
            Modified = DateTime.Now,
        };

        using (var document = SKDocument.CreatePdf(stream, metadata))
        {
            var writer = new Writer(document, fonts, $"Session {first.SessionId}");
            WriteHeader(writer, events, timeZone);
            foreach (var ev in events)
            {
                WriteEvent(writer, ev, screenshots, timeZone);
            }
            writer.Finish();
            document.Close();
        }

        return stream.ToArray();
    }

    private static void WriteHeader(Writer w, IReadOnlyList<ClaudeAssistantEvent> events, TimeZoneInfo zone)
    {
        var first = events[0];
        w.Text([new Run("Claude conversation", w.Fonts.Bold, 20, Fg)], spaceAfter: 2);
        w.Text([new Run(first.DeviceName ?? first.DeviceId, w.Fonts.Regular, 13, Muted)], spaceAfter: 14);

        var technicians = events.Select(x => x.UserName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var rows = new List<(string, string)>
        {
            ("Device", first.DeviceName ?? first.DeviceId),
            ("Technician", string.Join(", ", technicians)),
            ("Started", Format(first.Timestamp, zone, "yyyy-MM-dd HH:mm")),
            ("Last activity", Format(events[^1].Timestamp, zone, "yyyy-MM-dd HH:mm")),
            ("Commands", events.Count(x => x.Kind == ClaudeAssistantEventKind.CommandRequested).ToString(CultureInfo.InvariantCulture)),
            ("Screenshots", events.Count(x => x.Kind == ClaudeAssistantEventKind.Screenshot).ToString(CultureInfo.InvariantCulture)),
            ("Session ID", first.SessionId.ToString()),
        };

        w.Box(Sunken, 10, () =>
        {
            foreach (var (label, value) in rows)
            {
                w.Text(
                    [new Run($"{label}:  ", w.Fonts.Bold, 9.5f, Muted), new Run(value, w.Fonts.Regular, 9.5f, Fg)],
                    indent: 10, width: ContentWidth - 20, spaceAfter: 2);
            }
        });
        w.Space(16);
    }

    private static void WriteEvent(Writer w, ClaudeAssistantEvent ev, IReadOnlyDictionary<Guid, byte[]> screenshots, TimeZoneInfo zone)
    {
        var time = Format(ev.Timestamp, zone, "HH:mm:ss");
        switch (ev.Kind)
        {
            case ClaudeAssistantEventKind.UserMessage:
                w.KeepTogether(60);
                w.Label($"{ev.UserName} · {time}", Brand);
                w.Box(BrandSoft, 8, () => w.Text(Plain(ev.Text, w.Fonts.Regular, 10.5f, Fg), indent: 10, width: ContentWidth - 20));
                w.Space(12);
                break;

            case ClaudeAssistantEventKind.AssistantMessage:
                w.KeepTogether(60);
                w.Label($"Claude · {time}", Muted);
                WriteMarkdown(w, ev.Text ?? string.Empty);
                w.Space(12);
                break;

            case ClaudeAssistantEventKind.CommandRequested:
                w.KeepTogether(80);
                var readOnly = ev.IsReadOnly == true;
                w.Text(
                    [
                        new Run(readOnly ? "Read-only command" : "Command that changes the system", w.Fonts.Bold, 9, readOnly ? Muted : Warning),
                        new Run($"  ·  {ev.Shell}  ·  {time}", w.Fonts.Regular, 9, Muted),
                    ],
                    spaceAfter: 3);
                if (!string.IsNullOrWhiteSpace(ev.Purpose))
                {
                    w.Text(Plain(ev.Purpose, w.Fonts.Regular, 10, Fg), spaceAfter: 4);
                }
                w.Code(ev.Text ?? string.Empty, MaxOutputLines);
                w.Space(4);
                break;

            case ClaudeAssistantEventKind.CommandApproved:
                w.Text([new Run($"Approved by {ev.DecidedBy} · {time}", w.Fonts.Regular, 9, Success)], spaceAfter: 6);
                break;

            case ClaudeAssistantEventKind.CommandRejected:
                var note = string.IsNullOrWhiteSpace(ev.Output) ? "" : $": {ev.Output}";
                w.Text([new Run($"Rejected by {ev.DecidedBy}{note} · {time}", w.Fonts.Regular, 9, Danger)], spaceAfter: 12);
                break;

            case ClaudeAssistantEventKind.CommandCompleted:
            case ClaudeAssistantEventKind.CommandFailed:
                var failed = ev.Kind == ClaudeAssistantEventKind.CommandFailed;
                w.KeepTogether(50);
                w.Label(failed ? $"Command failed · {time}" : $"Output · {time}", failed ? Danger : Muted);
                w.Code(ev.Output ?? string.Empty, MaxOutputLines);
                w.Space(12);
                break;

            case ClaudeAssistantEventKind.Screenshot:
                if (screenshots.TryGetValue(ev.Id, out var jpeg) && jpeg.Length > 0)
                {
                    w.Image(jpeg);
                }
                w.Text([new Run($"Claude looked at the screen ({ev.Text}) · {time}", w.Fonts.Regular, 8.5f, Muted)], spaceAfter: 12);
                break;

            case ClaudeAssistantEventKind.Error:
                w.Text(Plain(ev.Text, w.Fonts.Bold, 10, Danger), spaceAfter: 12);
                break;

            case ClaudeAssistantEventKind.SessionResumed:
                w.Text([new Run($"Continued by {ev.UserName} · {time}", w.Fonts.Regular, 9, Muted)], spaceAfter: 12);
                break;
        }
    }

    /// <summary>Headings, bullets, numbered lists, code blocks, **bold** and `code`.  Enough for Claude's answers.</summary>
    private static void WriteMarkdown(Writer w, string markdown)
    {
        var lines = markdown.Replace("\r", "").Trim().Split('\n');
        var code = new StringBuilder();
        var inCode = false;

        foreach (var raw in lines)
        {
            if (raw.TrimStart().StartsWith("```"))
            {
                if (inCode)
                {
                    w.Code(code.ToString(), MaxOutputLines);
                    w.Space(4);
                    code.Clear();
                }
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                code.AppendLine(raw);
                continue;
            }

            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                w.Space(5);
                continue;
            }

            if (HeadingRegex().Match(line) is { Success: true } heading)
            {
                w.Space(3);
                w.Text(Inline(w, heading.Groups[2].Value, w.Fonts.Bold, heading.Groups[1].Length <= 2 ? 12.5f : 11), spaceAfter: 3);
            }
            else if (BulletRegex().Match(line) is { Success: true } bullet)
            {
                var indent = Math.Min(bullet.Groups[1].Length, 8) * 4f;
                w.Text(Inline(w, bullet.Groups[2].Value, w.Fonts.Regular, 10.5f), indent: indent + 12, width: ContentWidth - indent - 12, bullet: "•", spaceAfter: 2);
            }
            else if (NumberedRegex().Match(line) is { Success: true } numbered)
            {
                var indent = Math.Min(numbered.Groups[1].Length, 8) * 4f;
                w.Text(Inline(w, numbered.Groups[3].Value, w.Fonts.Regular, 10.5f), indent: indent + 16, width: ContentWidth - indent - 16, bullet: numbered.Groups[2].Value + ".", spaceAfter: 2);
            }
            else if (line.TrimStart().StartsWith('|'))
            {
                // Tables are rare in answers; keep their alignment readable in monospace.
                if (!TableRuleRegex().IsMatch(line))
                {
                    w.Text([new Run(line.Trim(), w.Fonts.Mono, 8.5f, Fg)], spaceAfter: 1);
                }
            }
            else if (line.StartsWith('>'))
            {
                w.Text(Inline(w, line.TrimStart('>', ' '), w.Fonts.Regular, 10.5f, Muted), indent: 10, width: ContentWidth - 10, spaceAfter: 2);
            }
            else
            {
                w.Text(Inline(w, line, w.Fonts.Regular, 10.5f), spaceAfter: 2);
            }
        }

        if (inCode && code.Length > 0)
        {
            w.Code(code.ToString(), MaxOutputLines);
        }
    }

    private static List<Run> Plain(string? text, SKTypeface font, float size, SKColor color) =>
        [new Run(text ?? string.Empty, font, size, color)];

    private static List<Run> Inline(Writer w, string text, SKTypeface baseFont, float size, SKColor? color = null)
    {
        var runs = new List<Run>();
        foreach (Match m in InlineRegex().Matches(text))
        {
            if (m.Groups["code"].Success)
            {
                runs.Add(new Run(m.Groups["code"].Value, w.Fonts.Mono, size - 1, color ?? Fg));
            }
            else if (m.Groups["bold"].Success)
            {
                runs.Add(new Run(m.Groups["bold"].Value, w.Fonts.Bold, size, color ?? Fg));
            }
            else
            {
                runs.Add(new Run(m.Value, baseFont, size, color ?? Fg));
            }
        }
        return runs;
    }

    private static string Format(DateTimeOffset value, TimeZoneInfo zone, string format) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString(format, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(\s*)(\d{1,3})[.)]\s+(.*)$")]
    private static partial Regex NumberedRegex();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}")]
    private static partial Regex TableRuleRegex();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|[^`*]+|[`*]")]
    private static partial Regex InlineRegex();

    private sealed record Run(string Text, SKTypeface Font, float Size, SKColor Color);

    private sealed class Fonts : IDisposable
    {
        public required SKTypeface Regular { get; init; }
        public required SKTypeface Bold { get; init; }
        public required SKTypeface Mono { get; init; }

        public static Fonts Load() => new()
        {
            Regular = LoadFont("DejaVuSans.ttf", SKFontStyle.Normal, "sans-serif"),
            Bold = LoadFont("DejaVuSans-Bold.ttf", SKFontStyle.Bold, "sans-serif"),
            Mono = LoadFont("DejaVuSansMono.ttf", SKFontStyle.Normal, "monospace"),
        };

        private static SKTypeface LoadFont(string file, SKFontStyle style, string family)
        {
            var path = Path.Combine(FontDir, file);
            var typeface = File.Exists(path)
                ? SKTypeface.FromFile(path)
                : SKFontManager.Default.MatchFamily(family, style);

            return typeface ?? throw new InvalidOperationException(
                $"No font available for PDF export.  Install fonts-dejavu-core ({path} is missing).");
        }

        public void Dispose()
        {
            Regular.Dispose();
            Bold.Dispose();
            Mono.Dispose();
        }
    }

    /// <summary>Top-to-bottom layout with page breaks.</summary>
    private sealed class Writer
    {
        private readonly SKDocument _document;
        private readonly string _footer;
        private readonly Dictionary<(SKTypeface, float), SKFont> _fontCache = [];
        private SKCanvas? _canvas;
        private int _page;
        private float _y;

        // While inside Box(): its background and padding, redrawn on each page it spans.
        private (SKColor Color, float Padding)? _box;

        public Writer(SKDocument document, Fonts fonts, string footer)
        {
            _document = document;
            Fonts = fonts;
            _footer = footer;
            NewPage();
        }

        public Fonts Fonts { get; }

        private float Bottom => PageHeight - Margin - FooterSpace;

        public void Space(float points)
        {
            _y = Math.Min(_y + points, Bottom);
        }

        /// <summary>Starts a new page when less than this much room is left, so a heading doesn't end a page.</summary>
        public void KeepTogether(float points)
        {
            if (_y + points > Bottom)
            {
                NewPage();
            }
        }

        public void Label(string text, SKColor color) =>
            Text([new Run(text, Fonts.Bold, 8.5f, color)], spaceAfter: 3);

        public void Box(SKColor color, float padding, Action content)
        {
            var previous = _box;
            _box = (color, padding);
            Strip(padding);
            content();
            Strip(padding);
            _box = previous;
        }

        public void Text(List<Run> runs, float indent = 0, float? width = null, string? bullet = null, float spaceAfter = 0)
        {
            var maxWidth = width ?? ContentWidth - indent;
            var lines = Wrap(runs, maxWidth);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                // An empty line (a blank line in the text) keeps the height of the text around it.
                var height = (line.Count > 0 ? line.Max(x => x.Run.Size) : runs.FirstOrDefault()?.Size ?? 10f) * 1.4f;
                EnsureRoom(height);
                Strip(height, draw: false);

                var baseline = _y + height * 0.76f;
                if (bullet is not null && i == 0 && line.Count > 0)
                {
                    var font = Font(line[0].Run.Font == Fonts.Mono ? Fonts.Regular : line[0].Run.Font, line[0].Run.Size);
                    using var paint = Paint(Fg);
                    _canvas!.DrawText(bullet, Margin + indent - Measure(font, bullet) - 5, baseline, font, paint);
                }

                var x = Margin + indent;
                foreach (var (run, text) in line)
                {
                    var font = Font(run.Font, run.Size);
                    if (run.Font == Fonts.Mono && _box is null)
                    {
                        using var shade = Paint(Sunken);
                        _canvas!.DrawRect(x - 1, _y + height * 0.12f, Measure(font, text) + 2, height * 0.8f, shade);
                    }
                    using var paint = Paint(run.Color);
                    _canvas!.DrawText(text, x, baseline, font, paint);
                    x += Measure(font, text);
                }
                _y += height;
            }
            Space(spaceAfter);
        }

        /// <summary>Monospace block on a grey background, hard-wrapped to the page width.</summary>
        public void Code(string text, int maxLines)
        {
            const float size = 8f;
            var font = Font(Fonts.Mono, size);
            var charsPerLine = Math.Max(20, (int)((ContentWidth - 16) / Measure(font, "M")));
            var lines = new List<string>();
            foreach (var raw in Clean(text).TrimEnd().Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    lines.Add(string.Empty);
                }
                for (var i = 0; i < line.Length; i += charsPerLine)
                {
                    lines.Add(line.Substring(i, Math.Min(charsPerLine, line.Length - i)));
                }
            }

            var hidden = lines.Count - maxLines;
            if (hidden > 0)
            {
                lines = lines.Take(maxLines).ToList();
                lines.Add($"[{hidden} more lines are in the history in SignalRemote.]");
            }

            Box(Sunken, 6, () =>
            {
                using var paint = Paint(Fg);
                foreach (var line in lines)
                {
                    var height = size * 1.35f;
                    EnsureRoom(height);
                    Strip(height, draw: false);
                    _canvas!.DrawText(Glyphs(line, Fonts.Mono), Margin + 8, _y + height * 0.78f, font, paint);
                    _y += height;
                }
            });
        }

        public void Image(byte[] jpeg)
        {
            using var image = SKImage.FromEncodedData(jpeg);
            if (image is null || image.Width == 0)
            {
                return;
            }

            var width = ContentWidth;
            var height = width * image.Height / image.Width;
            var maxHeight = (Bottom - Margin) * 0.75f;
            if (height > maxHeight)
            {
                width *= maxHeight / height;
                height = maxHeight;
            }

            EnsureRoom(height + 4);
            var rect = SKRect.Create(Margin, _y, width, height);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            _canvas!.DrawImage(image, rect, paint);
            using var border = Paint(Border);
            border.Style = SKPaintStyle.Stroke;
            _canvas.DrawRect(rect, border);
            _y += height + 4;
        }

        public void Finish()
        {
            DrawFooter();
            _document.EndPage();
            _canvas = null;
        }

        private void EnsureRoom(float height)
        {
            if (_y + height > Bottom)
            {
                if (_box is { } box)
                {
                    // Close the box on this page and reopen it on the next.
                    Strip(box.Padding);
                    NewPage();
                    Strip(box.Padding);
                }
                else
                {
                    NewPage();
                }
            }
        }

        // Background of the current box for a band of the given height.
        private void Strip(float height, bool draw = true)
        {
            if (_box is { } box)
            {
                using var paint = Paint(box.Color);
                _canvas!.DrawRect(Margin, _y, ContentWidth, height, paint);
            }
            if (draw)
            {
                _y += height;
            }
        }

        private void NewPage()
        {
            if (_canvas is not null)
            {
                DrawFooter();
                _document.EndPage();
            }
            _page++;
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            _y = Margin;
        }

        private void DrawFooter()
        {
            var font = Font(Fonts.Regular, 7.5f);
            using var paint = Paint(Muted);
            var y = PageHeight - Margin + 6;
            using var line = Paint(Border);
            _canvas!.DrawLine(Margin, y - 12, PageWidth - Margin, y - 12, line);
            _canvas.DrawText(_footer, Margin, y, font, paint);
            var page = $"Page {_page}";
            _canvas.DrawText(page, PageWidth - Margin - Measure(font, page), y, font, paint);
        }

        private List<List<(Run Run, string Text)>> Wrap(List<Run> runs, float maxWidth)
        {
            var lines = new List<List<(Run, string)>>();
            var current = new List<(Run, string)>();
            var width = 0f;

            void Push(Run run, string text)
            {
                if (current.Count > 0 && current[^1].Item1 == run)
                {
                    current[^1] = (run, current[^1].Item2 + text);
                }
                else
                {
                    current.Add((run, text));
                }
            }

            void Break()
            {
                lines.Add(current);
                current = [];
                width = 0;
            }

            foreach (var run in runs)
            {
                var font = Font(run.Font, run.Size);
                var paragraphs = Clean(run.Text).Split('\n');
                for (var p = 0; p < paragraphs.Length; p++)
                {
                    if (p > 0)
                    {
                        Break();
                    }

                    foreach (Match token in TokenRegex().Matches(Glyphs(paragraphs[p], run.Font)))
                    {
                        var word = token.Value;
                        var w = Measure(font, word);
                        var isSpace = string.IsNullOrWhiteSpace(word);

                        if (width + w <= maxWidth)
                        {
                            if (!(isSpace && width == 0))
                            {
                                Push(run, word);
                                width += w;
                            }
                            continue;
                        }

                        if (isSpace)
                        {
                            Break();
                            continue;
                        }

                        if (width > 0)
                        {
                            Break();
                        }

                        // A word wider than the line: split it.
                        var piece = new StringBuilder();
                        foreach (var c in word)
                        {
                            if (Measure(font, piece.ToString() + c) > maxWidth && piece.Length > 0)
                            {
                                Push(run, piece.ToString());
                                Break();
                                piece.Clear();
                            }
                            piece.Append(c);
                        }
                        Push(run, piece.ToString());
                        width = Measure(font, piece.ToString());
                    }
                }
            }

            if (current.Count > 0 || lines.Count == 0)
            {
                if (current.Count == 0)
                {
                    current.Add((runs.FirstOrDefault() ?? new Run("", Fonts.Regular, 10, Fg), ""));
                }
                lines.Add(current);
            }
            return lines;
        }

        // Characters the font can't draw (emoji, mostly) become "?" instead of empty boxes.
        private string Glyphs(string text, SKTypeface typeface)
        {
            var font = Font(typeface, 10);
            var sb = new StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                var s = rune.ToString();
                sb.Append(font.ContainsGlyphs(s) ? s : "?");
            }
            return sb.ToString();
        }

        private static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (c == '\t')
                {
                    sb.Append("    ");
                }
                else if (c == '\n' || !char.IsControl(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private SKFont Font(SKTypeface typeface, float size)
        {
            if (!_fontCache.TryGetValue((typeface, size), out var font))
            {
                font = new SKFont(typeface, size) { Subpixel = true };
                _fontCache[(typeface, size)] = font;
            }
            return font;
        }

        // Measure with the same SKFont that draws, through glyph ids (SkiaSharp 2.88 has no
        // string overload), so wrapping matches what ends up on the page.
        private static float Measure(SKFont font, string text)
        {
            if (text.Length == 0)
            {
                return 0;
            }
            var glyphs = new ushort[text.Length];
            font.GetGlyphs(text, glyphs);
            var count = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                count++;
            }
            return font.MeasureText(glyphs.AsSpan(0, count), (SKPaint?)null);
        }

        private static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true };
    }

    [GeneratedRegex(@"\s+|\S+")]
    private static partial Regex TokenRegex();
}
