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
        private uint openedDecksCounter = 0;
        private int[,] _InventoryLayout;
        private Queue<ServerInventory.InventSlotItem> _SlotsWithStackedDecks;
        private RectangleF _InventoryRect;
        private float _CellSize;
        private readonly WaitTime _Wait1ms = new WaitTime(1);
        private Coroutine _UnstackCoroutine;
        private uint _CoroutineIterations;

        public UnstackDecks()
        {
            Name = "UnstackDecks";
        }

        public override bool Initialise()
        {
            base.Initialise();

            Input.RegisterKey(Settings.UnstackHotkey);

            Settings.Enable.OnValueChanged += (sender, value) => { _SaveSettings(); };
            Settings.PreserveOriginalCursorPosition.OnValueChanged += (sender, value) => { _SaveSettings(); };
            Settings.ReverseMouseButtons.OnValueChanged += (sender, value) => { _SaveSettings(); };

            Settings.UnstackHotkey.OnValueChanged += () =>
            {
                Input.RegisterKey(Settings.UnstackHotkey);
                _SaveSettings();
            };
            return true;
        }

        public override Job Tick()
        {
            if (Settings.UnstackHotkey.PressedOnce())
            {
                if (_UnstackCoroutine == null)
                {
                    _UnstackCoroutine = new Coroutine(UnstackDecksRoutine(), this, Name);
                    _DebugTimer.Start();
                    openedDecksCounter = 0;
                    Core.ParallelRunner.Run(_UnstackCoroutine);
                }
                else
                {
                    //stopping the plugin when hotkey is pressed during action
                    StopCoroutine();
                    _UnstackCoroutine = null;
                }
            }
            return null;
        }
        private IEnumerator UnstackDecksRoutine()
        {
            //int tries = 3;
            if (!areRequirementsMet())
            {
                StopCoroutine();
                yield break;
            }
            while (haveStackedDecks())
            {
                ParseInventory();
                yield return PopTheStacks();
                //tries++;
            }
            StopCoroutine();
        }

        /// <summary>
        /// Checks the requirements for the different modes.
        /// 1.is Inventory open
        /// 2.do we have any Stacked Decks in our Inventory
        /// 3.do we fullfill mode specific requirements
        ///     3a. DropToDivTab:   is our stash open
        ///                         is the currently visible stash a divination stashtab
        ///     3b. DropToGround:   are we not in town or hideout
        /// </summary>
        /// <returns>Returns true if no requirement failed.</returns>
        private bool areRequirementsMet()
        {
            //Inventory open
            //(Stash open and a divtab) when drop to divtab active
            //(not in town/hideout) when drop to ground active
            if (!GameController.IngameState.IngameUi.InventoryPanel.IsVisible) return false;
            if (!haveStackedDecks()) return false;
            if (Settings.DropToDivTab &&
                (!GameController.IngameState.IngameUi.StashElement.IsVisible ||
                GameController.IngameState.IngameUi.StashElement.VisibleStash.InvType != InventoryType.DivinationStash)) return false;
            else if (Settings.DropToGround && (GameController.Area.CurrentArea.IsHideout || GameController.Area.CurrentArea.IsTown)) return false;
            return true;
        }
        /// <summary>
        /// Checks if we have stacked decks in our Inventory
        /// </summary>
        /// <returns>Return true if we have atleast one Stacked Deck item in Inventory</returns>
        private bool haveStackedDecks()
        {
            return GameController.IngameState.ServerData.PlayerInventories[0].Inventory.Items.Where(x => x.Path == "Metadata/Items/DivinationCards/DivinationCardDeck").Count() > 0;
        }

        private void StopCoroutine()
        {
            _UnstackCoroutine = Core.ParallelRunner.FindByName(Name);
            _UnstackCoroutine?.Done();
            _DebugTimer.Stop();
            _UnstackCoroutine = null;
            LogMessage($"{openedDecksCounter} were opened in {_DebugTimer.ElapsedMilliseconds}: {_DebugTimer.ElapsedMilliseconds / openedDecksCounter} ms per Card at {Settings.TimeBetweenClicks.Value} ms between Clicks Setting", 10);
            _DebugTimer.Reset();
        }

        private void ParseInventory()
        {
            try
            {
                var playerInventory = GameController.Game.IngameState.ServerData.PlayerInventories[0].Inventory;
                _InventoryLayout = GetInventoryLayout(playerInventory.InventorySlotItems);
                _InventoryRect =
                    GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory]
                        .GetClientRect();
                _CellSize = _InventoryRect.Width / playerInventory.Columns;
                
                _SlotsWithStackedDecks = new Queue<ServerInventory.InventSlotItem>(playerInventory.InventorySlotItems.Where(slot =>
                    slot.Item.Path == "Metadata/Items/DivinationCards/DivinationCardDeck").ToList().OrderBy(x => x.InventoryPosition.X).ThenBy(x => x.InventoryPosition.Y).ToList());
            }   
            catch (Exception ex)
            {
                LogError($"{ex}");
            }
        }

        private IEnumerator PopTheStacks()
        {
            //DebugWindow.LogError("Test in PopTheStacks", 5);
            while(_SlotsWithStackedDecks.Count() > 0)
            {
                //DebugWindow.LogError($"PopTheStacks: {_SlotsWithStackedDecks.Peek()}", 5);
                yield return PopAStack(_SlotsWithStackedDecks.Dequeue());
                //yield return _Wait1ms;
            }
        }

        private IEnumerator PopAStack(ServerInventory.InventSlotItem item)
        {
            //DebugWindow.LogError("Test in PopAStack", 5);
            var invSlot = item.InventoryPosition; //initial invslot
            var openSlotPos = Point.Zero;
            var stacksize = item.Item.GetComponent<Stack>()?.Size ?? 0;
            var slotRectCenter = item.GetClientRect().Center;
            var cursorInv = GameController.Game.IngameState.ServerData.PlayerInventories[12].Inventory;
            while (stacksize > 0)
            {
                //check profile requirements
                if (!Settings.DropToGround && !Settings.DropToDivTab && !_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                {
                    DebugWindow.LogError(
                        "UnstackDecks => Inventory doesn't have space to place the next div card.");
                    StopCoroutine();
                    yield break;

                }
                else if (!areRequirementsMet())
                {
                    StopCoroutine();
                    yield break;
                }
                //right click the stackdeck stack
                yield return Input.SetCursorPositionAndClick(slotRectCenter, Settings.ReverseMouseButtons ? MouseButtons.Left : MouseButtons.Right, Settings.TimeBetweenClicks);
                //check if MouseInventory contains an item and waits for it
                yield return new WaitFunctionTimed(() => cursorInv.TotalItemsCounts == 1, true);

                Vector2 destination;
                if (Settings.DropToGround)
                {
                    destination = GameController.Window.GetWindowRectangle().Center;
                }
                else if (Settings.DropToDivTab)
                {
                    destination = GameController.Game.IngameState.IngameUi.StashElement.VisibleStash.GetClientRectCache.Center;
                }
                else
                {
                    //drop off the item from cursor at free inventory slot
                    destination = GetClientRectFromPoint(openSlotPos, 1, 1).Center;
                }
                yield return Input.SetCursorPositionAndClick(destination, Settings.ReverseMouseButtons ? MouseButtons.Right : MouseButtons.Left, Settings.TimeBetweenClicks);
                //wait for item on cursor to be dropped off
                yield return new WaitFunctionTimed(() => cursorInv.TotalItemsCounts == 0, true);
                openedDecksCounter++;
                if (!Settings.DropToGround && !Settings.DropToDivTab) yield return MarkSlotUsed(openSlotPos);
                //update item and the stacksize more safely
                //find the item by invslot
                item = GameController.IngameState.ServerData.PlayerInventories[0].Inventory.InventorySlotItems.ToList().Find(x => x.InventoryPosition == invSlot); //the item object is rebuilt completely by the game after removing a card from the stack
                yield return new WaitFunctionTimed(() => item.Item.HasComponent<Stack>() == true);  //the game doesnt seem to like it when you unstack too fast and gives ingame chat error messages. The caching of the stacksize and simply decrementing brought other problems
                if (!item.Item.HasComponent<Stack>())
                {
                    StopCoroutine();
                    yield break;
                }
                stacksize = item.Item.GetComponent<Stack>().Size;
            }
        }
        #region Helperfunctions
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
                _InventoryRect.X + pos.X + _CellSize * pos.X,
                _InventoryRect.Y + pos.Y + _CellSize * pos.Y,
                pos.X + width * _CellSize,
                pos.Y + height * _CellSize);
        }

        private IEnumerator MarkSlotUsed(Vector2 slotPosition)
        {
            _InventoryLayout.Fill(1, slotPosition);
            yield return _Wait1ms;
        }
        #endregion
    }
}