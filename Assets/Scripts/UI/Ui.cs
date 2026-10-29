using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChessGame
{
    /// <summary>Small uGUI construction kit so every screen shares one look.</summary>
    public static class Ui
    {
        // Alphas are high on purpose: UI blends in linear space, where dark translucent panels look far more see-through.
        public static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.1f, 0.985f);
        public static readonly Color CardBg = new Color(0.13f, 0.145f, 0.17f, 0.97f);
        public static readonly Color ButtonBg = new Color(0.19f, 0.21f, 0.25f, 1f);
        public static readonly Color ButtonHover = new Color(0.27f, 0.3f, 0.36f, 1f);
        public static readonly Color ButtonPressed = new Color(0.34f, 0.38f, 0.45f, 1f);
        public static readonly Color Accent = new Color(0.88f, 0.72f, 0.35f, 1f);
        public static readonly Color AccentDark = new Color(0.55f, 0.43f, 0.18f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.93f, 0.94f, 1f);
        public static readonly Color TextDim = new Color(0.63f, 0.66f, 0.71f, 1f);
        public static readonly Color Danger = new Color(0.86f, 0.33f, 0.3f, 1f);

        static Font _font;
        public static Font DefaultFont => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static Sprite _round, _roundSmall;
        public static Sprite Round => _round ??= TextureGen.RoundedRect(14);
        public static Sprite RoundSmall => _roundSmall ??= TextureGen.RoundedRect(8);

        /// <summary>Played on any button click (set by the app).</summary>
        public static Action ClickSound;

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool rounded = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded) { img.sprite = Round; img.type = Image.Type.Sliced; }
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft,
            FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static LayoutElement Size(this Component c, float prefHeight = -1, float prefWidth = -1, float flexW = -1, float flexH = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (prefHeight >= 0) { le.preferredHeight = prefHeight; le.minHeight = prefHeight; }
            if (prefWidth >= 0) { le.preferredWidth = prefWidth; le.minWidth = prefWidth; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VList(Component c, float spacing, int pad = 0, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HList(Component c, float spacing, int pad = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        public static Button Button(Transform parent, string text, Action onClick, int fontSize = 22, bool accent = false)
        {
            var img = Panel(parent, text, Color.white);
            img.sprite = RoundSmall;
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = accent ? Accent : ButtonBg;
            cb.highlightedColor = accent ? Color.Lerp(Accent, Color.white, 0.25f) : ButtonHover;
            cb.selectedColor = cb.normalColor;
            cb.pressedColor = accent ? AccentDark : ButtonPressed;
            cb.disabledColor = new Color(0.21f, 0.22f, 0.25f, 0.55f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label(img.transform, text, fontSize, accent ? new Color(0.1f, 0.08f, 0.04f) : Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.Stretch(8, 2, 8, 2);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            b.onClick.AddListener(() => { ClickSound?.Invoke(); onClick?.Invoke(); });
            return b;
        }

        public static Text LabelOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>Greys out the label too, so a disabled state does not rely on colour of the background alone.</summary>
        public static void SetEnabled(Button b, bool on)
        {
            if (b.interactable == on) return;
            b.interactable = on;
            var t = LabelOf(b);
            var c = t.color; c.a = on ? 1f : 0.35f; t.color = c;
        }

        public static RectTransform Row(Transform parent, string label, float height = 52)
        {
            var row = Rect(parent, label);
            HList(row, 16);
            row.Size(height);
            var l = Label(row, label, 22, Text);
            l.Size(flexW: 1);
            return row;
        }

        /// <summary>"‹ value ›" selector: accessible, no dropdown template needed.</summary>
        public static Action<int> Cycler(Transform parent, string label, IReadOnlyList<string> options, int index, Action<int> onChange,
            float width = 380)
        {
            var row = Row(parent, label);
            var box = Rect(row, "Cycler");
            box.Size(prefWidth: width);
            HList(box, 6);
            int cur = Mathf.Clamp(index, 0, options.Count - 1);
            Text value = null;
            void Set(int i, bool notify)
            {
                cur = (i % options.Count + options.Count) % options.Count;
                value.text = options[cur];
                if (notify) onChange(cur);
            }
            Button(box, "<", () => Set(cur - 1, true), 24).Size(prefWidth: 52);
            var valBg = Panel(box, "Value", CardBg);
            valBg.Size(flexW: 1);
            value = Label(valBg.transform, options[cur], 21, Text, TextAnchor.MiddleCenter);
            value.rectTransform.Stretch(4, 0, 4, 0);
            Button(box, ">", () => Set(cur + 1, true), 24).Size(prefWidth: 52);
            return i => Set(i, false);
        }

        public static Action<bool> Switch(Transform parent, string label, bool value, Action<bool> onChange, float width = 380)
        {
            var row = Row(parent, label);
            bool cur = value;
            Button b = null;
            void Refresh()
            {
                LabelOf(b).text = cur ? "On" : "Off";
                var cb = b.colors;
                cb.normalColor = cb.selectedColor = cur ? AccentDark : ButtonBg;
                b.colors = cb;
            }
            b = Button(row, "", () => { cur = !cur; Refresh(); onChange(cur); }, 21);
            b.Size(prefWidth: width);
            Refresh();
            return v => { cur = v; Refresh(); };
        }

        public static Slider Slider(Transform parent, string label, float min, float max, float value, Action<float> onChange,
            Func<float, string> format, float width = 380)
        {
            var row = Row(parent, label);
            var box = Rect(row, "Slider");
            box.Size(prefWidth: width);
            HList(box, 12);

            var sRt = Rect(box, "Track");
            sRt.Size(flexW: 1);
            var bg = Panel(sRt, "Background", CardBg);
            bg.rectTransform.anchorMin = new Vector2(0, 0.5f); bg.rectTransform.anchorMax = new Vector2(1, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0, 10);
            var fillArea = Rect(sRt, "Fill Area");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(0, 10);
            var fill = Panel(fillArea, "Fill", Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect(sRt, "Handle Area").Stretch(10, 0, 10, 0);
            var handle = Panel(handleArea, "Handle", Text);
            handle.rectTransform.sizeDelta = new Vector2(22, 0);
            handle.rectTransform.anchorMin = new Vector2(0, 0.2f);
            handle.rectTransform.anchorMax = new Vector2(0, 0.8f);

            var s = sRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.value = value;
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            var val = Label(box, format(value), 20, TextDim, TextAnchor.MiddleRight);
            val.Size(prefWidth: 70);
            s.onValueChanged.AddListener(v => { val.text = format(v); onChange(v); });
            return s;
        }

        public static ScrollRect Scroll(Transform parent, out RectTransform content, float spacing = 6)
        {
            var root = Rect(parent, "Scroll");
            var sr = root.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;
            var viewport = Rect(root, "Viewport").Stretch(0, 0, 14, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0); // receives scroll/drag events
            content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            VList(content, spacing);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport;
            sr.content = content;

            var bar = Rect(root, "Scrollbar");
            bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, 0.5f);
            bar.sizeDelta = new Vector2(8, 0);
            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.color = new Color(1, 1, 1, 0.05f);
            var sliding = Rect(bar, "Sliding").Stretch();
            var handle = Panel(sliding, "Handle", new Color(1, 1, 1, 0.25f));
            handle.rectTransform.sizeDelta = Vector2.zero;
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle.rectTransform;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.targetGraphic = handle;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            return sr;
        }

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void Show(GameObject go, bool on, float duration = 0.16f)
        {
            if (!go.TryGetComponent<Fader>(out var f)) f = go.AddComponent<Fader>();
            f.Show(on, duration);
        }
    }

    /// <summary>Fades a panel in/out and blocks input only while visible.</summary>
    public sealed class Fader : MonoBehaviour
    {
        CanvasGroup _g;
        float _target, _speed;

        public void Show(bool on, float duration)
        {
            if (_g == null && !TryGetComponent(out _g)) _g = gameObject.AddComponent<CanvasGroup>();
            _target = on ? 1 : 0;
            _speed = duration > 0 ? 1f / duration : float.MaxValue;
            if (on) { gameObject.SetActive(true); if (duration <= 0) _g.alpha = 1; }
            _g.interactable = _g.blocksRaycasts = on;
            if (!on && duration <= 0) { _g.alpha = 0; gameObject.SetActive(false); }
            enabled = true;
        }

        void Update()
        {
            _g.alpha = Mathf.MoveTowards(_g.alpha, _target, _speed * Time.unscaledDeltaTime);
            if (Mathf.Approximately(_g.alpha, _target))
            {
                enabled = false;
                if (_target == 0) gameObject.SetActive(false);
            }
        }
    }
}
