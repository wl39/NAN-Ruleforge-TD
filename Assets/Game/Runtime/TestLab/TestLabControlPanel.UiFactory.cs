using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    public sealed partial class TestLabControlPanel
    {
        private RectTransform CreateSection(
            string titleText,
            float minimumHeight)
        {
            RectTransform section = CreatePanel(
                titleText + " Section",
                contentRoot,
                SectionColor);
            VerticalLayoutGroup layout =
                section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter =
                section.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            AddLayoutElement(
                section.gameObject,
                minimumHeight,
                -1f);

            Text title = CreateText(
                "Section Title",
                section,
                titleText,
                18,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            AddLayoutElement(title.gameObject, 30f, 30f);
            return section;
        }

        private InputField CreateLabeledInput(
            Transform parent,
            string labelText,
            string defaultValue,
            InputField.ContentType contentType,
            Action applyAction = null,
            string applyLabel = null,
            double stepSize = 1d,
            double minimumValue = -1000000000d,
            double maximumValue = 1000000000d)
        {
            RectTransform row = CreateRow(
                labelText + " Row",
                parent,
                46f);
            Text label = CreateText(
                "Label",
                row,
                labelText,
                14,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 14;
            LayoutElement labelLayout =
                label.gameObject.AddComponent<LayoutElement>();
            labelLayout.minWidth = 118f;
            labelLayout.preferredWidth = 190f;
            labelLayout.minHeight = 44f;
            labelLayout.preferredHeight = 44f;

            Button decrement = CreateButton(
                labelText + " Decrement",
                row,
                "−",
                ButtonColor,
                null);
            SetFixedLayoutSize(decrement.gameObject, 44f, 44f);

            InputField input = CreateInputField(
                labelText + " Input",
                row,
                defaultValue,
                contentType);
            AddFlexible(input.gameObject, 1f);

            Button increment = CreateButton(
                labelText + " Increment",
                row,
                "+",
                ButtonColor,
                null);
            SetFixedLayoutSize(increment.gameObject, 44f, 44f);

            TestLabNumericStepper stepper =
                row.gameObject.AddComponent<TestLabNumericStepper>();
            stepper.Configure(
                input,
                decrement,
                increment,
                ParseDefaultNumericValue(defaultValue),
                stepSize,
                minimumValue,
                maximumValue,
                contentType ==
                InputField.ContentType.IntegerNumber,
                applyAction);

            if (applyAction != null)
            {
                Button button = CreateButton(
                    labelText + " Apply",
                    row,
                    string.IsNullOrWhiteSpace(applyLabel)
                        ? "적용"
                        : applyLabel,
                    ButtonColor,
                    applyAction);
                SetFixedLayoutSize(button.gameObject, 68f, 44f);
            }

            return input;
        }

        private static double ParseDefaultNumericValue(
            string defaultValue)
        {
            return double.TryParse(
                defaultValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed)
                    ? parsed
                    : 0d;
        }

        private void AddButtonRow(
            Transform parent,
            params (string label, Action action)[] definitions)
        {
            RectTransform row = CreateRow(
                "Button Row",
                parent,
                42f);
            for (int i = 0; i < definitions.Length; i++)
            {
                (string label, Action action) definition =
                    definitions[i];
                Button button = CreateButton(
                    definition.label + " Button",
                    row,
                    definition.label,
                    i == 0
                        ? ImportantButtonColor
                        : ButtonColor,
                    definition.action);
                AddFlexible(button.gameObject, 1f);
            }
        }

        private RectTransform CreateRow(
            string objectName,
            Transform parent,
            float height)
        {
            GameObject rowHost = CreateUiObject(
                objectName,
                parent,
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            HorizontalLayoutGroup layout =
                rowHost.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            LayoutElement element =
                rowHost.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return rowHost.GetComponent<RectTransform>();
        }

        private Dropdown CreateDropdown(
            string objectName,
            Transform parent,
            bool addLayoutElement = true)
        {
            GameObject host = CreateUiObject(
                objectName,
                parent,
                typeof(Image),
                typeof(Dropdown));
            host.GetComponent<Image>().color = FieldColor;
            Dropdown dropdown = host.GetComponent<Dropdown>();

            Text caption = CreateText(
                "Label",
                host.transform,
                string.Empty,
                14,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin =
                new Vector2(12f, 2f);
            caption.rectTransform.offsetMax =
                new Vector2(-34f, -2f);
            dropdown.captionText = caption;

            Text arrow = CreateText(
                "Arrow",
                host.transform,
                "▼",
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            arrow.rectTransform.anchorMin =
                new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax =
                new Vector2(1f, 1f);
            arrow.rectTransform.pivot =
                new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta =
                new Vector2(30f, 0f);

            RectTransform template = CreateDropdownTemplate(
                host.transform,
                out Text itemText);
            dropdown.template = template;
            dropdown.itemText = itemText;
            dropdown.targetGraphic = host.GetComponent<Image>();
            template.gameObject.SetActive(false);

            if (addLayoutElement)
            {
                AddLayoutElement(host, 42f, 42f);
            }

            return dropdown;
        }

        private RectTransform CreateDropdownTemplate(
            Transform parent,
            out Text itemText)
        {
            RectTransform template = CreatePanel(
                "Template",
                parent,
                new Color32(20, 27, 38, 255));
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition =
                new Vector2(0f, -2f);
            template.sizeDelta = new Vector2(0f, 250f);

            ScrollRect scroll =
                template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType =
                ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            RectTransform viewport = CreatePanel(
                "Viewport",
                template,
                Color.white);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(2f, 2f);
            viewport.offsetMax = new Vector2(-2f, -2f);
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewport;

            GameObject contentHost = CreateUiObject(
                "Content",
                viewport,
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            RectTransform content =
                contentHost.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout =
                contentHost.GetComponent<VerticalLayoutGroup>();
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter contentFitter =
                contentHost.GetComponent<ContentSizeFitter>();
            contentFitter.verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            GameObject itemHost = CreateUiObject(
                "Item",
                content,
                typeof(Toggle),
                typeof(Image),
                typeof(LayoutElement));
            itemHost.GetComponent<Image>().color =
                new Color32(31, 42, 56, 255);
            LayoutElement itemLayout =
                itemHost.GetComponent<LayoutElement>();
            itemLayout.minHeight = 38f;
            itemLayout.preferredHeight = 38f;
            Toggle toggle = itemHost.GetComponent<Toggle>();
            toggle.targetGraphic = itemHost.GetComponent<Image>();

            Text checkmark = CreateText(
                "Item Checkmark",
                itemHost.transform,
                "◆",
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                SuccessColor);
            checkmark.rectTransform.anchorMin =
                new Vector2(0f, 0f);
            checkmark.rectTransform.anchorMax =
                new Vector2(0f, 1f);
            checkmark.rectTransform.pivot =
                new Vector2(0f, 0.5f);
            checkmark.rectTransform.sizeDelta =
                new Vector2(28f, 0f);
            toggle.graphic = checkmark;

            itemText = CreateText(
                "Item Label",
                itemHost.transform,
                "Option",
                13,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            itemText.rectTransform.anchorMin = Vector2.zero;
            itemText.rectTransform.anchorMax = Vector2.one;
            itemText.rectTransform.offsetMin =
                new Vector2(30f, 0f);
            itemText.rectTransform.offsetMax =
                new Vector2(-6f, 0f);
            return template;
        }

        private InputField CreateInputField(
            string objectName,
            Transform parent,
            string defaultValue,
            InputField.ContentType contentType)
        {
            GameObject host = CreateUiObject(
                objectName,
                parent,
                typeof(Image),
                typeof(InputField));
            host.GetComponent<Image>().color = FieldColor;
            InputField field = host.GetComponent<InputField>();
            Text text = CreateText(
                "Text",
                host.transform,
                defaultValue,
                15,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin =
                new Vector2(10f, 2f);
            text.rectTransform.offsetMax =
                new Vector2(-10f, -2f);
            field.textComponent = text;
            field.contentType = contentType;
            field.lineType = InputField.LineType.SingleLine;
            field.text = defaultValue;
            field.caretColor = TextColor;
            field.selectionColor =
                new Color32(90, 144, 176, 150);
            return field;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string labelText,
            Color background,
            Action action)
        {
            GameObject host = CreateUiObject(
                objectName,
                parent,
                typeof(Image),
                typeof(Button));
            host.GetComponent<Image>().color = background;
            Button button = host.GetComponent<Button>();
            if (action != null)
            {
                button.onClick.AddListener(
                    delegate { action(); });
            }

            Text label = CreateText(
                "Label",
                host.transform,
                labelText,
                13,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(4f, 2f);
            label.rectTransform.offsetMax = new Vector2(-4f, -2f);
            return button;
        }

        private void CreateReopenButton(Transform parent)
        {
            Button reopen = CreateButton(
                "Open TestLab Panel Button",
                parent,
                "TEST LAB",
                ImportantButtonColor,
                delegate { SetVisible(true); });
            RectTransform rect =
                reopen.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(112f, 42f);
            // Stage HUD의 시작/속도 버튼 아래에서 다시 열 수 있게 한다.
            rect.anchoredPosition = new Vector2(-12f, -112f);
            reopenButton = reopen.gameObject;
            reopenButton.SetActive(false);
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            string value,
            int fontSize,
            FontStyle style,
            TextAnchor alignment,
            Color? color = null)
        {
            GameObject host = CreateUiObject(
                objectName,
                parent,
                typeof(Text));
            Text text = host.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color ?? TextColor;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            text.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            text.verticalOverflow =
                VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform CreatePanel(
            string objectName,
            Transform parent,
            Color color)
        {
            GameObject host = CreateUiObject(
                objectName,
                parent,
                typeof(Image));
            host.GetComponent<Image>().color = color;
            return host.GetComponent<RectTransform>();
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            params Type[] components)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform));
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            for (int i = 0; i < components.Length; i++)
            {
                Type component = components[i];
                if (component != typeof(RectTransform) &&
                    host.GetComponent(component) == null)
                {
                    host.AddComponent(component);
                }
            }

            return host;
        }

        private static void SetRect(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static void AddLayoutElement(
            GameObject host,
            float minimumHeight,
            float preferredHeight)
        {
            LayoutElement element =
                host.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = host.AddComponent<LayoutElement>();
            }

            element.minHeight = minimumHeight;
            if (preferredHeight >= 0f)
            {
                element.preferredHeight = preferredHeight;
            }
        }

        private static void AddFlexible(
            GameObject host,
            float width)
        {
            LayoutElement element =
                host.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = host.AddComponent<LayoutElement>();
            }

            element.flexibleWidth = Math.Max(0.01f, width);
            element.minWidth = 54f;
        }

        private static void SetFixedLayoutSize(
            GameObject host,
            float width,
            float height)
        {
            LayoutElement element =
                host.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = host.AddComponent<LayoutElement>();
            }

            element.minWidth = width;
            element.preferredWidth = width;
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;
        }

        private static void PopulateDropdown<T>(
            Dropdown dropdown,
            IReadOnlyList<T> source,
            Func<T, string> format)
        {
            var labels = new List<string>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    labels.Add(format(source[i]));
                }
            }

            if (labels.Count == 0)
            {
                labels.Add("(구현된 콘텐츠 없음)");
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.SetValueWithoutNotify(0);
            dropdown.interactable =
                source != null && source.Count > 0;
            dropdown.RefreshShownValue();
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }
}
