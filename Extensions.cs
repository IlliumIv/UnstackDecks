using ExileCore;
using ExileCore.Shared;
using SharpDX;
using System.Collections;
using System.Windows.Forms;

namespace UnstackDecks
{
    public static class InputExtension
    {
        public static IEnumerator SetCursorPositionAndClick(Vector2 vec, MouseButtons button = MouseButtons.Left, int delay = 3)
        {
            Input.SetCursorPos(vec);
            Input.Click(button);
            return new WaitTime(delay);
        }
    }
}
