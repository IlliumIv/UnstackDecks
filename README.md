# UnstackDecks

Scans your inventory for stacks of Stacked Deck items and unstacks them into Divination cards. 

## Usage

Press the hotkey you assigned to trigger the unstacking of Stacked Decks. As long as you have a location in your inventory where a div card can be dropped, it will unstack.

## Known issues

If you find the mouse drifting to a corner of the screen and won't release control, then kill the hud process through task manager and let me know it's still happening.

## For other developers

Feel free to yoink the code or reappropriate the idea of unstacking inventory items. If you do so, let me know so I can redirect this repository to your project.

## Settings

* Enable - Determines whether to listen for hotkey presses.
* UnstackHotkey - The hot key that triggers unstacking.
* TimeBetweenClicks - A delay between unstacking and stacking div cards.
* MouseSpeed = The speed the cursor moves around the screen. (Not tested)
* PreserveOriginalCursorPosition - Moves the cursor back to its original location when operations are completed.
* ReverseMouseButtons - Flips the mouse buttons so left-click unstacks and right-click drops the div card. For left-handed mouse users.
