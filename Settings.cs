using System.Windows.Forms;
using ExileCore.Shared.Attributes;
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
            TimeBetweenClicks = new RangeNode<int>(20, 20, 200);
            MouseSpeed = new RangeNode<float>(1, 0.1f, 5);
            PreserveOriginalCursorPosition = new ToggleNode(false);
            ReverseMouseButtons = new ToggleNode(false);
        }

        [Menu("Enable", "Enables listening for hotkey to unstack.")]
        public ToggleNode Enable { get; set; }   
        [Menu("Hotkey", "Hotkey to be pressed to start unstacking.")]
        public HotkeyNode UnstackHotkey { get; set; }
        [Menu("Extra Delay", "Delay between each major action")]
        public RangeNode<int> ExtraDelay { get; set; }
        [Menu("Time Between Clicks", "Minimum time (ms) between clicks.")]
        public RangeNode<int> TimeBetweenClicks { get; set; }
        [Menu("Mouse Speed", "The pace the mouse moves between locations.")]
        public RangeNode<float> MouseSpeed { get; set; }
        [Menu("Preserve Cursor Position", "Resets the mouse position back to where it was before unstacking.")]
        public ToggleNode PreserveOriginalCursorPosition { get; set; }
        [Menu("Reverse mouse buttons", "Right-click to unstack is the default. If you have left-handed mouse, use this.")]
        public ToggleNode ReverseMouseButtons { get; set; }
    }
}
