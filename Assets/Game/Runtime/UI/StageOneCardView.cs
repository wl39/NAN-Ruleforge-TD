using System.Globalization;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Reusable, asset-optional card presentation for Stage 01.
    /// The outer frame communicates tier while the inner body communicates
    /// whether the owning tower interprets the card as a projectile or enemy.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image), typeof(Button))]
    public sealed class StageOneCardView :
        MonoBehaviour,
        IPointerDownHandler
    {
        private const float PointerToggleDebounceSeconds = 0.15f;
        private const string EquippedIconResourcePath =
            "RuleforgeTD/UI/Loadout/RuleforgeCardEquipped";

        public static readonly Color ProjectileBodyColor =
            new Color32(164, 94, 52, 255);
        public static readonly Color EnemyBodyColor =
            new Color32(132, 59, 55, 255);

        private static readonly Color NameBackplateColor =
            Color.clear;
        private static readonly Color DescriptionBackplateColor =
            Color.clear;
        private static readonly Color ArtworkPlaceholderColor =
            Color.clear;
        private static readonly Color PrimaryTextColor =
            new Color32(255, 247, 222, 255);
        private static readonly Color DescriptionTextColor =
            new Color32(48, 34, 24, 255);
        private static readonly Color EquippedBadgeColor =
            new Color32(255, 220, 73, 255);
        private static readonly Color EquippedBadgeTextColor =
            new Color32(255, 247, 222, 255);

        [SerializeField]
        private Image borderImage;

        [SerializeField]
        private Image bodyImage;

        [SerializeField]
        private Image nameBackplateImage;

        [SerializeField]
        private Image artworkImage;

        [SerializeField]
        private Image artworkFrameImage;

        [SerializeField]
        private Image descriptionBackplateImage;

        [SerializeField]
        private Image tierBadgeImage;

        [SerializeField]
        private Image equippedBadgeImage;

        [SerializeField]
        private Image equippedHighlightImage;

        [SerializeField]
        private Image tierAccentImage;

        [SerializeField]
        private Text nameText;

        [SerializeField]
        private Text artworkSymbolText;

        [SerializeField]
        private Text descriptionText;

        [SerializeField]
        private Text tierBadgeText;

        [SerializeField]
        private Text equippedBadgeText;

        [SerializeField]
        private Button button;

        private Font font;
        private StageOneCardDisplay display;
        private CardTier tier = CardTier.Common;
        private SubjectType subjectType = SubjectType.Projectile;
        private SubjectType configuredSubjectType =
            SubjectType.Projectile;
        private string customPlaceholderSymbol;
        private bool built;
        private bool hasConfiguredDisplay;
        private bool interpretationPreviewOverridden;
        private bool equipped;
        private bool expandedPresentation;
        private float lastPointerToggleTime =
            float.NegativeInfinity;

        public Button Button => button;
        public Image BorderImage => borderImage;
        public Image BodyImage => bodyImage;
        public Image NameBackplateImage => nameBackplateImage;
        public Image ArtworkImage => artworkImage;
        public Image ArtworkFrameImage => artworkFrameImage;
        public Image DescriptionBackplateImage =>
            descriptionBackplateImage;
        public Image TierBadgeImage => tierBadgeImage;
        public Image EquippedBadgeImage => equippedBadgeImage;
        public Image EquippedHighlightImage =>
            equippedHighlightImage;
        public Image TierAccentImage => tierAccentImage;
        public Text NameText => nameText;
        public Text ArtworkSymbolText => artworkSymbolText;
        public Text DescriptionText => descriptionText;
        public Text TierBadgeText => tierBadgeText;
        public Text EquippedBadgeText => equippedBadgeText;
        public GameObject EquippedBadgeRoot =>
            equippedBadgeImage == null
                ? null
                : equippedBadgeImage.gameObject;
        public StageOneCardDisplay Display => display;
        public CardTier Tier => tier;
        public SubjectType SubjectType => subjectType;
        public Sprite ArtworkSprite =>
            artworkImage == null ? null : artworkImage.sprite;
        public Sprite EquippedBadgeSprite =>
            equippedBadgeImage == null
                ? null
                : equippedBadgeImage.sprite;
        public bool IsEquipped =>
            EquippedBadgeRoot != null &&
            EquippedBadgeRoot.activeSelf;
        public bool IsInteractable =>
            button != null && button.interactable;
        public bool IsBuilt => built;
        public bool IsExpandedPresentation => expandedPresentation;

        private void Awake()
        {
            BuildInterface();
            StageOneCardRightClickBridge.EnsureExists();
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#else
            if (!Input.GetMouseButtonDown(1) ||
                !isActiveAndEnabled ||
                !display.IsValid)
            {
                return;
            }

            TryToggleAtScreenPosition(Input.mousePosition);
#endif
        }

        public static StageOneCardView CreateRuntime(
            string objectName,
            Transform parent,
            Font uiFont = null)
        {
            var host = new GameObject(
                string.IsNullOrWhiteSpace(objectName)
                    ? "Stage One Card"
                    : objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(StageOneCardView));
            host.transform.SetParent(parent, false);
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(113f, 192f);

            StageOneCardView view =
                host.GetComponent<StageOneCardView>();
            view.SetFont(uiFont);
            return view;
        }

        public void Configure(
            StageOneCardDisplay cardDisplay,
            CardTier cardTier,
            SubjectType targetType,
            Sprite artwork = null,
            bool equipped = false,
            string equippedBadgeLabel = null,
            bool interactable = true)
        {
            BuildInterface();
            SubjectType normalizedTarget =
                NormalizeTarget(targetType);
            bool preserveInterpretationPreview =
                hasConfiguredDisplay &&
                interpretationPreviewOverridden &&
                display.StableId == cardDisplay.StableId &&
                configuredSubjectType == normalizedTarget;
            display = cardDisplay;
            configuredSubjectType = normalizedTarget;
            hasConfiguredDisplay = true;
            nameText.text = display.Name;
            SetTier(cardTier);
            if (!preserveInterpretationPreview)
            {
                interpretationPreviewOverridden = false;
                SetTarget(normalizedTarget);
            }
            else
            {
                SetTarget(subjectType);
            }
            SetArtwork(
                artwork != null
                    ? artwork
                    : StageOneCardArtworkCatalog.Load(
                        display.StableId));
            SetEquipped(equipped, equippedBadgeLabel);
            SetInteractable(interactable);
        }

        public void SetFont(Font uiFont)
        {
            font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            BuildInterface();
            Text[] texts =
            {
                nameText,
                artworkSymbolText,
                descriptionText,
                tierBadgeText,
                equippedBadgeText
            };
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].font = font;
                }
            }
        }

        public void SetTier(CardTier cardTier)
        {
            BuildInterface();
            tier = NormalizeTier(cardTier);
            Color tierColor = GetTierColor(tier);
            borderImage.color = Color.white;
            tierBadgeImage.color = tierColor;
            tierAccentImage.color = tierColor;
            tierBadgeText.text = GetTierLabel(tier);
            RuleforgePixelCardUi.ApplyFrame(button, tier, equipped);
        }

        public void SetTarget(SubjectType targetType)
        {
            BuildInterface();
            subjectType = NormalizeTarget(targetType);
            bodyImage.color = GetTargetBodyColor(subjectType);
            RefreshDescription();
        }

        public void ToggleInterpretation()
        {
            if (!isActiveAndEnabled || !display.IsValid)
            {
                return;
            }

            SetTarget(
                subjectType == SubjectType.Enemy
                    ? SubjectType.Projectile
                    : SubjectType.Enemy);
            interpretationPreviewOverridden =
                subjectType != configuredSubjectType;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null ||
                eventData.button !=
                    PointerEventData.InputButton.Right)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            eventData.Use();
#else
            ToggleInterpretationFromPointer();
            eventData.Use();
#endif
        }

        public void SetArtwork(Sprite artwork)
        {
            BuildInterface();
            artworkImage.sprite = artwork;
            artworkImage.preserveAspect = true;
            artworkImage.color = artwork == null
                ? ArtworkPlaceholderColor
                : Color.white;
            artworkSymbolText.gameObject.SetActive(artwork == null);
            RefreshPlaceholderSymbol();
        }

        public void SetPlaceholderSymbol(string symbol)
        {
            customPlaceholderSymbol =
                string.IsNullOrWhiteSpace(symbol)
                    ? null
                    : symbol.Trim();
            RefreshPlaceholderSymbol();
        }

        public void SetEquipped(
            bool equipped,
            string badgeLabel = null)
        {
            BuildInterface();
            this.equipped = equipped;
            equippedBadgeImage.gameObject.SetActive(equipped);
            equippedHighlightImage.gameObject.SetActive(equipped);
            equippedBadgeText.text =
                equipped && equippedBadgeImage.sprite == null
                    ? "◆"
                    : string.Empty;
            RuleforgePixelCardUi.ApplyFrame(button, tier, equipped);
        }

        public void SetInteractable(bool interactable)
        {
            BuildInterface();
            button.interactable = interactable;
        }

        public void SetTextScale(float scale)
        {
            BuildInterface();
            float normalized = Mathf.Clamp(scale, 0.75f, 2f);
            SetScaledText(
                nameText,
                expandedPresentation ? 19 : 14,
                expandedPresentation ? 15 : 11,
                expandedPresentation ? 20 : 14,
                normalized);
            SetScaledText(
                artworkSymbolText,
                expandedPresentation ? 34 : 26,
                expandedPresentation ? 22 : 16,
                expandedPresentation ? 38 : 30,
                normalized);
            SetScaledText(
                descriptionText,
                expandedPresentation ? 15 : 11,
                expandedPresentation ? 13 : 7,
                expandedPresentation ? 15 : 11,
                normalized);
            SetScaledText(
                tierBadgeText,
                8,
                8,
                8,
                normalized);
            SetScaledText(
                equippedBadgeText,
                8,
                7,
                8,
                normalized);
        }

        /// <summary>
        /// Uses the roomier text-safe layout intended for reward choices and
        /// inventory hover previews. The compact inventory grid keeps the
        /// original layout so more owned cards remain visible at once.
        /// </summary>
        public void SetExpandedPresentation(bool expanded)
        {
            BuildInterface();
            expandedPresentation = expanded;

            SetAnchors(
                nameBackplateImage.rectTransform,
                expanded
                    ? new Vector2(0.1f, 0.805f)
                    : new Vector2(0.09f, 0.815f),
                expanded
                    ? new Vector2(0.9f, 0.915f)
                    : new Vector2(0.91f, 0.915f),
                Vector2.zero,
                Vector2.zero);
            Stretch(
                nameText.rectTransform,
                expanded ? 10f : 8f,
                expanded ? 5f : 2f,
                expanded ? 10f : 8f,
                expanded ? 0f : 2f);

            SetAnchors(
                artworkFrameImage.rectTransform,
                expanded
                    ? new Vector2(0.13f, 0.47f)
                    : new Vector2(0.115f, 0.47f),
                expanded
                    ? new Vector2(0.87f, 0.745f)
                    : new Vector2(0.885f, 0.735f),
                Vector2.zero,
                Vector2.zero);

            SetAnchors(
                descriptionBackplateImage.rectTransform,
                expanded
                    ? new Vector2(0.145f, 0.09f)
                    : new Vector2(0.105f, 0.095f),
                expanded
                    ? new Vector2(0.855f, 0.375f)
                    : new Vector2(0.895f, 0.42f),
                Vector2.zero,
                Vector2.zero);
            Stretch(
                descriptionText.rectTransform,
                expanded ? 8f : 12f,
                expanded ? 10f : 12f,
                expanded ? 8f : 12f,
                expanded ? 12f : 10f);
            descriptionText.lineSpacing = expanded ? 0.95f : 0.85f;
            SetTextScale(1f);
        }

        public static Color GetTargetBodyColor(SubjectType targetType)
        {
            return targetType == SubjectType.Enemy
                ? EnemyBodyColor
                : ProjectileBodyColor;
        }

        private static SubjectType NormalizeTarget(
            SubjectType targetType)
        {
            return targetType == SubjectType.Enemy
                ? SubjectType.Enemy
                : SubjectType.Projectile;
        }

        public static Color GetTierColor(CardTier cardTier)
        {
            switch (NormalizeTier(cardTier))
            {
                case CardTier.Uncommon:
                    return new Color32(107, 151, 91, 255);
                case CardTier.Rare:
                    return new Color32(92, 139, 164, 255);
                case CardTier.Legendary:
                    return new Color32(218, 167, 61, 255);
                case CardTier.Mythic:
                    return new Color32(181, 77, 111, 255);
                default:
                    return new Color32(174, 160, 132, 255);
            }
        }

        public static string GetTierLabel(CardTier cardTier)
        {
            return "T" + (int)NormalizeTier(cardTier);
        }

        private void RefreshDescription()
        {
            if (descriptionText == null)
            {
                return;
            }

            string contextualDescription =
                display.GetDescription(subjectType);
            descriptionText.text =
                string.IsNullOrWhiteSpace(contextualDescription)
                    ? display.Description
                    : contextualDescription;
        }

        private void ToggleInterpretationFromPointer()
        {
            float now = Time.unscaledTime;
            if (now - lastPointerToggleTime <
                PointerToggleDebounceSeconds)
            {
                return;
            }

            lastPointerToggleTime = now;
            ToggleInterpretation();
        }

        internal bool TryToggleAtScreenPosition(
            Vector2 screenPosition)
        {
            RectTransform rectTransform =
                transform as RectTransform;
            if (!isActiveAndEnabled ||
                !display.IsValid ||
                rectTransform == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(
                    rectTransform,
                    screenPosition,
                    ResolveEventCamera()))
            {
                return false;
            }

            ToggleInterpretationFromPointer();
            return true;
        }

        private Camera ResolveEventCamera()
        {
            Canvas owningCanvas = GetComponentInParent<Canvas>();
            if (owningCanvas == null ||
                owningCanvas.renderMode ==
                    RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return owningCanvas.worldCamera;
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            if (HasCompleteInterface())
            {
                built = true;
                font = nameText.font;
                return;
            }

            font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            borderImage = GetComponent<Image>();
            if (GetComponent<RectMask2D>() == null)
            {
                gameObject.AddComponent<RectMask2D>();
            }
            button = GetComponent<Button>();
            button.targetGraphic = borderImage;
            RuleforgePixelCardUi.ApplyFrame(button, tier, equipped);
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Shadow shadow = GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = new Color(0f, 0f, 0f, 0.52f);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;

            bodyImage = CreateImage("Card Body", transform);
            Stretch(bodyImage.rectTransform, 0f);
            RuleforgePixelCardUi.ApplyPanel(
                bodyImage,
                RuleforgePixelCardPanel.Body,
                ProjectileBodyColor);
            bodyImage.enabled = false;

            equippedHighlightImage =
                CreateImage("Equipped Highlight", bodyImage.transform);
            Stretch(equippedHighlightImage.rectTransform, 4f);
            equippedHighlightImage.color =
                new Color32(76, 224, 222, 34);
            equippedHighlightImage.gameObject.SetActive(false);

            tierAccentImage =
                CreateImage("Tier Accent", bodyImage.transform);
            SetAnchors(
                tierAccentImage.rectTransform,
                new Vector2(0.025f, 0.1f),
                new Vector2(0.055f, 0.9f),
                Vector2.zero,
                Vector2.zero);
            tierAccentImage.color = GetTierColor(tier);
            tierAccentImage.enabled = false;

            nameBackplateImage =
                CreateImage("Name Backplate", bodyImage.transform);
            SetAnchors(
                nameBackplateImage.rectTransform,
                new Vector2(0.09f, 0.815f),
                new Vector2(0.91f, 0.915f),
                Vector2.zero,
                Vector2.zero);
            nameBackplateImage.color = NameBackplateColor;
            RuleforgePixelCardUi.ApplyPanel(
                nameBackplateImage,
                RuleforgePixelCardPanel.Name,
                Color.white);
            nameBackplateImage.enabled = false;

            nameText = CreateText(
                "Card Name",
                nameBackplateImage.transform,
                14,
                FontStyle.Bold,
                DescriptionTextColor,
                TextAnchor.MiddleCenter);
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 11;
            nameText.resizeTextMaxSize = 14;
            Stretch(nameText.rectTransform, 8f, 2f, 8f, 2f);

            artworkFrameImage =
                CreateImage("Artwork Frame", bodyImage.transform);
            SetAnchors(
                artworkFrameImage.rectTransform,
                new Vector2(0.115f, 0.47f),
                new Vector2(0.885f, 0.735f),
                Vector2.zero,
                Vector2.zero);
            RuleforgePixelCardUi.ApplyPanel(
                artworkFrameImage,
                RuleforgePixelCardPanel.Artwork,
                Color.white);
            artworkFrameImage.enabled = false;

            artworkImage =
                CreateImage("Card Artwork", artworkFrameImage.transform);
            Stretch(artworkImage.rectTransform, 0f);
            artworkImage.color = ArtworkPlaceholderColor;

            artworkSymbolText = CreateText(
                "Artwork Placeholder Symbol",
                artworkImage.transform,
                26,
                FontStyle.Bold,
                DescriptionTextColor,
                TextAnchor.MiddleCenter);
            artworkSymbolText.resizeTextForBestFit = true;
            artworkSymbolText.resizeTextMinSize = 16;
            artworkSymbolText.resizeTextMaxSize = 30;
            Stretch(artworkSymbolText.rectTransform, 4f);

            descriptionBackplateImage =
                CreateImage(
                    "Description Backplate",
                    bodyImage.transform);
            SetAnchors(
                descriptionBackplateImage.rectTransform,
                new Vector2(0.105f, 0.095f),
                new Vector2(0.895f, 0.42f),
                Vector2.zero,
                Vector2.zero);
            descriptionBackplateImage.color =
                DescriptionBackplateColor;
            RuleforgePixelCardUi.ApplyPanel(
                descriptionBackplateImage,
                RuleforgePixelCardPanel.Description,
                Color.white);
            descriptionBackplateImage.enabled = false;

            descriptionText = CreateText(
                "Card Description",
                descriptionBackplateImage.transform,
                11,
                FontStyle.Normal,
                DescriptionTextColor,
                TextAnchor.UpperLeft);
            descriptionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow =
                VerticalWrapMode.Truncate;
            descriptionText.resizeTextForBestFit = true;
            descriptionText.resizeTextMinSize = 7;
            descriptionText.resizeTextMaxSize = 11;
            descriptionText.lineSpacing = 0.85f;
            Stretch(descriptionText.rectTransform, 12f, 12f, 12f, 10f);

            tierBadgeImage =
                CreateImage("Tier Badge", bodyImage.transform);
            AnchorAtTopCenter(
                tierBadgeImage.rectTransform,
                new Vector2(0f, -1f),
                new Vector2(24f, 11f));
            RuleforgePixelCardUi.ApplyPanel(
                tierBadgeImage,
                RuleforgePixelCardPanel.Badge,
                GetTierColor(tier));
            tierBadgeImage.enabled = false;
            tierBadgeText = CreateText(
                "Tier Badge Text",
                tierBadgeImage.transform,
                8,
                FontStyle.Bold,
                PrimaryTextColor,
                TextAnchor.MiddleCenter);
            Stretch(tierBadgeText.rectTransform, 2f);

            equippedBadgeImage =
                CreateImage("Equipped Badge", bodyImage.transform);
            AnchorAtTopRight(
                equippedBadgeImage.rectTransform,
                new Vector2(-5f, -3f),
                new Vector2(30f, 30f));
            equippedBadgeImage.sprite =
                Resources.Load<Sprite>(EquippedIconResourcePath);
            equippedBadgeImage.type = Image.Type.Simple;
            equippedBadgeImage.preserveAspect = true;
            equippedBadgeImage.color = equippedBadgeImage.sprite == null
                ? EquippedBadgeColor
                : Color.white;
            equippedBadgeText = CreateText(
                "Equipped Badge Text",
                equippedBadgeImage.transform,
                8,
                FontStyle.Bold,
                EquippedBadgeTextColor,
                TextAnchor.MiddleCenter);
            equippedBadgeText.resizeTextForBestFit = true;
            equippedBadgeText.resizeTextMinSize = 7;
            equippedBadgeText.resizeTextMaxSize = 8;
            Stretch(equippedBadgeText.rectTransform, 1f);

            built = true;
            SetTier(tier);
            SetTarget(subjectType);
            SetArtwork(null);
            SetEquipped(false);
        }

        private bool HasCompleteInterface()
        {
            return borderImage != null &&
                bodyImage != null &&
                nameBackplateImage != null &&
                artworkFrameImage != null &&
                artworkImage != null &&
                descriptionBackplateImage != null &&
                tierBadgeImage != null &&
                equippedBadgeImage != null &&
                equippedHighlightImage != null &&
                tierAccentImage != null &&
                nameText != null &&
                artworkSymbolText != null &&
                descriptionText != null &&
                tierBadgeText != null &&
                equippedBadgeText != null &&
                button != null;
        }

        private void RefreshPlaceholderSymbol()
        {
            if (artworkSymbolText == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(customPlaceholderSymbol))
            {
                artworkSymbolText.text = customPlaceholderSymbol;
                return;
            }

            string source = string.IsNullOrWhiteSpace(display.Name)
                ? display.StableId
                : display.Name;
            artworkSymbolText.text =
                string.IsNullOrWhiteSpace(source)
                    ? "?"
                    : StringInfo.GetNextTextElement(source.Trim());
        }

        private static CardTier NormalizeTier(CardTier cardTier)
        {
            int value = Mathf.Clamp((int)cardTier, 1, 5);
            return (CardTier)value;
        }

        private static void SetScaledText(
            Text text,
            int baseFontSize,
            int baseMinimumSize,
            int baseMaximumSize,
            float scale)
        {
            if (text == null)
            {
                return;
            }

            text.fontSize = Mathf.RoundToInt(baseFontSize * scale);
            text.resizeTextMinSize =
                Mathf.RoundToInt(baseMinimumSize * scale);
            text.resizeTextMaxSize =
                Mathf.RoundToInt(baseMaximumSize * scale);
        }

        private Image CreateImage(
            string objectName,
            Transform parent)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            host.transform.SetParent(parent, false);
            Image image = host.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text),
                typeof(Outline));
            host.transform.SetParent(parent, false);
            Text text = host.GetComponent<Text>();
            RuleforgeUiTypography.Configure(
                text,
                font,
                fontSize,
                color,
                alignment,
                RuleforgeUiTypography.IsLight(color));
            return text;
        }

        private static void Stretch(
            RectTransform rect,
            float inset)
        {
            Stretch(rect, inset, inset, inset, inset);
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetAnchors(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void AnchorAtTopLeft(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void AnchorAtTopCenter(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void AnchorAtTopRight(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class StageOneCardRightClickBridge :
        MonoBehaviour
    {
        public const string ReceiverName =
            "StageOneCardRightClickBridge";

        private static StageOneCardRightClickBridge instance;

        public static void EnsureExists()
        {
            if (instance != null)
            {
                return;
            }

            GameObject existing = GameObject.Find(ReceiverName);
            if (existing != null)
            {
                instance = existing.GetComponent<
                    StageOneCardRightClickBridge>();
            }

            if (instance != null)
            {
                return;
            }

            var host = new GameObject(
                ReceiverName,
                typeof(StageOneCardRightClickBridge));
            instance = host.GetComponent<
                StageOneCardRightClickBridge>();
        }

        [UnityEngine.Scripting.Preserve]
        public void HandleWebGLRightClick(string payload)
        {
            if (!TryParseNormalizedPosition(
                    payload,
                    out Vector2 normalizedPosition))
            {
                return;
            }

            var screenPosition = new Vector2(
                normalizedPosition.x * Screen.width,
                (1f - normalizedPosition.y) *
                Screen.height);
            StageOneCardView[] cards =
                FindObjectsOfType<StageOneCardView>();
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].TryToggleAtScreenPosition(
                        screenPosition))
                {
                    return;
                }
            }
        }

        private static bool TryParseNormalizedPosition(
            string payload,
            out Vector2 normalizedPosition)
        {
            normalizedPosition = Vector2.zero;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            string[] parts = payload.Split(',');
            if (parts.Length != 2 ||
                !float.TryParse(
                    parts[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float x) ||
                !float.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float y))
            {
                return false;
            }

            normalizedPosition =
                new Vector2(
                    Mathf.Clamp01(x),
                    Mathf.Clamp01(y));
            return true;
        }
    }
}
