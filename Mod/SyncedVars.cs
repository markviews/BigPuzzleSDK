using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

    // Synced variables made in code, not placed in the scene:
    //     private readonly SyncedInt score = new SyncedInt("score");
    //     private readonly SyncedBool open = new SyncedBool("open");
    // Only the host changes them (calls on other players' PCs are ignored); everyone reads Value, and
    // Changed runs on every PC when the value changes (also for a player who joins later and finds a
    // different value). Make them as fields, when your script is created: every PC then makes them in the
    // same order, which is how each one is matched with the host's. The name is only for the log.

    internal interface ISyncedVar {
        bool Alive { get; }
        void Step();
    }

    internal static class SyncedVars {
        private static readonly List<ISyncedVar> all = new List<ISyncedVar>();
        private static bool warnedOutside;

        // Called every frame (see BigPuzzleEditor)
        internal static void Tick() {
            for (int i = all.Count - 1; i >= 0; i--) {
                ISyncedVar v = all[i];
                if (!v.Alive) { all.RemoveAt(i); continue; }
                v.Step();
            }
        }

        internal static void Add(ISyncedVar v) { all.Add(v); }

        // The puzzle a variable belongs to: the one being built (null outside a build)
        internal static Transform Parent(string name) {
            Transform root = PuzzleLoader.BuildRoot;
            if (root == null && !warnedOutside) {
                warnedOutside = true;
                Plugin.log.LogWarning($"[SyncedVars] '{name}' was made outside a puzzle's start-up, so it can't be synced. Make synced variables as fields of your script (private readonly SyncedInt x = new SyncedInt(\"x\");).");
            }
            return root;
        }

        internal static void Raise<T>(Action<T> handlers, T value, string name) {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList()) {
                try { ((Action<T>)d)(value); } catch (Exception ex) {
                    Exception inner = ex.InnerException ?? ex;
                    Plugin.log.LogWarning($"[SyncedVars] '{name}': a Changed handler failed: {inner.GetType().Name}: {inner.Message}");
                }
            }
        }
    }

    // A whole number every player sees (any int)
    public class SyncedInt {
        private readonly string name;
        private int value;
        private readonly SyncedLink link;

        public event Action<int> Changed;

        public SyncedInt(string name, int initialValue = 0) {
            this.name = name ?? "";
            value = initialValue;
            link = new SyncedLink("int", this.name, SyncedLink.Text(value));
            link.Arrived = OnArrived;
        }

        public int Value => value;

        // Host only (ignored on other players' PCs)
        public void Set(int newValue) {
            if (!PuzzleSDK.IsHost || newValue == value) return;
            value = newValue;
            link.Send(SyncedLink.Text(value));
            SyncedVars.Raise(Changed, value, name);
        }

        // Host only. A negative number takes away. Stops at the ends of the int range.
        public void Add(int amount) {
            if (!PuzzleSDK.IsHost) return;
            Set((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value + amount)));
        }

        private void OnArrived(string host) {
            int number;
            if (!int.TryParse(host, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number) || number == value) return;
            value = number;
            SyncedVars.Raise(Changed, value, name);
        }
    }

    // A true / false every player sees
    public class SyncedBool {
        private readonly string name;
        private bool value;
        private readonly SyncedLink link;

        public event Action<bool> Changed;

        public SyncedBool(string name, bool initialValue = false) {
            this.name = name ?? "";
            value = initialValue;
            link = new SyncedLink("bool", this.name, initialValue ? "1" : "0");
            link.Arrived = OnArrived;
        }

        public bool Value => value;

        // Host only (ignored on other players' PCs)
        public void Set(bool newValue) {
            if (!PuzzleSDK.IsHost || newValue == value) return;
            value = newValue;
            link.Send(value ? "1" : "0");
            SyncedVars.Raise(Changed, value, name);
        }

        private void OnArrived(string host) {
            bool wanted = host == "1";
            if (wanted == value) return;
            value = wanted;
            SyncedVars.Raise(Changed, value, name);
        }
    }

    // A function call every player runs: the host calls Fire, and Fired runs on every PC (the host's too).
    // Nothing is saved: a player who joins later does not run events from before they arrived. Fire can
    // carry one whole number.
    // Fire on another player's PC does not run Fired: it tells the host, which runs FiredFromClient (with who
    // fired it). If nothing on the host listens for that, it is ignored.
    public class SyncedEvent {
        private readonly string name;
        private readonly SyncedLink link;

        public event Action<int> Fired;

        // Host only: a player fired the event on their PC. Gives who it was, and the number.
        public event Action<PuzzlePlayer, int> FiredFromClient;

        public SyncedEvent(string name) {
            this.name = name ?? "";
            link = new SyncedLink("event", this.name, "", true);
            link.Arrived = OnArrived;
            link.FromClient = OnFromClient;
        }

        // On the host: runs Fired here and on every other PC. On another player's PC: tells the host
        // (FiredFromClient there); Fired does not run.
        public void Fire(int argument = 0) {
            if (!PuzzleSDK.IsHost) { link.FireToHost(SyncedLink.Text(argument)); return; }
            link.Fire(SyncedLink.Text(argument));
            SyncedVars.Raise(Fired, argument, name);
        }

        // Host: a player fired the event
        private void OnFromClient(PuzzlePlayer player, string argument) {
            if (FiredFromClient == null) return;
            int number;
            if (!int.TryParse(argument, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)) return;
            foreach (Delegate d in FiredFromClient.GetInvocationList()) {
                try { ((Action<PuzzlePlayer, int>)d)(player, number); } catch (Exception ex) {
                    Exception inner = ex.InnerException ?? ex;
                    Plugin.log.LogWarning($"[SyncedEvent] '{name}': a FiredFromClient handler failed: {inner.GetType().Name}: {inner.Message}");
                }
            }
        }

        // Other players: the host fired
        private void OnArrived(string argument) {
            int number;
            if (!int.TryParse(argument, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)) number = 0;
            SyncedVars.Raise(Fired, number, name);
        }
    }

    // How an event's argument travels as text
    internal static class EventArgument<T> {
        internal static readonly bool Supported = typeof(T) == typeof(int) || typeof(T) == typeof(float)
            || typeof(T) == typeof(bool) || typeof(T) == typeof(string) || typeof(T) == typeof(Vector3)
            || typeof(T) == typeof(Vector2) || typeof(T) == typeof(Quaternion) || typeof(T) == typeof(PuzzlePlayer);

        private static string F(float value) { return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

        // "1.5,2,3" -> numbers; null if there are not exactly 'count' of them
        private static float[] Numbers(string text, int count) {
            string[] part = (text ?? "").Split(',');
            if (part.Length != count) return null;
            var numbers = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(part[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])) return null;
            return numbers;
        }

        internal static string Encode(T value) {
            object o = value;
            if (o is int) return ((int)o).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (o is float) return ((float)o).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (o is bool) return (bool)o ? "1" : "0";
            if (o is Vector3) { Vector3 v = (Vector3)o; return F(v.x) + "," + F(v.y) + "," + F(v.z); }
            if (o is Vector2) { Vector2 v = (Vector2)o; return F(v.x) + "," + F(v.y); }
            if (o is Quaternion) { Quaternion q = (Quaternion)o; return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
            if (typeof(T) == typeof(PuzzlePlayer)) { PuzzlePlayer p = o as PuzzlePlayer; return p != null ? p.id : ""; }
            bool cut;
            return SyncedText.Fit(o as string, out cut);
        }

        internal static bool Decode(string text, out T value) {
            object o = null;
            if (typeof(T) == typeof(int)) {
                int i;
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out i)) o = i;
            } else if (typeof(T) == typeof(float)) {
                float f;
                if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) o = f;
            } else if (typeof(T) == typeof(bool)) {
                o = text == "1";
            } else if (typeof(T) == typeof(string)) {
                o = text ?? "";
            } else if (typeof(T) == typeof(Vector3)) {
                float[] n = Numbers(text, 3);
                if (n != null) o = new Vector3(n[0], n[1], n[2]);
            } else if (typeof(T) == typeof(Vector2)) {
                float[] n = Numbers(text, 2);
                if (n != null) o = new Vector2(n[0], n[1]);
            } else if (typeof(T) == typeof(Quaternion)) {
                float[] n = Numbers(text, 4);
                if (n != null) o = new Quaternion(n[0], n[1], n[2], n[3]);
            } else if (typeof(T) == typeof(PuzzlePlayer)) {
                // The player with that id on this PC; none if they are not in the list (the event still runs)
                PuzzlePlayer found = null;
                if (!string.IsNullOrEmpty(text))
                    foreach (PuzzlePlayer p in PlayerRoster.View) if (p.id == text) { found = p; break; }
                value = (T)(object)found;
                return true;
            }
            if (o == null) { value = default(T); return false; }
            value = (T)o;
            return true;
        }
    }

    // A function call every player runs, with one value of type T (int, float, bool, string, Vector3, Vector2, Quaternion or PuzzlePlayer).
    // Works like SyncedEvent: the host calls Fire and Fired runs on every PC; a player's Fire tells the host, which
    // runs FiredFromClient. Text can be up to 16 KB.
    public class SyncedEvent<T> {
        private readonly string name;
        private readonly SyncedLink link;

        public event Action<T> Fired;

        // Host only: a player fired the event on their PC
        public event Action<PuzzlePlayer, T> FiredFromClient;

        public SyncedEvent(string name) {
            this.name = name ?? "";
            if (!EventArgument<T>.Supported)
                Plugin.log.LogWarning($"[SyncedEvent] '{this.name}': SyncedEvent<{typeof(T).Name}> is not supported; use int, float, bool, string, Vector3, Vector2, Quaternion or PuzzlePlayer.");
            link = new SyncedLink("event", this.name, "", true);
            link.Arrived = OnArrived;
            link.FromClient = OnFromClient;
        }

        // On the host: runs Fired here and on every other PC. On another player's PC: tells the host
        // (FiredFromClient there); Fired does not run.
        public void Fire(T argument = default(T)) {
            if (!EventArgument<T>.Supported) return;
            string text = EventArgument<T>.Encode(argument);
            if (!PuzzleSDK.IsHost) { link.FireToHost(text); return; }
            T sent;
            if (!EventArgument<T>.Decode(text, out sent)) return;   // what the others get, so everyone sees the same
            link.Fire(text);
            SyncedVars.Raise(Fired, sent, name);
        }

        private void OnArrived(string text) {
            T value;
            if (EventArgument<T>.Decode(text, out value)) SyncedVars.Raise(Fired, value, name);
        }

        private void OnFromClient(PuzzlePlayer player, string text) {
            if (FiredFromClient == null) return;
            T value;
            if (!EventArgument<T>.Decode(text, out value)) return;
            foreach (Delegate d in FiredFromClient.GetInvocationList()) {
                try { ((Action<PuzzlePlayer, T>)d)(player, value); } catch (Exception ex) {
                    Exception inner = ex.InnerException ?? ex;
                    Plugin.log.LogWarning($"[SyncedEvent] '{name}': a FiredFromClient handler failed: {inner.GetType().Name}: {inner.Message}");
                }
            }
        }
    }

    // ----------------------------------------------------------------------
    // Text and dictionaries. The host keeps the value in memory and sends it to the players in a network message of
    // its own, and again to anyone who joins later, so there are no hidden network states and no size to choose
    // (a value can be up to 16 KB). Like the numbers above, a variable is told apart from the others by the order
    // the puzzle's scripts make them in, which is the same on every PC.
    // ----------------------------------------------------------------------
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

    // What every PC knows: the latest value of every variable, and who is listening for it
    internal static class SyncedStore {
        private const string Tag = "sv";

        private class Stored { public string name, value; }
        private static readonly Dictionary<string, Stored> values = new Dictionary<string, Stored>();
        private static readonly Dictionary<string, SyncedLink> live = new Dictionary<string, SyncedLink>();
        private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
        private static readonly HashSet<int> synced = new HashSet<int>();   // host: connections that have been sent everything
        private static float nextCheck;
        private static bool warnedBad;

        // The loader is about to build a puzzle: its variables are numbered from 0 again
        internal static void BeginPuzzle(string packId) {
            if (packId == null) return;
            counters[packId] = 0;
            string livePrefix = packId + "#";
            var gone = new List<string>();
            foreach (string key in live.Keys) if (key.StartsWith(livePrefix, StringComparison.Ordinal)) gone.Add(key);
            foreach (string key in gone) live.Remove(key);
            if (!PuzzleSDK.IsHost) return;   // other players keep what they were told: the host sends the new values
            string prefix = packId + "#";
            var stale = new List<string>();
            foreach (string key in values.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(key);
            foreach (string key in stale) values.Remove(key);
        }

        // The next variable of the puzzle being built (null when none is being built)
        internal static string NextKey() {
            string pack = PuzzleLoader.BuildPackId;
            if (pack == null) return null;
            counters.TryGetValue(pack, out int n);
            counters[pack] = n + 1;
            return pack + "#" + n;
        }

        internal static void Add(SyncedLink link, string key, string name, string initial, bool isEvent) {
            live[key] = link;
            if (PuzzleSDK.IsHost && !isEvent) values[key] = new Stored { name = name, value = initial };
        }

        internal static bool TryGet(string key, out string value) {
            if (values.TryGetValue(key, out Stored stored)) { value = stored.value; return true; }
            value = null;
            return false;
        }

        // Host: a variable has a new value
        internal static void Host(string key, string name, string value) {
            values[key] = new Stored { name = name, value = value };
            Broadcast(key, name, value);
        }

        // Host: an event was fired. Not saved, so only the players here right now hear it.
        internal static void HostEvent(string key, string argument) {
            if (!NetworkServer.active || synced.Count == 0) return;
            try {
                string text = "ev|" + Uri.EscapeDataString(key) + "|" + Uri.EscapeDataString(argument ?? "");
                foreach (var pair in NetworkServer.connections) {
                    if (pair.Value == null || !synced.Contains(pair.Key)) continue;
                    PlayerCommands.SendText(pair.Value, text);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Synced] could not send an event: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Player -> host: an event fired on a player's PC
        internal static void ClientEvent(string key, string argument) {
            PlayerCommands.SendToHost("ce|" + Uri.EscapeDataString(key) + "|" + Uri.EscapeDataString(argument ?? ""));
        }

        private static readonly Dictionary<int, int> clientBurst = new Dictionary<int, int>();
        private static float clientBurstStart;

        // Host: a player's PC sent an event ("ce|key|argument")
        internal static void OnClientEvent(int connectionId, string text) {
            if (!SdkHandshake.HasSameVersion(connectionId)) return;
            // A modified game could flood us: at most 30 a second from one player
            if (Time.unscaledTime - clientBurstStart >= 1f) { clientBurst.Clear(); clientBurstStart = Time.unscaledTime; }
            clientBurst.TryGetValue(connectionId, out int sent);
            if (sent >= 30) return;
            clientBurst[connectionId] = sent + 1;

            string[] part = text.Split('|');
            if (part.Length < 3) return;
            SyncedLink link;
            if (!live.TryGetValue(Uri.UnescapeDataString(part[1]), out link)) return;
            PuzzlePlayer sender = null;
            foreach (PuzzlePlayer player in PlayerRoster.View) {
                PlayerNetworking pn = player.Networking;
                NetworkConnectionToClient conn = pn != null ? pn.connectionToClient : null;
                if (conn != null && conn.connectionId == connectionId) { sender = player; break; }
            }
            if (sender == null) return;
            link.ReceiveFromClient(sender, Uri.UnescapeDataString(part[2]));
        }

        // Host: tell every player who has been sent everything before
        internal static void Broadcast(string key, string name, string value) {
            if (!NetworkServer.active || synced.Count == 0) return;
            try {
                string text = Pack(key, name, value);
                foreach (var pair in NetworkServer.connections) {
                    if (pair.Value == null || !synced.Contains(pair.Key)) continue;
                    PlayerCommands.SendText(pair.Value, text);
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Synced] could not send a value: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string Pack(string key, string name, string value) {
            return Tag + "|" + Uri.EscapeDataString(key) + "|" + Uri.EscapeDataString(name) + "|"
                + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? ""));
        }

        // Host: a player who has the SDK (the same version) gets everything once, as soon as they can take it;
        // after that they get every change as it happens
        internal static void Tick() {
            if (!NetworkServer.active) { if (synced.Count > 0) synced.Clear(); return; }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            try {
                var present = new HashSet<int>();
                var local = NetworkServer.localConnection;
                foreach (var pair in NetworkServer.connections) {
                    NetworkConnectionToClient conn = pair.Value;
                    if (conn == null || (local != null && conn.Pointer == local.Pointer)) continue;
                    int id = conn.connectionId;
                    present.Add(id);
                    // Not answered (yet, or again after a reload): they will be sent everything once they have
                    if (!SdkHandshake.HasSameVersion(id)) { synced.Remove(id); continue; }
                    if (!synced.Add(id)) continue;
                    foreach (KeyValuePair<string, Stored> entry in values)
                        PlayerCommands.SendText(conn, Pack(entry.Key, entry.Value.name, entry.Value.value));
                }
                synced.RemoveWhere(id => !present.Contains(id));
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Synced] {ex.GetType().Name}: {ex.Message}");
                nextCheck = Time.unscaledTime + 5f;
            }
        }

        // Other players: the host sent a value ("sv|key|name|value")
        internal static void OnMessage(string text) {
            try {
                string[] part = text.Split('|');
                if (part[0] == "ev") {
                    if (part.Length < 3) return;
                    SyncedLink fired;
                    if (live.TryGetValue(Uri.UnescapeDataString(part[1]), out fired)) fired.ReceiveEvent(Uri.UnescapeDataString(part[2]));
                    return;
                }
                if (part.Length < 4) return;
                string key = Uri.UnescapeDataString(part[1]);
                string name = Uri.UnescapeDataString(part[2]);
                string value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(part[3]));
                values[key] = new Stored { name = name, value = value };
                if (live.TryGetValue(key, out SyncedLink link)) link.Receive(name, value);
            } catch (Exception ex) {
                if (!warnedBad) { warnedBad = true; Plugin.log.LogWarning($"[Synced] could not read a value from the host: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

    // One synced text on this PC. SyncedString and SyncedDict hold the text; this keeps it in step with the host.
    internal sealed class SyncedLink : ISyncedVar {
        private readonly string name;
        private readonly string key;        // null: made outside a puzzle's build, so it only lives on this PC
        private readonly Transform owner;   // the puzzle it belongs to
        private string current;
        private int steps;
        private bool warnedName;
        private readonly bool isEvent;      // an event is never saved, only sent

        // Other players: the host's text changed (or, for an event, its argument arrived)
        internal Action<string> Arrived;

        internal static string Text(int number) { return number.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        internal SyncedLink(string kind, string name, string initial, bool isEvent = false) {
            this.isEvent = isEvent;
            this.name = name;
            current = initial ?? "";
            owner = SyncedVars.Parent(kind + " " + name);
            if (owner == null) return;
            key = SyncedStore.NextKey();
            if (key == null) return;
            SyncedStore.Add(this, key, name, current, isEvent);
            SyncedVars.Add(this);
        }

        // Host: this is the new text
        internal void Send(string value) {
            current = value;
            if (key != null) SyncedStore.Host(key, name, value);
        }

        // Host: the event fired with this argument
        internal void Fire(string argument) {
            if (key != null) SyncedStore.HostEvent(key, argument);
        }

        // Other players: the host fired this event
        internal void ReceiveEvent(string argument) {
            if (Arrived != null) Arrived(argument);
        }

        // Player: fire this event at the host
        internal void FireToHost(string argument) {
            if (key != null) SyncedStore.ClientEvent(key, argument);
        }

        // Host: a player fired this event
        internal Action<PuzzlePlayer, string> FromClient;
        internal void ReceiveFromClient(PuzzlePlayer player, string argument) {
            if (FromClient != null) FromClient(player, argument);
        }

        // Other players: the host sent a value for this variable
        internal void Receive(string hostName, string value) {
            if (hostName != name && !warnedName) {
                warnedName = true;
                Plugin.log.LogWarning($"[Synced] '{name}' here is '{hostName}' on the host: the puzzle's synced variables are not made in the same order on every PC.");
            }
            Apply(value);
        }

        private void Apply(string value) {
            if (steps < 2 || value == current) return;   // before that, the next step takes the value
            current = value;
            if (Arrived != null) Arrived(value);
        }

        bool ISyncedVar.Alive { get { return owner != null; } }

        void ISyncedVar.Step() {
            steps++;
            if (steps != 2 || key == null || isEvent) return;   // the first frame lets the script set up its handlers
            if (PuzzleSDK.IsHost) { SyncedStore.Broadcast(key, name, current); return; }
            if (SyncedStore.TryGet(key, out string stored)) Apply(stored);
        }
    }

    // A piece of text every player sees (up to 16 KB).
    public class SyncedString {
        private readonly string name;
        private string value;
        private readonly SyncedLink link;
        private bool warnedCut;

        public event Action<string> Changed;

        public SyncedString(string name, string initialValue = "") {
            this.name = name ?? "";
            value = SyncedText.Fit(initialValue, out bool cut);
            link = new SyncedLink("string", this.name, value);
            link.Arrived = OnArrived;
        }

        public string Value => value;

        // Host only (ignored on other players' PCs). Text longer than 16 KB is cut off.
        public void Set(string newValue) {
            if (!PuzzleSDK.IsHost) return;
            string fit = SyncedText.Fit(newValue, out bool cut);
            if (cut && !warnedCut) {
                warnedCut = true;
                Plugin.log.LogWarning($"[SyncedString] '{name}': the text is longer than {SyncedText.MaxBytes} bytes; it was cut off.");
            }
            if (fit == value) return;
            value = fit;
            link.Send(fit);
            SyncedVars.Raise(Changed, value, name);
        }

        private void OnArrived(string host) {
            if (host == value) return;
            value = host;
            SyncedVars.Raise(Changed, value, name);
        }
    }

    // Text keys and text values every player sees (up to 16 KB in all).
    public class SyncedDict {
        private readonly string name;
        private SortedDictionary<string, string> items = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private readonly SyncedLink link;
        private bool warnedFull;

        // Runs on every PC with the key that was added, changed or removed
        public event Action<string> Changed;

        public SyncedDict(string name) {
            this.name = name ?? "";
            link = new SyncedLink("dict", this.name, "");
            link.Arrived = OnArrived;
        }

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

        // Host only (ignored on other players' PCs). False if it was not stored: not the host, the key is empty, or
        // there is no room left (16 KB for everything together).
        public bool Set(string key, string value) {
            if (!PuzzleSDK.IsHost || string.IsNullOrEmpty(key)) return false;
            value = value ?? "";
            string old;
            if (items.TryGetValue(key, out old) && old == value) return true;
            var next = new SortedDictionary<string, string>(items, StringComparer.Ordinal);
            next[key] = value;
            if (!Commit(next)) return false;
            SyncedVars.Raise(Changed, key, name);
            return true;
        }

        // Host only. False if the key was not there.
        public bool Remove(string key) {
            if (!PuzzleSDK.IsHost || key == null || !items.ContainsKey(key)) return false;
            var next = new SortedDictionary<string, string>(items, StringComparer.Ordinal);
            next.Remove(key);
            if (!Commit(next)) return false;
            SyncedVars.Raise(Changed, key, name);
            return true;
        }

        // Host only
        public void Clear() {
            if (!PuzzleSDK.IsHost || items.Count == 0) return;
            var removed = new List<string>(items.Keys);
            items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            link.Send("");
            foreach (string key in removed) SyncedVars.Raise(Changed, key, name);
        }

        // key=value pairs joined with &, each side escaped, so any text can be a key or a value
        private static string Serialize(SortedDictionary<string, string> entries) {
            var text = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, string> entry in entries) {
                if (text.Length > 0) text.Append('&');
                text.Append(Uri.EscapeDataString(entry.Key)).Append('=').Append(Uri.EscapeDataString(entry.Value));
            }
            return text.ToString();
        }

        private static SortedDictionary<string, string> Parse(string text) {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string pair in text.Split('&')) {
                int split = pair.IndexOf('=');
                if (split <= 0) continue;
                result[Uri.UnescapeDataString(pair.Substring(0, split))] = Uri.UnescapeDataString(pair.Substring(split + 1));
            }
            return result;
        }

        // Host: keep 'next' as the contents if it fits
        private bool Commit(SortedDictionary<string, string> next) {
            string text = Serialize(next);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > SyncedText.MaxBytes) {
                if (!warnedFull) {
                    warnedFull = true;
                    Plugin.log.LogWarning($"[SyncedDict] '{name}': that would take more than {SyncedText.MaxBytes} bytes in all, so it was not stored.");
                }
                return false;
            }
            items = next;
            link.Send(text);
            return true;
        }

        // Other players: the host's contents changed. Changed runs for each key that is new, different or gone.
        private void OnArrived(string host) {
            SortedDictionary<string, string> next = Parse(host);
            var changed = new List<string>();
            foreach (KeyValuePair<string, string> entry in next) {
                string old;
                if (!items.TryGetValue(entry.Key, out old) || old != entry.Value) changed.Add(entry.Key);
            }
            foreach (string key in items.Keys) if (!next.ContainsKey(key)) changed.Add(key);
            items = next;
            foreach (string key in changed) SyncedVars.Raise(Changed, key, name);
        }
    }

} // namespace BigPuzzleSDK