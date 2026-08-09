#if UNITY_EDITOR
using UnityEngine;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Per-stage layout data for the shared decoration pipeline. Keeping this
    /// data separate from generation code makes a new stage a data-authoring
    /// task instead of another procedural implementation.
    /// </summary>
    internal static class FieldStageDecorationSpecs
    {
        public static FieldStageDecorationSpec CreateStageOne(
            Vector2[] pathPoints,
            Vector2[] buildSpots)
        {
            return new FieldStageDecorationSpec(
                "stage01",
                -3,
                27,
                -4,
                17,
                1.35f,
                pathPoints,
                buildSpots,
                new[]
                {
                    new FieldStageBiomeSpec(
                        "forest_west", "Dense Forest",
                        new Vector2(0.5f, 12f),
                        new Vector2(4.2f, 3.8f),
                        1103, 11, 16, 5, 7, 0.72f, 190f, 350f),
                    new FieldStageBiomeSpec(
                        "forest_north", "Dense Forest",
                        new Vector2(6.6f, 14.1f),
                        new Vector2(4.6f, 2.3f),
                        2207, 10, 14, 4, 6, 0.68f, 205f, 340f),
                    new FieldStageBiomeSpec(
                        "forest_ridge", "Forest Edge",
                        new Vector2(12.2f, 14.8f),
                        new Vector2(3.6f, 1.8f),
                        3319, 6, 9, 4, 5, 0.62f, 205f, 335f),
                    new FieldStageBiomeSpec(
                        "forest_east", "Woodland",
                        new Vector2(25f, 4.9f),
                        new Vector2(2.7f, 3.6f),
                        4421, 7, 12, 5, 5, 0.65f, 105f, 255f),
                    new FieldStageBiomeSpec(
                        "camp_northeast", "Ranger Camp",
                        new Vector2(25.1f, 15f),
                        new Vector2(2.5f, 1.7f),
                        5527, 0, 0, 0, 0, 0.5f),
                    new FieldStageBiomeSpec(
                        "camp_southwest", "Abandoned Camp",
                        new Vector2(0f, -2.85f),
                        new Vector2(2.7f, 1.05f),
                        6637, 0, 0, 0, 0, 0.42f),
                    new FieldStageBiomeSpec(
                        "scrub_south", "Rocky Scrub",
                        new Vector2(20.8f, -1.5f),
                        new Vector2(7f, 1.9f),
                        7753, 0, 3, 18, 5, 0.3f)
                },
                new[]
                {
                    new FieldStageMeadowSpec(
                        "meadow_west", new Vector2(2.25f, 5.9f),
                        new Vector2(4.5f, 2.7f),
                        9109, 3, 15, 5, 1, 1, 2, 4),
                    new FieldStageMeadowSpec(
                        "meadow_central", new Vector2(11.7f, 10f),
                        new Vector2(3f, 1.8f),
                        10111, 2, 10, 3, 1, 3, 4, 5),
                    new FieldStageMeadowSpec(
                        "meadow_east", new Vector2(20.1f, 4.7f),
                        new Vector2(3.1f, 2.8f),
                        11213, 2, 12, 4, 1, 1, 5, 6)
                },
                new[]
                {
                    new FieldStageRoadMarkerSpec(
                        0, 0.2f, -1f, "3 Pointer/1", false, false),
                    new FieldStageRoadMarkerSpec(
                        0, 0.7f, 1f, "Flag_DownRight", true, false),
                    new FieldStageRoadMarkerSpec(
                        1, 0.45f, 1f, "3 Pointer/2", false, true),
                    new FieldStageRoadMarkerSpec(
                        2, 0.35f, 1f, "Flag_Down", true, false),
                    new FieldStageRoadMarkerSpec(
                        3, 0.8f, 1f, "3 Pointer/4", false, true),
                    new FieldStageRoadMarkerSpec(
                        4, 0.85f, 1f, "Flag_UpRight", true, false)
                },
                130,
                5);
        }

        public static FieldStageDecorationSpec CreateStageTwo(
            Vector2[] pathPoints,
            Vector2[] buildSpots)
        {
            return new FieldStageDecorationSpec(
                "stage02",
                -3,
                21,
                -4,
                42,
                1.35f,
                pathPoints,
                buildSpots,
                new[]
                {
                    new FieldStageBiomeSpec(
                        "forest_lower_west", "Dense Forest",
                        new Vector2(0.3f, 3.5f),
                        new Vector2(2.7f, 6f),
                        12017, 10, 16, 7, 8, 0.82f, 205f, 350f),
                    new FieldStageBiomeSpec(
                        "forest_lower_east", "Woodland",
                        new Vector2(19f, 16f),
                        new Vector2(1.6f, 5.2f),
                        13007, 8, 13, 6, 6, 0.74f, 115f, 255f),
                    new FieldStageBiomeSpec(
                        "forest_upper_west", "Dense Forest",
                        new Vector2(-0.2f, 38.5f),
                        new Vector2(1.5f, 2.5f),
                        15013, 7, 12, 4, 5, 0.8f, 195f, 345f),
                    new FieldStageBiomeSpec(
                        "grove_lower_center", "Shrub Grove",
                        new Vector2(5.1f, 1.3f),
                        new Vector2(1f, 2.2f),
                        15101, 0, 8, 5, 3, 0.76f, 160f, 300f),
                    new FieldStageBiomeSpec(
                        "grove_middle_left", "Shrub Grove",
                        new Vector2(-0.2f, 15f),
                        new Vector2(1.5f, 2.2f),
                        15203, 0, 10, 6, 3, 0.8f, 175f, 350f),
                    new FieldStageBiomeSpec(
                        "grove_middle_meadow", "Shrub Grove",
                        new Vector2(5.2f, 23f),
                        new Vector2(1.8f, 1.5f),
                        15307, 0, 8, 6, 3, 0.74f, 200f, 335f),
                    new FieldStageBiomeSpec(
                        "grove_upper_east", "Shrub Grove",
                        new Vector2(19.1f, 37f),
                        new Vector2(1.2f, 1.5f),
                        15401, 0, 7, 5, 3, 0.72f, 110f, 250f),
                    new FieldStageBiomeSpec(
                        "camp_lower_east", "Ranger Camp",
                        new Vector2(19.2f, 1.6f),
                        new Vector2(1.7f, 1.6f),
                        16001, 0, 0, 0, 0, 0.48f),
                    new FieldStageBiomeSpec(
                        "camp_upper_west", "Abandoned Camp",
                        new Vector2(0f, 41f),
                        new Vector2(1.8f, 1f),
                        17011, 0, 0, 0, 0, 0.42f),
                    new FieldStageBiomeSpec(
                        "scrub_lower", "Rocky Scrub",
                        new Vector2(13.5f, -1.4f),
                        new Vector2(5f, 1.8f),
                        18013, 0, 5, 18, 6, 0.42f),
                    new FieldStageBiomeSpec(
                        "scrub_middle", "Rocky Scrub",
                        new Vector2(17f, 29.8f),
                        new Vector2(3.8f, 1.5f),
                        19001, 0, 5, 15, 5, 0.38f),
                    new FieldStageBiomeSpec(
                        "scrub_upper", "Rocky Scrub",
                        new Vector2(14.5f, 41f),
                        new Vector2(4.5f, 1.2f),
                        20011, 0, 4, 15, 5, 0.4f)
                },
                new[]
                {
                    new FieldStageMeadowSpec(
                        "meadow_lower", new Vector2(4.2f, 7.8f),
                        new Vector2(2f, 1.5f),
                        21001, 2, 15, 6, 1, 1, 2, 4),
                    new FieldStageMeadowSpec(
                        "meadow_lower_turn", new Vector2(14.6f, 14.6f),
                        new Vector2(1.7f, 1.45f),
                        22003, 2, 14, 6, 1, 3, 4, 5),
                    new FieldStageMeadowSpec(
                        "meadow_middle", new Vector2(5.1f, 22.5f),
                        new Vector2(2f, 1.45f),
                        23003, 2, 15, 6, 1, 1, 5, 6),
                    new FieldStageMeadowSpec(
                        "meadow_lower_east", new Vector2(20f, 7.6f),
                        new Vector2(0.6f, 1.4f),
                        24001, 1, 11, 4, 1, 7, 8, 9),
                    new FieldStageMeadowSpec(
                        "meadow_upper", new Vector2(4.8f, 36.5f),
                        new Vector2(2f, 1.35f),
                        25013, 2, 15, 6, 1, 2, 6, 10)
                },
                new[]
                {
                    new FieldStageRoadMarkerSpec(
                        0, 0.2f, 1f, "3 Pointer/1", false, false),
                    new FieldStageRoadMarkerSpec(
                        0, 0.72f, -1f, "Flag_Up", true, false),
                    new FieldStageRoadMarkerSpec(
                        2, 0.48f, -1f, "3 Pointer/2", false, true),
                    new FieldStageRoadMarkerSpec(
                        3, 0.14f, -1f, "3 Pointer/4", false, true),
                    new FieldStageRoadMarkerSpec(
                        4, 0.55f, 1f, "Flag_UpRight", true, false),
                    new FieldStageRoadMarkerSpec(
                        5, 0.18f, 1f, "3 Pointer/3", false, false),
                    new FieldStageRoadMarkerSpec(
                        6, 0.6f, 1f, "Flag_Left", true, false),
                    new FieldStageRoadMarkerSpec(
                        7, 0.35f, 1f, "3 Pointer/5", false, true),
                    new FieldStageRoadMarkerSpec(
                        8, 0.58f, 1f, "Flag_UpLeft", true, false),
                    new FieldStageRoadMarkerSpec(
                        9, 0.82f, -1f, "Flag_Down", true, false),
                    new FieldStageRoadMarkerSpec(
                        10, 0.62f, -1f, "3 Pointer/6", false, false),
                    new FieldStageRoadMarkerSpec(
                        11, 0.45f, -1f, "Flag_Right", true, false)
                },
                270,
                8);
        }

        public static FieldStageDecorationSpec CreateStageThree(
            Vector2[] pathPoints,
            Vector2[] buildSpots)
        {
            return new FieldStageDecorationSpec(
                "stage03",
                -4,
                43,
                -4,
                20,
                1.35f,
                pathPoints,
                buildSpots,
                new[]
                {
                    new FieldStageBiomeSpec(
                        "forest_west", "Dense Forest",
                        new Vector2(-1f, 15.5f),
                        new Vector2(2.4f, 4f),
                        31013, 9, 14, 6, 7, 0.8f, 190f, 345f),
                    new FieldStageBiomeSpec(
                        "forest_northwest", "Forest Edge",
                        new Vector2(9.5f, 18.2f),
                        new Vector2(5.5f, 1.8f),
                        32009, 7, 12, 5, 6, 0.72f, 205f, 340f),
                    new FieldStageBiomeSpec(
                        "forest_northeast", "Woodland",
                        new Vector2(29.5f, 18.5f),
                        new Vector2(4.2f, 1.5f),
                        33013, 4, 11, 5, 5, 0.7f, 110f, 255f),
                    new FieldStageBiomeSpec(
                        "forest_east", "Dense Forest",
                        new Vector2(42f, 8f),
                        new Vector2(1.3f, 5.2f),
                        34019, 6, 13, 6, 6, 0.78f, 195f, 350f),
                    new FieldStageBiomeSpec(
                        "grove_center", "Shrub Grove",
                        new Vector2(21f, 8.3f),
                        new Vector2(2.2f, 3.2f),
                        35023, 0, 12, 7, 4, 0.76f, 160f, 300f),
                    new FieldStageBiomeSpec(
                        "grove_southeast", "Shrub Grove",
                        new Vector2(37.5f, 1f),
                        new Vector2(3.5f, 1.4f),
                        36007, 0, 10, 6, 4, 0.72f, 175f, 330f),
                    new FieldStageBiomeSpec(
                        "camp_southwest", "Ranger Camp",
                        new Vector2(10f, -1.6f),
                        new Vector2(2.2f, 1.4f),
                        37003, 0, 0, 0, 0, 0.48f),
                    new FieldStageBiomeSpec(
                        "camp_southeast", "Abandoned Camp",
                        new Vector2(29f, -1.7f),
                        new Vector2(2.2f, 1.3f),
                        38011, 0, 0, 0, 0, 0.44f),
                    new FieldStageBiomeSpec(
                        "scrub_south", "Rocky Scrub",
                        new Vector2(19.5f, -2.1f),
                        new Vector2(5f, 1.2f),
                        39019, 0, 5, 18, 5, 0.4f),
                    new FieldStageBiomeSpec(
                        "scrub_east", "Rocky Scrub",
                        new Vector2(38f, -2.1f),
                        new Vector2(4.3f, 1.3f),
                        40009, 0, 1, 14, 5, 0.38f)
                },
                new[]
                {
                    new FieldStageMeadowSpec(
                        "meadow_west", new Vector2(1f, 1f),
                        new Vector2(2.5f, 1.8f),
                        41011, 2, 14, 5, 1, 1, 2, 4),
                    new FieldStageMeadowSpec(
                        "meadow_upper_west", new Vector2(10.5f, 12.7f),
                        new Vector2(2.4f, 1.6f),
                        42013, 2, 14, 5, 1, 3, 4, 5),
                    new FieldStageMeadowSpec(
                        "meadow_center", new Vector2(20.8f, 4.2f),
                        new Vector2(2f, 1.4f),
                        43003, 2, 13, 5, 1, 1, 5, 6),
                    new FieldStageMeadowSpec(
                        "meadow_upper_east", new Vector2(29.3f, 14f),
                        new Vector2(2.3f, 1.35f),
                        44017, 2, 14, 5, 1, 7, 8, 9),
                    new FieldStageMeadowSpec(
                        "meadow_east", new Vector2(39.5f, 7.8f),
                        new Vector2(1.5f, 1.6f),
                        45007, 2, 12, 4, 1, 2, 6, 10)
                },
                new[]
                {
                    new FieldStageRoadMarkerSpec(
                        0, 0.625f, 1f, "3 Pointer/1", false, false),
                    new FieldStageRoadMarkerSpec(
                        0, 0.72f, -1f, "Flag_Right", true, false),
                    new FieldStageRoadMarkerSpec(
                        1, 0.3f, 1f, "3 Pointer/2", false, true),
                    new FieldStageRoadMarkerSpec(
                        2, 0.52f, -1f, "Flag_Right", true, false),
                    new FieldStageRoadMarkerSpec(
                        3, 0.35f, 1f, "3 Pointer/4", false, true),
                    new FieldStageRoadMarkerSpec(
                        4, 0.56f, -1f, "Flag_Right", true, false),
                    new FieldStageRoadMarkerSpec(
                        5, 0.65f, 1f, "3 Pointer/3", false, false),
                    new FieldStageRoadMarkerSpec(
                        6, 0.2f, -1f, "Flag_Right", true, false),
                    new FieldStageRoadMarkerSpec(
                        7, 0.48f, 1f, "3 Pointer/5", false, true),
                    new FieldStageRoadMarkerSpec(
                        8, 0.3f, -1f, "Flag_Right", true, false)
                },
                220,
                5);
        }
    }
}
#endif
