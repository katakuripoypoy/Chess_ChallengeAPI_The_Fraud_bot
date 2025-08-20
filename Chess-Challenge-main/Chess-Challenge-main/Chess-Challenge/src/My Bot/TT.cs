using System;
using System.Collections.Generic;
using ChessChallenge.API;

public class TT
{
    public enum EntryType
    {
        Exact,
        LowerBound,
        UpperBound
    }

    
    public struct Entry
    {
        public ulong Key;
        public int Depth;
        public int Score;      // Stored with mate-distance normalization
        public EntryType Flag;
        public Move BestMove;  // For move ordering
    }

    private readonly Entry[] table;
    private readonly int mask;

    // sizePowerOfTwo = 20 => ~1M entries. Feel free to tune.
    public TT(int sizePowerOfTwo = 20)
    {
        int size = 1 << sizePowerOfTwo;
        mask = size - 1;
        table = new Entry[size];
    }

    public void Clear() => Array.Clear(table, 0, table.Length);

    public bool Probe(ulong key, out Entry e)
    {
        e = table[(int)(key & (ulong)mask)];
        return e.Key == key;
    }

    // Always-replace policy; easy and effective for starters
    public void Store(ulong key, int depth, int score, EntryType flag, Move bestMove)
    {
        table[(int)(key & (ulong)mask)] = new Entry
        {
            Key = key,
            Depth = depth,
            Score = score,
            Flag = flag,
            BestMove = bestMove
        };
    }

    // Helper conversions for mate scores (keep mate distances correct across nodes)
    public static int ToTTScore(int score, int plyFromRoot, int mateScore = 500000)
    {
        if (score > mateScore - 1000) return score + plyFromRoot;
        if (score < -mateScore + 1000) return score - plyFromRoot;
        return score;
    }

    public static int FromTTScore(int score, int plyFromRoot, int mateScore = 500000)
    {
        if (score > mateScore - 1000) return score - plyFromRoot;
        if (score < -mateScore + 1000) return score + plyFromRoot;
        return score;
    }
}