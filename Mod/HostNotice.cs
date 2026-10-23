using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BigPuzzleSDK {

    // A small message box at the top of the screen, shown to this player only (used to tell the
    // host when someone joins without the SDK or with different puzzles). Several messages stack
    // under each other; each fades out after its own 20 seconds. Built from Unity UI the first time
    // it's needed, using the font the game's own UI uses.
    // Needs project references to BepInEx/interop/UnityEngine.UIModule.dll and
    // BepInEx/interop/UnityEngine.UI.dll (and Unity.TextMeshPro.dll, already used by PuzzleTimer).
    internal static class HostNotice {
        private const float ShowSeconds = 20f;
        private const float FadeInSeconds = 0.25f;
        private const float FadeOutSeconds = 1f;
        private const float Width = 820f;
        private const float Top = 48f;       // gap above the first message
        private const float Spacing = 10f;   // gap between stacked messages

        private const string LineColor = "#FF5A5A";   // red, for every line under the title

        // One message on screen: its own box, fading and timing out on its own
        private class Notice {
            public GameObject box;
            public CanvasGroup group;
            public RectTransform rect;
            public float height;
            public float shownAt;
        }
        private static readonly List<Notice> shown = new List<Notice>();   // top to bottom, oldest first

        private static GameObject root;
        private static TMP_FontAsset font;
        private static bool broken;   // building the UI failed once: the log has the messages anyway

        // Show a message: a title, then any number of short red lines under it. Messages stack
        // downwards; each disappears after its own time.
        internal static void Show(string title, params string[] lines) {
            if (broken || !Build()) return;
            try {
                // <noparse> so a puzzle or player name can't be read as formatting
                string content = "<noparse>" + title + "</noparse>";
                if (lines != null)
                    foreach (string line in lines)
                        content += "\n<color=" + LineColor + "><noparse>" + line + "</noparse></color>";
                shown.Add(MakeBox(content));
                Layout();
            } catch (Exception ex) {
                Plugin.log.LogWarning($"[SDK] could not show an on-screen message ({ex.GetType().Name}: {ex.Message}); see the log instead.");
            }
        }

        // Called every frame by BigPuzzleEditor: fades, and removes messages whose time is up
        internal static void Tick() {
            if (shown.Count == 0) return;
            bool removed = false;
            for (int i = shown.Count - 1; i >= 0; i--) {
                Notice notice = shown[i];
                float age = Time.unscaledTime - notice.shownAt;
                if (notice.box == null || age >= ShowSeconds) {
                    if (notice.box != null) UnityEngine.Object.Destroy(notice.box);
                    shown.RemoveAt(i);
                    removed = true;
                    continue;
                }
                float fadeIn = Mathf.Clamp01(age / FadeInSeconds);
                float fadeOut = Mathf.Clamp01((ShowSeconds - age) / FadeOutSeconds);
                notice.group.alpha = Mathf.Min(fadeIn, fadeOut);
            }
            if (removed) Layout();   // the ones below move up
        }

        // Places the messages one under another from the top of the screen
        private static void Layout() {
            float y = -Top;
            foreach (Notice notice in shown) {
                if (notice.rect == null) continue;
                notice.rect.anchoredPosition = new Vector2(0f, y);
                y -= notice.height + Spacing;
            }
        }

        private static Notice MakeBox(string content) {
            var box = new GameObject("Message");
            box.transform.SetParent(root.transform, false);
            Image background = box.AddComponent<Image>();
            background.color = new Color(0.07f, 0.07f, 0.09f, 0.88f);
            background.raycastTarget = false;
            CanvasGroup group = box.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;   // never gets in the way of the mouse
            group.alpha = 0f;
            RectTransform rect = box.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(box.transform, false);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 18f);
            textRect.offsetMax = new Vector2(-24f, -18f);
            if (font != null) text.font = font;
            text.fontSize = 32f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = content;

            float height = text.GetPreferredValues(content, Width - 48f, 0f).y + 36f;
            rect.sizeDelta = new Vector2(Width, height);
            return new Notice { box = box, group = group, rect = rect, height = height, shownAt = Time.unscaledTime };
        }

        // The screen-wide layer the messages sit on (made the first time one is shown)
        private static bool Build() {
            if (root != null) return true;
            try {
                root = new GameObject("BigPuzzleSDK Notice");
                UnityEngine.Object.DontDestroyOnLoad(root);

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;   // above the game's own UI

                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                font = GameFont();
                return true;
            } catch (Exception ex) {
                broken = true;
                Plugin.log.LogWarning($"[SDK] could not show an on-screen message ({ex.GetType().Name}: {ex.Message}); see the log instead.");
                if (root != null) UnityEngine.Object.Destroy(root);
                root = null;
                return false;
            }
        }

        // The font of a text the game itself shows, so the message looks like part of the game
        internal static TMP_FontAsset GameFont() {
            try {
                foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TextMeshProUGUI>())) {
                    TextMeshProUGUI candidate = obj.TryCast<TextMeshProUGUI>();
                    if (candidate == null || candidate.font == null || !candidate.gameObject.scene.IsValid()) continue;
                    return candidate.font;
                }
            } catch (Exception) { }
            try { return TMP_Settings.defaultFontAsset; } catch (Exception) { return null; }
        }
    }

} // namespace BigPuzzleSDK