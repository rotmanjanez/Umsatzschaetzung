using System.Text;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rules;

namespace Umsatzschaetzung.Service;

// What every local part does around a call: off the caller's thread, with the stores' errors told as service errors.
static class Local
{
    public static Task<T> Guard<T>(CancellationToken ct, Func<T> body) => Guard(ct, () => Task.FromResult(body()));

    public static async Task<T> Guard<T>(CancellationToken ct, Func<Task<T>> body)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(body, ct);
        }
        catch (CaseExistsException e)
        {
            throw new ServiceError(ErrorCode.Conflict, e.Message, e.Labels, e);
        }
        catch (Exception e) when (e is not (ServiceError or OperationCanceledException))
        {
            throw new ServiceError(e switch
            {
                CaseNotFoundException => ErrorCode.NotFound,
                CaseInvalidException or RulesException or InvalidDataException => ErrorCode.Invalid,
                StoreUnavailableException => ErrorCode.Unavailable,
                _ => ErrorCode.Internal,
            }, e.Message, inner: e);
        }
    }

    public static string FileName(string label, string ext)
    {
        var b = new StringBuilder();
        foreach (var r in label)
        {
            switch (r)
            {
                case >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_': b.Append(r); break;
                case 'ä': b.Append('a'); break;
                case 'ö': b.Append('o'); break;
                case 'ü': b.Append('u'); break;
                case 'Ä': b.Append('A'); break;
                case 'Ö': b.Append('O'); break;
                case 'Ü': b.Append('U'); break;
                case 'ß': b.Append('s'); break;
                case ' ': b.Append('_'); break;
            }
        }
        var name = b.Length == 0 ? "Prüfung" : b.ToString();
        return "Umsatzschätzung-" + name + "-" + Today().ToString("yyyy-MM-dd") + "." + ext;
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
}
