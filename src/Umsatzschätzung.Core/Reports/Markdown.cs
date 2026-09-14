using System.Text;
using System.Text.RegularExpressions;

namespace Umsatzschätzung.Reports;

public static partial class Markdown
{
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex Star();

    [GeneratedRegex(@"(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)")]
    private static partial Regex Underscore();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    public static string ToHtml(string markdown, int headingOffset = 0)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var b = new StringBuilder();
        var paragraph = new List<string>();
        var i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            b.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
            paragraph.Clear();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.Trim() == "")
            {
                FlushParagraph();
                i++;
                continue;
            }
            if (line.StartsWith('#'))
            {
                FlushParagraph();
                var level = line.TakeWhile(ch => ch == '#').Count();
                var text = line[level..].Trim();
                var h = Math.Clamp(level + headingOffset, 1, 6);
                b.Append($"<h{h}>").Append(Inline(text)).Append($"</h{h}>\n");
                i++;
                continue;
            }
            if (line.StartsWith("```"))
            {
                FlushParagraph();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```")) code.Add(lines[i++]);
                i++;
                AppendCode(b, code);
                continue;
            }
            if (line.StartsWith("    ") && paragraph.Count == 0)
            {
                var code = new List<string>();
                while (i < lines.Length && (lines[i].StartsWith("    ") || lines[i].Trim() == ""))
                {
                    if (lines[i].Trim() == "" && (i + 1 >= lines.Length || !lines[i + 1].StartsWith("    "))) break;
                    code.Add(lines[i].Length >= 4 ? lines[i][4..] : "");
                    i++;
                }
                AppendCode(b, code);
                continue;
            }
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                FlushParagraph();
                b.Append("<ul>\n");
                while (i < lines.Length && (lines[i].StartsWith("- ") || lines[i].StartsWith("* ")))
                {
                    var item = new StringBuilder(lines[i][2..].Trim());
                    i++;
                    while (i < lines.Length && lines[i].Trim() != "" && !lines[i].StartsWith("- ") && !lines[i].StartsWith("* "))
                        item.Append(' ').Append(lines[i++].Trim());
                    b.Append("<li>").Append(Inline(item.ToString())).Append("</li>\n");
                }
                b.Append("</ul>\n");
                continue;
            }
            if (line.StartsWith('|') && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                FlushParagraph();
                var header = Cells(line);
                i += 2;
                b.Append("<table>\n<thead><tr>");
                foreach (var h in header) b.Append("<th>").Append(Inline(h)).Append("</th>");
                b.Append("</tr></thead>\n<tbody>\n");
                while (i < lines.Length && lines[i].StartsWith('|'))
                {
                    b.Append("<tr>");
                    foreach (var cell in Cells(lines[i])) b.Append("<td>").Append(Inline(cell)).Append("</td>");
                    b.Append("</tr>\n");
                    i++;
                }
                b.Append("</tbody>\n</table>\n");
                continue;
            }
            paragraph.Add(line.Trim());
            i++;
        }
        FlushParagraph();
        return b.ToString();
    }

    private static void AppendCode(StringBuilder b, List<string> code)
    {
        while (code.Count > 0 && code[^1].Trim() == "") code.RemoveAt(code.Count - 1);
        b.Append("<pre>").Append(Html.Esc(string.Join('\n', code))).Append("</pre>\n");
    }

    private static bool IsSeparator(string line) =>
        line.StartsWith('|') && line.Trim('|', ' ').Split('|').All(c => c.Trim().Length > 0 && c.Trim().All(ch => ch is '-' or ':'));

    private static string[] Cells(string line)
    {
        var s = line.Trim();
        if (s.StartsWith('|')) s = s[1..];
        if (s.EndsWith('|')) s = s[..^1];
        return s.Split('|').Select(c => c.Trim()).ToArray();
    }

    private static string Inline(string text)
    {
        var s = Html.Esc(text);
        s = Code().Replace(s, "<code>$1</code>");
        s = Bold().Replace(s, "<strong>$1</strong>");
        s = Star().Replace(s, "<em>$1</em>");
        s = Underscore().Replace(s, "<em>$1</em>");
        return s;
    }
}
