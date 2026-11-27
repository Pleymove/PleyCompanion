//
//  CompanionManager.NoRightControl.cs
//  OpenClicky (Windows only)
//
//  The optional tutorial can suggest another dictation key. Outside it, Settings always owns
//  the choice: on French keyboards Right Alt is AltGr and is needed to type ordinary characters.
//

using OpenClicky.Core.Shortcuts;

namespace OpenClicky.Core.Companion;

public sealed partial class CompanionManager
{
    private void OtherDictationKeyHeld(DictationHotkey key)
    {
        if (FirstRunTour.Step == FirstRunTourStep.Dictate) FirstRunTour.AdoptDictationKey(key);
    }
}
