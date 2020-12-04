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
        private int currDecksInInventory = 0;
        private int startDecksInInventory = 0;
        private int[,] _InventoryLayout;
        private Queue<ServerInventory.InventSlotItem> _SlotsWithStackedDecks;
        private RectangleF _InventoryRect;
        private float _CellSize;
        private readonly WaitTime _Wait1ms = new WaitTime(1);
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
            Settings.TurnInDivCardsHotkey.OnValueChanged += () =>
            {
                Input.RegisterKey(Settings.TurnInDivCardsHotkey);
                _SaveSettings();
            };
            return true;
        }

        public override Job Tick()
        {
            if (Settings.UnstackHotkey.PressedOnce())
            {
                if (Core.ParallelRunner.FindByName(Name) == null)
                {
                    StartCoroutine(Routines.OpenStackedDecksRoutine);
                }
                else 
                {
                    StopCoroutine(Name);
                }
            }
            else if (Settings.TurnInDivCardsHotkey.PressedOnce())
            {
                DebugWindow.LogMsg("Turn in Div Cards not yet implemented");
                return null;

                if (Core.ParallelRunner.FindByName("TurnInvDivCards") == null)
                {
                    StartCoroutine(Routines.DivCardTurnInRoutine);
                }
                else
                {
                    StopCoroutine("TurnInDivCards");
                }
            }
            return null;
        }

        private void StartCoroutine(Routines routine)
        {
            switch (routine)
            {
                case Routines.OpenStackedDecksRoutine:
                    _DebugTimer.Reset();
                    _DebugTimer.Start();
                    Core.ParallelRunner.Run(new Coroutine(UnstackDecksRoutine(), this, Name));
                    break;

                case Routines.DivCardTurnInRoutine:
                    _DebugTimer.Reset();
                    _DebugTimer.Start();
                    Core.ParallelRunner.Run(new Coroutine(TurnInDivCardsRoutine(), this, "TurnInDivCards"));
                    break;
            }
        }
       
        private IEnumerator UnstackDecksRoutine()
        {
            if (!areRequirementsMet())
            {
                StopCoroutine(Name);
                yield break;
            }
            int tries = 0;
            initStatistics();
            while (haveStackedDecks() && tries < 3)
            {
                ParseInventory();
                tries++;
                yield return PopTheStacks();
            }
            StopCoroutine(Name);
        }
        private IEnumerator TurnInDivCardsRoutine()
        {
            if (!areRequirementsMet())
            {
                StopCoroutine("TurnInDivCards");
                yield break;
            }
            int tries = 0;
            initStatistics();
            while (HaveDivCardSets() && tries < 3)
            {
                ParseInventory();
                tries++;
                yield return PopTheStacks();
            }
            StopCoroutine("TurnInDivCards");
        }

        private bool HaveDivCardSets()
        {
            return GameController.IngameState.ServerData.PlayerInventories[0].Inventory.Items.Where(x =>
                x.Metadata.StartsWith("Metadata/Items/DivinationCards/") &&
                !x.Metadata.StartsWith("Metadata/Items/DivinationCards/DivinationCardDeck") &&
                x.HasComponent<Stack>() &&
                (x.GetComponent<Stack>().Info.MaxStackSize == x.GetComponent<Stack>().Size)).ToList().Count > 0;
                // x.GetComponent<Stack>().FullStack).ToList().Count > 0;
        }

        private void initStatistics()
        {
            startDecksInInventory = GameController.Game.IngameState.ServerData.PlayerInventories[0].Inventory.Items.
                Where(x => x.Path == "Metadata/Items/DivinationCards/DivinationCardDeck").Sum(x => x.GetComponent<Stack>()?.Size ?? 0);
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
        /// <summary>
        /// Stops the Coroutine, Debugtimer and resets it and also Logs a Statistic Message containing Informations about opened Cards, total time spent and time spent per Unit.
        /// </summary>
        private void StopCoroutine(string routineName)
        {
            var routine = Core.ParallelRunner.FindByName(routineName);
            routine?.Done();
            _DebugTimer.Stop();
            LogStatistic();
            _DebugTimer.Reset();
        }

        private void LogStatistic()
        {
            ParseInventory();
            var openedDecks = startDecksInInventory - currDecksInInventory;
            if(openedDecks > 0)
            {
                LogMessage($"{openedDecks} were opened in {_DebugTimer.ElapsedMilliseconds} ms: {_DebugTimer.ElapsedMilliseconds / openedDecks} ms per Card at {Settings.TimeBetweenClicks.Value} ms between Clicks Setting", 10);
            }
        }

        /// <summary>
        /// Parses the Inventory for stacked Decks and marks used slots in the inventory
        /// </summary>
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
                currDecksInInventory = _SlotsWithStackedDecks.Sum(x => x.Item.GetComponent<Stack>()?.Size ?? 0);
            }   
            catch (Exception ex)
            {
                LogError($"{ex}");
            }
        }
        #region Unstack Stacked Decks
        private IEnumerator PopTheStacks()
        {
            while(_SlotsWithStackedDecks.Count() > 0)
            {
                yield return PopAStack(_SlotsWithStackedDecks.Dequeue());
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
            int latency = (int) GameController.IngameState.CurLatency;
            int maxWaitTime = Settings.MaxWatitTime.Value;
            while (stacksize > 0)
            {
                //check profile requirements
                if (!Settings.DropToGround && !Settings.DropToDivTab && !_InventoryLayout.GetNextOpenSlot(ref openSlotPos))
                {
                    DebugWindow.LogError(
                        "UnstackDecks => Inventory doesn't have space to place the next div card.");
                    StopCoroutine(Name);
                    yield break;

                }
                else if (!areRequirementsMet())
                {
                    LogError("Requirements not met!");
                    StopCoroutine(Name);
                    yield break;
                }
                //click the stackdeck stack
                yield return Input.SetCursorPositionAndClick(slotRectCenter, Settings.ReverseMouseButtons ? MouseButtons.Left : MouseButtons.Right, Settings.TimeBetweenClicks);

                //check if MouseInventory contains an item and waits for it
                yield return new WaitFunctionTimed(() => cursorInv.CountItems > 0, true, maxWaitTime);
                if (cursorInv.TotalItemsCounts == 0)
                {
                    //LogError("Cursorinventory not filled");
                    //StopCoroutine();
                    yield break;
                }
                    
                //click at the dropoff location
                yield return Input.SetCursorPositionAndClick(chooseDestination(openSlotPos), Settings.ReverseMouseButtons ? MouseButtons.Right : MouseButtons.Left, Settings.TimeBetweenClicks);

                //wait for item on cursor to be dropped off
                yield return new WaitFunctionTimed(() => cursorInv.CountItems == 0, true, maxWaitTime);
                if (cursorInv.TotalItemsCounts != 0)
                {
                    //LogError("Cursorinventory not empty");
                    //StopCoroutine();
                    yield break;
                }
                if (!Settings.DropToGround && !Settings.DropToDivTab) yield return MarkSlotUsed(openSlotPos);
                //update item and the stacksize more safely
                //find the item by invslot
                item = GameController.IngameState.ServerData.PlayerInventories[0].Inventory.InventorySlotItems.ToList().Find(x => x.InventoryPosition == invSlot); //the item object is rebuilt completely by the game after removing a card from the stack
                yield return new WaitFunctionTimed(() => item.Item.HasComponent<Stack>(), true, maxWaitTime);  //the game doesnt seem to like it when you unstack too fast and gives ingame chat error messages. The caching of the stacksize and simply decrementing brought other problems
                if (!item.Item.HasComponent<Stack>())
                {
                    //LogError("No Stack component of current item found");
                    //StopCoroutine();
                    yield break;
                }
                stacksize = item.Item.GetComponent<Stack>().Size;
            }
        }

        private void handlingFail()
        {
            throw new NotImplementedException();
        }

        private Vector2 chooseDestination(Point openSlotPos)
        {
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
            return destination;
        }
        #endregion
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

        /// <summary>
        /// Checks if any of the possible Coroutines of this Plugin area currently running in any shape or form
        /// </summary>
        /// <returns>boolean</returns>
        private bool AnyCoroutineRunning()
        {
            if (Core.ParallelRunner.FindByName(Name) != null) return true;
            if (Core.ParallelRunner.FindByName("TurnInDivCards") != null) return true;
            return false;
        }
        #endregion
    }
}