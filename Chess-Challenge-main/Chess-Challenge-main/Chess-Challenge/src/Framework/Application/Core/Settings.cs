﻿using System.Numerics;

namespace ChessChallenge.Application
{
    public static class Settings
    {
        public const string Version = "1.20";

        // Game settings
        public const int GameDurationMilliseconds = 600 * 1000;
        public const int IncrementMilliseconds = 0 * 1000;
        public const float MinMoveDelay = 0;
        public static readonly bool RunBotsOnSeparateThread = true;

        // Display settings
        public const bool DisplayBoardCoordinates = true;
        public static readonly Vector2 ScreenSizeSmall = new(1280, 720);
        public static readonly Vector2 ScreenSizeBig = new(1920, 1080);

        // Other settings
        public const int MaxTokenCount = 4096;
        public const LogType MessagesToLog = LogType.All;

        public enum LogType
        {
            None,
            ErrorOnly,
            All
        }
    }
}
