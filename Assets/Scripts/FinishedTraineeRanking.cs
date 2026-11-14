using System;
using System.Collections.Generic;

public static class FinishedTraineeRanking
{
    public readonly struct Threshold
    {
        public readonly string rank;
        public readonly int minimumScore;

        public Threshold(string rank, int minimumScore)
        {
            this.rank = rank;
            this.minimumScore = minimumScore;
        }
    }

    private static readonly Threshold[] DefaultThresholds =
    {
        new Threshold("F", 0),
        new Threshold("F+", 1150),
        new Threshold("E", 1200),
        new Threshold("E+", 1250),
        new Threshold("D", 1300),
        new Threshold("D+", 1350),
        new Threshold("C", 1400),
        new Threshold("C+", 1450),
        new Threshold("B", 1500),
        new Threshold("B+", 1600),
        new Threshold("A", 1700),
        new Threshold("A+", 1800),
        new Threshold("S", 1900),
        new Threshold("S+", 2000)
    };

    public static string GetRank(int score, IReadOnlyList<Threshold> thresholds = null)
    {
        if (score < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Score cannot be negative.");
        }

        thresholds = thresholds ?? DefaultThresholds;
        if (thresholds.Count == 0 || thresholds[0].minimumScore != 0)
        {
            throw new ArgumentException("Rank thresholds must start at zero.", nameof(thresholds));
        }

        string rank = null;
        for (int i = 0; i < thresholds.Count; i++)
        {
            Threshold threshold = thresholds[i];
            if (string.IsNullOrWhiteSpace(threshold.rank)
                || (i > 0 && threshold.minimumScore <= thresholds[i - 1].minimumScore))
            {
                throw new ArgumentException("Rank thresholds must have names and increasing minimum scores.",
                    nameof(thresholds));
            }
            if (score >= threshold.minimumScore)
            {
                rank = threshold.rank;
            }
        }
        return rank;
    }
}
