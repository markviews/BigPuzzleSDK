using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BigPuzzleSDK {

    // A message over the password screen that lists what doesn't match the host (SDK version, missing or
    // out-of-date puzzles; worked out from the lobby's data by LobbyData). Everything under it is covered, and
    // the only way out is its Back button: a copy of the password screen's own Cancel button, which calls the
    // screen's own Cancel() and closes the message.
    // Needs project references to UnityEngine.UI.dll, Unity.TextMeshPro.dll and UnityEngine.UIModule.dll
    // (already needed by HostNotice).
    internal static class JoinPopup {
        private const string Title = "PuzzleSDK versions don't match host:";
        private const string LineColor = "#FF5A5A";   // red, like HostNotice's lines

        private static string[] pending;           // what to show once the password screen is up (null = nothing)
        private static PasswordMenu cachedMenu;
        private static PasswordMenu menu;          // the screen the popup is over (while it's open)
        private static TMP_InputField lockedField; // the password box, switched off while the popup is open
        private static bool fieldWasInteractable;
        private static GameObject root;
        private static bool open;
        private static float nextCheck;
        private static float pendingSince;
        private static bool reportedWaiting;

        // LobbyData: this PC doesn't match the lobby it's joining
        internal static void Set(string[] lines) {
            if (lines == null || lines.Length == 0) { pending = null; return; }
            pending = lines;
            pendingSince = Time.unscaledTime;
            reportedWaiting = false;
            Plugin.log.LogInfo("[JoinPopup] the password screen will show: " + string.Join(" | ", lines));
        }

        // LobbyData: nothing to warn about (or not a lobby the SDK matters for)
        internal static void Clear() { pending = null; }

        // Every frame (cheap when nothing is waiting): opens the popup when the password screen is up
        internal static void Tick() {
            if (open) {
                bool gone;
                try { gone = menu == null || root == null || !menu.gameObject.activeInHierarchy; } catch (Exception) { gone = true; }
                if (gone) Close();   // the screen was left some other way
                return;
            }
            if (pending == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            PasswordMenu found = ActiveMenu();
            if (found != null) { Open(found); return; }
            if (!reportedWaiting && Time.unscaledTime - pendingSince > 6f) {
                reportedWaiting = true;
                string state;
                try { state = cachedMenu == null ? "no PasswordMenu object found" : "PasswordMenu found but not shown (activeSelf=" + cachedMenu.gameObject.activeSelf + ", activeInHierarchy=" + cachedMenu.gameObject.activeInHierarchy + ")"; } catch (Exception) { state = "PasswordMenu state unreadable"; }
                Plugin.log.LogInfo($"[JoinPopup] 6 s later the password screen still isn't up: {state}. A lobby without a password may skip that screen, so nothing is shown.");
            }
        }

        private static PasswordMenu ActiveMenu() {
            try {
                if (cachedMenu == null) {
                    foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PasswordMenu>())) {
                        PasswordMenu candidate = obj != null ? obj.TryCast<PasswordMenu>() : null;
                        if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                        cachedMenu = candidate;
                        break;
                    }
                }
                return cachedMenu != null && cachedMenu.gameObject.activeInHierarchy ? cachedMenu : null;
            } catch (Exception) {
                cachedMenu = null;
                return null;
            }
        }

        private static void Open(PasswordMenu screen) {
            string[] lines = pending;
            try {
                menu = screen;
                Transform original = FindChild(screen.transform, GameRefs.PasswordPopup.CancelButtonName);

                // The same canvas settings as the game's UI, so sizes and fonts come out the same
                CanvasScaler theirs = null;
                try { theirs = screen.GetComponentInParent<CanvasScaler>(); } catch (Exception) { }
                root = new GameObject("BigPuzzleSDK Join Popup");
                UnityEngine.Object.DontDestroyOnLoad(root);
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32001;   // above the game's UI and the SDK's other messages
                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                if (theirs != null) {
                    scaler.uiScaleMode = theirs.uiScaleMode;
                    scaler.referenceResolution = theirs.referenceResolution;
                    scaler.screenMatchMode = theirs.screenMatchMode;
                    scaler.matchWidthOrHeight = theirs.matchWidthOrHeight;
                    scaler.scaleFactor = theirs.scaleFactor;
                    scaler.referencePixelsPerUnit = theirs.referencePixelsPerUnit;
                } else {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                root.AddComponent<GraphicRaycaster>();

                // Invisible, but swallows clicks meant for the password screen
                GameObject dim = new GameObject("Dim");
                dim.transform.SetParent(root.transform, false);
                Image dimImage = dim.AddComponent<Image>();
                dimImage.color = new Color(0f, 0f, 0f, 0f);   // invisible, only swallows clicks
                dimImage.raycastTarget = true;
                Stretch(dim.GetComponent<RectTransform>());

                // Sizes follow the game's own: the screen's width, and the Cancel button's size and text size
                float screenWidth = screen.GetComponent<RectTransform>().rect.width;
                if (screenWidth < 200f) screenWidth = 1920f;
                Vector2 buttonSize = new Vector2(420f, 110f);
                float fontSize = 36f;
                if (original != null) {
                    Vector2 measured = original.GetComponent<RectTransform>().rect.size;
                    if (measured.x > 10f && measured.y > 10f) buttonSize = measured;
                    TextMeshProUGUI label = original.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (label != null && label.fontSize > 8f) fontSize = label.fontSize;
                }

                // The message box
                string content = "<b><nobr><noparse>" + Title + "</noparse></nobr></b>";   // nobr: the title stays on one line
                foreach (string line in lines) content += "\n<color=" + LineColor + "><noparse>" + line + "</noparse></color>";

                float width = screenWidth * 0.65f, pad = fontSize * 0.8f, gap = fontSize * 1.8f;
                GameObject box = new GameObject("Message");
                box.transform.SetParent(root.transform, false);
                Image boxImage = box.AddComponent<Image>();
                boxImage.color = Color.white;   // one white panel behind the text and the button
                boxImage.raycastTarget = false;
                RectTransform boxRect = box.GetComponent<RectTransform>();
                Center(boxRect);

                GameObject textObject = new GameObject("Text");
                textObject.transform.SetParent(box.transform, false);
                TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
                TMP_FontAsset font = HostNotice.GameFont();
                if (font != null) text.font = font;
                text.fontSize = fontSize;
                text.color = Color.black;
                text.alignment = TextAlignmentOptions.Top;
                text.raycastTarget = false;
                text.text = content;
                float textHeight = text.GetPreferredValues(content, width - 2f * pad, 0f).y;
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0f, 1f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.pivot = new Vector2(0.5f, 1f);
                textRect.sizeDelta = new Vector2(-2f * pad, textHeight);
                textRect.anchoredPosition = new Vector2(0f, -pad);

                // The panel holds the text, then the button, centred on the screen
                // (the panel reaches further down than its content, so it hides the game's own button under it)
                float contentHeight = pad + textHeight + gap + buttonSize.y + pad;
                float extra = buttonSize.y * 0.8f;
                boxRect.sizeDelta = new Vector2(width, contentHeight + extra);
                boxRect.anchoredPosition = new Vector2(0f, -extra / 2f);
                Vector2 buttonPosition = new Vector2(0f, -(contentHeight / 2f - pad - buttonSize.y / 2f));

                GameObject button = original != null ? MakeCopy(original, buttonSize, buttonPosition) : MakeFallback(buttonSize, buttonPosition, fontSize, font);

                // Only the Back button can be used: the password box is switched off, and the button is selected
                try {
                    lockedField = screen.passwordField;
                    if (lockedField != null) { fieldWasInteractable = lockedField.interactable; lockedField.interactable = false; }
                } catch (Exception) { lockedField = null; }
                try { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button); } catch (Exception) { }

                open = true;
                Plugin.log.LogInfo($"[JoinPopup] shown over the password screen ({(original != null ? "copy of the game's Cancel button" : "the game's Cancel button wasn't found, plain button used")}).");
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[JoinPopup] could not show the message ({ex.GetType().Name}: {ex.Message}); the problems are in the log instead.");
                pending = null;   // don't try again for this lobby
                Close();
            }
        }

        // The game's own Cancel button, copied as it is (image, text, sounds, highlight), with its clicks replaced by ours
        private static GameObject MakeCopy(Transform original, Vector2 size, Vector2 position) {
            GameObject copy = UnityEngine.Object.Instantiate((UnityEngine.Object)original.gameObject, root.transform).Cast<GameObject>();
            copy.name = original.gameObject.name + " (SDK)";
            copy.SetActive(true);
            RectTransform rect = copy.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Button button = copy.GetComponent<Button>();
            if (button != null) {
                // The copy still points at the real screen's methods: switch those off and use ours
                int count = button.onClick.GetPersistentEventCount();
                for (int i = 0; i < count; i++) button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(new Action(OnBack));
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;   // arrow keys / stick must not walk onto the screen underneath
                button.navigation = navigation;
            }
            return copy;
        }

        // Only if the game's Cancel button can't be found: a plain button, so there is always a way back
        private static GameObject MakeFallback(Vector2 size, Vector2 position, float fontSize, TMP_FontAsset font) {
            GameObject go = new GameObject("Back (SDK)");
            go.transform.SetParent(root.transform, false);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.25f, 1f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(new Action(OnBack));
            RectTransform rect = go.GetComponent<RectTransform>();
            Center(rect);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(go.transform, false);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = "Back";
            Stretch(textObject.GetComponent<RectTransform>());
            return go;
        }

        // The button: the game's own back function, then close this message
        private static void OnBack() {
            PasswordMenu screen = menu;
            pending = null;   // not again for this lobby
            try {
                if (screen != null) screen.Cancel();
            } catch (Exception ex) {
                Exception inner = ex.InnerException ?? ex;
                Plugin.log.LogWarning($"[JoinPopup] the password screen's Cancel() failed: {inner.GetType().Name}: {inner.Message}");
            }
            Close();
        }

        private static void Close() {
            try { if (lockedField != null) lockedField.interactable = fieldWasInteractable; } catch (Exception) { }
            lockedField = null;
            try { if (root != null) UnityEngine.Object.Destroy(root); } catch (Exception) { }
            root = null;
            menu = null;
            open = false;
        }

        private static Transform FindChild(Transform parent, string name) {
            Transform direct = parent.Find(name);
            if (direct != null) return direct;
            for (int i = 0; i < parent.childCount; i++) {
                Transform deeper = FindChild(parent.GetChild(i), name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static void Center(RectTransform rect) {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

} // namespace BigPuzzleSDK