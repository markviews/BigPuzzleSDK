using System;
using UnityEngine;

namespace BigPuzzleSDK {

// Synced variables made in code, not placed in the scene. Make them as fields of your script, when it
// is created:
//     private readonly SyncedInt score = new SyncedInt("score");
//     private readonly SyncedBool open = new SyncedBool("open");
// In-game (with the BigPuzzleSDK mod) the host changes them (calls on other players' PCs are ignored),
// everyone reads Value, and Changed runs on every PC when the value changes, also for a player who
// joins later and finds a different value. The name is only for the log. In the Unity editor they
// are plain local values, so you can test your logic.

// A whole number every player sees (-8,388,608 to 8,388,607)
public class SyncedInt {
    private int value;

    public event Action<int> Changed;

    public SyncedInt(string name, int initialValue = 0) {
        value = Mathf.Clamp(initialValue, -8388608, 8388607);
    }

    public int Value => value;

    // Host only. Kept between -8,388,608 and 8,388,607.
    public void Set(int newValue) {
        newValue = Mathf.Clamp(newValue, -8388608, 8388607);
        if (newValue == value) return;
        value = newValue;
        if (Changed != null) Changed(value);
    }
    // Host only. A negative number takes away.
    public void Add(int amount) { Set((int)Math.Max(-8388608L, Math.Min(8388607L, (long)value + amount))); }
}

// A true / false every player sees
public class SyncedBool {
    private bool value;

    public event Action<bool> Changed;

    public SyncedBool(string name, bool initialValue = false) {
        value = initialValue;
    }

    public bool Value => value;

    // Host only
    public void Set(bool newValue) {
        if (newValue == value) return;
        value = newValue;
        if (Changed != null) Changed(value);
    }
}

// A function call every player runs: the host calls Fire, and Fired runs on every PC (the host's too).
// Nothing is saved: a player who joins later does not run events from before they arrived. Fire can
// carry one small number (-128 to 127). In the Unity editor it just runs here.
public class SyncedEvent {
    public event Action<int> Fired;

    public SyncedEvent(string name) { }

    // Host only
    public void Fire(int argument = 0) {
        if (Fired != null) Fired(Mathf.Clamp(argument, -128, 127));
    }
}

}
