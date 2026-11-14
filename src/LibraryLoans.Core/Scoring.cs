namespace LibraryLoans.Core;

public sealed record ScoreConfig(int DayWeight, int FineDivisor);

public static class Scoring
{
    public static readonly Func<int, Func<int, Func<FineLine, int>>> Curried =
        dayWeight => fineDivisor => line => Score(dayWeight, fineDivisor, line);

    public static Func<FineLine, int> CreateScore(ScoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.DayWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(config), "DayWeight must not be negative.");
        if (config.FineDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "FineDivisor must be positive.");

        return Curried(config.DayWeight)(config.FineDivisor);
    }

    public static readonly ScoreConfig ReminderConfig = new(1, 100);
    public static readonly ScoreConfig EscalationConfig = new(5, 50);

    public static Func<FineLine, int> Reminder { get; } = CreateScore(ReminderConfig);
    public static Func<FineLine, int> Escalation { get; } = CreateScore(EscalationConfig);

    public static bool TryGet(string name, out Func<FineLine, int> score)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "reminder": score = Reminder; return true;
            case "escalation": score = Escalation; return true;
            default: score = Reminder; return false;
        }
    }

    private static int Score(int dayWeight, int fineDivisor, FineLine line) =>
        line.DaysLate * dayWeight + line.Fine / fineDivisor;
}
