using ExileCore;
using SharpDX;

namespace UnstackDecks
{
    internal static class ArrayExtensions
    {
        public static void Fill(this int[,] matrix, int value, int col, int row, int width, int height)
        {
            for (var r = row; r < row + height; r++)
            {
                for (var c = col; c < col + width; c++)
                {
                    if (r >= 0 && c >= 0 && r < matrix.GetLength(0) && c < matrix.GetLength(1))
                    {
                        matrix[r, c] = value;
                    }
                }
            }
        }

        public static bool GetNextOpenSlot(this int[,] matrix, ref Point start)
        {
            if (start.X < 0 || start.Y < 0 || start.X >= matrix.GetLength(0) || start.Y >= matrix.GetLength(1)) 
            {
                start.X = -1;
                start.Y = -1;
                return false;
            }

            for (; start.X < matrix.GetLength(1); start.X++)
            {
                for (; start.Y < matrix.GetLength(0); start.Y++)
                {
                    if (matrix[start.Y, start.X] == 0)
                    {
                        DebugWindow.LogDebug($"[{start.X}, {start.Y}] [{matrix.GetLength(0)}, {matrix.GetLength(1)}]");
                        return true;
                    }
                }

                start.Y = 0;
            }

            start.X = -1;
            start.Y = -1;
            return false;
        }
    }
}