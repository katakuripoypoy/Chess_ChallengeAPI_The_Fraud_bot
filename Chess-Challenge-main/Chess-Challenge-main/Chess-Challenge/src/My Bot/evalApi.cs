using System;
using System.Collections.Generic;
using ChessChallenge.API;

public class EvalApi
{
    // this class is only for computing the advanced evaluation
    // the only things this class will not include are: 
    // 1. Material Difference
    // 2. Piece-Square Tables

    static int Chebyshev(int f1, int r1, int f2, int r2) => Math.Max(Math.Abs(f1 - f2), Math.Abs(r1 - r2));

    static int DistToEdge(Square s)
    {
        int f = s.File, r = s.Rank;
        // 0 (on edge) .. 3 (one step inside) .. 3 (center band)
        return Math.Min(Math.Min(f, 7 - f), Math.Min(r, 7 - r));
    }

    static int DistToNearestCorner(Square s)
    {
        // Chebyshev distances to four corners; take the min
        int f = s.File, r = s.Rank;
        int d1 = Math.Max(f, r);           // a1 (0,0)
        int d2 = Math.Max(f, 7 - r);       // a8 (0,7)
        int d3 = Math.Max(7 - f, r);       // h1 (7,0)
        int d4 = Math.Max(7 - f, 7 - r);   // h8 (7,7)
        return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4)); // 0..7
    }

    static int DistToCenter4(Square s)
    {
        // distance to nearest of d4(3,3), e4(4,3), d5(3,4), e5(4,4) via Chebyshev
        int f = s.File, r = s.Rank;
        int d1 = Chebyshev(f, r, 3, 3);
        int d2 = Chebyshev(f, r, 4, 3);
        int d3 = Chebyshev(f, r, 3, 4);
        int d4 = Chebyshev(f, r, 4, 4);
        return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4)); // 0..4
    }

    // ---------- Tiny center occupancy nudge (orthogonal to your PST) ----------
    static int CenterOccupancyScore(Board b)
    {
        // +6 per piece on d4/e4/d5/e5 for its side
        int score = 0;
        int[] centers = { 27, 28, 35, 36 }; // API square indices: d4,e4,d5,e5

        foreach (var list in b.GetAllPieceLists())
        {
            int side = list.IsWhitePieceList ? 1 : -1;
            foreach (var p in list)
            {
                int idx = p.Square.Index;
                if (idx == centers[0] || idx == centers[1] || idx == centers[2] || idx == centers[3])
                    score += side * 6;
            }
        }
        return score;
    }

    // ---------- Individual terms ----------
    public static int EdgeCornerPull(Board b, bool whitePerspective = true)
    {
        // Reward pushing the ENEMY king toward edges/corners.
        const int EDGE_PULL_BONUS = 4; // per step closer to edge
        const int CORNER_PULL_BONUS = 3; // per step closer to nearest corner

        var wK = b.GetKingSquare(true);
        var bK = b.GetKingSquare(false);

        int edgeWhiteGets = (3 - DistToEdge(bK)) * EDGE_PULL_BONUS;   // White wants Black king on edge
        int edgeBlackGets = (3 - DistToEdge(wK)) * EDGE_PULL_BONUS;   // Black wants White king on edge
        int cornerWhiteGets = (7 - DistToNearestCorner(bK)) * CORNER_PULL_BONUS; // White wants Black king in corner
        int cornerBlackGets = (7 - DistToNearestCorner(wK)) * CORNER_PULL_BONUS; // Black wants White king in corner

        int pull = (edgeWhiteGets + cornerWhiteGets) - (edgeBlackGets + cornerBlackGets);
        return whitePerspective ? pull : -pull;
    }

    public static int KingCentralize(Board b, bool whitePerspective = true)
    {
        // Encourage own king toward the 4 central squares (small).
        const int KING_CENTRALIZE = 2;

        var wK = b.GetKingSquare(true);
        var bK = b.GetKingSquare(false);

        int sc = (4 - DistToCenter4(wK)) * KING_CENTRALIZE
               - (4 - DistToCenter4(bK)) * KING_CENTRALIZE;

        return whitePerspective ? sc : -sc;
    }

    public static int KingsProximity(Board b, int signedMaterialNoKings)
    {
        // Bring kings closer if you're ahead; avoid if you're behind.
        // signedMaterialNoKings > 0 => White ahead, < 0 => Black ahead.
        const int KINGS_PROX_STRENGTH = 4;

        var wK = b.GetKingSquare(true);
        var bK = b.GetKingSquare(false);

        int dist = Chebyshev(wK.File, wK.Rank, bK.File, bK.Rank); // 1..7 typically
        int prox = (7 - dist) * KINGS_PROX_STRENGTH;
        if (signedMaterialNoKings > 0) return +prox; // White ahead => encourage closing the net
        if (signedMaterialNoKings < 0) return -prox; // Black ahead  => discourage proximity for White (i.e., benefit Black)
        return 0;
    }

    public static int CenterControl(Board b)
    {
        // Very light center bonus so PST remains primary.
        const int CENTER_CONTROL = 1;
        return CenterOccupancyScore(b) * CENTER_CONTROL;
    }


    //------------- Midde Game Herustics -------------

    static int PassedPawns(Board b)
    {
        const int BASE = 15;
        int score = 0;

        foreach (bool side in new[] { true, false })
        {
            var pawns = b.GetPieceList(PieceType.Pawn, side);
            foreach (var p in pawns)
            {
                bool passed = true;
                for (int df = -1; df <= 1; df++)
                {
                    int f = p.Square.File + df;
                    if (f < 0 || f > 7) continue;

                    // Scan forward ranks
                    int start = p.Square.Rank + (side ? 1 : -1);
                    for (int r = start; r >= 0 && r < 8; r += (side ? 1 : -1))
                    {
                        var sq = new Square(f, r);
                        var piece = b.GetPiece(sq);
                        if (piece.IsPawn && piece.IsWhite != side)
                        {
                            passed = false; break;
                        }
                    }
                    if (!passed) break;
                }

                if (passed)
                {
                    int advance = side ? p.Square.Rank : (7 - p.Square.Rank);
                    score += (side ? 1 : -1) * (BASE * advance);
                }
            }
        }
        return score;
    }

    static int RookFiles(Board b)
    {
        const int OPEN = 20, SEMI = 10;
        int score = 0;

        foreach (bool side in new[] { true, false })
        {
            var rooks = b.GetPieceList(PieceType.Rook, side);
            foreach (var r in rooks)
            {
                ulong friendlyPawns = b.GetPieceBitboard(PieceType.Pawn, side);
                ulong enemyPawns = b.GetPieceBitboard(PieceType.Pawn, !side);

                // Mask for all squares in this file
                ulong fileMask = 0x0101010101010101UL << r.Square.File;

                bool hasFriendly = (friendlyPawns & fileMask) != 0;
                bool hasEnemy = (enemyPawns & fileMask) != 0;

                if (!hasFriendly && !hasEnemy) score += side ? OPEN : -OPEN;
                else if (!hasFriendly && hasEnemy) score += side ? SEMI : -SEMI;
            }
        }
        return score;
    }

    static int KnightOutposts(Board b)
    {
        const int BONUS = 15;
        int score = 0;

        foreach (bool side in new[] { true, false })
        {
            var knights = b.GetPieceList(PieceType.Knight, side);
            foreach (var n in knights)
            {
                // Only central 16 squares
                if (n.Square.File < 2 || n.Square.File > 5) continue;
                if (n.Square.Rank < 2 || n.Square.Rank > 5) continue;

                // Pawn support
                int pawnRank = n.Square.Rank + (side ? -1 : 1);
                if (pawnRank >= 0 && pawnRank < 8)
                {
                    var left = new Square(n.Square.File - 1, pawnRank);
                    var right = new Square(n.Square.File + 1, pawnRank);

                    bool supported =
                        (left.File >= 0 && left.File <= 7 && b.GetPiece(left).IsPawn && b.GetPiece(left).IsWhite == side) ||
                        (right.File >= 0 && right.File <= 7 && b.GetPiece(right).IsPawn && b.GetPiece(right).IsWhite == side);

                    if (supported)
                        score += side ? BONUS : -BONUS;
                }
            }
        }
        return score;
    }



    // ---------- Combined "Endgame Extras" ----------
    public static int EndgameExtras(Board b, int signedMaterialNoKings, bool whitePerspective = true)
    {
        // Sum of all terms; safe to call only when your bot's IsEndGame()==true
        int score = 0;
        score += EdgeCornerPull(b, whitePerspective);
        score += KingCentralize(b, whitePerspective);
        score += KingsProximity(b, signedMaterialNoKings);

        return score;
    }
    
    public static int MiddleGameExtras(Board b, bool whitePerspective = true)
    {
        // Sum of all terms; safe to call only when your bot's IsEndGame()==false
        int score = 0;
        score += CenterControl(b);
        // Add other middle game terms here as needed
        score += PassedPawns(b);
        score += RookFiles(b);
        score += KnightOutposts(b);

        return score;
    }

}