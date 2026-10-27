using System;
using System.Collections.Generic;
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

// A whole number every player sees (any int)
public class SyncedInt {
    private int value;

    public event Action<int> Changed;

    public SyncedInt(string name, int initialValue = 0) {
        value = initialValue;
    }

    public int Value => value;

    // Host only.
    public void Set(int newValue) {
        if (newValue == value) return;
        value = newValue;
        if (Changed != null) Changed(value);
    }
    // Host only. A negative number takes away. Stops at the ends of the int range.
    public void Add(int amount) { Set((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value + amount))); }
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
// carry one whole number. In the Unity editor it just runs here.
// In the game, Fire on another player's PC does not run Fired: it tells the host, which runs FiredFromClient
// (with who fired it). In the Unity editor you are always the host, so FiredFromClient never runs there.
public class SyncedEvent {
    public event Action<int> Fired;

    // Host only: a player fired the event on their PC
    public event Action<PuzzlePlayer, int> FiredFromClient;

    public SyncedEvent(string name) { }

    // Host only
    public void Fire(int argument = 0) {
        if (Fired != null) Fired(argument);
    }
}

// The same, with a value of type T (int, float, bool, string, Vector3, Vector2, Quaternion or PuzzlePlayer) instead of a whole number. Text can be up to 16 KB.
// In the Unity editor it just runs here.
public class SyncedEvent<T> {
    public event Action<T> Fired;

    // Host only: a player fired the event on their PC (never runs in the Unity editor, where you are the host)
    public event Action<PuzzlePlayer, T> FiredFromClient;

    public SyncedEvent(string name) {
        Type type = typeof(T);
        if (type != typeof(int) && type != typeof(float) && type != typeof(bool) && type != typeof(string)
                && type != typeof(Vector3) && type != typeof(Vector2) && type != typeof(Quaternion) && type != typeof(PuzzlePlayer))
            Debug.LogWarning("[SyncedEvent] SyncedEvent<" + type.Name + "> is not supported; use int, float, bool, string, Vector3, Vector2, Quaternion or PuzzlePlayer.");
    }

    public void Fire(T argument = default(T)) {
        if (Fired != null) Fired(argument);
    }
}

// Text up to 16 KB is kept by the host and sent to everyone, so there is no size to choose.
internal static class SyncedText {
    internal const int MaxBytes = 16384;

    // The longest start of 'text' that fits in MaxBytes of UTF-8
    internal static string Fit(string text, out bool cut) {
        cut = false;
        if (string.IsNullOrEmpty(text)) return "";
        if (System.Text.Encoding.UTF8.GetByteCount(text) <= MaxBytes) return text;
        var result = new System.Text.StringBuilder();
        int bytes = 0;
        for (int i = 0; i < text.Length; i++) {
            int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            string piece = text.Substring(i, length);
            i += length - 1;
            int size = System.Text.Encoding.UTF8.GetByteCount(piece);
            if (bytes + size > MaxBytes) break;
            result.Append(piece);
            bytes += size;
        }
        cut = true;
        return result.ToString();
    }
}

// A piece of text every player sees (up to 16 KB). In the Unity editor it is a plain local value, so you can test
// your logic.
public class SyncedString {
    private string value;

    public event Action<string> Changed;

    public SyncedString(string name, string initialValue = "") {
        value = SyncedText.Fit(initialValue, out bool cut);
    }

    public string Value => value;

    // Host only. Text longer than 16 KB is cut off.
    public void Set(string newValue) {
        string fit = SyncedText.Fit(newValue, out bool cut);
        if (cut) Debug.LogWarning("[SyncedString] The text is longer than " + SyncedText.MaxBytes + " bytes; it was cut off.");
        if (fit == value) return;
        value = fit;
        if (Changed != null) Changed(value);
    }
}

// Text keys and text values every player sees (up to 16 KB in all). In the Unity editor it is a plain local
// dictionary, so you can test your logic.
public class SyncedDict {
    private SortedDictionary<string, string> items = new SortedDictionary<string, string>(StringComparer.Ordinal);

    // Runs with the key that was added, changed or removed
    public event Action<string> Changed;

    public SyncedDict(string name) { }

    public int Count => items.Count;
    public IReadOnlyCollection<string> Keys => new List<string>(items.Keys);

    public bool ContainsKey(string key) { return key != null && items.ContainsKey(key); }

    public bool TryGetValue(string key, out string value) {
        string found;
        if (key != null && items.TryGetValue(key, out found)) { value = found; return true; }
        value = "";
        return false;
    }

    public string Get(string key, string fallback = "") {
        string found;
        return key != null && items.TryGetValue(key, out found) ? found : fallback;
    }

    // Host only. False if it was not stored: the key is empty, or there is no room left.
    public bool Set(string key, string value) {
        if (string.IsNullOrEmpty(key)) return false;
        value = value ?? "";
        string old;
        if (items.TryGetValue(key, out old) && old == value) return true;
        var next = new SortedDictionary<string, string>(items, StringComparer.Ordinal);
        next[key] = value;
        if (!Commit(next)) return false;
        if (Changed != null) Changed(key);
        return true;
    }

    // Host only. False if the key was not there.
    public bool Remove(string key) {
        if (key == null || !items.ContainsKey(key)) return false;
        var next = new SortedDictionary<string, string>(items, StringComparer.Ordinal);
        next.Remove(key);
        if (!Commit(next)) return false;
        if (Changed != null) Changed(key);
        return true;
    }

    // Host only
    public void Clear() {
        if (items.Count == 0) return;
        var removed = new List<string>(items.Keys);
        items = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in removed) if (Changed != null) Changed(key);
    }

    private bool Commit(SortedDictionary<string, string> next) {
        int bytes = 0;
        foreach (KeyValuePair<string, string> entry in next)
            bytes += System.Text.Encoding.UTF8.GetByteCount(Uri.EscapeDataString(entry.Key)) + System.Text.Encoding.UTF8.GetByteCount(Uri.EscapeDataString(entry.Value)) + 2;
        if (bytes > SyncedText.MaxBytes) {
            Debug.LogWarning("[SyncedDict] That would take more than " + SyncedText.MaxBytes + " bytes in all, so it was not stored.");
            return false;
        }
        items = next;
        return true;
    }
}

}
