#if UNITY_EDITOR
using System;
using NUnit.Framework;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class WorldSortingLayerTests
    {
        [Test]
        public void WorldRoleLayers_HaveStableBackToFrontOrder()
        {
            string[] expected =
            {
                WorldSortingLayers.Route,
                WorldSortingLayers.Tower,
                WorldSortingLayers.Enemy,
                WorldSortingLayers.Object,
                WorldSortingLayers.Effects,
                "Default"
            };
            SortingLayer[] actual = SortingLayer.layers;
            int previousIndex = -1;
            for (int i = 0; i < expected.Length; i++)
            {
                int index = Array.FindIndex(
                    actual,
                    layer => layer.name == expected[i]);
                Assert.That(
                    index,
                    Is.GreaterThan(previousIndex),
                    expected[i] +
                    " must render after the previous world role.");
                previousIndex = index;
            }
        }
    }
}
#endif
