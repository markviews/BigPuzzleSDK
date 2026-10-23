using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace BigPuzzleSDK {

// One puzzle's entry in the save file
public class SavedPuzzle {
    public string name { get; set; }    // the puzzle's name, to make the file readable (not used to match)
    public bool won { get; set; }
    public string gourd { get; set; }   // "held", "tower" or "lost": won puzzles with a vice, or a box puzzle whose gourd has been held
    public Dictionary<string, string> items { get; set; }   // Grabbable Items that save: "name [id]" -> "held"
    public Dictionary<string, string> texts { get; set; }   // Spare Signs a player wrote on: "name [id]" -> the text
    public Dictionary<string, string> switches { get; set; }   // Switches: where each is in the hierarchy -> "on" or "off"
}

public class PuzzleSaveData {
    public int formatVersion { get; set; }
    public Dictionary<string, SavedPuzzle> puzzles { get; set; }   // by puzzle id
}

// Saves which modded puzzles are won and where their gourds are, next to the game's own save.
// Host only: the host's game owns the save; other players see the result through the network.
//
//   <the game's save folder>/../BigPuzzleSDK/<the game save's file id>.json
//   (Windows: %USERPROFILE%/AppData/LocalLow/House House/Big Walk/user_data/BigPuzzleSDK, next to the
//   game's own save_games folder, so it is the same whichever mod loader started the game)
//
// Written every time the game writes its save, deleted with it. Puzzles that aren't installed
// keep their entries, so reinstalling one brings its progress back.
//
// Loading, once the puzzles are built:
//   won   -> IsWon is true, the vice is open and empty, nothing fires (default puzzle state)
//   held  -> the gourd is at the spawn area (held in hands or in a backpack when saved)
//   lost  -> the gourd is on its puzzle's pillow (no pillow: the vice releases it as on a win)
//   tower -> the gourd fills the tower's next plinth; when there are none left, the spawn area
//
// Switches save "on" or "off" and start that way (a Switch always starts off with no save). A switch
// is found again by its place in the hierarchy under the puzzle root, so moving it loses its position.
//
// A Buoy Light also saves its light: "on" or "off" ("held on" / "held off" while it is held).
//
// Grabbable Items with Saves On Reload are saved like gourds: only "held" (hand, belt or backpack)
// is written, never a position. On load a held item is laid at the spawn area; any other item
// starts where the puzzle puts it. An item loaded this way stays "held" while it lies at the spawn
// area (picking it up and putting it back there changes nothing); once it has been taken away and
// put down elsewhere it only saves while someone is holding it.
internal static class PuzzleSave {
    private const int FormatVersion = 1;
    internal const string Held = "held", Tower = "tower", Lost = "lost";

    // Next to the game's own save_games folder (not inside it, which the game reads), so every way of
    // launching the game, r2modman, the Thunderstore app or a manual BepInEx install, finds the same files
    private static string Folder => Path.Combine(UnityEngine.Application.persistentDataPath, "user_data", "BigPuzzleSDK");

    // Where this SDK used to keep them: inside the BepInEx profile, so a different profile had its own
    private static string OldFolder => Path.Combine(Paths.PluginPath, "BigPuzzleSDK", "saves");

    private static Dictionary<string, SavedPuzzle> entries;   // every puzzle in the file, installed or not
    private static string entriesUid;                         // the game save they belong to
    private static Dictionary<string, SavedPuzzle> reloadSnapshot;   // Ctrl+F2: the state just before
    private static bool applied;      // won puzzles are restored (step 1)
    private static bool gourdsPlaced; // and their gourds put back (step 2): saving may now update the entries

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- Hooks on the game's save ----
    internal static void InstallHooks(Harmony harmony) {
        try {
            MethodInfo write = AccessTools.Method(typeof(SaveManager), GameRefs.Hooks.WriteCurrentSaveData);
            if (write == null) throw new MissingMethodException("SaveManager.WriteCurrentSaveData not found");
            harmony.Patch(write, postfix: new HarmonyMethod(typeof(PuzzleSave).GetMethod(nameof(AfterGameSaved), BindingFlags.Static | BindingFlags.NonPublic)));
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not hook the game's save ({ex.GetType().Name}: {ex.Message}); modded puzzle progress will not be saved");
        }
        try {
            MethodInfo delete = AccessTools.Method(typeof(SaveManager), GameRefs.Hooks.DeleteSave);
            if (delete == null) throw new MissingMethodException("SaveManager.Delete not found");
            harmony.Patch(delete, prefix: new HarmonyMethod(typeof(PuzzleSave).GetMethod(nameof(BeforeGameDeleted), BindingFlags.Static | BindingFlags.NonPublic)));
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not hook save deletion ({ex.GetType().Name}: {ex.Message}); deleted saves leave their puzzle file behind");
        }
    }

    private static void AfterGameSaved() {
        try {
            if (!NetworkServer.active || !gourdsPlaced) return;   // host only, and only once our state is loaded
            string uid = CurrentUid();
            if (uid == null || uid != entriesUid) return;   // a different save than the one loaded
            entries = Capture();
            Write(uid, entries);
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not save modded puzzle progress: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void BeforeGameDeleted(SaveData __0) {
        try {
            string uid = __0 != null ? __0.filenameUid : null;
            if (string.IsNullOrEmpty(uid)) return;
            string file = FileFor(uid);
            if (File.Exists(file)) {
                File.Delete(file);
                Plugin.log.LogInfo($"[Save] the game deleted a save; removed its puzzle progress ({Path.GetFileName(file)})");
            }
            if (uid == entriesUid) { entries = null; entriesUid = null; applied = false; gourdsPlaced = false; }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not remove the deleted save's puzzle progress: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Loading (called by the loader, host only) ----

    // Every (re)build of the puzzles: what was loaded no longer describes the world until applied again
    // True from the moment the saved puzzles are restored until their gourds are put back
    internal static bool GourdsPending => applied && !gourdsPlaced;

    internal static void BeforeBuild() { applied = false; gourdsPlaced = false; }

    // Ctrl+F2: keep the progress of the puzzles being torn down, to put back after the rebuild
    internal static void SnapshotForReload() {
        if (!NetworkServer.active || !gourdsPlaced) return;
        reloadSnapshot = Capture();
    }

    // Step 1, straight after the puzzles are built (before their first frame): won puzzles start
    // won, their vices open and empty
    internal static void ApplyBuilt() {
        if (!NetworkServer.active) return;
        string uid = CurrentUid();
        if (uid == null) {
            Plugin.log.LogWarning("[Save] no game save is loaded, so modded puzzle progress isn't loaded or saved");
            return;
        }
        if (reloadSnapshot != null && uid == entriesUid) entries = reloadSnapshot;   // Ctrl+F2: as it was a moment ago
        else { entries = Read(uid); entriesUid = uid; }
        reloadSnapshot = null;

        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            if (!entries.TryGetValue(sdk.packId, out SavedPuzzle saved) || saved == null || !saved.won) continue;
            sdk.RestoreWon();
            PuzzleGourdVice vice = ViceOf(sdk);
            if (vice == null) continue;
            // A lost gourd with no pillow to go to: the vice releases it as on a win (step 2)
            if (GourdOf(saved) == Lost && PillowOf(sdk) == null) continue;
            vice.StartEmpty();
        }
        applied = true;
    }

    // Step 2, a moment later (the gourds can be woken, the tower has its plinths): the gourds
    internal static void ApplyGourds() {
        if (!NetworkServer.active || !applied || entries == null) return;
        List<PuzzleGourdPlinth> slots = TowerSlots();
        int nextSlot = 0, placed = 0, won = 0;
        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            if (!entries.TryGetValue(sdk.packId, out SavedPuzzle saved) || saved == null || !saved.won) continue;
            won++;
            PuzzleGourdVice vice = ViceOf(sdk);
            if (vice == null || vice.Gourd == null) continue;
            string where = GourdOf(saved);
            bool ok;
            if (where == Tower && nextSlot >= slots.Count)
                Plugin.log.LogInfo($"[Save] '{sdk.transform.root.name}': its gourd was in the tower, but {TowerReport(slots.Count, nextSlot)}; it goes to the spawn area");
            if (where == Tower && nextSlot < slots.Count) ok = vice.PlaceGourdIn(slots[nextSlot++]);
            else if (where == Tower || where == Held) ok = vice.PlaceGourdAt(SpawnSpot());
            else {
                PuzzlePillow pillow = PillowOf(sdk);
                if (pillow != null) { pillow.Show(); ok = vice.PlaceGourdAt(pillow.transform.position + Vector3.up * 0.5f); }
                else { vice.Release(); ok = true; }   // rolls out of the vice
            }
            if (ok) placed++;
            else Plugin.log.LogWarning($"[Save] could not put back the gourd of '{sdk.transform.root.name}' ({where})");
        }
        // Puzzles whose gourd sits in a box: no gourd entry means nobody has held it yet, so it goes in the box.
        // Once it has been held it is never put back in the box.
        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            PuzzleBox box = PuzzleBox.HolderOf(sdk.transform.root);
            if (box == null || box.Gourd == null) continue;
            string where = null;
            if (entries.TryGetValue(sdk.packId, out SavedPuzzle boxSaved) && boxSaved != null && !string.IsNullOrEmpty(boxSaved.gourd))
                where = GourdOf(boxSaved);
            bool ok;
            if (where == null) ok = box.PlaceInBox();
            else if (where == Tower && nextSlot < slots.Count) ok = box.PlaceGourdIn(slots[nextSlot++]);
            else if (where == Tower || where == Held) ok = box.PlaceGourdAt(SpawnSpot());
            else {
                PuzzlePillow pillow = PillowOf(sdk);
                ok = box.PlaceGourdAt(pillow != null ? pillow.transform.position + Vector3.up * 0.5f : SpawnSpot());
                if (pillow != null) pillow.Show();
            }
            if (ok) placed++;
            else Plugin.log.LogWarning($"[Save] could not put back the gourd of '{sdk.transform.root.name}' ({where ?? "in its box"})");
        }

        int itemsBack = PlaceHeldItems();
        gourdsPlaced = true;
        if (itemsBack > 0) Plugin.log.LogInfo($"[Save] {itemsBack} held item(s) put at the spawn area");
        if (won > 0 || entries.Count > 0)
            Plugin.log.LogInfo($"[Save] modded puzzle progress loaded: {won} won puzzle(s), {placed} gourd(s) put back");
    }

    // Items that were held when saved: laid at the spawn area
    private static int PlaceHeldItems() {
        int count = 0;
        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            if (!entries.TryGetValue(sdk.packId, out SavedPuzzle saved) || saved == null || saved.items == null) continue;
            int root = sdk.transform.root.GetInstanceID();
            foreach (KeyValuePair<string, string> entry in saved.items) {
                // "held", or for a buoy: "on", "off", "held on", "held off"
                string value = entry.Value ?? "";
                bool held = value == Held || value.StartsWith(Held + " ");
                bool lightOn = value == "on" || value.EndsWith(" on");
                bool lightKnown = lightOn || value == "off" || value.EndsWith(" off");
                ulong id = IdOf(entry.Key);
                foreach (ISavedHeld item in SavedHeldItems()) {
                    if (item.ItemId != id || item.Owner.transform.root.GetInstanceID() != root) continue;
                    PuzzleVanillaItem buoy = BuoyOf(item);
                    if (buoy != null && lightKnown) buoy.RestoreLight(lightOn);
                    if (held && item.SavesOnReload) { item.SendTo(SpawnSpot()); count++; }
                    break;
                }
            }
        }
        // Spare Signs a player had written on
        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            if (!entries.TryGetValue(sdk.packId, out SavedPuzzle saved) || saved == null || saved.texts == null) continue;
            int root = sdk.transform.root.GetInstanceID();
            foreach (KeyValuePair<string, string> entry in saved.texts) {
                ulong id = IdOf(entry.Key);
                foreach (ISavedHeld item in SavedHeldItems()) {
                    PuzzleVanillaItem sign = item as PuzzleVanillaItem;
                    if (sign == null || sign.ItemId != id || sign.Owner.transform.root.GetInstanceID() != root) continue;
                    sign.RestoreText(entry.Value);
                    break;
                }
            }
        }
        return count;
    }

    // Where a Switch is under its puzzle root, by name and sibling number: "Room#0/Lights#2/Switch#1"
    private static string SwitchKey(PuzzleSwitch s) {
        string path = s.name + "#" + s.transform.GetSiblingIndex();
        for (Transform p = s.transform.parent; p != null && p != s.transform.root; p = p.parent)
            path = p.name + "#" + p.GetSiblingIndex() + "/" + path;
        return path;
    }

    // True if the save says this switch was left on (host only: the others follow the host's value)
    internal static bool SwitchWasOn(PuzzleSwitch s) {
        if (!NetworkServer.active || !applied || entries == null) return false;
        try {
            int root = s.transform.root.GetInstanceID();
            foreach (PuzzleSDK sdk in LoadedPuzzles()) {
                if (sdk.transform.root.GetInstanceID() != root) continue;
                return entries.TryGetValue(sdk.packId, out SavedPuzzle saved) && saved != null && saved.switches != null
                    && saved.switches.TryGetValue(SwitchKey(s), out string value) && value == "on";
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not read the saved position of switch '{s.name}': {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    private static string KeyOf(ISavedHeld item) => item.Owner.name + " [" + item.ItemId + "]";

    // The Vanilla Item if it is a buoy (a buoy also saves whether its light is on)
    private static PuzzleVanillaItem BuoyOf(ISavedHeld item) {
        PuzzleVanillaItem vanilla = item as PuzzleVanillaItem;
        return vanilla != null && vanilla.IsBuoy ? vanilla : null;
    }

    // Grabbable Items and Vanilla Items
    private static IEnumerable<ISavedHeld> SavedHeldItems() {
        foreach (PuzzleItem item in PuzzleItem.Items()) yield return item;
        foreach (PuzzleVanillaCopy c in PuzzleVanillaCopy.all) {
            PuzzleVanillaItem vanilla = c as PuzzleVanillaItem;
            if (vanilla != null) yield return vanilla;
        }
    }

    // The id between the brackets (the name is only there to make the file readable)
    private static ulong IdOf(string key) {
        int a = key.LastIndexOf('['), b = key.LastIndexOf(']');
        return a >= 0 && b > a && ulong.TryParse(key.Substring(a + 1, b - a - 1), out ulong id) ? id : 0UL;
    }

    // ---- Reading the world ----

    // Every loaded puzzle that can be won (not the tower), with an id
    private static List<PuzzleSDK> LoadedPuzzles() {
        var list = new List<PuzzleSDK>();
        foreach (PuzzleSDK sdk in PuzzleSDK.all.ToArray()) {
            if (sdk == null || string.IsNullOrEmpty(sdk.packId) || sdk.puzzleType == PuzzleType.Tower) continue;
            if (!sdk.gameObject.activeInHierarchy) continue;   // switched off by a PuzzleHider
            list.Add(sdk);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.packId, b.packId));   // the same tower order every time
        return list;
    }

    // The file's entries with the loaded puzzles' current state written over them
    private static Dictionary<string, SavedPuzzle> Capture() {
        var result = new Dictionary<string, SavedPuzzle>(entries ?? new Dictionary<string, SavedPuzzle>());
        foreach (PuzzleSDK sdk in LoadedPuzzles()) {
            var saved = new SavedPuzzle { name = sdk.transform.root.name, won = sdk.IsWon };
            if (sdk.IsWon) {
                PuzzleGourdVice vice = ViceOf(sdk);
                if (vice != null) saved.gourd = WhereIs(vice.Gourd);
            }
            // A gourd in a box: only saved once someone has held it (no entry = still in the box)
            PuzzleBox box = PuzzleBox.HolderOf(sdk.transform.root);
            if (box != null && box.GourdTaken) saved.gourd = WhereIs(box.Gourd);
            int root = sdk.transform.root.GetInstanceID();
            foreach (ISavedHeld item in SavedHeldItems()) {
                if (item.Owner.transform.root.GetInstanceID() != root) continue;
                bool held = item.SavesOnReload && item.CountsAsHeld();
                PuzzleVanillaItem buoy = BuoyOf(item);
                PuzzleVanillaItem vanilla = item as PuzzleVanillaItem;
                if (vanilla != null && vanilla.TryGetWrittenText(out string written)) {
                    if (saved.texts == null) saved.texts = new Dictionary<string, string>();
                    saved.texts[KeyOf(item)] = written;
                }
                if (buoy == null && !held) continue;
                if (saved.items == null) saved.items = new Dictionary<string, string>();
                // A buoy also saves whether its light is on: "on" / "off", with "held " in front when held
                saved.items[KeyOf(item)] = buoy == null ? Held : (held ? Held + " " : "") + (buoy.LightOn ? "on" : "off");
            }
            // Switches. One that hasn't settled yet keeps the position the file already had for it
            Dictionary<string, string> before = null;
            if (result.TryGetValue(sdk.packId, out SavedPuzzle old) && old != null) before = old.switches;
            foreach (PuzzleSwitch sw in PuzzleSwitch.all.ToArray()) {
                if (sw == null || sw.transform.root.GetInstanceID() != root) continue;
                string key = SwitchKey(sw), value = sw.IsOn ? "on" : "off";
                if (!sw.Settled) { if (before == null || !before.TryGetValue(key, out value)) continue; }
                if (saved.switches == null) saved.switches = new Dictionary<string, string>();
                saved.switches[key] = value;
            }
            result[sdk.packId] = saved;
        }
        return result;
    }

    // Where a puzzle's gourd is now. Not out yet (the vice still opening) or anywhere else: lost.
    internal static string WhereIs(GameObject gourd) {
        if (gourd == null || !gourd.activeInHierarchy) return Lost;
        Prop prop = gourd.GetComponent<Prop>();
        if (prop == null) return Lost;
        try {
            if (prop.exclusiveHolder != null) return Held;   // in someone's hands
            if (prop.isInInventory) return Held;
            PropHome home = prop.currentHome;
            if (home == null) return Lost;
            if (home.parentCharacter != null || home.isInventory) return Held;   // a backpack or pocket
            foreach (PuzzleGourdPlinth plinth in PuzzleGourdPlinth.all) {
                if (plinth == null || plinth.Home == null || plinth.Home.Pointer != home.Pointer) continue;
                PuzzleSDK owner = plinth.GetComponentInParent<PuzzleSDK>();
                if (owner != null && owner.puzzleType == PuzzleType.Tower) return Tower;
            }
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not tell where a gourd is: {ex.GetType().Name}: {ex.Message}");
        }
        return Lost;
    }

    private static string GourdOf(SavedPuzzle saved) {
        string g = saved.gourd;
        return g == Held || g == Tower ? g : Lost;
    }

    private static PuzzleGourdVice ViceOf(PuzzleSDK sdk) {
        int root = sdk.transform.root.GetInstanceID();
        foreach (PuzzleGourdVice vice in PuzzleGourdVice.all)
            if (vice != null && vice.transform.root.GetInstanceID() == root) return vice;
        return sdk.GetComponentInChildren<PuzzleGourdVice>(true);   // before its Start (step 1)
    }

    private static PuzzlePillow PillowOf(PuzzleSDK sdk) {
        PuzzlePillow pillow = sdk.GetComponentInChildren<PuzzlePillow>(true);
        return pillow != null && !PuzzleHider.IsHidden(pillow.transform) ? pillow : null;
    }

    // Why a tower gourd found no plinth, for the log
    private static string TowerReport(int slots, int used) {
        int towers = 0, buttons = 0, plinths = 0;
        foreach (PuzzleSDK sdk in PuzzleSDK.all)
            if (sdk != null && sdk.puzzleType == PuzzleType.Tower && sdk.gameObject.activeInHierarchy) {
                towers++;
                plinths += sdk.GetComponentsInChildren<PuzzleGourdPlinth>(true).Length;
            }
        foreach (TowerButton b in TowerButton.all) if (b != null) buttons++;
        int puzzles = 0;
        foreach (PuzzleSDK sdk in PuzzleSDK.all)
            if (sdk != null && sdk.puzzleType == PuzzleType.Puzzle && sdk.gameObject.activeInHierarchy) puzzles++;
        if (towers == 0) return "no puzzle with Puzzle Type = Tower is loaded";
        return $"the tower has no free plinth ({used} of {slots} in use filled; tower plinths {plinths}, tower buttons {buttons}, modded puzzles of type Puzzle {puzzles})";
    }

    // The loaded tower's plinths in fill order
    private static List<PuzzleGourdPlinth> TowerSlots() {
        foreach (TowerButton button in TowerButton.all) {
            if (button == null || !button.gameObject.activeInHierarchy) continue;
            PuzzleSDK owner = button.GetComponentInParent<PuzzleSDK>();
            if (owner != null && owner.puzzleType == PuzzleType.Tower) return button.Slots;
        }
        // A tower without a TowerButton: its plinths in the order they sit in the hierarchy
        foreach (PuzzleSDK sdk in PuzzleSDK.all) {
            if (sdk == null || sdk.puzzleType != PuzzleType.Tower || !sdk.gameObject.activeInHierarchy) continue;
            var list = new List<PuzzleGourdPlinth>();
            foreach (PuzzleGourdPlinth p in sdk.GetComponentsInChildren<PuzzleGourdPlinth>(false))
                if (p != null && !p.HasGourd) list.Add(p);
            return list;
        }
        return new List<PuzzleGourdPlinth>();
    }

    // Where held gourds come back: the game's own spawn spot for items, else beside the host
    private static Vector3 SpawnSpot() {
        try {
            InventorySpawn spawn = UnityEngine.Object.FindObjectOfType<InventorySpawn>();
            if (spawn != null) return spawn.GetNextSpawnPosition() + Vector3.up * 0.5f;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not find the item spawn spot: {ex.GetType().Name}: {ex.Message}");
        }
        NetworkIdentity player = NetworkClient.localPlayer;
        if (player != null) return player.transform.position + player.transform.forward * 1.5f + Vector3.up;
        return Vector3.zero;
    }

    // ---- The file ----

    private static string CurrentUid() {
        try {
            SaveManager manager = SaveManager.instance;
            SaveData data = manager != null ? manager.currentData : null;
            string uid = data != null ? data.filenameUid : null;
            if (string.IsNullOrEmpty(uid)) return null;
            foreach (char ch in Path.GetInvalidFileNameChars()) uid = uid.Replace(ch, '_');
            return uid;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[Save] could not read which game save is loaded: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string FileFor(string uid) => Path.Combine(Folder, uid + ".json");

    private static Dictionary<string, SavedPuzzle> Read(string uid) {
        string file = FileFor(uid);
        var empty = new Dictionary<string, SavedPuzzle>();
        if (!File.Exists(file)) {
            // Progress saved by an older version, in this profile's plugins folder: bring it over (the old file stays)
            string old = Path.Combine(OldFolder, uid + ".json");
            try {
                if (File.Exists(old)) {
                    Directory.CreateDirectory(Folder);
                    File.Copy(old, file);
                    Plugin.log.LogInfo($"[Save] copied {Path.GetFileName(old)} from the old save folder to {Folder}");
                }
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[Save] could not copy the old save file: {ex.GetType().Name}: {ex.Message}");
            }
            if (!File.Exists(file)) return empty;
        }
        try {
            PuzzleSaveData data = JsonSerializer.Deserialize<PuzzleSaveData>(File.ReadAllText(file));
            if (data != null && data.formatVersion > FormatVersion)
                Plugin.log.LogWarning($"[Save] {Path.GetFileName(file)} was written by a newer BigPuzzleSDK; reading what this version understands");
            return data?.puzzles ?? empty;
        } catch (Exception ex) {
            // Keep the unreadable file for the player, and start this save's progress fresh
            string broken = Path.Combine(Folder, uid + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            try { File.Move(file, broken); } catch (Exception) { }
            Plugin.log.LogError($"[Save] {Path.GetFileName(file)} could not be read ({ex.GetType().Name}: {ex.Message}); moved it to {Path.GetFileName(broken)} and starting fresh");
            return empty;
        }
    }

    // Written to a temporary file first, then swapped in, so a crash mid-write can't break the save
    private static void Write(string uid, Dictionary<string, SavedPuzzle> puzzles) {
        Directory.CreateDirectory(Folder);
        string file = FileFor(uid), temp = file + ".tmp";
        var data = new PuzzleSaveData { formatVersion = FormatVersion, puzzles = puzzles };
        File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temp, file, true);
    }
}

} // namespace BigPuzzleSDK
