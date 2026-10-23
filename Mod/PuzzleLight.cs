using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BigPuzzleSDK {

// Which vanilla light a PuzzleLight becomes. The order must match the Unity-side enum.
public enum PuzzleLightType { TableRed, Pendant, Spotlight_Black, Spotlight_Red, Tube, Circle, Ring, Conductor }

// Runtime (mod) version of the SDK's PuzzleLight: copies a vanilla light into place. One
// script covers every kind of light; the creator picks the kind from a dropdown.
public class PuzzleLight : MonoBehaviour {
    public PuzzleLight(IntPtr ptr) : base(ptr) { }

    private const string CopyName = "VanillaLight";

    // Per kind of light: the vanilla object's name and the scene path to prefer when the
    // game has several with that name.
    private static string TemplateName(PuzzleLightType kind) => GameRefs.Light.TemplateName(kind);
    // Where each type's vanilla template sits (see Templates in Plugin.cs). Only TableRed has
    // been confirmed from the game so far; the others are best guesses and fall back to a
    // search (with a warning showing the real path) if wrong.
    private static string ExactPath(PuzzleLightType kind) => GameRefs.Light.ExactPath(kind);

    private static string PreferredPath(PuzzleLightType kind) => GameRefs.Light.PreferredPath(kind);

    // The game numbers duplicates, e.g. "Light_Spotlight (6)"
    private static bool NameMatches(string actual, string wanted) {
        return actual == wanted || actual.StartsWith(wanted + " (");
    }

    // Set in the Inspector (filled in by the loader). The names must match the Unity-side fields.
    private PuzzleLightType type;
    private Color color = Color.white;   // white (1,1,1,1) = keep the vanilla light's own colour
    private float range;                 // 0 = the lamp's own
    private float intensity;             // 0 = the lamp's own
    private float spotAngle;             // 0 = the lamp's own (spotlights only)

    private bool useCustomColor;          // the colour isn't the default white, or SetColor was called
    private bool colorChecked;

    // The host's colour (0-255 per channel), sent to every other player (see SyncedValue in Plugin.cs)
    private SyncedValue syncR, syncG, syncB;
    private int hostR, hostG, hostB;

    // Read-only for scripts
    public Color CurrentColor => color;
    public bool IsOn => on;
    public bool IsParty => party;

    private readonly List<Light> lights = new List<Light>();
    private readonly List<float> baseIntensity = new List<float>();   // each light's own brightness, for party mode
    private readonly List<Color> baseLightColor = new List<Color>();

    // ---- Party mode: random bright colours that blend into each other, breathing gently ----
    private const float PartyFadeSeconds = 1f;                // the very first fade in from dark
    private const float PartyTransitionSeconds = 1.5f;        // one colour blends into the next over this long
    private const float PartyDip = 0.5f;                      // brightness drops by this much at the middle of a blend
    private const float BreathDepth = 0.12f;                  // the gentle pulse: brightness varies by this much
    private float partyBegin;          // when the party started
    private Color partyPrev = Color.white;    // the colour being blended away from
    private Color shownColor = Color.white;   // the colour on show right now (during a party)
    private float breathRate;          // radians per second (1 to 1.8 pulses a second)
    private float breathPhase;
    private const float PartyHoldMinSeconds = 1.5f;   // each colour stays a random time between these, so lights drift apart
    private const float PartyHoldMaxSeconds = 6f;
    private float partyHold = 3f;     // how long the current colour stays at full brightness (the host picks it and sends it)
    private float PartyCycleSeconds => PartyTransitionSeconds + partyHold;
    private SyncedValue syncHold;     // partyHold in tenths of a second (15-60)
    private bool party;
    private float partyStart;        // when the current colour started blending in
    private float partyHue;
    private float level = 1f;        // 0-1 brightness multiplier while partying
    private Color savedColor;
    private bool savedCustom;
    // Ending with StopParty(): a short fade back to the lamp's own (white) look
    private const float PartyEndFadeSeconds = 1f;
    private bool fading;
    private bool fadeThenDefault;    // after the fade: the lamp's own colour (otherwise the colour set before the party arrives from the host)
    private float fadeStart;
    private Color fadeFromColor;
    private float fadeFromLevel;
    private Color fadeFromLens;
    // 1 = partying; 0 = stopped at once (colour comes from the colour sync); 2 = stopped at once, back to the lamp's own colour;
    // 3 = fading out, then the colour set before the party; 4 = fading out, then the lamp's own colour
    private SyncedValue syncParty;
    private readonly List<PropertyBlockHelper> glowHelpers = new List<PropertyBlockHelper>();
    private float recolorAt;    // reapply the colour shortly after a switch, in case the game reset it

    private const int OnValue = 1;    // state value that lights the lamp (assumed)
    private const int OffValue = 0;

    private readonly List<PeckSwitch> switches = new List<PeckSwitch>();
    private readonly List<TrackedPeckState> states = new List<TrackedPeckState>();
    private TrackedPeckState lampState;
    private Light lamp;
    private List<ushort> ticketIds = new List<ushort>();   // this copy's network ids (see Tickets in Plugin.cs)
    private bool on;
    private bool primed;

    // Called by the loader while the puzzle is still inactive
    internal void SetupLight(int index) {
        var log = Plugin.log;
        string wantedName = TemplateName(type);
        string preferred = PreferredPath(type);

        GameObject template = Templates.Find(ExactPath(type));
        if (template == null) foreach (string alt in GameRefs.Light.ExtraPaths(type)) { template = Templates.Find(alt); if (template != null) break; }
        if (template == null) {
            // Not at its known path: search every loaded object instead (slow)
            // Every vanilla light has a HouseLight somewhere inside it; walk up from each one to
            // the object with the wanted name (a few levels at most).
            foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<HouseLight>())) {
                HouseLight candidate = obj.TryCast<HouseLight>();
                if (candidate == null) continue;
                Transform signal = candidate.transform.parent;
                for (int up = 0; up < 4 && signal != null && !NameMatches(signal.name, wantedName); up++) signal = signal.parent;
                if (signal == null || !NameMatches(signal.name, wantedName)) continue;
                string path = PathOf(signal);
                if (path.Contains(CopyName)) continue;   // skip our own copies
                if (template == null) template = signal.gameObject;
                if (path.EndsWith(preferred)) { template = signal.gameObject; break; }
            }
            Templates.FoundBySearch(this, $"{type}", ExactPath(type), template);
        }
        if (template == null) {
            log.LogWarning($"[PuzzleLight] No vanilla '{wantedName}' is loaded, so '{name}' ({type}) has nothing to copy.");
            return;
        }

        Vector3 templateScale = template.transform.lossyScale;
        GameObject copy = Templates.Clone(template, transform);
        copy.name = CopyName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(true);

        Vector3 parentScale = transform.lossyScale;
        copy.transform.localScale = new Vector3(
            Mathf.Approximately(parentScale.x, 0f) ? templateScale.x : templateScale.x / parentScale.x,
            Mathf.Approximately(parentScale.y, 0f) ? templateScale.y : templateScale.y / parentScale.y,
            Mathf.Approximately(parentScale.z, 0f) ? templateScale.z : templateScale.z / parentScale.z);

        // The game switches off renderers of far-away objects; the copy inherits that
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true)) {
            Renderer r = c != null ? c.TryCast<Renderer>() : null;
            if (r != null) r.enabled = true;
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<Light>(), true)) {
            Light l = c != null ? c.TryCast<Light>() : null;
            if (l == null) continue;
            if (lamp == null) lamp = l;
            lights.Add(l);
            log.LogInfo($"[PuzzleLight] '{name}' ({type}): the lamp's own light is {l.type}, range {l.range:0.##}, intensity {l.intensity:0.##}, spot angle {l.spotAngle:0.#}, shadows {l.shadows}.");
            baseLightColor.Add(l.color);
            ApplyLightSettingsTo(l, false);
            baseIntensity.Add(intensity > 0f ? intensity : l.intensity);
        }

        // The glowing parts of a lamp get their colour from these helpers
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PropertyBlockHelper>(), true)) {
            PropertyBlockHelper helper = c != null ? c.TryCast<PropertyBlockHelper>() : null;
            if (helper == null) continue;
            glowHelpers.Add(helper);
        }

        // Hide the creator's placeholder
        Renderer placeholder = GetComponent<Renderer>();
        if (placeholder != null) placeholder.enabled = false;
        Collider placeholderCollider = GetComponent<Collider>();
        if (placeholderCollider != null) placeholderCollider.enabled = false;

        // Everything that carries a network id must get its own, or the copy would act on the
        // vanilla signal it was copied from.
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<PeckSwitch>(), true)) {
            PeckSwitch sw = c != null ? c.TryCast<PeckSwitch>() : null;
            if (sw == null) continue;
            switches.Add(sw);
        }
        foreach (Component c in copy.GetComponentsInChildren(Il2CppType.Of<TrackedPeckState>(), true)) {
            TrackedPeckState st = c != null ? c.TryCast<TrackedPeckState>() : null;
            if (st == null) continue;
            states.Add(st);
            if (lampState == null && st.transform.parent == copy.transform) lampState = st;
        }
        if (lampState == null && states.Count > 0) lampState = states[0];
        ticketIds = Tickets.Allocate(switches.Count + states.Count);
        AssignTickets(false);

        // Made here, during setup, so every PC hands out the same network ids in order
        syncR = new SyncedValue(transform, "light r " + name);
        syncG = new SyncedValue(transform, "light g " + name);
        syncB = new SyncedValue(transform, "light b " + name);
        syncParty = new SyncedValue(transform, "light party " + name);
        syncHold = new SyncedValue(transform, "light hold " + name);

        if (lampState == null) log.LogWarning($"[PuzzleLight] The copy for '{name}' has no state to drive; only its light will be switched (the lamp's glow and sound may not follow).");
    }

    private static string PathOf(Transform t, Transform stopAt = null) {
        string path = t.name;
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private void AssignTickets(bool register) {
        int slot = 0;
        foreach (PeckSwitch sw in switches) {
            if (register) Register(sw.Cast<LobbyNetworking.ITicketed>(), sw.ticket, ticketIds[slot]);
            sw.useTicket = true;
            sw.ticket = ticketIds[slot];
            sw.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
        foreach (TrackedPeckState st in states) {
            if (register) Register(st.Cast<LobbyNetworking.ITicketed>(), st.ticket, ticketIds[slot]);
            st.ticket = ticketIds[slot];
            st.shellReference = new SeaShell.ShellReference(ticketIds[slot]);
            slot++;
        }
    }

    private static void Register(LobbyNetworking.ITicketed item, ushort current, ushort wanted) {
        var office = LobbyNetworking.TicketOffice.instance;
        if (office == null) return;
        try {
            var tickets = office.tickets;
            if (current != wanted && tickets.ContainsKey(current) && tickets[current].Pointer == item.Pointer)
                tickets.Remove(current);
            tickets[wanted] = item;
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleLight] id registration failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Start() {
        AssignTickets(true);
        if (syncR != null) {
            // Other players: the host's colour, one channel at a time
            syncR.onChanged = v => { hostR = v + 128; OnHostColor(); };   // sent as -128..127, so 128 is added back
            syncG.onChanged = v => { hostG = v + 128; OnHostColor(); };
            syncB.onChanged = v => { hostB = v + 128; OnHostColor(); };
            syncR.Register(); syncG.Register(); syncB.Register();
            syncParty.onChanged = OnHostParty;
            syncParty.Register();
            syncHold.onChanged = v => { partyHold = Mathf.Clamp(v, 10, 127) / 10f; };
            syncHold.Register();
            hostR = Mathf.RoundToInt(color.r * 255f); hostG = Mathf.RoundToInt(color.g * 255f); hostB = Mathf.RoundToInt(color.b * 255f);
        }
    }

    private void OnHostColor() {
        color = new Color(hostR / 255f, hostG / 255f, hostB / 255f, 1f);
        useCustomColor = true;
        if (party) { partyPrev = shownColor; partyStart = Time.time; }   // a new party colour: blend to it from what is shown now
        ApplyColor();
    }

    // Other players: the host started or ended the party
    private void OnHostParty(int value) {
        if (value == 1) {
            party = true;
            fading = false;
            partyBegin = partyStart = Time.time;
            partyPrev = shownColor = useCustomColor ? color : Color.white;
            RandomBreath();
            return;
        }
        if (value == 3 || value == 4) {
            if (party) { party = false; fadeThenDefault = value == 4; BeginFade(); }
            return;
        }
        if (party || fading) { party = false; fading = false; level = 1f; RestoreIntensity(); }
        if (value == 2) { useCustomColor = false; color = Color.white; RestoreVanilla(); }
        else if (useCustomColor) ApplyColor();
    }

    // The light starts off. On the first frame, apply whatever TurnOn()/TurnOff() asked for by then
    // (e.g. from onPuzzleStop or a switch at spawn), whatever the vanilla one was showing
    private void Update() {
        if (!colorChecked) {   // the Inspector colour: white means "keep the lamp's own"
            colorChecked = true;
            if (!useCustomColor) useCustomColor = color != Color.white;
        }
        // Host: send the colour once it is a custom one
        if (useCustomColor && syncR != null && SyncedValue.IsHost) {
            // A synced value only holds -128..127, so each 0-255 channel is sent minus 128
            syncR.Push(Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f) - 128);
            syncG.Push(Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f) - 128);
            syncB.Push(Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f) - 128);
        }
        if (party && SyncedValue.IsHost && primed && Time.time >= partyStart + PartyCycleSeconds) NextPartyColor();
        // The game rewrites a lamp's glow whenever it switches, so keep our colour on top of it
        if (useCustomColor && primed && !party && Time.time >= recolorAt) { recolorAt = Time.time + 0.2f; ApplyColor(); }   // lens and the helpers' emission: the game resets both when the lamp switches
        if (nudgeAt > 0f && Time.time >= nudgeAt) {
            nudgeAt = 0f;
            if (on && lampState != null && !party && !fading) { lampState.SetState(OffValue); nudgeBack = Time.time + 0.15f; }
        }
        if (nudgeBack > 0f && Time.time >= nudgeBack) {
            nudgeBack = 0f;
            if (on && lampState != null) lampState.SetState(OnValue);
        }
        if (primed && !diagDone && Time.time >= diagAt) { diagDone = true; Diagnose(); }
        if (primed) return;
        diagAt = Time.time + 4f;
        primed = true;
        Apply(on, true);
    }

    // Party mode brightness, applied after the game has had its say each frame
    private void LateUpdate() {
        if (!primed) return;
        if (party) {
            float t = Time.time - partyStart;
            shownColor = Color.Lerp(partyPrev, color, Mathf.SmoothStep(0f, 1f, t / PartyTransitionSeconds));
            float dip = t < PartyTransitionSeconds ? 1f - PartyDip * Mathf.Sin(Mathf.PI * t / PartyTransitionSeconds) : 1f;   // never fully dark between colours
            float fadeIn = Mathf.SmoothStep(0f, 1f, (Time.time - partyBegin) / PartyFadeSeconds);
            float breath = 1f - BreathDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * breathRate + breathPhase));
            level = dip * fadeIn * breath;
            for (int i = 0; i < lights.Count; i++) {
                try {
                    if (lights[i] == null) continue;
                    lights[i].intensity = baseIntensity[i] * level;
                    lights[i].color = shownColor;
                } catch (Exception) { }
            }
            ApplyGlow();
        } else if (!fading) {
            for (int i = 0; i < lights.Count; i++) ApplyLightSettingsTo(lights[i], on);   // the game may reset these when the lamp switches
            if (useCustomColor) ApplyGlow();   // every frame, like party mode: the game rewrites the lens whenever it likes
        } else {
            float f = FadeAmount();
            for (int i = 0; i < lights.Count; i++) {
                try {
                    if (lights[i] == null) continue;
                    lights[i].intensity = baseIntensity[i] * Mathf.Lerp(fadeFromLevel, 1f, f);
                    lights[i].color = Color.Lerp(fadeFromColor, baseLightColor[i], f);
                } catch (Exception) { }
            }
            ApplyGlow();
            if (Time.time - fadeStart >= PartyEndFadeSeconds) FinishFade();   // every player finishes on their own clock
        }
    }

    // The Inspector's Range, Spot Angle and (while on) Intensity, on top of the lamp's own values
    private void ApplyLightSettingsTo(Light l, bool lampOn) {
        if (l == null) return;
        try {
            if (range > 0f) l.range = range;
            if (spotAngle > 0f && l.type == LightType.Spot) {
                l.spotAngle = spotAngle;
                if (l.innerSpotAngle > spotAngle) l.innerSpotAngle = spotAngle;
            }
            if (lampOn && intensity > 0f) l.intensity = intensity;
        } catch (Exception) { }
    }

    private float FadeAmount() { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - fadeStart) / PartyEndFadeSeconds)); }

    // Starts the fade from whatever the lamp shows right now
    private void BeginFade() {
        fading = true;
        fadeStart = Time.time;
        fadeFromColor = shownColor;
        fadeFromLevel = level;
        fadeFromLens = Color.Lerp(Color.black, Hue(shownColor), level);
    }

    private void FinishFade() {
        fading = false;
        level = 1f;
        RestoreIntensity();
        if (fadeThenDefault) {
            color = Color.white;
            useCustomColor = false;
            RestoreVanilla();
        } else if (SyncedValue.IsHost) {
            color = savedColor;
            useCustomColor = true;
            ApplyColor();   // reaches the other players through the colour sync
        }
    }

    private static Color Hue(Color c) {
        float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return peak <= 0f ? Color.white : new Color(c.r / peak, c.g / peak, c.b / peak, 1f);
    }

    // The lens colour for glow part i right now: party colour (black when dark), fading back to the lamp's own, or the plain colour
    private Color LensFor(int i) {
        if (fading) return Color.Lerp(fadeFromLens, i < glowOriginal.Count ? glowOriginal[i] : Color.white, FadeAmount());
        if (party) return Color.Lerp(Color.black, Hue(shownColor), level);
        return Hue(color);
    }

    private void RandomBreath() {
        breathRate = UnityEngine.Random.Range(1f, 1.8f) * 2f * Mathf.PI;
        breathPhase = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
    }

    // Host: the next party colour, a bright one that differs clearly from the last
    private void NextPartyColor() {
        partyPrev = shownColor;   // blend away from what is shown now
        partyHue = Mathf.Repeat(partyHue + UnityEngine.Random.Range(0.2f, 0.8f), 1f);
        color = Color.HSVToRGB(partyHue, UnityEngine.Random.Range(0.75f, 1f), 1f);
        useCustomColor = true;
        // How long this colour stays at full brightness: random, so lights drift out of step with each other
        int tenths = Mathf.RoundToInt(UnityEngine.Random.Range(PartyHoldMinSeconds, PartyHoldMaxSeconds) * 10f);
        partyHold = tenths / 10f;
        if (syncHold != null) syncHold.Push(tenths);
        partyStart = Time.time;
        ApplyColor();
    }

    private void RestoreIntensity() {
        for (int i = 0; i < lights.Count; i++) {
            try { if (lights[i] != null) lights[i].intensity = baseIntensity[i]; } catch (Exception) { }
        }
    }

    // Back to the lamp's own colours (when no colour was set before the party)
    private void RestoreVanilla() {
        for (int i = 0; i < lights.Count; i++) {
            try { if (lights[i] != null) lights[i].color = baseLightColor[i]; } catch (Exception) { }
        }
        if (!glowScanned) ScanGlow();
        for (int i = 0; i < glowRenderers.Count && i < glowOriginal.Count; i++) {
            try { if (glowRenderers[i] != null) glowRenderers[i].material.SetColor(LensColorProperty, glowOriginal[i]); } catch (Exception) { }
        }
    }

    // Colours both the light that is cast and the lamp's glowing parts
    private void ApplyColor() {
        if (!useCustomColor) return;
        var log = Plugin.log;
        foreach (Light l in lights) {
            try { if (l != null) l.color = color; } catch (Exception) { }
        }
        ApplyGlow();
        foreach (PropertyBlockHelper helper in glowHelpers) {
            if (helper == null) continue;
            try {
                var colors = helper.colorSettings;
                if (colors == null || colors.Length == 0) continue;
                // Prefer the emission (glow) entries; if none is named that way, recolour them all
                bool anyEmission = false;
                for (int i = 0; i < colors.Length; i++)
                    if ((colors[i].propertyName ?? "").ToLowerInvariant().Contains("emis")) anyEmission = true;
                for (int i = 0; i < colors.Length; i++) {
                    var entry = colors[i];
                    if (anyEmission && !(entry.propertyName ?? "").ToLowerInvariant().Contains("emis")) continue;
                    // Keep the original brightness (glow colours are often brighter than 1)
                    Color old = entry.color;
                    float strength = Mathf.Max(1f, Mathf.Max(old.r, Mathf.Max(old.g, old.b)));
                    entry.color = new Color(color.r * strength, color.g * strength, color.b * strength, old.a);
                    colors[i] = entry;
                }
                helper.Refresh();
            } catch (Exception ex) {
                log.LogWarning($"[PuzzleLight] '{name}' could not recolour '{helper.name}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private const string LensColorProperty = GameRefs.Light.LensColorProperty;
    private const string EmissiveMaskProperty = "_EmissiveMask0";
    private const float EmissiveOnValue = 5f;
    private readonly List<Renderer> glowRenderers = new List<Renderer>();
    private readonly List<Color> glowOriginal = new List<Color>();   // the lamp's own lens colours
    private bool glowScanned;

    // The lamp parts that glow are the ones carrying the game's PropertyBlockHelper
    private void ScanGlow() {
        glowScanned = true;
        foreach (PropertyBlockHelper helper in glowHelpers) {
            if (helper == null) continue;
            try {
                Renderer r = helper.targetRenderer;
                if (r == null) r = helper.gameObject.GetComponent<Renderer>();
                Material mat = r != null ? r.sharedMaterial : null;
                if (mat == null || !mat.HasProperty(LensColorProperty)) {
                    continue;
                }
                glowRenderers.Add(r);
                glowOriginal.Add(mat.GetColor(LensColorProperty));
            } catch (Exception) { }
        }
    }

    // Colours the glowing part of the lamp the way the game's coloured lamps do. Only the
    // colour's hue is used; how bright the glow is stays up to the lamp being on or off.
    private void ApplyGlow() {
        if (!glowScanned) ScanGlow();
        if (!party && !fading && Mathf.Max(color.r, Mathf.Max(color.g, color.b)) <= 0f) return;
        for (int i = 0; i < glowRenderers.Count; i++) {
            Renderer r = glowRenderers[i];
            if (r == null) continue;
            try {
                r.material.SetColor(LensColorProperty, LensFor(i));
                // The lens only glows while this is up: the game sets it when a lamp is on, but not for a
                // lamp that was switched on in code, so set it ourselves (party lamps show 5)
                if (!party && !fading) r.material.SetFloat(EmissiveMaskProperty, on ? EmissiveOnValue : 0f);
            } catch (Exception) { }
        }
    }

    // Changes the colour during play: "#FF0000", "FF0000", "#F00" or a colour name like "red".
    // Host only: the colour reaches everyone from the host.
    public void SetColor(string hex) {
        if (!PuzzleSDK.IsHost) return;
        Color parsed;
        if (string.IsNullOrEmpty(hex) || !(ColorUtility.TryParseHtmlString(hex, out parsed) || ColorUtility.TryParseHtmlString("#" + hex, out parsed))) {
            Plugin.log.LogWarning($"[PuzzleLight] '{name}': '{hex}' is not a colour like #FF0000");
            return;
        }
        if (party || fading) EndParty(false);
        color = parsed;
        useCustomColor = true;
        ApplyColor();
    }

    private void SetOn(bool value) {
        if (on == value && primed) return;
        on = value;
        if (primed) Apply(value, false);
    }

    // ---- Callable from code or from an Inspector event ----
    // Host only: on/off reaches everyone from the host
    public void TurnOn() { if (!PuzzleSDK.IsHost) return; if (party || fading) EndParty(false); SetOn(true); }
    public void TurnOff() { if (!PuzzleSDK.IsHost) return; if (party || fading) EndParty(false); SetOn(false); }

    // Host only. Turns the lamp on and blends it through random bright colours, never fully dark, with a
    // gentle pulse. StopParty() fades it back to white.
    public void StartParty() {
        if (!PuzzleSDK.IsHost || party) return;
        if (fading) FinishFade();   // a fade-out is still running: settle it first
        party = true;
        savedColor = color;
        savedCustom = useCustomColor;
        partyHue = UnityEngine.Random.value;
        partyBegin = Time.time;
        shownColor = savedCustom ? savedColor : Color.white;
        RandomBreath();
        syncParty.Push(1);
        SetOn(true);
        NextPartyColor();
    }

    // Host only. Ends party mode with a one second fade back to the lamp's own (white) look. The lamp
    // stays on; if it had a Color set before the party, that colour is applied when the fade ends.
    public void StopParty() {
        if (!PuzzleSDK.IsHost || !party) return;
        party = false;
        fadeThenDefault = !savedCustom;
        BeginFade();
        syncParty.Push(fadeThenDefault ? 4 : 3);
    }

    // Ends everything at once (used when something else takes over the lamp)
    private void EndParty(bool turnOff) {
        if (fading) {
            FinishFade();
            if (turnOff) SetOn(false);
            return;
        }
        if (!party) return;
        party = false;
        level = 1f;
        RestoreIntensity();
        if (savedCustom) {
            color = savedColor;
            useCustomColor = true;
            ApplyColor();
            syncParty.Push(0);
        } else {
            color = Color.white;
            useCustomColor = false;
            RestoreVanilla();
            syncParty.Push(2);
        }
        if (turnOff) SetOn(false);
    }

    private float nudgeAt;      // 0 = nothing to do; otherwise when to flick the lamp off
    private float nudgeBack;    // when to flick it back on
    private bool diagDone;
    private float diagAt;

    // One line per lamp a few seconds after it starts: why does (or doesn't) its glow show?
    private void Diagnose() {
        try {
            int stateValue = -1;
            if (lampState != null) PeckValue.TryRead(lampState, out stateValue);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[PuzzleLight][diag] '{name}': on={on} state={stateValue} custom={useCustomColor} color={color} party={party} glowParts={glowRenderers.Count} helpers={glowHelpers.Count}");
            foreach (PropertyBlockHelper h in glowHelpers) {
                if (h == null) continue;
                var cs = h.colorSettings;
                sb.Append(" | helper '" + h.name + "' colours:");
                if (cs != null) for (int i = 0; i < cs.Count; i++) sb.Append($" {cs[i].propertyName}={cs[i].color}");
                var fs = h.floatSettings;
                if (fs != null) { sb.Append(" floats:"); for (int i = 0; i < fs.Count; i++) sb.Append($" {fs[i].propertyName}={fs[i].floatValue}"); }
            }
            for (int i = 0; i < glowRenderers.Count; i++)
                if (glowRenderers[i] != null) sb.Append($" | lens[{i}]={glowRenderers[i].material.GetColor(LensColorProperty)}");
            foreach (Light l in lights) if (l != null) sb.Append($" | light enabled={l.enabled} intensity={l.intensity:0.##} color={l.color}");
            for (int i = 0; i < glowRenderers.Count; i++) {
                Renderer r = glowRenderers[i];
                if (r == null) continue;
                Material m = r.material;
                sb.Append($" | shader={(m.shader != null ? m.shader.name : "?")}");
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                string[] probe = { "_EmissiveMask0" };
                foreach (string pn in probe) {
                    string part = "";
                    if (m.HasProperty(pn)) {
                        try { part += " mat=" + m.GetFloat(pn); } catch (Exception) { try { part += " mat=" + m.GetColor(pn); } catch (Exception) { } }
                    }
                    if (block.HasProperty(pn)) {
                        try { part += " block=" + block.GetFloat(pn); } catch (Exception) { try { part += " block=" + block.GetColor(pn); } catch (Exception) { } }
                    }
                    if (part.Length > 0) sb.Append($" | {pn}:{part}");
                }
            }
            Plugin.log.LogInfo(sb.ToString());
        } catch (Exception ex) {
            Plugin.log.LogWarning($"[PuzzleLight][diag] '{name}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Apply(bool value, bool quiet) {
        var log = Plugin.log;
        try {
            if (lampState != null) { if (PuzzleSDK.IsHost) lampState.SetState(value ? OnValue : OffValue); }   // other players: arrives from the host
            else if (lamp != null) lamp.gameObject.SetActive(value);
            ApplyColor();
            recolorAt = Time.time + 0.3f;
            // A lamp switched on the moment it spawns never shows its glow (the lens only lights on a
            // change of state). Flick it off and on again once, a moment later, to start the glow.
            if (quiet && value && lampState != null && PuzzleSDK.IsHost) nudgeAt = Time.time + 0.5f;
        } catch (Exception ex) {
            log.LogWarning($"[PuzzleLight] '{name}' could not turn {(value ? "on" : "off")}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

} // namespace BigPuzzleSDK
