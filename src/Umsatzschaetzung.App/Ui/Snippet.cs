using System.Security;
using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public static partial class Snippet
{
    // The line's element of an e-invoice, as it stands in the file.
    public static string? Excerpt(string xml, int index, InvoiceLine line)
    {
        var items = LineElement().Matches(xml);
        var escaped = SecurityElement.Escape(line.Name);
        var at = Rows.Pick(items.Count, index, i => items[i].Value.Contains(line.Name) || items[i].Value.Contains(escaped));
        if (at < 0) return null;
        var m = items[at];
        var indent = m.Index - xml.LastIndexOf('\n', m.Index) - 1;
        return string.Join('\n', m.Value.Split('\n').Select((l, i) =>
            (i == 0 ? l : l[Math.Min(indent, l.Length - l.TrimStart().Length)..]).TrimEnd('\r')));
    }

    [GeneratedRegex(@"<(?:[\w.-]+:)?(InvoiceLine|CreditNoteLine|IncludedSupplyChainTradeLineItem)[\s>][\s\S]*?</(?:[\w.-]+:)?\1>")]
    private static partial Regex LineElement();
}
