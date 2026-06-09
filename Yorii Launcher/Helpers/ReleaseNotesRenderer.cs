using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Windows.UI.Text;

namespace Yorii_Launcher.Helpers
{
    // converts minecraft release notes html into winui xaml elements (hardest part in the whole launcher)
    public static partial class ReleaseNotesRenderer
    {
        private static readonly string[] BlockTags = ["p", "h1", "h2", "h3", "h4", "ul", "ol", "blockquote", "img", "hr", "pre"];
        private static readonly ConcurrentDictionary<string, UIElement> xamlCache = new();

        public static UIElement Render(string html)
        {
            var panel = new StackPanel { Spacing = 8 };

            foreach (var block in SplitBlocks(html))
            {
                var element = CreateBlock(block);

                if (element != null)
                    panel.Children.Add(element);
            }

            return panel;
        }

        public static UIElement RenderCached(string cacheKey, string html)
        {
            return xamlCache.GetOrAdd(cacheKey, _ => Render(html));
        }

        public static void InvalidateCache(string cacheKey)
        {
            xamlCache.TryRemove(cacheKey, out _);
        }

        public static void ClearCache()
        {
            xamlCache.Clear();
        }

        // split html into block elements (p, h1-h6, ul, img)
        private static List<BlockElement> SplitBlocks(string html)
        {
            var results = new List<BlockElement>();
            var index = 0;

            while (index < html.Length)
            {
                var tagStart = html.IndexOf('<', index);

                if (tagStart < 0)
                    break;

                var tagEnd = html.IndexOf('>', tagStart);

                if (tagEnd < 0)
                    break;

                var raw = html[(tagStart + 1)..tagEnd];
                var lower = raw.ToLowerInvariant();

                // skip closing tags
                if (lower.StartsWith('/'))
                {
                    index = tagEnd + 1;
                    continue;
                }

                // parse tag name and attributes
                var firstSpace = lower.IndexOfAny([' ', '/', '\n', '\r', '\t']);
                var tag = firstSpace > 0 ? lower[..firstSpace] : lower.TrimEnd('/');
                var attr = firstSpace > 0 ? raw[firstSpace..].Trim().TrimEnd('/').Trim() : "";
                var selfClosing = raw.EndsWith('/') || tag is "br" or "hr" or "img";

                if (selfClosing)
                {
                    results.Add(new BlockElement { Tag = tag, Attr = attr });
                    index = tagEnd + 1;
                    continue;
                }

                // find matching close tag
                var closeTag = $"</{tag}>";
                var closePos = html.IndexOf(closeTag, tagEnd + 1, StringComparison.OrdinalIgnoreCase);

                if (closePos >= 0)
                {
                    results.Add(new BlockElement
                    {
                        Tag = tag,
                        Attr = attr,
                        Inner = html[(tagEnd + 1)..closePos].Trim()
                    });

                    index = closePos + closeTag.Length;
                    continue;
                }

                // no close tag, grab until next block tag
                var nextBlock = FindNextBlockTag(html, tagEnd + 1);
                var inner = nextBlock >= 0
                    ? html[(tagEnd + 1)..nextBlock].Trim()
                    : html[(tagEnd + 1)..].Trim();

                results.Add(new BlockElement { Tag = tag, Attr = attr, Inner = inner });
                index = nextBlock >= 0 ? nextBlock : html.Length;
            }

            return results;
        }

        private static int FindNextBlockTag(string html, int start)
        {
            var earliest = int.MaxValue;

            foreach (var tag in BlockTags)
            {
                var position = IndexOfOpenTag(html, start, tag);

                if (position >= 0 && position < earliest)
                    earliest = position;
            }

            return earliest < int.MaxValue ? earliest : -1;
        }

        private static UIElement? CreateBlock(BlockElement block)
        {
            return block.Tag switch
            {
                "p" or "div" or "section" or "blockquote" => BuildParagraph(block.Inner),
                "h1" => BuildHeading(block.Inner, 24, 700, 0),
                "h2" => BuildHeading(block.Inner, 18, 600, 0),
                "h3" => BuildHeading(block.Inner, 16, 600, 12),
                "h4" => BuildHeading(block.Inner, 14, 600, 0),
                "h5" => BuildHeading(block.Inner, 14, 600, 4),
                "ul" or "ol" => BuildList(block.Tag, block.Inner),
                "img" => BuildImage(block.Attr),
                "hr" => new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    Opacity = 0.15,
                    Margin = new Thickness(0, 12, 0, 12)
                },


                "pre" => BuildCodeBlock(block.Inner),
                _ => string.IsNullOrWhiteSpace(block.Inner) ? null : BuildParagraph(block.Inner)
            };
        }

        private static UIElement? BuildParagraph(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner))
                return null;

            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.MaxHeight
            };

            BuildInlines(textBlock.Inlines, inner);
            return textBlock;
        }

        private static UIElement? BuildHeading(string inner, double size, ushort weight, double topMargin)
        {
            if (string.IsNullOrWhiteSpace(inner))
                return null;

            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = size,
                FontWeight = new FontWeight(weight),
                Margin = new Thickness(0, topMargin, 0, 0)
            };

            BuildInlines(textBlock.Inlines, inner);
            return textBlock;
        }

        private static UIElement? BuildList(string tag, string inner)
        {
            if (string.IsNullOrWhiteSpace(inner))
                return null;

            var items = ExtractListItems(inner);
            if (items.Count == 0)
                return null;

            var numbered = tag == "ol";
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                LineStackingStrategy = LineStackingStrategy.MaxHeight,
                Margin = new Thickness(12, 0, 0, 4)
            };

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    textBlock.Inlines.Add(new LineBreak());

                // bullet or number prefix
                textBlock.Inlines.Add(new Run
                {
                    Text = numbered ? $"{i + 1}.\u00a0" : "\u2022\u00a0"
                });

                BuildInlines(textBlock.Inlines, items[i]);
            }

            return textBlock;
        }

        private static UIElement? BuildCodeBlock(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner))
                return null;

            var code = WebUtility.HtmlDecode(StripTags(inner));

            return new Border
            {
                Padding = new Thickness(12),
                Margin = new Thickness(0, 6, 0, 0),

                CornerRadius = new CornerRadius(8),

                Background =
                    Application.Current.Resources.TryGetValue(
                        "CardBackgroundFillColorDefaultBrush",
                        out var brush)
                        ? (Brush)brush
                        : new SolidColorBrush(Microsoft.UI.Colors.DimGray),

                Child = new TextBlock
                {
                    Text = code,

                    FontFamily = new FontFamily("Consolas"),

                    TextWrapping = TextWrapping.Wrap,

                    IsTextSelectionEnabled = true,

                    LineHeight = 20
                }
            };
        }

        private static List<string> ExtractListItems(string inner)
        {
            var items = new List<string>();
            var matches = ListItemRegex().Matches(inner);

            foreach (Match match in matches)
            {
                var text = match.Groups[1].Value.Trim();

                if (!string.IsNullOrWhiteSpace(text))
                    items.Add(text);
            }

            if (items.Count == 0)
            {
                var fallback = StripTags(inner);

                if (!string.IsNullOrWhiteSpace(fallback))
                    items.Add(fallback);
            }

            return items;
        }

        private static UIElement? BuildImage(string attr)
        {
            var source = ExtractAttribute(attr, "src");

            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (!source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // relative url, prepend mojang domain
                source = "https://launchercontent.mojang.com" + (source.StartsWith('/') ? "" : "/") + source;
            }

            try
            {
                var bitmap = new BitmapImage
                {
                    DecodePixelWidth = 640
                };

                bitmap.UriSource = new Uri(source);

                return new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 320,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 4)
                };
            }
            catch
            {
                return null;
            }
        }

        private static void BuildInlines(InlineCollection inlines, string html)
        {
            var index = 0;

            while (index < html.Length)
            {
                // plain text before tag
                if (html[index] != '<')
                {
                    var nextTag = html.IndexOf('<', index);
                    var text = nextTag < 0 ? html[index..] : html[index..nextTag];
                    AddTextRun(inlines, text);
                    index = nextTag < 0 ? html.Length : nextTag;
                    continue;
                }

                var tagEnd = html.IndexOf('>', index);

                if (tagEnd < 0)
                {
                    AddTextRun(inlines, html[index..]);
                    break;
                }

                var raw = html[(index + 1)..tagEnd];
                var lower = raw.ToLowerInvariant();

                if (lower is "br" or "br/")
                {
                    inlines.Add(new LineBreak());
                    index = tagEnd + 1;
                    continue;
                }

                // skip closing tags
                if (lower.StartsWith('/'))
                {
                    index = tagEnd + 1;
                    continue;
                }

                // parse tag name
                var firstSpace = lower.IndexOfAny([' ', '/', '\n', '\r', '\t']);
                var tag = firstSpace > 0 ? lower[..firstSpace] : lower.TrimEnd('/');
                var closeTag = $"</{tag}>";
                var closePos = html.IndexOf(closeTag, tagEnd + 1, StringComparison.OrdinalIgnoreCase);

                if (closePos < 0)
                {
                    index = tagEnd + 1;
                    continue;
                }

                var inner = html[(tagEnd + 1)..closePos];

                switch (tag)
                {
                    case "strong":
                    case "b":
                        var bold = new Bold();
                        BuildInlines(bold.Inlines, inner);
                        inlines.Add(bold);
                        break;
                    case "em":
                    case "i":
                        var italic = new Italic();
                        BuildInlines(italic.Inlines, inner);
                        inlines.Add(italic);
                        break;
                    case "code":
                        inlines.Add(new Run
                        {
                            Text = WebUtility.HtmlDecode(StripTags(inner)),
                            FontSize = 13,
                            FontFamily = new FontFamily("Consolas")
                        });
                        break;
                    default:
                        AddTextRun(inlines, inner);
                        break;
                }

                index = closePos + closeTag.Length;
            }
        }

        private static void AddTextRun(InlineCollection inlines, string text)
        {
            var decoded = WebUtility.HtmlDecode(StripTags(text));

            if (!string.IsNullOrWhiteSpace(decoded))
                inlines.Add(new Run { Text = decoded });
        }

        private static int IndexOfOpenTag(string html, int start, string tag)
        {
            var index = start;

            while (index < html.Length)
            {
                var position = html.IndexOf($"<{tag}", index, StringComparison.OrdinalIgnoreCase);

                if (position < 0)
                    return -1;

                var after = position + tag.Length + 1;

                // check char after tag is delimiter, not part of longer tag name
                if (after >= html.Length || html[after] is '>' or ' ' or '/' or '\n' or '\r' or '\t')
                    return position;

                index = after;
            }

            return -1;
        }

        private static string? ExtractAttribute(string attributes, string name)
        {
            var match = Regex.Match(attributes, $@"{name}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);

            if (match.Success)
                return match.Groups[1].Value;

            match = Regex.Match(attributes, $@"{name}\s*=\s*([^\s""'>]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string StripTags(string html)
        {
            return TagRegex().Replace(html, "");
        }

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"<li[^>]*>(.*?)(?=<li|</ul>|</ol>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ListItemRegex();

        private sealed class BlockElement
        {
            public string Tag { get; set; } = "";
            public string Attr { get; set; } = "";
            public string Inner { get; set; } = "";
        }
    }
}
