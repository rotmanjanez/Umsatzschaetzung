using System.Collections.ObjectModel;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed class ImportJob(string caseId, string label) : Observable
{
    readonly CancellationTokenSource cts = new();

    int total, done;
    string file = "", summary = "";
    bool running;

    public string CaseId { get; } = caseId;
    public string Label { get; } = label;
    public Queue<PickedFile> Queue { get; } = new();
    public CancellationToken Ct => cts.Token;

    public ImportProgress Progress { get; } = new();

    public int Stored { get; set; }
    public int Drafts { get; set; }
    public List<string> Failed { get; } = [];

    public int Total { get => total; set { if (Set(ref total, value)) { Raise(nameof(Count)); Raise(nameof(Detail)); } } }
    public int Done { get => done; set { if (Set(ref done, value)) Raise(nameof(Count)); } }
    public string File { get => file; set { if (Set(ref file, value)) Raise(nameof(Detail)); } }
    public bool Running { get => running; set { if (Set(ref running, value)) { Raise(nameof(Count)); Raise(nameof(Detail)); } } }

    public string Count => Running ? "Datei " + Math.Min(Done + 1, Total) + " von " + Total : "Wartet";

    public string Detail => Running ? File : Total + (Total == 1 ? " Datei" : " Dateien");

    public string Summary { get => summary; set { if (Set(ref summary, value)) { Raise(nameof(Complete)); Raise(nameof(Pending)); } } }
    public bool Complete => summary != "";
    public bool Pending => summary == "";

    public double Fraction => Progress.Fraction;

    public string Percent => Math.Floor(Progress.Fraction * 100) + " %";

    public string Eta => Running ? Left(Progress.Remaining) : "";

    public void Sample()
    {
        Progress.Sample();
        Raise(nameof(Fraction));
        Raise(nameof(Percent));
        Raise(nameof(Eta));
    }

    public void Cancel() => cts.Cancel();

    static string Left(TimeSpan span) => span.TotalSeconds switch
    {
        < 12 => "noch wenige Sekunden",
        < 60 => "noch etwa " + (int)Math.Round(span.TotalSeconds / 5) * 5 + " Sekunden",
        < 120 => "noch etwa eine Minute",
        _ => "noch etwa " + (int)Math.Round(span.TotalMinutes) + " Minuten",
    };
}

public sealed class Imports
{
    readonly Session session;

    bool pumping;

    public Imports(Session session) => this.session = session;

    public ObservableCollection<ImportJob> Jobs { get; } = [];

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
        job.Progress.Plan(files.Count);
        if (!pumping) _ = Pump();
    }

    public void CancelAll()
    {
        foreach (var j in Jobs) j.Cancel();
    }

    async Task Pump()
    {
        pumping = true;
        using var activity = ImportActivity.Begin();
        try
        {
            while (Next() is { } job)
                await Run(job);
            foreach (var j in Jobs.ToList()) Jobs.Remove(j);
        }
        finally
        {
            pumping = false;
        }
    }

    ImportJob? Next() => Jobs.FirstOrDefault(j => !j.Ct.IsCancellationRequested && j.Queue.Count > 0);

    // Scans further down the queue are read while the one before them is stored: the
    // reader overlaps one page on the accelerator with another on the cores. The case is
    // still written one file at a time, in the order the files were picked.
    const int Ahead = 4;

    async Task Run(ImportJob job)
    {
        job.Running = true;
        var reading = new Queue<(PickedFile File, Task<OcrResp>? Ocr)>();
        while (!job.Ct.IsCancellationRequested)
        {
            while (reading.Count <= Ahead && job.Queue.TryDequeue(out var next)) reading.Enqueue((next, Read(job, next)));
            if (!reading.TryDequeue(out var item)) break;
            var file = item.File;
            job.File = file.Name;
            try
            {
                await Import(job, file, item.Ocr);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ServiceError ex)
            {
                job.Failed.Add(file.Name + ": " + ex.Message);
            }
            job.Progress.EndFile();
            job.Done++;
        }
        job.Running = false;
        Finish(job);
    }

    Task<OcrResp>? Read(ImportJob job, PickedFile file) =>
        InvoiceParser.Detect(file.Data) is Kind.Pdf or Kind.Image
            ? session.Service.OcrInvoice(job.CaseId, file.Name, file.Data, job.Ct)
            : null;

    async Task Import(ImportJob job, PickedFile file, Task<OcrResp>? reading)
    {
        job.Progress.Begin(ImportStage.Parse);
        var parsed = await session.Service.ParseInvoice(job.CaseId, file.Name, file.Data, job.Ct);
        if (!parsed.NeedsOcr)
        {
            Adopt(job, parsed.Case);
            job.Stored++;
            return;
        }
        job.Progress.Begin(ImportStage.Ocr);
        var ocr = await (reading ?? session.Service.OcrInvoice(job.CaseId, file.Name, file.Data, job.Ct));
        job.Progress.Begin(ImportStage.Verify);
        var v = await session.Service.VerifyInvoice(new VerifyReq(job.CaseId, ocr.Draft, Intent.Auto, file.Name, file.Data, ocr.Pages), job.Ct);
        Adopt(job, v.Case);
        if (v.Accepted)
        {
            job.Stored++;
            return;
        }
        job.Drafts++;
    }

    void Adopt(ImportJob job, Case? kase)
    {
        if (kase is not null && session.Case?.Id == job.CaseId) session.SetCase(kase);
    }

    void Finish(ImportJob job)
    {
        var where = session.Case?.Id == job.CaseId ? "" : job.Label + ": ";
        job.Summary = job.Stored + " Rechnungen übernommen, " + job.Drafts + " zur Durchsicht";
        if (job.Failed.Count > 0) session.Fail(where + "Nicht importiert: " + string.Join("; ", job.Failed));
        Jobs.Remove(job);
    }
}
