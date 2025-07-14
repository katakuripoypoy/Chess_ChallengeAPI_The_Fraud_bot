using System;
using System.Collections.Generic;
using System.Diagnostics;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };
    List<int[]> pieceAdjustments;

    

    int maxTime;
    bool iswhite;


    Move moveToPlay;

    int depth = 4;
        

        
    Board boardRef;
    Timer timeRef;
    int[] adjustmentValues = { 0, 10, 20, 30, 40, 45, 50, 55, 60, 65, 70, 75, 80, 90, 100 };
    public MyBot()
    {
        pieceAdjustments = new List<int[]>() { new int[] { 0 },// blank
        GetPieceAdjustments(new ulong[] { 13292315514680272486, 7378647193648342630 }),// pawn
        GetPieceAdjustments(new ulong[] { 12205485488516178448, 2454591752300046690 }),//knight
        GetPieceAdjustments(new ulong[] { 9760575157703033923, 4918887868711340132 }),//bishop
        GetPieceAdjustments(new ulong[] { 7378416150784730726, 8531619129795634789 }),//rook
        GetPieceAdjustments(new ulong[] { 8603413936259486787, 7550960606429581859 }),//queen
        GetPieceAdjustments(new ulong[] { 77125320457715986, 7550960606429581859   }),//king
        GetPieceAdjustments(new ulong[] { 15871470423304712720, 2459077696152591426 }),//king_endgame
        };

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




   
    public Move Think(Board board, Timer timer)
    {
        boardRef = board;
        timeRef = timer;
        iswhite = boardRef.IsWhiteToMove;

        int pieceCount = 0;
        Array.ForEach(boardRef.GetAllPieceLists(), list => pieceCount += list.Count);
        depth = pieceCount < 5 ? 7 : pieceCount < 10 ? 6 : 5;

        maxTime = timeRef.MillisecondsRemaining < 10000 ? 750 : timeRef.MillisecondsRemaining < 25000 ? 1000 : 2000;
        if (timeRef.MillisecondsRemaining < 5000)
        {
            maxTime = 500;
            depth = 5;
        }
        Search(depth, -600000, 600000, iswhite ? 1 : -1);

        
        return moveToPlay;


    }

    

    
    int Search(int currentDepth, int alpha, int beta, int color)
    {
        if (timeRef.MillisecondsElapsedThisTurn > maxTime) return 500000; 

        if (boardRef.IsInCheckmate() || boardRef.IsDraw()) return calculatePosition(color, currentDepth);

        

        if (currentDepth == 0) return QuiescenceSearch(alpha, beta, color, currentDepth);

        Move[] moves = GetSortedMoves(false);
        foreach (Move move in moves)
        {
            boardRef.MakeMove(move);
            int eval = -Search(currentDepth - 1, -beta, -alpha, -color);
            boardRef.UndoMove(move);
            if (eval >= beta) return beta; 
            
            if (eval > alpha)
            {
                alpha = eval;
                if (currentDepth == depth) moveToPlay = move;
            }
        }
        return alpha;
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


    Move[] GetSortedMoves(bool capturesOnly)
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



            scoreGuess += GetSquareValue(move.TargetSquare, boardRef.IsWhiteToMove, GetAdjustmentList(movePieceType));
            boardRef.MakeMove(move);
            if (boardRef.IsInCheck())
            {
                scoreGuess += 5000;
                if (boardRef.IsInCheckmate()) { scoreGuess += 500000; }

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



    int calculatePosition(int color, int currentDepth) {

        if (boardRef.IsInCheckmate()) return -500000 - currentDepth;
        
            
        
        if (boardRef.IsDraw()) return 0;

        int score = 0;

        PieceList[] pieceLists = boardRef.GetAllPieceLists();

        

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



    bool IsEndGame() {
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
    
}