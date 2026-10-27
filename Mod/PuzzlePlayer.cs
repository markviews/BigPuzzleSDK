using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// One player of the lobby, for puzzle scripts (PuzzleSDK.players). Everything is read-only, except that the host
// can move a player (Teleport) and make them put something down (DropItem, DropHat).
// A player who disconnects stays in the list, in the same place, with isOnline false; the values below then
// keep what they were when the player left (the position, colors and name), and seat / hat / held are empty.
// If the same player joins again they get their old entry back.
public sealed class PuzzlePlayer {
    internal PuzzlePlayer(string key) { this.key = key; }

    private readonly string key;
    private PlayerCharacter character;   // null while offline
    private PlayerNetworking net;

    // What the player was last seen with (kept while offline)
    private string lastName = "A player";
    private bool lastHost;
    private bool lastLocal;
    private Color cHead = Color.white, cBody = Color.white, cFeet = Color.white;
    private Vector3 lastPosition;
    private Quaternion lastHeadRotation = Quaternion.identity;

    // ---- Read-only values for scripts ----
    public string id => key;
    public string name => lastName;
    public bool isHost => lastHost;
    public bool isLocal => character != null && lastLocal;
    public bool isOnline => character != null;
    public Color colorHead => cHead;
    public Color colorBody => cBody;
    public Color colorFeet => cFeet;

    public Vector3 position {
        get {
            if (character != null) { try { lastPosition = character.transform.position; } catch (Exception) { } }
            return lastPosition;
        }
    }

    public Quaternion headRotation {
        get {
            if (character != null) {
                try {
                    Transform cam = character.cameraTransform;
                    lastHeadRotation = cam != null ? cam.rotation : character.transform.rotation;
                } catch (Exception) { }
            }
            return lastHeadRotation;
        }
    }

    // The Seat (from your puzzle) the player is sitting in, or null
    public PuzzleSeat seat {
        get {
            PlayerCharacter pc = character;
            if (pc == null) return null;
            try {
                foreach (PuzzleSeat s in PuzzleSeat.all) {
                    if (s == null) continue;
                    PlayerCharacter sitter = s.Occupant;
                    if (sitter != null && sitter.Pointer == pc.Pointer) return s;
                }
            } catch (Exception) { }
            return null;
        }
    }

    // What the player wears on their head (the blindfold slot): the name of the item, or null
    public string hat {
        get {
            PlayerCharacter pc = character;
            if (pc == null) return null;
            try {
                PropHome home = PlayerRoster.HatHome(pc);
                return home != null ? PlayerRoster.NameOfProp(home.pinnedProp) : null;
            } catch (Exception) { return null; }
        }
    }

    // What the player holds in their hands: the name of the item, or null
    public string held {
        get {
            PlayerCharacter pc = character;
            if (pc == null) return null;
            try { return pc.hands != null ? PlayerRoster.NameOfProp(pc.hands.heldProp) : null; } catch (Exception) { return null; }
        }
    }

    // ---- Host only: what a script may do to a player ----

    // Moves the player (standing them up first if they sit). Keeps the way they face unless you pass a rotation.
    public void Teleport(Transform target) {
        if (target == null) return;
        Teleport(target.position, target.rotation);
    }

    public void Teleport(Vector3 position, Quaternion? rotation = null) {
        if (!PuzzleSDK.IsHost || character == null) return;
        PlayerCommands.Teleport(this, position, rotation);
    }

    // Stands the player up if they are sitting (in a seat, at a telescope...), where the game puts them when they stand
    public void EjectFromSeat() {
        if (!PuzzleSDK.IsHost || character == null) return;
        PlayerCommands.StandUp(character);
    }

    // Makes the player put down what they hold in their hands
    public void DropItem() {
        if (!PuzzleSDK.IsHost || character == null || net == null) return;
        try {
            if (character.hands == null || character.hands.heldProp == null) return;
            net.ServerDropPropAutomatic(false);
        } catch (Exception ex) {
            PlayerRoster.Warn("dropitem", $"[Players] DropItem failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Makes the player take off what they wear on their head
    public void DropHat() {
        if (!PuzzleSDK.IsHost || character == null) return;
        try {
            PropHome home = PlayerRoster.HatHome(character);
            Prop worn = home != null ? home.pinnedProp : null;
            if (worn != null) worn.ServerSetUnpinned();   // the game's own "take it out of its home"
        } catch (Exception ex) {
            PlayerRoster.Warn("drophat", $"[Players] DropHat failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- For the roster ----
    internal PlayerCharacter Character => character;
    internal string Key => key;

    // This PC found the player in the game: take what we can from them
    internal void Bind(PlayerCharacter pc, PlayerNetworking pn) {
        character = pc;
        net = pn;
        // The name the moderation menu shows (their Steam name, cleaned up). The game's own "username" is an internal id.
        string shown = null;
        try { shown = pn.moderationNameSanitized; } catch (Exception) { }
        if (string.IsNullOrWhiteSpace(shown)) { try { shown = pn.moderationName; } catch (Exception) { } }
        if (string.IsNullOrWhiteSpace(shown)) { try { shown = pn.username; } catch (Exception) { } }
        if (!string.IsNullOrWhiteSpace(shown)) lastName = shown.Trim();
        try { lastHost = pn.isHost; } catch (Exception) { }
        try { lastLocal = pn.isLocalPlayer; } catch (Exception) { }
        try {
            PlayerLooks looks = pc.looks;
            if (looks != null) {
                cHead = Opaque(looks.GetColor(pn.lookIdHead).color);
                cBody = Opaque(looks.GetColor(pn.lookIdTorso).color);
                cFeet = Opaque(looks.GetColor(pn.lookIdLegs).color);
            }
        } catch (Exception) { }
        try { lastPosition = pc.transform.position; } catch (Exception) { }
    }

    // The game's look colors have no alpha set (0); a color for scripts should be solid
    private static Color Opaque(Color c) { return new Color(c.r, c.g, c.b, 1f); }

    // The player is gone (their last values stay)
    internal void SetOffline() {
        if (character != null) {
            try { lastPosition = character.transform.position; } catch (Exception) { }
        }
        character = null;
        net = null;
    }

    internal PlayerNetworking Networking => net;
}

// Keeps the list of players: the same order on every PC (the host first, then by when they joined), and players
// who leave stay where they were.
internal static class PlayerRoster {
    private static readonly List<PuzzlePlayer> list = new List<PuzzlePlayer>();
    private static readonly Dictionary<string, PuzzlePlayer> byKey = new Dictionary<string, PuzzlePlayer>();
    private static readonly Dictionary<uint, float> waitingForId = new Dictionary<uint, float>();
    private static readonly HashSet<string> warned = new HashSet<string>();
    internal static readonly ReadOnlyCollection<PuzzlePlayer> View = new ReadOnlyCollection<PuzzlePlayer>(list);

    private const float IdWait = 2f;   // how long to wait for a player's id before using their network id instead
    private static float nextTick;
    private static float emptySince = -1f;
    private static IntPtr lastLocal = IntPtr.Zero;

    private struct Found { public PlayerCharacter pc; public PlayerNetworking pn; public string key; public uint netId; public bool host; }

    internal static void Warn(string what, string message) {
        if (warned.Add(what)) Plugin.log.LogWarning(message);
    }

    internal static void Tick() {
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 0.25f;
        try { Refresh(); } catch (Exception ex) { Warn("roster", $"[Players] could not update the player list: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void Clear() {
        list.Clear();
        byKey.Clear();
        waitingForId.Clear();
    }

    // A player's id. It has to be the same on every PC, so it is the Epic user id: the game sends it to everyone.
    // (userPlatformIdString is not the same everywhere: the host sees a Steam id, other players see the Epic id.)
    // The others are only used if it never shows up. Null until known.
    private static string KeyOf(PlayerNetworking pn, bool anything) {
        string id = null;
        try { id = pn.epicUserId; } catch (Exception) { }
        if (string.IsNullOrWhiteSpace(id) && anything) { try { id = pn.userPlatformIdString; } catch (Exception) { } }
        if (string.IsNullOrWhiteSpace(id) && anything) { try { id = pn.identifier; } catch (Exception) { } }
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    private static void Refresh() {
        float now = Time.unscaledTime;
        var found = new List<Found>();
        var keys = new HashSet<string>();
        IntPtr local = IntPtr.Zero;

        var characters = PlayerCharacter.allPlayerCharacters;
        if (characters != null) {
            for (int i = 0; i < characters.Count; i++) {
                PlayerCharacter pc = characters[i];
                if (pc == null || !pc.gameObject.scene.IsValid()) continue;
                PlayerNetworking pn = pc.playerNetworking;
                if (pn == null) continue;
                if (pn.isLocalPlayer) local = pc.Pointer;

                uint netId = pn.netId;
                string key = KeyOf(pn, false);
                if (key == null) {
                    if (!waitingForId.TryGetValue(netId, out float since)) { waitingForId[netId] = now; continue; }
                    if (now - since < IdWait) continue;
                    key = KeyOf(pn, true) ?? "net" + netId.ToString(CultureInfo.InvariantCulture);
                }
                if (!keys.Add(key)) continue;   // the same player twice
                bool host = false;
                try { host = pn.isHost; } catch (Exception) { }
                found.Add(new Found { pc = pc, pn = pn, key = key, netId = netId, host = host });
            }
        }

        // A new game (this PC's own player is a different one), or nobody left for a while: start the list over
        if (local != IntPtr.Zero && lastLocal != IntPtr.Zero && local != lastLocal) Clear();
        if (local != IntPtr.Zero) lastLocal = local;
        if (found.Count == 0) {
            if (emptySince < 0f) emptySince = now;
            else if (now - emptySince > 3f && list.Count > 0) { Clear(); lastLocal = IntPtr.Zero; }
        } else {
            emptySince = -1f;
        }

        // Players we already know: take their latest values. New ones are added at the end, the host first
        var fresh = new List<Found>();
        foreach (Found f in found) {
            if (byKey.TryGetValue(f.key, out PuzzlePlayer known)) known.Bind(f.pc, f.pn);
            else fresh.Add(f);
        }
        fresh.Sort((a, b) => a.host != b.host ? (a.host ? -1 : 1) : a.netId.CompareTo(b.netId));
        foreach (Found f in fresh) {
            var player = new PuzzlePlayer(f.key);
            player.Bind(f.pc, f.pn);
            list.Add(player);
            byKey[f.key] = player;
        }

        // The ones who are gone
        foreach (PuzzlePlayer p in list)
            if (p.isOnline && !keys.Contains(p.Key)) p.SetOffline();

        // The host is always first
        int hostAt = list.FindIndex(p => p.isHost);
        if (hostAt > 0) {
            PuzzlePlayer host = list[hostAt];
            list.RemoveAt(hostAt);
            list.Insert(0, host);
        }
    }

    // The player this PC is (the one whose body it controls)
    internal static PlayerCharacter LocalCharacter() {
        foreach (PuzzlePlayer p in list) if (p.isOnline && p.isLocal) return p.Character;
        return null;
    }

    // ---- What a player wears and holds ----

    // The player's head slot (the blindfold's): the pocket that belongs to this player and is named like one
    internal static PropHome HatHome(PlayerCharacter pc) {
        var homes = PropHome.allPropHomes;
        if (homes == null) return null;
        for (int i = 0; i < homes.Count; i++) {
            PropHome home = homes[i];
            if (home == null) continue;
            PlayerCharacter owner = home.parentCharacter;
            if (owner == null || owner.Pointer != pc.Pointer) continue;
            if (home.pinnedProp == null) continue;
            string homeName = home.gameObject.name.ToLowerInvariant();
            foreach (string part in GameRefs.Players.HatHomeNames)
                if (homeName.Contains(part)) return home;
        }
        return null;
    }

    // A name for an item: the creator's own name for an Item, the type for a Vanilla Item, else the game's object name
    internal static string NameOfProp(Prop prop) {
        if (prop == null) return null;
        try {
            foreach (PuzzleItem item in PuzzleItem.Items()) {
                Prop mine = item != null ? item.GameProp : null;
                if (mine != null && mine.Pointer == prop.Pointer) return item.name;
            }
            foreach (PuzzleVanillaCopy copy in PuzzleVanillaCopy.all) {
                PuzzleVanillaItem vanilla = copy as PuzzleVanillaItem;
                Prop mine = vanilla != null ? vanilla.GameProp : null;
                if (mine != null && mine.Pointer == prop.Pointer) return vanilla.itemType.ToString();
            }
            string objectName = prop.gameObject.name;
            int clone = objectName.IndexOf("(Clone)", StringComparison.Ordinal);
            return (clone >= 0 ? objectName.Substring(0, clone) : objectName).Trim();
        } catch (Exception) { return null; }
    }
}

// Moving another player needs that player's own PC to do it (a player's body belongs to their PC), so the host
// sends it a message. Only players who have the same SDK version (they said so in its handshake) are sent one.
internal static class PlayerCommands {
    private const ushort MessageId = 0xB15E;   // our own network message, apart from the handshake's
    private const float AfterStandingUp = 0.3f;

    private class Pending { public PuzzlePlayer player; public string text; public float at; }
    private static readonly List<Pending> pending = new List<Pending>();
    private static NetworkMessageDelegate clientHandler;   // kept so it isn't collected
    private static NetworkMessageDelegate serverHandler;
    private static float nextHandlerCheck;

    private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }

    // Host: stand the player up if they sit, then move them
    internal static void Teleport(PuzzlePlayer player, Vector3 position, Quaternion? rotation) {
        PlayerCharacter pc = player.Character;
        if (pc == null) return;
        bool stoodUp = StandUp(pc);
        string text = "tp|" + F(position.x) + "|" + F(position.y) + "|" + F(position.z) + "|"
            + (rotation.HasValue ? "1|" + F(rotation.Value.eulerAngles.y) : "0|0");
        pending.Add(new Pending { player = player, text = text, at = Time.unscaledTime + (stoodUp ? AfterStandingUp : 0f) });
    }

    // Host: takes the player out of any pose (seat, telescope...) they are in. True if there was one.
    internal static bool StandUp(PlayerCharacter pc) {
        bool any = false;
        try {
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerPose>())) {
                PlayerPose pose = obj != null ? obj.TryCast<PlayerPose>() : null;
                if (pose == null || !pose.gameObject.scene.IsValid()) continue;
                PlayerCharacter sitter = null;
                try { sitter = pose.occupant; } catch (Exception) { }
                if (sitter == null || sitter.Pointer != pc.Pointer) continue;
                pose.Evict();
                any = true;
            }
        } catch (Exception ex) {
            PlayerRoster.Warn("standup", $"[Players] could not stand a player up before moving them: {ex.GetType().Name}: {ex.Message}");
        }
        return any;
    }

    internal static void Tick() {
        // Everyone: listen for the host's messages (the game's message table is rebuilt when a lobby is joined)
        if (Time.unscaledTime >= nextHandlerCheck) {
            nextHandlerCheck = Time.unscaledTime + 0.5f;
            if (NetworkClient.active && !NetworkServer.active) EnsureHandler();
            if (NetworkServer.active) EnsureServerHandler();
        }
        if (pending.Count == 0) return;
        for (int i = pending.Count - 1; i >= 0; i--) {
            Pending p = pending[i];
            if (Time.unscaledTime < p.at) continue;
            pending.RemoveAt(i);
            Deliver(p);
        }
    }

    // Host: this PC's own player is moved here, anyone else's through a message to their PC
    private static void Deliver(Pending p) {
        PlayerCharacter pc = p.player.Character;
        PlayerNetworking pn = p.player.Networking;
        if (pc == null || pn == null) return;   // left in the meantime
        try {
            if (p.player.isLocal) { Apply(pc, p.text); return; }
            NetworkConnectionToClient conn = pn.connectionToClient;
            if (conn == null) return;
            if (!SdkHandshake.HasSameVersion(conn.connectionId)) return;   // no SDK there, or another version: they would not understand it
            Send(conn, p.text);
        } catch (Exception ex) {
            PlayerRoster.Warn("deliver", $"[Players] could not move a player: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A player's PC tells the host something (a SyncedEvent fired on a player's PC). False if there is no host to tell.
    internal static bool SendToHost(string text) {
        try {
            if (!NetworkClient.active || NetworkServer.active || NetworkClient.connection == null) return false;
            Send(NetworkClient.connection, text);
            return true;
        } catch (Exception ex) {
            PlayerRoster.Warn("sendtohost", $"[Synced] could not tell the host: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void EnsureServerHandler() {
        var handlers = NetworkServer.handlers;
        if (handlers == null) return;
        if (serverHandler == null)
            serverHandler = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(new Action<NetworkConnection, NetworkReader, int>(OnServerMessage));
        if (handlers.ContainsKey(MessageId)) return;
        handlers[MessageId] = serverHandler;
    }

    // Host: a player's PC sent something
    private static void OnServerMessage(NetworkConnection conn, NetworkReader reader, int channelId) {
        try {
            string text = ReadText(reader);
            if (text == null || conn == null) return;
            if (text.StartsWith("ce|", StringComparison.Ordinal)) SyncedStore.OnClientEvent(conn.connectionId, text);
            else if (text.StartsWith("iu|", StringComparison.Ordinal)) ItemUse.OnPlayerMessage(conn.connectionId, text);
        } catch (Exception ex) {
            PlayerRoster.Warn("onservermessage", $"[Synced] could not read a player's message: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Any text message of ours (teleports, synced texts)
    internal static void SendText(NetworkConnection conn, string text) { Send(conn, text); }

    private static void Send(NetworkConnection conn, string text) {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(text);
        var writer = new NetworkWriter();
        writer.WriteByte((byte)(MessageId & 0xFF));
        writer.WriteByte((byte)(MessageId >> 8));
        int length = payload.Length;
        for (int i = 0; i < 4; i++) writer.WriteByte((byte)(length >> (8 * i)));
        writer.WriteBytes((Il2CppStructArray<byte>)payload, 0, payload.Length);
        conn.Send(writer.ToArraySegment(), 0);   // channel 0: reliable
    }

    private static string ReadText(NetworkReader reader) {
        int length = 0;
        for (int i = 0; i < 4; i++) length |= reader.ReadByte() << (8 * i);
        if (length < 0 || length > 65536 || length > reader.Remaining) return null;
        var data = new Il2CppStructArray<byte>(length);
        reader.ReadBytes(data, length);
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = data[i];
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void EnsureHandler() {
        var handlers = NetworkClient.handlers;
        if (handlers == null) return;
        if (clientHandler == null)
            clientHandler = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(new Action<NetworkConnection, NetworkReader, int>(OnMessage));
        if (handlers.ContainsKey(MessageId)) return;
        handlers[MessageId] = clientHandler;
    }

    // Player: the host moves this PC's player
    private static void OnMessage(NetworkConnection conn, NetworkReader reader, int channelId) {
        try {
            string text = ReadText(reader);
            if (text == null) return;
            if (text.StartsWith("sv|", StringComparison.Ordinal)) { SyncedStore.OnMessage(text); return; }
            if (text.StartsWith("ua|", StringComparison.Ordinal)) { ItemUseAnimation.OnMessage(text); return; }
            PlayerCharacter pc = PlayerRoster.LocalCharacter();
            if (pc == null) return;
            Apply(pc, text);
        } catch (Exception ex) {
            PlayerRoster.Warn("onmessage", $"[Players] could not read the host's message: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // On the PC that owns the player: "tp|x|y|z|hasRotation|yaw"
    private static void Apply(PlayerCharacter pc, string text) {
        string[] parts = text.Split('|');
        if (parts.Length < 6 || parts[0] != "tp") return;
        float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
        float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
        float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
        bool turn = parts[4] == "1";
        float yaw = float.Parse(parts[5], CultureInfo.InvariantCulture);
        var target = new Vector3(x, y, z);
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);

        // The same steps as the creator key (Ctrl+F1) takes
        Rigidbody body = pc.rb;
        pc.transform.position = target;
        if (turn) pc.transform.rotation = facing;
        if (body != null) {
            body.position = target;
            if (turn) body.rotation = facing;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}

} // namespace BigPuzzleSDK
