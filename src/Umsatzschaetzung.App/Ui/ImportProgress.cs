using System.Diagnostics;

namespace Umsatzschaetzung.App.Ui;

public enum ImportStage
{
    Parse,
    Ocr,
    Verify,
}

public sealed class ImportProgress
{
    static readonly double[] Seed = [0.5, 6.0, 0.4];

    readonly double[] cost = (double[])Seed.Clone();
    readonly int[] seen = new int[Seed.Length];
    readonly Stopwatch stage = new(), file = new();

    double fraction;
    int files, finished, ocr;
    bool needsOcr, replan;
    ImportStage current;

    public double Fraction => fraction;

    public TimeSpan Remaining => TimeSpan.FromSeconds(Rest());

    public void Plan(int count)
    {
        files += count;
        replan = true;
    }

    public void Begin(ImportStage next)
    {
        Close();
        current = next;
        needsOcr |= next == ImportStage.Ocr;
        stage.Restart();
        file.Start();
    }

    public void EndFile()
    {
        Close();
        file.Reset();
        finished++;
        if (needsOcr) ocr++;
        needsOcr = false;
    }

    public void Sample()
    {
        var expected = cost[(int)ImportStage.Parse] + Share() * Tail();
        var partial = 1 - Math.Exp(-file.Elapsed.TotalSeconds / expected);
        var value = files == 0 ? 0 : Math.Min((finished + partial) / files, 1);
        fraction = replan ? value : Math.Max(fraction, value);
        replan = false;
    }

    void Close()
    {
        if (!stage.IsRunning) return;
        var elapsed = stage.Elapsed.TotalSeconds;
        stage.Reset();
        var i = (int)current;
        cost[i] += (elapsed - cost[i]) / Math.Min(++seen[i], 8);
    }

    double Share() => (ocr + 0.5) / (finished + 1);

    double Tail() => cost[(int)ImportStage.Ocr] + cost[(int)ImportStage.Verify];

    double Rest()
    {
        var share = Share();
        var tail = Tail();
        var pending = Math.Max(files - finished - (stage.IsRunning ? 1 : 0), 0) * (cost[(int)ImportStage.Parse] + share * tail);
        if (!stage.IsRunning) return pending;
        var here = Math.Max(cost[(int)current] - stage.Elapsed.TotalSeconds, 0);
        return pending + here + current switch
        {
            ImportStage.Parse => share * tail,
            ImportStage.Ocr => cost[(int)ImportStage.Verify],
            _ => 0,
        };
    }
}
