using System.Globalization;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using UnityEngine;
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
    public sealed class StageOneCardView : MonoBehaviour
    {
        public static readonly Color ProjectileBodyColor =
            new Color32(172, 86, 35, 248);
        public static readonly Color EnemyBodyColor =
            new Color32(145, 43, 48, 248);

        private static readonly Color NameBackplateColor =
            new Color32(30, 24, 22, 212);
        private static readonly Color DescriptionBackplateColor =
            new Color32(29, 24, 23, 228);
        private static readonly Color ArtworkPlaceholderColor =
            new Color32(49, 43, 42, 242);
        private static readonly Color PrimaryTextColor =
            new Color32(255, 247, 222, 255);
        private static readonly Color DescriptionTextColor =
            new Color32(246, 238, 220, 255);
        private static readonly Color EquippedBadgeColor =
            new Color32(255, 220, 73, 255);
        private static readonly Color EquippedBadgeTextColor =
            new Color32(48, 36, 12, 255);

        [SerializeField]
        private Image borderImage;

        [SerializeField]
        private Image bodyImage;

        [SerializeField]
        private Image nameBackplateImage;

        [SerializeField]
        private Image artworkImage;

        [SerializeField]
        private Image descriptionBackplateImage;

        [SerializeField]
        private Image tierBadgeImage;

        [SerializeField]
        private Image equippedBadgeImage;

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
        private string customPlaceholderSymbol;
        private bool built;

        public Button Button => button;
        public Image BorderImage => borderImage;
        public Image BodyImage => bodyImage;
        public Image NameBackplateImage => nameBackplateImage;
        public Image ArtworkImage => artworkImage;
        public Image DescriptionBackplateImage =>
            descriptionBackplateImage;
        public Image TierBadgeImage => tierBadgeImage;
        public Image EquippedBadgeImage => equippedBadgeImage;
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
        public bool IsEquipped =>
            EquippedBadgeRoot != null &&
            EquippedBadgeRoot.activeSelf;
        public bool IsInteractable =>
            button != null && button.interactable;
        public bool IsBuilt => built;

        private void Awake()
        {
            BuildInterface();
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
            rect.sizeDelta = new Vector2(156f, 220f);

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
            display = cardDisplay;
            nameText.text = display.Name;
            descriptionText.text = display.Description;
            SetTier(cardTier);
            SetTarget(targetType);
            SetArtwork(artwork);
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
            borderImage.color = tierColor;
            tierBadgeImage.color = tierColor;
            tierBadgeText.text = GetTierLabel(tier);
        }

        public void SetTarget(SubjectType targetType)
        {
            BuildInterface();
            subjectType =
                targetType == SubjectType.Enemy
                    ? SubjectType.Enemy
                    : SubjectType.Projectile;
            bodyImage.color = GetTargetBodyColor(subjectType);
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
            equippedBadgeImage.gameObject.SetActive(equipped);
            equippedBadgeText.text =
                string.IsNullOrWhiteSpace(badgeLabel)
                    ? "✓"
                    : badgeLabel.Trim();
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
                17,
                11,
                17,
                normalized);
            SetScaledText(
                artworkSymbolText,
                38,
                20,
                42,
                normalized);
            SetScaledText(
                descriptionText,
                13,
                9,
                13,
                normalized);
            SetScaledText(
                tierBadgeText,
                12,
                12,
                12,
                normalized);
            SetScaledText(
                equippedBadgeText,
                12,
                8,
                12,
                normalized);
        }

        public static Color GetTargetBodyColor(SubjectType targetType)
        {
            return targetType == SubjectType.Enemy
                ? EnemyBodyColor
                : ProjectileBodyColor;
        }

        public static Color GetTierColor(CardTier cardTier)
        {
            switch (NormalizeTier(cardTier))
            {
                case CardTier.Uncommon:
                    return new Color32(67, 153, 211, 255);
                case CardTier.Rare:
                    return new Color32(151, 87, 205, 255);
                case CardTier.Legendary:
                    return new Color32(242, 174, 49, 255);
                case CardTier.Mythic:
                    return new Color32(234, 66, 105, 255);
                default:
                    return new Color32(188, 161, 117, 255);
            }
        }

        public static string GetTierLabel(CardTier cardTier)
        {
            return "T" + (int)NormalizeTier(cardTier);
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
            button = GetComponent<Button>();
            button.targetGraphic = borderImage;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color32(255, 250, 226, 255);
            colors.pressedColor =
                new Color32(208, 200, 181, 255);
            colors.selectedColor =
                new Color32(255, 242, 197, 255);
            colors.disabledColor =
                new Color32(130, 130, 130, 210);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Shadow shadow = GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(3f, -3f);
            shadow.useGraphicAlpha = true;

            bodyImage = CreateImage("Card Body", transform);
            Stretch(bodyImage.rectTransform, 5f);

            nameBackplateImage =
                CreateImage("Name Backplate", bodyImage.transform);
            SetAnchors(
                nameBackplateImage.rectTransform,
                new Vector2(0.05f, 0.79f),
                new Vector2(0.95f, 0.96f),
                Vector2.zero,
                Vector2.zero);
            nameBackplateImage.color = NameBackplateColor;

            nameText = CreateText(
                "Card Name",
                nameBackplateImage.transform,
                17,
                FontStyle.Bold,
                PrimaryTextColor,
                TextAnchor.MiddleCenter);
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 11;
            nameText.resizeTextMaxSize = 17;
            Stretch(nameText.rectTransform, 30f, 4f, 58f, 4f);

            artworkImage =
                CreateImage("Card Artwork", bodyImage.transform);
            SetAnchors(
                artworkImage.rectTransform,
                new Vector2(0.1f, 0.37f),
                new Vector2(0.9f, 0.76f),
                Vector2.zero,
                Vector2.zero);
            artworkImage.color = ArtworkPlaceholderColor;

            artworkSymbolText = CreateText(
                "Artwork Placeholder Symbol",
                artworkImage.transform,
                38,
                FontStyle.Bold,
                PrimaryTextColor,
                TextAnchor.MiddleCenter);
            artworkSymbolText.resizeTextForBestFit = true;
            artworkSymbolText.resizeTextMinSize = 20;
            artworkSymbolText.resizeTextMaxSize = 42;
            Stretch(artworkSymbolText.rectTransform, 8f);

            descriptionBackplateImage =
                CreateImage(
                    "Description Backplate",
                    bodyImage.transform);
            SetAnchors(
                descriptionBackplateImage.rectTransform,
                new Vector2(0.06f, 0.05f),
                new Vector2(0.94f, 0.34f),
                Vector2.zero,
                Vector2.zero);
            descriptionBackplateImage.color =
                DescriptionBackplateColor;

            descriptionText = CreateText(
                "Card Description",
                descriptionBackplateImage.transform,
                13,
                FontStyle.Normal,
                DescriptionTextColor,
                TextAnchor.MiddleCenter);
            descriptionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow =
                VerticalWrapMode.Truncate;
            descriptionText.resizeTextForBestFit = true;
            descriptionText.resizeTextMinSize = 9;
            descriptionText.resizeTextMaxSize = 13;
            Stretch(descriptionText.rectTransform, 7f, 5f, 7f, 5f);

            tierBadgeImage =
                CreateImage("Tier Badge", bodyImage.transform);
            AnchorAtTopLeft(
                tierBadgeImage.rectTransform,
                new Vector2(5f, -5f),
                new Vector2(35f, 24f));
            tierBadgeText = CreateText(
                "Tier Badge Text",
                tierBadgeImage.transform,
                12,
                FontStyle.Bold,
                PrimaryTextColor,
                TextAnchor.MiddleCenter);
            Stretch(tierBadgeText.rectTransform, 2f);

            equippedBadgeImage =
                CreateImage("Equipped Badge", bodyImage.transform);
            AnchorAtTopRight(
                equippedBadgeImage.rectTransform,
                new Vector2(-5f, -5f),
                new Vector2(54f, 24f));
            equippedBadgeImage.color = EquippedBadgeColor;
            equippedBadgeText = CreateText(
                "Equipped Badge Text",
                equippedBadgeImage.transform,
                12,
                FontStyle.Bold,
                EquippedBadgeTextColor,
                TextAnchor.MiddleCenter);
            equippedBadgeText.resizeTextForBestFit = true;
            equippedBadgeText.resizeTextMinSize = 8;
            equippedBadgeText.resizeTextMaxSize = 12;
            Stretch(equippedBadgeText.rectTransform, 2f);

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
                artworkImage != null &&
                descriptionBackplateImage != null &&
                tierBadgeImage != null &&
                equippedBadgeImage != null &&
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
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            Outline outline = host.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
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
}
