using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Turns Markdown into a WPF <see cref="FlowDocument"/> for the integrated viewer: Markdig parses,
/// this maps its syntax tree to flow elements. Colors are references to the theme brushes, so an
/// open document follows a light/dark switch. Only local images are shown: viewing a file never
/// fetches anything from the network.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter() // SKILL.md files start with one
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily CodeFont = new("Consolas");

    /// <param name="baseDir">Folder relative image paths are resolved against (the file's own).</param>
    /// <param name="openLink">Called with a link's target when it is clicked.</param>
    public static FlowDocument Render(string markdown, string? baseDir, Action<string> openLink)
    {
        var doc = new FlowDocument
        {
            FontFamily = BodyFont,
            FontSize = 14,
            PagePadding = new Thickness(28, 20, 28, 28),
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
        var renderer = new Renderer(markdown, baseDir, openLink);
        foreach (var block in Markdown.Parse(markdown, Pipeline))
            if (renderer.Block(block) is { } b) doc.Blocks.Add(b);
        return doc;
    }

    /// <summary>
    /// Full path of a link or image target that points to the file system, or null for web/mail
    /// links. Relative targets are resolved against <paramref name="baseDir"/>; the #fragment is dropped.
    /// </summary>
    public static string? ResolveLocalPath(string url, string? baseDir)
    {
        var target = url.Split('#', 2)[0];
        if (target.Length == 0) return null;
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
            return absolute.IsFile ? absolute.LocalPath : null;
        if (baseDir is null) return null;
        try
        {
            return Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(target).Replace('/', '\\')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private sealed class Renderer(string markdown, string? baseDir, Action<string> openLink)
    {
        public WpfBlock? Block(MdBlock block) => block switch
        {
            HeadingBlock h => Heading(h),
            ParagraphBlock p => TextParagraph(p.Inline, new Thickness(0, 0, 0, 10)),
            ListBlock l => List(l),
            QuoteBlock q => Quote(q),
            CodeBlock c => Code(c.Lines.ToString()), // fenced, indented and YAML front matter
            HtmlBlock html => Code(html.Lines.ToString()), // shown as source, never interpreted
            ThematicBreakBlock => Rule(),
            MdTable t => Table(t),
            LinkReferenceDefinitionGroup => null,
            ContainerBlock c => Children(c),
            LeafBlock { Inline: { } inline } => TextParagraph(inline, new Thickness(0, 0, 0, 10)),
            _ => new Paragraph(new Run(Source(block))),
        };

        private Paragraph Heading(HeadingBlock h)
        {
            var p = TextParagraph(h.Inline, default);
            p.FontWeight = FontWeights.SemiBold;
            (p.FontSize, p.Margin) = h.Level switch
            {
                1 => (26.0, new Thickness(0, 6, 0, 12)),
                2 => (21.0, new Thickness(0, 18, 0, 8)),
                3 => (17.0, new Thickness(0, 14, 0, 6)),
                _ => (15.0, new Thickness(0, 12, 0, 4)),
            };
            if (h.Level <= 2)
            {
                p.BorderThickness = new Thickness(0, 0, 0, 1);
                p.Padding = new Thickness(0, 0, 0, 4);
                p.SetResourceReference(WpfBlock.BorderBrushProperty, "Border");
            }
            return p;
        }

        private Paragraph TextParagraph(ContainerInline? inlines, Thickness margin)
        {
            var p = new Paragraph { Margin = margin };
            AddInlines(p.Inlines, inlines);
            return p;
        }

        private WpfList List(ListBlock l)
        {
            var list = new WpfList
            {
                MarkerStyle = l.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(24, 0, 0, 0),
            };
            if (l.IsOrdered && int.TryParse(l.OrderedStart, out var start) && start > 0) list.StartIndex = start;
            foreach (var item in l.OfType<ListItemBlock>())
            {
                var li = new ListItem();
                foreach (var child in item)
                    if (Block(child) is { } b)
                    {
                        // Items sit close together, nested lists included.
                        if (b is Paragraph or WpfList) b.Margin = new Thickness(0, 0, 0, 3);
                        li.Blocks.Add(b);
                    }
                list.ListItems.Add(li);
            }
            return list;
        }

        private Section Quote(QuoteBlock q)
        {
            var s = Children(q);
            s.Margin = new Thickness(0, 0, 0, 10);
            s.Padding = new Thickness(12, 0, 0, 0);
            s.BorderThickness = new Thickness(3, 0, 0, 0);
            s.SetResourceReference(WpfBlock.BorderBrushProperty, "ControlBorder");
            s.SetResourceReference(TextElement.ForegroundProperty, "TextSecondary");
            return s;
        }

        private Section Children(ContainerBlock container)
        {
            var s = new Section();
            foreach (var child in container)
                if (Block(child) is { } b) s.Blocks.Add(b);
            return s;
        }

        private static Paragraph Code(string code)
        {
            var p = new Paragraph
            {
                FontFamily = CodeFont,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12, 8, 12, 8),
                BorderThickness = new Thickness(1),
            };
            p.SetResourceReference(TextElement.BackgroundProperty, "ToolButtonHover");
            p.SetResourceReference(WpfBlock.BorderBrushProperty, "Border");
            var lines = code.TrimEnd('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run(lines[i].TrimEnd('\r')));
            }
            return p;
        }

        private static BlockUIContainer Rule()
        {
            var line = new Border { Height = 1 };
            line.SetResourceReference(Border.BackgroundProperty, "Border");
            return new BlockUIContainer(line) { Margin = new Thickness(0, 8, 0, 16) };
        }

        private WpfTable Table(MdTable t)
        {
            var table = new WpfTable { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1, 1, 0, 0) };
            table.SetResourceReference(WpfBlock.BorderBrushProperty, "Border");
            var rows = t.OfType<MdTableRow>().ToList();
            var columns = Math.Max(1, rows.Select(r => r.OfType<MdTableCell>().Sum(c => Math.Max(1, c.ColumnSpan))).DefaultIfEmpty(0).Max());
            for (var i = 0; i < columns; i++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            foreach (var row in rows)
            {
                var tr = new WpfTableRow();
                if (row.IsHeader)
                {
                    tr.FontWeight = FontWeights.SemiBold;
                    tr.SetResourceReference(TextElement.BackgroundProperty, "ToolButtonHover");
                }
                foreach (var cell in row.OfType<MdTableCell>())
                {
                    var tc = new WpfTableCell
                    {
                        Padding = new Thickness(8, 4, 8, 4),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        ColumnSpan = Math.Max(1, cell.ColumnSpan),
                        TextAlignment = Alignment(t, cell.ColumnIndex),
                    };
                    tc.SetResourceReference(WpfTableCell.BorderBrushProperty, "Border");
                    foreach (var child in cell)
                        if (Block(child) is { } b)
                        {
                            b.Margin = new Thickness(0);
                            tc.Blocks.Add(b);
                        }
                    tr.Cells.Add(tc);
                }
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static TextAlignment Alignment(MdTable t, int column)
            => (column >= 0 && column < t.ColumnDefinitions.Count ? t.ColumnDefinitions[column].Alignment : null) switch
            {
                MdTableColumnAlign.Center => TextAlignment.Center,
                MdTableColumnAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            };

        private void AddInlines(InlineCollection target, ContainerInline? container)
        {
            if (container is null) return;
            foreach (var inline in container)
                if (Inline(inline) is { } i) target.Add(i);
        }

        private WpfInline? Inline(MdInline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    return new Run(literal.Content.ToString());
                case CodeInline code:
                    var run = new Run(code.Content) { FontFamily = CodeFont };
                    run.SetResourceReference(TextElement.BackgroundProperty, "ToolButtonHover");
                    return run;
                case LineBreakInline lineBreak:
                    return lineBreak.IsHard ? new LineBreak() : new Run(" ");
                case TaskList task:
                    return new Run(task.Checked ? "☑ " : "☐ ");
                case EmphasisInline emphasis:
                    return Emphasis(emphasis);
                case LinkInline { IsImage: true } image:
                    return Picture(image);
                case LinkInline link:
                    var hyperlink = Link(link.Url);
                    AddInlines(hyperlink.Inlines, link);
                    if (hyperlink.Inlines.Count == 0) hyperlink.Inlines.Add(new Run(link.Url));
                    if (!string.IsNullOrEmpty(link.Title)) hyperlink.ToolTip = link.Title;
                    return hyperlink;
                case AutolinkInline autolink:
                    var auto = Link(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url);
                    auto.Inlines.Add(new Run(autolink.Url));
                    return auto;
                case HtmlEntityInline entity:
                    return new Run(entity.Transcoded.ToString());
                case HtmlInline html:
                    var tag = new Run(html.Tag);
                    tag.SetResourceReference(TextElement.ForegroundProperty, "TextMuted");
                    return tag;
                case ContainerInline container:
                    var span = new Span();
                    AddInlines(span.Inlines, container);
                    return span;
                default:
                    return new Run(Source(inline));
            }
        }

        private Span Emphasis(EmphasisInline emphasis)
        {
            var span = new Span();
            AddInlines(span.Inlines, emphasis);
            if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
            else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
            else span.FontStyle = FontStyles.Italic;
            return span;
        }

        private Hyperlink Link(string? url)
        {
            var link = new Hyperlink { Cursor = Cursors.Hand, ToolTip = url };
            link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            if (!string.IsNullOrWhiteSpace(url)) link.Click += (_, _) => openLink(url);
            return link;
        }

        private WpfInline Picture(LinkInline image)
        {
            var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
            if (image.Url is { } url && ResolveLocalPath(url, baseDir) is { } path && File.Exists(path))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    return new InlineUIContainer(new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        MaxWidth = 720,
                        ToolTip = alt.Length > 0 ? alt : null,
                    });
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException or UnauthorizedAccessException)
                {
                    // Not a readable image: fall back to its description.
                }
            }
            var placeholder = new Run($"[{(alt.Length > 0 ? alt : "image")}]");
            placeholder.SetResourceReference(TextElement.ForegroundProperty, "TextMuted");
            return placeholder;
        }

        /// <summary>The Markdown source of an element this renderer has no mapping for.</summary>
        private string Source(MarkdownObject element)
            => element.Span.Start >= 0 && element.Span.End < markdown.Length
                ? markdown.Substring(element.Span.Start, element.Span.Length)
                : "";
    }
}
