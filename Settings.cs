using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace UnstackDecks
{
    public class UnstackDecksSettings : ISettings
    {
        public UnstackDecksSettings()
        {
            Enable = new ToggleNode(true);
            UnstackHotkey = Keys.F1;
            ExtraDelay = new RangeNode<int>(0, 0, 200);
            MouseSpeed = new RangeNode<float>(1, 0, 30);
            PreserveOriginalCursorPosition = new ToggleNode(true);
            ReverseMouseButtons = new ToggleNode(true);
        }

        public ToggleNode Enable { get; set; }   
        public HotkeyNode UnstackHotkey { get; set; }
        public RangeNode<int> ExtraDelay { get; set; }
        public RangeNode<float> MouseSpeed { get; set; }
        public ToggleNode PreserveOriginalCursorPosition { get; set; }
        public ToggleNode ReverseMouseButtons { get; set; }
    }
}
