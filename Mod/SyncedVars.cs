using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigPuzzleSDK {

    // Synced variables made in code, not placed in the scene:
    //     private readonly SyncedInt score = new SyncedInt("score");
    //     private readonly SyncedBool open = new SyncedBool("open");
    // Only the host changes them (calls on other players' PCs are ignored); everyone reads Value, and
    // Changed runs on every PC when the value changes (also for a player who joins later and finds a
    // different value). Make them as fields, when your script is created: every PC then makes them in the
    // same order, which is how they get the same network ids. The name is only for the log.

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

        // Where a variable's hidden states go: the puzzle being built (null outside a build)
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

    // A whole number every player sees (-8,388,608 to 8,388,607)
    public class SyncedInt : ISyncedVar {
        private const int Limit = 8388607;
        private const float SteadySeconds = 0.1f;

        private readonly string name;
        private int value;
        private readonly SyncedValue[] channels = new SyncedValue[3];
        private int steps;
        private int pending = int.MinValue;
        private float pendingSince;

        public event Action<int> Changed;

        public SyncedInt(string name, int initialValue = 0) {
            this.name = name ?? "";
            value = Mathf.Clamp(initialValue, -Limit - 1, Limit);
            Transform parent = SyncedVars.Parent(this.name);
            if (parent == null) return;
            for (int i = 0; i < channels.Length; i++) {
                var c = new SyncedValue(parent, "synced int " + this.name + " " + i);
                if (!c.Ready) { channels[0] = null; Plugin.log.LogWarning($"[SyncedInt] '{this.name}': could not create its network state; it only changes on this PC."); return; }
                channels[i] = c;
                c.RegisterSoon();
            }
            SyncedVars.Add(this);
        }

        public int Value => value;

        // Host only (ignored on other players' PCs). Kept between -8,388,608 and 8,388,607.
        public void Set(int newValue) {
            if (!PuzzleSDK.IsHost) return;
            newValue = Mathf.Clamp(newValue, -Limit - 1, Limit);
            if (newValue == value) return;
            value = newValue;
            if (steps >= 2) Send();
            SyncedVars.Raise(Changed, value, name);
        }

        public void Add(int amount) {
            if (!PuzzleSDK.IsHost) return;
            Set((int)Math.Max(-Limit - 1L, Math.Min(Limit, (long)value + amount)));
        }

        bool ISyncedVar.Alive { get { return channels[0] != null && channels[0].Alive; } }

        void ISyncedVar.Step() {
            steps++;
            if (steps < 2) return;   // the first frame lets the ids get registered
            if (PuzzleSDK.IsHost) {
                if (steps == 2) Send();
                return;
            }
            Receive();
        }

        private void Send() {
            long u = (long)value + Limit + 1;   // 0 .. 16,777,215
            for (int i = 0; i < 3; i++) channels[i].Push((int)((u >> (8 * i)) & 255) - 128);
        }

        // Other players: take the host's number once all three parts agree for a moment
        private void Receive() {
            long u = 0;
            for (int i = 0; i < 3; i++) {
                if (!channels[i].TryGet(out int part)) return;   // nothing from the host yet
                u |= (long)(part + 128) << (8 * i);
            }
            int host = (int)(u - Limit - 1);
            if (host == value) { pending = int.MinValue; return; }
            if (host != pending) { pending = host; pendingSince = Time.time; return; }
            if (Time.time - pendingSince < SteadySeconds) return;
            pending = int.MinValue;
            value = host;
            SyncedVars.Raise(Changed, value, name);
        }
    }

    // A true / false every player sees
    public class SyncedBool : ISyncedVar {
        private readonly string name;
        private bool value;
        private readonly SyncedValue channel;
        private int steps;

        public event Action<bool> Changed;

        public SyncedBool(string name, bool initialValue = false) {
            this.name = name ?? "";
            value = initialValue;
            Transform parent = SyncedVars.Parent(this.name);
            if (parent == null) return;
            var c = new SyncedValue(parent, "synced bool " + this.name);
            if (!c.Ready) { Plugin.log.LogWarning($"[SyncedBool] '{this.name}': could not create its network state; it only changes on this PC."); return; }
            channel = c;
            c.RegisterSoon();
            SyncedVars.Add(this);
        }

        public bool Value => value;

        // Host only (ignored on other players' PCs)
        public void Set(bool newValue) {
            if (!PuzzleSDK.IsHost || newValue == value) return;
            value = newValue;
            if (channel != null && steps >= 2) channel.Push(value ? 1 : 0);
            SyncedVars.Raise(Changed, value, name);
        }

        bool ISyncedVar.Alive { get { return channel != null && channel.Alive; } }

        void ISyncedVar.Step() {
            steps++;
            if (steps < 2) return;
            if (PuzzleSDK.IsHost) {
                if (steps == 2) channel.Push(value ? 1 : 0);
                return;
            }
            if (!channel.TryGet(out int host)) return;
            bool wanted = host != 0;
            if (wanted == value) return;
            value = wanted;
            SyncedVars.Raise(Changed, value, name);
        }
    }

    // A function call every player runs: the host calls Fire, and Fired runs on every PC (the host's too).
    // Nothing is saved: a player who joins later does not run events from before they arrived. Fire can
    // carry one small number (-128 to 127).
    public class SyncedEvent : ISyncedVar {
        private const int Cycle = 100;      // the counter goes 1..100 and round again
        private const int MaxCatchUp = 5;   // at most this many runs for one arrival

        private readonly string name;
        private readonly SyncedValue counter, argument;
        private int fired;                  // host: the counter
        private int lastSeen = -1;          // other players: the last counter that arrived
        private int steps;
        private readonly List<int> early = new List<int>();   // host: fired before the network states were ready

        public event Action<int> Fired;

        public SyncedEvent(string name) {
            this.name = name ?? "";
            Transform parent = SyncedVars.Parent(this.name);
            if (parent == null) return;
            var c = new SyncedValue(parent, "synced event " + this.name, oneShot: true);
            var a = new SyncedValue(parent, "synced event arg " + this.name, oneShot: true);
            if (!c.Ready || !a.Ready) { Plugin.log.LogWarning($"[SyncedEvent] '{this.name}': could not create its network state; it only runs on this PC."); return; }
            counter = c; argument = a;
            counter.onChanged = OnHostFired;
            counter.RegisterSoon();
            argument.RegisterSoon();
            SyncedVars.Add(this);
        }

        // Host only (ignored on other players' PCs). Runs Fired here and on every other PC.
        public void Fire(int argument = 0) {
            if (!PuzzleSDK.IsHost) return;
            argument = Mathf.Clamp(argument, -128, 127);
            if (counter != null) {
                if (steps < 2) early.Add(argument);
                else Send(argument);
            }
            SyncedVars.Raise(Fired, argument, name);
        }

        private void Send(int arg) {
            this.argument.Set(arg);   // the number first, so it is there when the counter arrives
            fired = fired % Cycle + 1;
            counter.Push(fired);
        }

        bool ISyncedVar.Alive { get { return counter != null && counter.Alive; } }

        void ISyncedVar.Step() {
            steps++;
            if (steps < 2 || early.Count == 0 || !PuzzleSDK.IsHost) return;
            foreach (int arg in early) Send(arg);
            early.Clear();
        }

        // Other players: the host fired (maybe several times since the last arrival)
        private void OnHostFired(int value) {
            int times = 1;
            if (lastSeen >= 0) times = Mathf.Clamp(((value - lastSeen) % Cycle + Cycle) % Cycle, 1, MaxCatchUp);
            lastSeen = value;
            int arg = 0;
            if (argument != null && argument.TryGet(out int got)) arg = got;
            for (int i = 0; i < times; i++) SyncedVars.Raise(Fired, arg, name);
        }
    }

} // namespace BigPuzzleSDK
