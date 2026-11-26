using UglyToad.PdfPig;

namespace BookUniversity;

internal static class PdfSearch
{
    public record Hit(int Page, string Snippet);

    public static List<Hit> Find(byte[] pdf, string query, int max = 500)
    {
        var hits = new List<Hit>();
        using var doc = PdfDocument.Open(pdf);
        for (var i = 1; i <= doc.NumberOfPages && hits.Count < max; i++)
        {
            var text = doc.GetPage(i).Text;
            var at = 0;
            while (hits.Count < max && (at = text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var s = Math.Max(0, at - 40);
                var e = Math.Min(text.Length, at + query.Length + 40);
                hits.Add(new Hit(i - 1, text[s..e].Replace('\n', ' ').Replace('\r', ' ')));
                at += query.Length;
            }
        }
        return hits;
    }
}
