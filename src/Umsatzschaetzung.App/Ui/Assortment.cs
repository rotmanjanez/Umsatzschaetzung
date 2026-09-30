using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed record SuggestionRow(string ProductId, string Name);

public static class Assortment
{
    public static async Task<Report?> Calculate(Session session, string caseId, CancellationToken ct) =>
        (await Ask(session, () => session.Service.Reports.Calculate(caseId, ct)))?.Report;

    public static async Task<List<SuggestionRow>?> Suggest(Session session, string caseId, RuleSet rs, IReadOnlySet<string> dismissed, CancellationToken ct) =>
        await Ask(session, () => session.Service.Assortment.Suggest(caseId, [.. dismissed], ct)) is { } ids
            ? [.. ids.Select(id => new SuggestionRow(id, Names.Product(rs, id)))]
            : null;

    static async Task<T?> Ask<T>(Session session, Func<Task<T>> call) where T : class
    {
        try
        {
            await session.Saved;
            return await call();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ServiceError e)
        {
            session.Fail(e);
            return null;
        }
        catch (Exception e)
        {
            session.Fail("Kalkulation fehlgeschlagen: " + e.Message);
            return null;
        }
    }
}
