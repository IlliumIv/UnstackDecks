using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using Input = ExileCore.Input;
using RectangleF = SharpDX.RectangleF;
using Stack = ExileCore.PoEMemory.Components.Stack;

namespace UnstackDecks
{
    public class UnstackDecks : BaseSettingsPlugin<UnstackDecksSettings>
    {
        private readonly Stopwatch _DebugTimer = Stopwatch.StartNew();
        private int[,] _InventoryLayout;
        private List<ServerInventory.InventSlotItem> _SlotsWithStackedDecks;
        private RectangleF _InventoryRect;
        private float _CellSize;
        private float _PixelsPerStep = 100.0f;
        private float _MouseSpeed;

        private readonly WaitTime _Wait1ms = new WaitTime(1);
        private WaitTime _WaitUserDefined = new WaitTime(0);
        private WaitTime _WaitBetweenClicks = new WaitTime(40);

        private Coroutine _UnstackCoroutine;
        private uint _CoroutineIterations;

        public UnstackDecks()
        {
            Name = "UnstackDecks";
        }

        public override bool Initialise()
        {
            base.Initialise();

            Input.RegisterKey(Settings.UnstackHotkey.Value);

            Settings.Enable.OnValueChanged += (sender, value) => { _SaveSettings(); };
            Settings.PreserveOriginalCursorPosition.OnValueChanged += (sender, value) => { _SaveSettings(); };
            Settings.ReverseMouseButtons.OnValueChanged += (sender, value) => { _SaveSettings(); };

            Settings.UnstackHotkey.OnValueChanged += () =>
            {
                Input.RegisterKey(Settings.UnstackHotkey);
                _SaveSettings();
            };

            _MouseSpeed = _PixelsPerStep * Clamp(Settings.MouseSpeed.Value, 0.1f, 2.0f);
            Settings.MouseSpeed.OnValueChanged += (sender, f) =>
            {
                _MouseSpeed = _PixelsPerStep * Clamp(Settings.MouseSpeed.Value, 0.1f, 2.0f);
                _SaveSettings();
            };

            _WaitBetweenClicks = new WaitTime(Clamp(Settings.TimeBetweenClicks.Value, 20, 200));
            Settings.TimeBetweenClicks.OnValueChanged += (sender, i) =>
            {
                _WaitBetweenClicks = new WaitTime(Clamp(Settings.TimeBetweenClicks, 20, 200));
                _SaveSettings();
            };

            return true;
        }

        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            return value.CompareTo(max) > 0 ? max : value;
        }

        public override void Render()
        {
            base.Render();

            if (_UnstackCoroutine != null && _UnstackCoroutine.IsDone)
            {
                _UnstackCoroutine = null;
            }

            var requiredPanelsOpen = GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible;

            if (_UnstackCoroutine != null && _UnstackCoroutine.Running)
            {
                if (_DebugTimer.ElapsedMilliseconds > 15000)
                {
                    DebugWindow.LogError(
                        "Unstacking the current stacked deck has reached the time limit for an operation.");
                    StopCoroutine();
                }

                if (!requiredPanelsOpen || Input.GetKeyState(Keys.Escape))
                {
                    StopCoroutine();
                }

                return;
            }

            if (!Settings.UnstackHotkey.PressedOnce()) return;
            if (!requiredPanelsOpen) return;

            _UnstackCoroutine = new Coroutine(UnstackTheDecks(), this, Name);

            Core.ParallelRunner.Run(_UnstackCoroutine);
        }

        private void StopCoroutine()
        {
            _UnstackCoroutine = Core.ParallelRunner.FindByName(Name);
            _UnstackCoroutine?.Done();
            _DebugTimer.Restart();
            _DebugTimer.Stop();
        }

        private IEnumerator UnstackTheDecks()
        {
            _DebugTimer.Restart();
            yield return ParseInventory();

            var originalCursorPosition = Input.ForceMousePosition;
            if (_SlotsWithStackedDecks.Count > 0)
            {
                _DebugTimer.Restart();
                yield return PopTheStacks();
            }

            if (Settings.PreserveOriginalCursorPosition)
            {
                _DebugTimer.Restart();
                yield return SmoothlyMoveCursor(new Vector2(originalCursorPosition.X,
                    originalCursorPosition.Y));
                Input.MouseMove();
            }

            _UnstackCoroutine = Core.ParallelRunner.FindByName(Name);
            _UnstackCoroutine?.Done();
            _DebugTimer.Stop();
        }

        private IEnumerator ParseInventory()
        {
            try
            {
                var playerInventory = GameController.Game.IngameState.ServerData.PlayerInventories[0].Inventory;
                _InventoryLayout = GetInventoryLayout(playerInventory.InventorySlotItems);
                _InventoryRect =
                    GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory]
                        .GetClientRect();
                _CellSize = _InventoryRect.Width / playerInventory.Columns;
                _SlotsWithStackedDecks = playerInventory.InventorySlotItems.Where(slot =>
                    GameController.Files.BaseItemTypes.Translate(slot.Item.Path).BaseName == "Stacked Deck").ToList();
            }
            catch
            {
                // ignored
            }

            yield return _Wait1ms;
        }

        private static int[,] GetInventoryLayout(IEnumerable<ServerInventory.InventSlotItem> slots)
        {
            var inventorySlots = new[,]
            {
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
                {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
            };

            foreach (var slot in slots)
            {
                inventorySlots.Fill(1, slot.PosX, slot.PosY, slot.SizeX, slot.SizeY);
            }

            return inventorySlots;
        }

        private RectangleF GetClientRectFromPoint(Point pos, int width, int height)
        {
            return new RectangleF(
                _InventoryRect.X + _CellSize * pos.X,
                _InventoryRect.Y + _CellSize * pos.Y,
                width * _CellSize,
                height * _CellSize);
        }

        private IEnumerator SmoothlyMoveCursor(Vector2 to)
        {
            var step = Math.Max(Vector2.Distance(Input.ForceMousePosition, to) / _MouseSpeed, 4);

            for (var i = 0; i < step; i++)
            {
                Input.SetCursorPos(Vector2.SmoothStep(Input.ForceMousePosition, to, i / step));
                Input.MouseMove();
                yield return _Wait1ms;
            }
        }


        private IEnumerator MarkSlotUsed(Vector2 slotPosition)
        {
            _InventoryLayout.Fill(1, slotPosition);
            yield return _Wait1ms;
        }

        private IEnumerator PopTheStacks()
        {
            var openSlotPos = Point.Zero;

            foreach (var slot in _SlotsWithStackedDecks)
            {
                //yield return _Wait1ms;
                var stackSize = slot.Item.GetComponent<Stack>()?.Size ?? 0;
                var slotRectCenter = slot.GetClientRect().Center;
                //yield return _Wait1ms;

                _DebugTimer.Restart();

                while (stackSize <= 10 && stackSize >= 1)
                {
                    if (!_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                    {
                        DebugWindow.LogError(
                            "UnstackDecks => Inventory doesn't have space to place the next div card.");
                        yield break;
                    }
                    //right click the stackdeck stack aka PickUpCard()
                    yield return Input.SetCursorPositionAndClick(slotRectCenter, MouseButtons.Right, Settings.TimeBetweenClicks);
                    //check if MouseInventory contains an item and waits for it
                    yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.Cursor.ChildCount == 1, true);
                    //drop off the item from cursor at free inventory slot
                    yield return Input.SetCursorPositionAndClick(GetClientRectFromPoint(openSlotPos, 1, 1).Center, MouseButtons.Left, Settings.TimeBetweenClicks);
                    //wait for item to be dropped off
                    yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.Cursor.ChildCount == 0, true);
                    yield return MarkSlotUsed(openSlotPos);

                    //is our stacksize still valid ?
                    //stackSize = slot.Item.GetComponent<Stack>()?.Size ?? 0; for some weird ass reason thats an issue
                    --stackSize;
                    ++_CoroutineIterations;
                    _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
                }
            }
        }

        [Obsolete]
        private IEnumerator PickUpCard()
        {
            Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Left : MouseButtons.Right);
            Input.MouseMove();
            yield return _Wait1ms;
        }
        [Obsolete]
        private IEnumerator DropOffCard()
        {
            Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Right : MouseButtons.Left);
            Input.MouseMove();
            yield return _Wait1ms;
        }

        #region Adding / Removing Entities

        public override void EntityAdded(Entity _)
        {
        }

        public override void EntityRemoved(Entity _)
        {
        }

        #endregion
    }
}