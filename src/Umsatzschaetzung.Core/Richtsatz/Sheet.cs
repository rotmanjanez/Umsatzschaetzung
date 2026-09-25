namespace Umsatzschaetzung.Richtsatz;

// Page geometry with y growing downwards, so that reading order is ascending.
public readonly record struct Word(string Text, double X0, double X1, double Top, double Bottom, double Baseline, double Size)
{
    public double Xc => (X0 + X1) / 2;
}

public readonly record struct Rule(double X0, double X1, double Top, double Bottom)
{
    public double Width => X1 - X0;
    public double Height => Bottom - Top;
}

public sealed record Sheet(int Number, double Width, double Height, List<Word> Words, List<Rule> Rules);
