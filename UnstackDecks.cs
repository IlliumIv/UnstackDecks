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
using Point = SharpDX.Point;
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

            Settings.ExtraDelay.OnValueChanged += (sender, value) =>
            {
                _WaitUserDefined = new WaitTime(Settings.ExtraDelay.Value);
                _SaveSettings();
            };

            Settings.MouseSpeed.OnValueChanged += (sender, f) =>
            {
                Mouse.speedMouse = Settings.MouseSpeed.Value;
                _SaveSettings();
            };

            Settings.TimeBetweenClicks.OnValueChanged += (sender, i) =>
            {
                _WaitBetweenClicks = new WaitTime(Settings.TimeBetweenClicks);
                _SaveSettings();
            };

            return true;
        }

        public override void Render()
        {
            base.Render();

            if (_UnstackCoroutine != null && _UnstackCoroutine.IsDone)
            {
                _UnstackCoroutine = null;
            }

            var requiredPanelsOpen = GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible;

            if (!requiredPanelsOpen && _UnstackCoroutine != null && !_UnstackCoroutine.IsDone)
            {
                _UnstackCoroutine = Core.ParallelRunner.FindByName(Name);
                _UnstackCoroutine?.Done();
            }

            if (_UnstackCoroutine != null && _UnstackCoroutine.Running && _DebugTimer.ElapsedMilliseconds > 5000)
            {
                _UnstackCoroutine?.Done();
                _DebugTimer.Restart();
                _DebugTimer.Stop();
            }

            if (!Settings.UnstackHotkey.PressedOnce()) return;
            if (!requiredPanelsOpen) return;

            _UnstackCoroutine = new Coroutine(UnstackTheDecks(), this, Name);

            Core.ParallelRunner.Run(_UnstackCoroutine);
        }

        private IEnumerator UnstackTheDecks()
        {
            _DebugTimer.Restart();
            yield return ParseInventory();

            var originalCursorPosition = Input.ForceMousePosition;
            if (_SlotsWithStackedDecks.Count > 0)
            {
                yield return PopTheStacks();
            }

            if (Settings.PreserveOriginalCursorPosition)
            {
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

        private IEnumerator PopTheStacks()
        {
            var openSlotPos = Point.Zero;

            foreach (var slot in _SlotsWithStackedDecks)
            {
                var stackSize = slot.Item.GetComponent<Stack>().Size;

                while (stackSize > 1)
                {
                    if (!_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                    {
                        _UnstackCoroutine?.Done();
                    }
                    else
                    {
                        yield return PopStack(slot.GetClientRect().Center,
                            GetClientRectFromPoint(openSlotPos, 1, 1).Center);
                    }

                    --stackSize;
                    ++_CoroutineIterations;
                    _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
                }

                if (!_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                {
                    DebugWindow.LogError("UnstackDecks => Inventory doesn't have space to place the next div card.");
                    _UnstackCoroutine?.Done();
                    ++_CoroutineIterations;
                    _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
                }
                else
                {
                    yield return PopStack(slot.GetClientRect().Center, slot.GetClientRect().Center);
                }
            }
        }

        private IEnumerator SmoothlyMoveCursor(Vector2 to)
        {
            var step = Math.Max(Vector2.Distance(Input.ForceMousePosition, to) / 100, 4);

            for (var i = 0; i < step; i++)
            {
                Input.SetCursorPos(Vector2.SmoothStep(Input.ForceMousePosition, to, i / step));
                Input.MouseMove();
                yield return _Wait1ms;
            }
        }

        private IEnumerator PopStack(Vector2 source, Vector2 destination)
        {
            //var cursorInventory = GameController.Game.IngameState.ServerData.PlayerInventories[12].Inventory;
            var delay = new WaitTime((int) GameController.Game.IngameState.CurLatency * 2 + _WaitUserDefined.Milliseconds);

            //if (cursorInventory.Items.Count == 0)
            {
                yield return SmoothlyMoveCursor(source);
                yield return delay;

                Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Left : MouseButtons.Right);
                Input.MouseMove();
                yield return _WaitBetweenClicks;
                ++_CoroutineIterations;
                _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
            }

            //if (cursorInventory.Items.Count == 1)
            {
                yield return SmoothlyMoveCursor(destination);
                yield return delay;

                Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Right : MouseButtons.Left);
                Input.MouseMove();
                yield return _WaitBetweenClicks;
                ++_CoroutineIterations;
                _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
            }
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