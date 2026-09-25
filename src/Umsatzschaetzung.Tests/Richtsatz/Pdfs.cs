using System.Collections.Concurrent;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

static class Pdfs
{
    static readonly ConcurrentDictionary<int, Lazy<Sammlung>> Parsed = new();

    public static byte[] Bytes(int year) => File.ReadAllBytes(TestData.Fixture($"richtsatzsammlung/richtsatzsammlung-{year}.pdf"));

    public static Sammlung Sammlung(int year) => Parsed.GetOrAdd(year, y => new(() => Richtsätze.Read(Sheets.Read(Bytes(y))))).Value;

    public static string ShippedJson(int year) => File.ReadAllText(Path.Combine(TestData.Repo, "data", "richtsatz", $"{year}.json"));

    public static IEnumerable<int> ShippedYears() =>
        Directory.GetFiles(Path.Combine(TestData.Repo, "data", "richtsatz"), "*.json")
            .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f))).Order();
}
