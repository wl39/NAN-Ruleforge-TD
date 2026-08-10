using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Validated Korean copy used by the runtime game-guide modal. Keeping the
    /// copy in Resources lets the guide be shared by the title and battle
    /// scenes without adding serialized scene dependencies.
    /// </summary>
    public sealed class GameGuideCatalog
    {
        public const string DefaultResourcePath =
            "RuleforgeTD/GameGuideKo";

        private static readonly string[] ExpectedTabIds =
        {
            "basics",
            "towers",
            "cards",
            "combat",
            "monsters",
            "rewards",
            "controls"
        };

        private static readonly string[] ExpectedStarterCardIds =
        {
            "split",
            "burn",
            "explode",
            "poison",
            "pierce",
            "mark",
            "corrosion",
            "ricochet",
            "bleed",
            "knockback",
            "shock"
        };

        private static readonly string[] ExpectedEnemyIds =
        {
            "raider",
            "runner",
            "armored_knight",
            "elite_golem",
            "boss_guardian",
            "boss_summoner",
            "boss_time_walker"
        };

        private readonly GameGuideTab[] tabs;
        private readonly GameGuideReferenceEntry[] starterCards;
        private readonly GameGuideReferenceEntry[] enemies;

        private GameGuideCatalog(GameGuideDocumentDto source)
        {
            Title = source.title.Trim();
            Subtitle = source.subtitle.Trim();
            CloseLabel = source.close.Trim();
            TutorialReplayLabel = source.tutorialReplay.Trim();
            TutorialReplayHint = source.tutorialReplayHint.Trim();
            tabs = source.tabs;
            starterCards = source.starterCards;
            enemies = source.enemies;
        }

        public string Title { get; }
        public string Subtitle { get; }
        public string CloseLabel { get; }
        public string TutorialReplayLabel { get; }
        public string TutorialReplayHint { get; }
        public int TabCount => tabs.Length;
        public int StarterCardCount => starterCards.Length;
        public int EnemyCount => enemies.Length;

        public static GameGuideCatalog LoadDefault()
        {
            TextAsset asset = Resources.Load<TextAsset>(
                DefaultResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "The game-guide localization resource is missing: " +
                    DefaultResourcePath);
            }

            return Load(asset);
        }

        public static GameGuideCatalog Load(TextAsset source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.text))
            {
                throw new ArgumentException(
                    "Game-guide localization must not be empty.",
                    nameof(source));
            }

            GameGuideDocumentDto dto =
                JsonUtility.FromJson<GameGuideDocumentDto>(source.text);
            Validate(dto);
            return new GameGuideCatalog(dto);
        }

        public GameGuideTab GetTab(int index)
        {
            if (index < 0 || index >= tabs.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return tabs[index];
        }

        public GameGuideReferenceEntry GetStarterCard(int index)
        {
            if (index < 0 || index >= starterCards.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return starterCards[index];
        }

        public GameGuideReferenceEntry GetEnemy(int index)
        {
            if (index < 0 || index >= enemies.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return enemies[index];
        }

        public int FindTabIndex(string id)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                if (string.Equals(
                        tabs[i].Id,
                        id,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public string BuildTabBody(int index)
        {
            GameGuideTab tab = GetTab(index);
            var builder = new StringBuilder(2048);
            builder.AppendLine(tab.Intro);
            for (int i = 0; i < tab.SectionCount; i++)
            {
                GameGuideSection section = tab.GetSection(i);
                builder.AppendLine();
                builder.Append("◆ ");
                builder.AppendLine(section.Heading);
                builder.AppendLine(section.Body);
            }

            if (string.Equals(
                    tab.Id,
                    "cards",
                    StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.AppendLine("◆ 스테이지 시작 카드 11종");
                for (int i = 0; i < starterCards.Length; i++)
                {
                    AppendReference(builder, starterCards[i]);
                }
            }
            else if (string.Equals(
                         tab.Id,
                         "monsters",
                         StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.AppendLine("◆ 주요 몬스터와 보스");
                for (int i = 0; i < enemies.Length; i++)
                {
                    AppendReference(builder, enemies[i]);
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendReference(
            StringBuilder builder,
            GameGuideReferenceEntry entry)
        {
            builder.Append("· ");
            builder.Append(entry.Name);
            builder.Append(" — ");
            builder.AppendLine(entry.Summary);
        }

        private static void Validate(GameGuideDocumentDto dto)
        {
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.title) ||
                string.IsNullOrWhiteSpace(dto.subtitle) ||
                string.IsNullOrWhiteSpace(dto.close) ||
                string.IsNullOrWhiteSpace(dto.tutorialReplay) ||
                string.IsNullOrWhiteSpace(dto.tutorialReplayHint))
            {
                throw new InvalidOperationException(
                    "Game-guide localization has incomplete header copy.");
            }

            ValidateTabs(dto.tabs);
            ValidateReferenceEntries(
                dto.starterCards,
                ExpectedStarterCardIds,
                "starter-card");
            ValidateReferenceEntries(
                dto.enemies,
                ExpectedEnemyIds,
                "enemy");
        }

        private static void ValidateTabs(GameGuideTab[] source)
        {
            if (source == null || source.Length != ExpectedTabIds.Length)
            {
                throw new InvalidOperationException(
                    "The game guide must define exactly seven tabs.");
            }

            for (int i = 0; i < source.Length; i++)
            {
                GameGuideTab tab = source[i];
                if (tab == null ||
                    !string.Equals(
                        tab.Id,
                        ExpectedTabIds[i],
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(tab.Title) ||
                    string.IsNullOrWhiteSpace(tab.Intro) ||
                    tab.SectionCount == 0)
                {
                    throw new InvalidOperationException(
                        "Game-guide tab " + i +
                        " is missing or out of order.");
                }

                for (int sectionIndex = 0;
                     sectionIndex < tab.SectionCount;
                     sectionIndex++)
                {
                    GameGuideSection section =
                        tab.GetSection(sectionIndex);
                    if (section == null ||
                        string.IsNullOrWhiteSpace(section.Heading) ||
                        string.IsNullOrWhiteSpace(section.Body))
                    {
                        throw new InvalidOperationException(
                            "Game-guide tab " + tab.Id +
                            " has an incomplete section.");
                    }
                }
            }
        }

        private static void ValidateReferenceEntries(
            GameGuideReferenceEntry[] source,
            string[] expectedIds,
            string label)
        {
            if (source == null || source.Length != expectedIds.Length)
            {
                throw new InvalidOperationException(
                    "The game guide must define exactly " +
                    expectedIds.Length + " " + label + " entries.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Length; i++)
            {
                GameGuideReferenceEntry entry = source[i];
                if (entry == null ||
                    !string.Equals(
                        entry.Id,
                        expectedIds[i],
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(entry.Name) ||
                    string.IsNullOrWhiteSpace(entry.Summary) ||
                    !ids.Add(entry.Id))
                {
                    throw new InvalidOperationException(
                        "Game-guide " + label + " entry " + i +
                        " is incomplete, duplicated, or out of order.");
                }
            }
        }

        [Serializable]
        private sealed class GameGuideDocumentDto
        {
            public string title;
            public string subtitle;
            public string close;
            public string tutorialReplay;
            public string tutorialReplayHint;
            public GameGuideTab[] tabs;
            public GameGuideReferenceEntry[] starterCards;
            public GameGuideReferenceEntry[] enemies;
        }
    }

    [Serializable]
    public sealed class GameGuideTab
    {
        [SerializeField]
        private string id;

        [SerializeField]
        private string title;

        [SerializeField]
        private string intro;

        [SerializeField]
        private GameGuideSection[] sections;

        public string Id => id;
        public string Title => title;
        public string Intro => intro;
        public int SectionCount => sections == null ? 0 : sections.Length;

        public GameGuideSection GetSection(int index)
        {
            if (sections == null || index < 0 || index >= sections.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return sections[index];
        }
    }

    [Serializable]
    public sealed class GameGuideSection
    {
        [SerializeField]
        private string heading;

        [SerializeField]
        private string body;

        public string Heading => heading;
        public string Body => body;
    }

    [Serializable]
    public sealed class GameGuideReferenceEntry
    {
        [SerializeField]
        private string id;

        [SerializeField]
        private string name;

        [SerializeField]
        private string summary;

        public string Id => id;
        public string Name => name;
        public string Summary => summary;
    }
}
