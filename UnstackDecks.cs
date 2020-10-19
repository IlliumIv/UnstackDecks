using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
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
        
        private readonly WaitTime wait1ms = new WaitTime(1);

        private Coroutine _UnstackCoroutine;
        private uint _CoroutineIterations;
        
        public UnstackDecks()
        {
            Name = "Unstack Decks";
        }

        public override bool Initialise()
        {
            Input.RegisterKey(Settings.UnstackHotkey.Value);

            Settings.UnstackHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.UnstackHotkey); };

            Settings.MouseSpeed.OnValueChanged += (sender, f) => { Mouse.speedMouse = Settings.MouseSpeed.Value; };
            return true;
        }

        public override void Render()
        {
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
                yield return Input.SetCursorPositionSmooth(new SharpDX.Vector2(originalCursorPosition.X,
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

            yield return wait1ms;
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
                        DebugWindow.LogError("UnstackDecks => Inventory doesn't have space to place the next div card.");
                        yield return wait1ms;
                    }
                    
                    yield return PopStack(slot.GetClientRect().Center, GetClientRectFromPoint(openSlotPos, 1, 1).Center);
                    --stackSize;
                    ++_CoroutineIterations;
                    _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
                }

                if (!_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                {
                    DebugWindow.LogError("UnstackDecks => Inventory doesn't have space to place the next div card.");
                    yield return wait1ms;
                }

                yield return PopStack(slot.GetClientRect().Center, slot.GetClientRect().Center);
                ++_CoroutineIterations;
                _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
            }
            ++_CoroutineIterations;
            _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
        }

        private IEnumerator PopStack(SharpDX.Vector2 source, SharpDX.Vector2 destination)
        {
            //var cursorInventory = GameController.Game.IngameState.ServerData.PlayerInventories[12].Inventory;
            var delay = new WaitTime((int) GameController.Game.IngameState.CurLatency * 2 + Settings.ExtraDelay);

            //while (cursorInventory.Items.Count == 0)
            {
                yield return Input.SetCursorPositionSmooth(source);
                yield return delay;

                Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Left: MouseButtons.Right);
                Input.MouseMove();
                yield return delay;
                ++_CoroutineIterations;
                _UnstackCoroutine?.UpdateTicks(_CoroutineIterations);
            }

            yield return delay;
            //while (cursorInventory.Items.Count == 1)
            {
                yield return Input.SetCursorPositionSmooth(destination);
                yield return delay;

                Input.Click(Settings.ReverseMouseButtons ? MouseButtons.Right : MouseButtons.Left);
                Input.MouseMove();
                yield return delay;
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