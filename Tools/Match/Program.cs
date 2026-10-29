using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Chess;
using Chess.AI;

// Plays short matches between the built-in levels (and optionally Stockfish) from varied openings.
static class Program
{
    static readonly string[] Openings =
    {
        "e2e4 e7e5", "d2d4 d7d5", "c2c4 e7e5", "e2e4 c7c5", "g1f3 d7d5", "e2e4 e7e6", "d2d4 g8f6", "e2e4 c7c6",
    };

    static SearchLimits Easy() => new SearchLimits { MaxDepth = 2, TimeMs = 1000, FullEval = false, RandomMarginCp = 40, SloppyChance = 0.15 };
    static SearchLimits Hard() => new SearchLimits { MaxDepth = 64, TimeMs = 1500, FullEval = true };

    static int Main(string[] args)
    {
        int games = args.Length > 0 ? int.Parse(args[0]) : 8;
        string sf = args.Length > 1 ? args[1] : null;
        Play("Easy", Easy, "Hard", Hard, games);
        if (sf != null) PlayVsStockfish(sf, games);
        return 0;
    }

    static void Play(string an, Func<SearchLimits> a, string bn, Func<SearchLimits> b, int games)
    {
        var sa = new Searcher(20); var sb = new Searcher(20);
        double aScore = 0;
        for (int i = 0; i < games; i++)
        {
            bool aWhite = i % 2 == 0;
            var g = Game.FromUciMoves(Position.StartFen, Openings[i / 2 % Openings.Length].Split(' '));
            sa.Clear(); sb.Clear();
            while (!g.IsOver && g.History.Count < 300)
            {
                bool aMoves = (g.SideToMove == Side.White) == aWhite;
                var r = (aMoves ? sa : sb).Search(g.Position.Clone(), g.KeyHistory, aMoves ? a() : b());
                if (!g.TryMakeMove(r.Best)) throw new Exception("illegal " + r.Best);
            }
            double s = g.Result == GameResult.Draw || !g.IsOver ? 0.5 : (g.Result == GameResult.WhiteWins) == aWhite ? 1 : 0;
            aScore += s;
            Console.WriteLine($"{an} vs {bn} game {i + 1}: {(aWhite ? an : bn)} white, {g.ResultString} {g.Reason} ({g.History.Count} plies)");
        }
        Console.WriteLine($"== {an} {aScore}/{games} vs {bn} {games - aScore}/{games}");
    }

    static void PlayVsStockfish(string path, int games)
    {
        using var sf = new UciEngine(path);
        sf.StartAsync().Wait();
        var hard = new Searcher(20);
        double hScore = 0;
        for (int i = 0; i < games; i++)
        {
            bool hWhite = i % 2 == 0;
            var g = Game.FromUciMoves(Position.StartFen, Openings[i / 2 % Openings.Length].Split(' '));
            sf.NewGame(); hard.Clear();
            while (!g.IsOver && g.History.Count < 300)
            {
                if ((g.SideToMove == Side.White) == hWhite)
                    g.TryMakeMove(hard.Search(g.Position.Clone(), g.KeyHistory, Hard()).Best);
                else
                {
                    var r = sf.GoAsync(g.StartFen, g.History.Select(h => h.Move.ToUci()), 300, 0, CancellationToken.None).Result;
                    if (!g.TryMakeUci(r.BestMove)) throw new Exception("stockfish illegal " + r.BestMove);
                }
            }
            hScore += g.Result == GameResult.Draw || !g.IsOver ? 0.5 : (g.Result == GameResult.WhiteWins) == hWhite ? 1 : 0;
            Console.WriteLine($"Hard vs Stockfish game {i + 1}: {g.ResultString} {g.Reason} ({g.History.Count} plies)");
        }
        Console.WriteLine($"== Hard {hScore}/{games} vs Stockfish(300ms) {games - hScore}/{games}");
    }
}
