using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class ImportJob(string caseId, string label) : Observable
{
    readonly CancellationTokenSource cts = new();

    int total, done;
    string file = "", tps = "";
    bool running;

    public string CaseId { get; } = caseId;
    public string Label { get; } = label;
    public Queue<PickedFile> Queue { get; } = new();
    public CancellationToken Ct => cts.Token;

    public int Stored { get; set; }
    public int Drafts { get; set; }
    public List<string> Failed { get; } = [];
    public string? FirstDraft { get; set; }

    public int Total { get => total; set { if (Set(ref total, value)) Raise(nameof(Text)); } }
    public int Done { get => done; set { if (Set(ref done, value)) Raise(nameof(Text)); } }
    public string File { get => file; set { if (Set(ref file, value)) Raise(nameof(Text)); } }
    public string Tps { get => tps; set => Set(ref tps, value); }
    public bool Running { get => running; set { if (Set(ref running, value)) Raise(nameof(Text)); } }

    public string Text => Running
        ? Math.Min(Done + 1, Total) + " von " + Total + " · " + File
        : "Wartet · " + Total + (Total == 1 ? " Datei" : " Dateien");

    public void Cancel() => cts.Cancel();
}

public sealed class Imports
{
    readonly Session session;
    readonly Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    ImportJob? current;
    bool pumping;
    int lastGen;
    double lastGenMs;
    long tokens;
    double millis;

    public Imports(Session session, ILlmProgress? llm)
    {
        this.session = session;
        if (llm is not null) llm.Progress += OnProgress;
    }

    public ObservableCollection<ImportJob> Jobs { get; } = [];

    public event Action<ImportJob>? Finished;

    public void Add(string caseId, string label, List<PickedFile> files)
    {
        if (files.Count == 0) return;
        var job = Jobs.FirstOrDefault(j => j.CaseId == caseId && !j.Ct.IsCancellationRequested);
        if (job is null)
        {
            job = new ImportJob(caseId, label);
            Jobs.Add(job);
        }
        foreach (var f in files) job.Queue.Enqueue(f);
        job.Total += files.Count;
        if (!pumping) _ = Pump();
    }

    public void CancelAll()
    {
        foreach (var j in Jobs) j.Cancel();
    }

    async Task Pump()
    {
        pumping = true;
        try
        {
            while (Next() is { } job)
                await Run(job);
            foreach (var j in Jobs.ToList()) Jobs.Remove(j);
        }
        finally
        {
            pumping = false;
            current = null;
        }
    }

    ImportJob? Next() => Jobs.FirstOrDefault(j => !j.Ct.IsCancellationRequested && j.Queue.Count > 0);

    async Task Run(ImportJob job)
    {
        current = job;
        job.Running = true;
        ResetStats(job);
        while (job.Queue.Count > 0 && !job.Ct.IsCancellationRequested)
        {
            var file = job.Queue.Dequeue();
            job.File = file.Name;
            try
            {
                await Import(job, file);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ServiceError ex)
            {
                job.Failed.Add(file.Name + ": " + ex.Message);
            }
            job.Done++;
        }
        job.Running = false;
        Finish(job);
    }

    async Task Import(ImportJob job, PickedFile file)
    {
        var parsed = await session.Service.ParseInvoice(job.CaseId, file.Name, file.Data, job.Ct);
        if (!parsed.NeedsOcr)
        {
            Adopt(job, parsed.Case);
            job.Stored++;
            return;
        }
        var ocr = await session.Service.OcrInvoice(job.CaseId, file.Name, file.Data, job.Ct);
        var v = await session.Service.VerifyInvoice(new VerifyReq(job.CaseId, ocr.Draft, false, true, file.Name, file.Data), job.Ct);
        session.Drafts[v.Invoice.Id] = ocr;
        Adopt(job, v.Case);
        job.Drafts++;
        job.FirstDraft ??= v.Invoice.Id;
    }

    void Adopt(ImportJob job, CaseResp? resp)
    {
        if (resp is not null && session.Case?.Id == job.CaseId) session.SetCase(resp);
    }

    void Finish(ImportJob job)
    {
        var where = session.Case?.Id == job.CaseId ? "" : job.Label + ": ";
        session.Message = where + job.Stored + " Rechnungen übernommen, " + job.Drafts + " zur Prüfung";
        if (job.Failed.Count > 0) session.Fail(where + "Nicht importiert: " + string.Join("; ", job.Failed));
        Finished?.Invoke(job);
        Jobs.Remove(job);
    }

    void ResetStats(ImportJob job)
    {
        lastGen = 0;
        lastGenMs = 0;
        tokens = 0;
        millis = 0;
        job.Tps = "";
    }

    // The engine reports a few times a second from the worker thread, and imports run one
    // at a time, so the sample belongs to whichever job is current. A falling token count
    // means the next completion started, so the finished one folds into the job total.
    void OnProgress(LlmStats stats)
    {
        if (current is not { } job) return;
        if (stats.GenTokens < lastGen)
        {
            tokens += lastGen;
            millis += lastGenMs;
        }
        lastGen = stats.GenTokens;
        lastGenMs = stats.GenMs;
        var text = stats.GenTokens == 0 && stats.PromptDone < stats.PromptTokens
            ? Rate(stats.PromptDone, stats.PromptMs) + " · Beleg wird gelesen"
            : Rate(tokens + stats.GenTokens, millis + stats.GenMs);
        dispatcher.BeginInvoke(() => job.Tps = text);
    }

    static string Rate(long tokens, double ms) =>
        tokens + " Token · " + (ms > 0 ? tokens * 1000.0 / ms : 0).ToString("0.0") + " T/s";
}
