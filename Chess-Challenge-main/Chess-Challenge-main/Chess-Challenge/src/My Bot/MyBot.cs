using System;
using System.Collections.Generic;
using ChessChallenge.API;
using System.Buffers;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };
    List<int[]> pieceAdjustments;



    int maxTime;
    bool iswhite;



    Move moveToPlay;

    int depth = 256;

    TT tt = new TT(64);

    Board boardRef;
    Timer timeRef;
    EvalApi evalApi;
    int[] adjustmentValues = { 0, 10, 20, 30, 40, 45, 50, 55, 60, 65, 70, 75, 80, 90, 100 };
    public MyBot()
    {
        pieceAdjustments = new List<int[]>() { new int[] { 0 },// blank
        GetPieceAdjustments(new ulong[] { 13292315514680272486, 7378647193648342630 }),// pawn
        GetPieceAdjustments(new ulong[] { 12205485488516178448, 2454591752300046690 }),//knight
        GetPieceAdjustments(new ulong[] { 9760575157703033923, 4918887868711340132 }),//bishop
        GetPieceAdjustments(new ulong[] { 7378416150784730726, 8531619129795634789 }),//rook
        GetPieceAdjustments(new ulong[] { 8603413936259486787, 6071810403824072550 }),//queen
        GetPieceAdjustments(new ulong[] { 77125320457715986, 7550960606429581859   }),//king
        GetPieceAdjustments(new ulong[] { 15871470423304712720, 2459077696152591426 }),//king_endgame
        };
        string path = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, @"C:\Users\PC\Desktop\Chess-Challenge-main\Chess-Challenge-main\Chess-Challenge\src\My Bot\book.txt");

        TryLoadOpeningBook(path);

    }

    int[] GetPieceAdjustments(ulong[] rows)
    {
        int[] adjustments = new int[32];


        for (int i = 0; i < rows.Length; i++)
        {
            for (int j = 0; j < 16; j++)
                adjustments[i * 16 + j] = adjustmentValues[(int)(((1 << 4) - 1) & (rows[i] >> (4 * j)))] - 50;


        }

        return adjustments;
    }

    static string NormalizeFenKey(string raw)
    {
        // Strip tabs/spaces
        raw = raw.Trim().Replace('\t', ' ');

        // Take only the piece placement + side-to-move
        string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return raw;

        return parts[0] + " " + (parts[1][0] == 'w' ? "w" : "b");
    }



    static string MakeFenKey(Board b)
    {
        string fen = b.GetFenString();
        int i = fen.IndexOf(' ');
        string piecesOnly = (i >= 0 ? fen[..i] : fen);
        return NormalizeFenKey(piecesOnly + " " + (b.IsWhiteToMove ? "w" : "b"));
    }

   
    


    static readonly Dictionary<string, List<(string uci, int weight)>> OpeningBook =
    new(capacity: 621_037); // pre-size to avoid rehash

    static volatile bool BookLoaded = false;
    static readonly object BookLock = new();
    int bestScoreAtDepth = 0;
    Move _prevBestRoot = default;

    public Move Think(Board board, Timer timer)
    {
        boardRef = board;
        timeRef = timer;
        iswhite = boardRef.IsWhiteToMove;
        evalApi = new EvalApi();

        // Get the Book Move if available
        if (BookLoaded)
        {
            string fenKey = NormalizeFenKey(MakeFenKey(boardRef));
            Console.WriteLine($"[BOOK] Searching for book move for {fenKey}");
            if (OpeningBook.TryGetValue(fenKey, out var entries) && entries.Count > 0)
            {
                int total = 0;
                foreach (var e in entries) total += e.weight;


                foreach (var (uci, w) in entries)
                {


                    foreach (var mv in boardRef.GetLegalMoves())
                    {
                        Console.WriteLine($"Comparing: book {uci} vs move {mv}");
                        string mvUci = mv.StartSquare.Name + mv.TargetSquare.Name;
                        if (mvUci == uci)
                        {
                            Console.WriteLine($"[BOOK HIT] Book move {uci} found for {fenKey} with weight {w} (total: {total})");
                            return mv; // play book move
                        }
                    }
                    Console.WriteLine($"[BOOK FALLBACK] Book move {uci} not legal, falling back to search.");
                    goto SEARCH; // fallback to search if somehow not legal

                }
            }
            Console.WriteLine($"[BOOK FALLBACK] No book move found for {fenKey}");
            goto SEARCH; // fallback to search if no book move found
        }

        SEARCH:
        SetLimits();
        bool timeUp = false;
        Move bestAtDepth = default;
        for (int d = 1; d <= depth; d++)
        {

            int score = Search(d, -600000, 600000, iswhite ? 1 : -1, ref timeUp);
            if (timeUp) { Console.WriteLine($"[TIMEOUT] Best Move Found: {_prevBestRoot} ");  return _prevBestRoot; } // Stop if time exceeded

            bestAtDepth = moveToPlay;
            bestScoreAtDepth = score;
            _prevBestRoot = bestAtDepth;
            Console.WriteLine($"Depth: {d}, Score: {bestScoreAtDepth}, Move: {bestAtDepth}");
        }


        return bestAtDepth;


    }


    int Search(int currentDepth, int alpha, int beta, int color, ref bool timeUp, int plyFromRoot = 0)
    {
        const int INF = 600000;
        const int MATE = 500000;

        // Hard time cut: don't store cut results in TT
        if (timeRef.MillisecondsElapsedThisTurn > maxTime) { timeUp = true; return alpha; }

        if (boardRef.IsInCheckmate() || boardRef.IsDraw()) return calculatePosition(color, currentDepth);

        if (currentDepth == 0) return QuiescenceSearch(alpha, beta, color, currentDepth);

        int originalAlpha = alpha;
        Move ttMove = default;

        ulong key = boardRef.ZobristKey; // SebLague API exposes this
        if (tt.Probe(key, out var entry) && entry.Depth >= currentDepth)
        {
            int ttScore = TT.FromTTScore(entry.Score, plyFromRoot, MATE);

            if (entry.Flag == TT.EntryType.Exact)
                return ttScore;

            if (entry.Flag == TT.EntryType.LowerBound)
            {
                if (ttScore >= beta) return ttScore;
                alpha = Math.Max(alpha, ttScore);
            }
            else if (entry.Flag == TT.EntryType.UpperBound)
            {
                if (ttScore <= alpha) return ttScore;
                beta = Math.Min(beta, ttScore);
            }

            // Even when bounds aren't decisive, use stored move to improve ordering
            ttMove = entry.BestMove;
        }

        Move[] moves = GetSortedMoves(false, ttMove);
        int bestScore = -INF;
        Move bestMoveLocal = default;
        foreach (Move move in moves)
        {
            boardRef.MakeMove(move);
            int eval = -Search(currentDepth - 1, -beta, -alpha, -color, ref timeUp, plyFromRoot + 1);
            boardRef.UndoMove(move);
            if (eval > bestScore)
            {
                bestScore = eval;
                bestMoveLocal = move;

                if (eval > alpha)
                {
                    alpha = eval;
                    if (plyFromRoot == 0) moveToPlay = move;
                    if (alpha >= beta) break; // fail-high
                }
            }

        }

        TT.EntryType flag;
        if (bestScore <= originalAlpha) flag = TT.EntryType.UpperBound;   // fail-low
        else if (bestScore >= beta) flag = TT.EntryType.LowerBound;    // fail-high
        else flag = TT.EntryType.Exact;

        // Normalize mate distance before storing
        int storeScore = TT.ToTTScore(bestScore, plyFromRoot, MATE);
        tt.Store(key, currentDepth, storeScore, flag, bestMoveLocal);

        return bestScore;
    }


    int QuiescenceSearch(int alpha, int beta, int color, int currentDepth)
    {
        int eval = calculatePosition(color, currentDepth);
        if (eval >= beta) return beta;
        alpha = Math.Max(alpha, eval);

        Move[] captureMoves = GetSortedMoves(true);



        foreach (Move capture in captureMoves)
        {

            boardRef.MakeMove(capture);

            eval = -QuiescenceSearch(-beta, -alpha, -color, currentDepth - 1);
            boardRef.UndoMove(capture);


            if (eval >= beta) { return beta; }

            alpha = Math.Max(alpha, eval);
        }

        return alpha;
    }


    Move[] GetSortedMoves(bool capturesOnly, Move ttmove = default)
    {
        Move[] moves = boardRef.GetLegalMoves(capturesOnly);
        int[] scores = new int[moves.Length];
        int count = 0;



        foreach (Move move in moves)
        {
            int scoreGuess = 0;
            int movePieceType = (int)move.MovePieceType;

            if (move.IsCapture)
                scoreGuess = 10 * pieceValues[(int)move.CapturePieceType] - pieceValues[movePieceType];


            if (move.IsPromotion)
                scoreGuess += pieceValues[(int)move.PromotionPieceType] - pieceValues[movePieceType];


            if (ttmove != default)
            {
                if (move == ttmove)
                {
                    scoreGuess += 100_000; // Boost TT move
                }
            }

            if (move == _prevBestRoot)
            {
                scoreGuess += 200_000; // Boost best move at depth
            }

            scoreGuess += GetSquareValue(move.TargetSquare, boardRef.IsWhiteToMove, GetAdjustmentList(movePieceType));
            boardRef.MakeMove(move);
            if (boardRef.IsInCheck())
            {
                scoreGuess += 5000;
                if (boardRef.IsInCheckmate()) { scoreGuess += 500_000; }

            }
            boardRef.UndoMove(move);
            scores[count] = scoreGuess;
            count++;
        }



        for (int i = 0; i < moves.Length; i++)
        {
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (scores[i] < scores[j])
                {
                    int tempScore = scores[i];
                    scores[i] = scores[j];
                    scores[j] = tempScore;

                    Move tepmMove = moves[i];
                    moves[i] = moves[j];
                    moves[j] = tepmMove;
                }
            }
        }

        return moves;
    }



    int calculatePosition(int color, int currentDepth)
    {

        if (boardRef.IsInCheckmate()) return -500000 - currentDepth;



        if (boardRef.IsDraw()) return 0;

        int score = 0;

        PieceList[] pieceLists = boardRef.GetAllPieceLists();
        if (!IsEndGame())
            score += EvalApi.MiddleGameExtras(boardRef, iswhite);

        if (IsEndGame())
            score += EvalApi.EndgameExtras(boardRef, SignedMaterialNoKings());

        foreach (PieceList pieceList in pieceLists)
        {
            int pieceValue = pieceValues[(int)pieceList.TypeOfPieceInList];
            int[] adjustmentArray = GetAdjustmentList((int)pieceList.TypeOfPieceInList);



            foreach (Piece piece in pieceList)
            {
                int value = GetSquareValue(piece.Square, piece.IsWhite, adjustmentArray) + pieceValue;
                if (!piece.IsWhite)
                    value *= -1;

                score += value;

            }
        }

        return score * color;
    }



    int[] GetAdjustmentList(int pieceType)
    {
        return pieceType == 6 && IsEndGame() ? pieceAdjustments[7] : pieceAdjustments[pieceType];
    }


    int GetSquareValue(Square square, bool iswhite, int[] adjustmentArray)
    {
        int file = square.File;
        int rank = square.Rank;
        rank = iswhite ? 7 - rank : rank;
        if (file > 3)
            file = 7 - file;

        return adjustmentArray[rank * 4 + file];
    }



    bool IsEndGame()
    {
        bool[] sides = { true, false };



        var GetPieces = boardRef.GetPieceList;
        foreach (bool side in sides)
        {
            int queenCount = GetPieces(PieceType.Queen, side).Count;
            int minorPieceCount = GetPieces(PieceType.Rook, side).Count + GetPieces(PieceType.Bishop, side).Count + GetPieces(PieceType.Knight, side).Count;
            if ((queenCount == 0 && minorPieceCount > 2) || (queenCount == 1 && minorPieceCount > 1))
                return false;

        }
        return true;
    }



    void SetLimits()
    {
        int remain = timeRef.MillisecondsRemaining;
        int moveNumber = boardRef.PlyCount / 2 + 1;

        // Phase sensing
        int pieceCount = 0;
        Array.ForEach(boardRef.GetAllPieceLists(), l => pieceCount += l.Count);
        bool endgame = IsEndGame();

        // Heuristic "moves to go" based on phase
        int movesToGo =
            endgame ? 16 :
            (pieceCount > 20) ? 30 :
            (pieceCount > 14) ? 24 : 18;

        // Base budget
        int baseMs = remain / (movesToGo + 2);

        // Spend a bit more early and in endgame
        if (moveNumber <= 8) baseMs = baseMs * 12 / 10;
        if (endgame) baseMs = baseMs * 15 / 10;

        int minMs = (remain < 15_000) ? 150 : 300;
        int maxMs = Math.Min(3500, remain / 12);
        if (baseMs < minMs) baseMs = minMs;
        if (baseMs > maxMs) baseMs = maxMs;
        maxTime = baseMs;

        if (remain < 3000)
        {
            if (maxTime > 150) maxTime = 150;
        }
    }



    int SignedMaterialNoKings()
    {
        int sum = 0;
        foreach (var list in boardRef.GetAllPieceLists())
        {
            if (list.TypeOfPieceInList == PieceType.King) continue;
            int v = pieceValues[(int)list.TypeOfPieceInList];
            sum += (list.IsWhitePieceList ? +1 : -1) * v * list.Count;
        }
        return sum;
    }


    static void TryLoadOpeningBook(string path)
    {
        if (BookLoaded) return;

        lock (BookLock)
        {
            if (BookLoaded) return;

            int lines = 0, kept = 0;

            try
            {
                using var sr = new System.IO.StreamReader(path);
                string? raw;
                while ((raw = sr.ReadLine()) != null)
                {
                    lines++;
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    // Normalize whitespace: tabs → spaces
                    raw = raw.Trim().Replace('\t', ' ');

                    // Split into tokens
                    string[] tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 3) continue;

                    // FEN key = piece placement + side-to-move
                    string fenKey = tokens[0] + " " + tokens[1];

                    // Parse moves (uci:weight)
                    var list = new List<(string uci, int weight)>();
                    for (int i = 2; i < tokens.Length; i++)
                    {
                        string tok = tokens[i];
                        int colon = tok.IndexOf(':');
                        if (colon <= 0 || colon >= tok.Length - 1) continue;

                        string uci = tok[..colon];
                        if (!int.TryParse(tok[(colon + 1)..], out int w) || w <= 0) continue;

                        list.Add((uci, w));
                    }

                    if (list.Count > 0)
                    {
                        OpeningBook[fenKey] = list;
                        kept++;
                    }
                }
                BookLoaded = true; // only now mark it ready
                Console.WriteLine($"[Book] positions loaded: {OpeningBook.Count} (from {lines} lines, kept {kept})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Book] load error: {ex.Message}");
                // leave BookLoaded=false so we don't claim readiness
            }
        }
    }




}