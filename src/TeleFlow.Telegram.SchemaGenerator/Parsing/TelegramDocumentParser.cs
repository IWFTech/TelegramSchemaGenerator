using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Parsing;

internal static class TelegramDocumentParser
{
    public static TelegramDocument Parse(string html, string sourceUrl)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var categories = new List<TelegramCategoryNode>();
        TelegramCategoryBuilder? currentCategory = null;

        foreach (var heading in document.QuerySelectorAll("h3, h4"))
        {
            if (heading.TagName.Equals("H3", StringComparison.OrdinalIgnoreCase))
            {
                if (currentCategory is not null)
                {
                    categories.Add(currentCategory.Build());
                }

                currentCategory = CreateCategory(heading);
                continue;
            }

            if (!heading.TagName.Equals("H4", StringComparison.OrdinalIgnoreCase) || currentCategory is null)
            {
                continue;
            }

            currentCategory.Sections.Add(ParseSection(heading));
        }

        if (currentCategory is not null)
        {
            categories.Add(currentCategory.Build());
        }

        return new TelegramDocument(sourceUrl, categories);
    }

    private static TelegramCategoryBuilder CreateCategory(IElement heading)
    {
        return new TelegramCategoryBuilder(
            GetAnchorName(heading) ?? NormalizeAnchor(heading.TextContent),
            NormalizeWhitespace(heading.TextContent));
    }

    private static TelegramSectionNode ParseSection(IElement heading)
    {
        var anchor = GetAnchorName(heading) ?? throw new InvalidOperationException($"The section '{NormalizeWhitespace(heading.TextContent)}' is missing an anchor.");
        var blocks = new List<TelegramBlockNode>();

        for (var sibling = heading.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.TagName.Equals("H3", StringComparison.OrdinalIgnoreCase) ||
                sibling.TagName.Equals("H4", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var block = ParseBlock(sibling);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return new TelegramSectionNode(anchor, NormalizeWhitespace(heading.TextContent), blocks);
    }

    private static TelegramBlockNode? ParseBlock(IElement element)
    {
        return element.TagName.ToUpperInvariant() switch
        {
            "P" => CreateTextBlock("paragraph", element),
            "BLOCKQUOTE" => CreateTextBlock("blockquote", element),
            "UL" => CreateListBlock("unordered-list", element),
            "OL" => CreateListBlock("ordered-list", element),
            "TABLE" => CreateTableBlock(element),
            _ => null
        };
    }

    private static TelegramBlockNode CreateTextBlock(string kind, IElement element)
    {
        return new TelegramBlockNode(
            kind,
            NormalizeWhitespace(element.TextContent),
            element.InnerHtml,
            ParseInlines(element),
            Array.Empty<TelegramListItemNode>(),
            null);
    }

    private static TelegramBlockNode CreateListBlock(string kind, IElement element)
    {
        var items = element.QuerySelectorAll(":scope > li")
            .Select(ParseListItem)
            .ToArray();

        return new TelegramBlockNode(
            kind,
            string.Join(Environment.NewLine, items.Select(static item => item.Text)),
            element.InnerHtml,
            Array.Empty<TelegramInlineNode>(),
            items,
            null);
    }

    private static TelegramListItemNode ParseListItem(IElement element)
    {
        return new TelegramListItemNode(
            NormalizeWhitespace(element.TextContent),
            element.InnerHtml,
            ParseInlines(element));
    }

    private static TelegramBlockNode CreateTableBlock(IElement element)
    {
        var headers = element.QuerySelectorAll("thead th")
            .Select(ParseCell)
            .ToArray();

        var rows = element.QuerySelectorAll("tbody tr")
            .Select(row => new TelegramRowNode(
                row.QuerySelectorAll("td")
                    .Select(ParseCell)
                    .ToArray()))
            .Where(static row => row.Cells.Count > 0)
            .ToArray();

        return new TelegramBlockNode(
            "table",
            string.Empty,
            element.InnerHtml,
            Array.Empty<TelegramInlineNode>(),
            Array.Empty<TelegramListItemNode>(),
            new TelegramTableNode(headers, rows));
    }

    private static TelegramCellNode ParseCell(IElement element)
    {
        return new TelegramCellNode(
            NormalizeWhitespace(element.TextContent),
            element.InnerHtml,
            ParseInlines(element));
    }

    private static List<TelegramInlineNode> ParseInlines(IElement element)
    {
        var nodes = new List<TelegramInlineNode>();
        AppendInlines(element.ChildNodes, nodes);
        return nodes;
    }

    private static void AppendInlines(INodeList childNodes, ICollection<TelegramInlineNode> nodes)
    {
        foreach (var node in childNodes)
        {
            switch (node)
            {
                case IText textNode:
                    {
                        var text = NormalizeWhitespace(textNode.Text);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            nodes.Add(new TelegramInlineNode("text", text, null, null));
                        }

                        break;
                    }
                case IElement element:
                    {
                        if (element.TagName.Equals("BR", StringComparison.OrdinalIgnoreCase))
                        {
                            nodes.Add(new TelegramInlineNode("line-break", string.Empty, null, null));
                            break;
                        }

                        if (element.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                        {
                            var href = element.GetAttribute("href");
                            var anchor = href?.StartsWith('#') == true ? href[1..] : null;
                            var text = NormalizeWhitespace(element.TextContent);

                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                nodes.Add(new TelegramInlineNode("link", text, href, anchor));
                            }

                            break;
                        }

                        var kind = element.TagName.ToUpperInvariant() switch
                        {
                            "CODE" => "code",
                            "EM" => "emphasis",
                            "STRONG" => "strong",
                            _ => "container"
                        };

                        var inlineText = NormalizeWhitespace(element.TextContent);
                        if (!string.IsNullOrWhiteSpace(inlineText) && kind is not "container")
                        {
                            nodes.Add(new TelegramInlineNode(kind, inlineText, null, null));
                        }
                        else
                        {
                            AppendInlines(element.ChildNodes, nodes);
                        }

                        break;
                    }
            }
        }
    }

    private static string? GetAnchorName(IElement heading)
    {
        return heading.QuerySelector("a.anchor")?.GetAttribute("name");
    }

    private static string NormalizeAnchor(string value)
    {
        var normalized = NormalizeWhitespace(value).Replace(' ', '-');
        return string.Create(normalized.Length, normalized, static (chars, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                chars[index] = char.ToLowerInvariant(source[index]);
            }
        });
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(
            " ",
            value
                .Replace('\u00A0', ' ')
                .Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class TelegramCategoryBuilder(string anchor, string title)
    {
        public string Anchor { get; } = anchor;
        public string Title { get; } = title;
        public List<TelegramSectionNode> Sections { get; } = [];

        public TelegramCategoryNode Build()
        {
            return new TelegramCategoryNode(Anchor, Title, Sections.ToArray());
        }
    }
}
