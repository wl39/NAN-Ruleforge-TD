#if UNITY_EDITOR
using System;
using System.IO;
using RuleforgeTD.Battle;
using RuleforgeTD.Editor.BuildTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Keeps the serialized Stage 01 card-module list synchronized with every
    /// JSON module below Assets/Game/Data/Cards. Runtime code only consumes the
    /// serialized catalog and never depends on AssetDatabase.
    /// </summary>
    [InitializeOnLoad]
    public static class CardContentModuleCatalogDiscovery
    {
        public const string ModuleRoot =
            CardContentModuleAssetDiscovery.ModuleRoot;

        private static bool refreshScheduled;
        private static bool refreshInProgress;

        static CardContentModuleCatalogDiscovery()
        {
            ScheduleRefresh();
        }

        public static string[] DiscoverAssetPaths()
        {
            return CardContentModuleAssetDiscovery
                .DiscoverAssetPaths();
        }

        public static TextAsset[] DiscoverTextAssets()
        {
            return CardContentModuleAssetDiscovery
                .DiscoverTextAssets();
        }

        public static void ScheduleRefresh()
        {
            if (refreshScheduled)
            {
                return;
            }

            refreshScheduled = true;
            EditorApplication.delayCall +=
                ExecuteScheduledRefresh;
        }

        public static void SynchronizeCatalogNow()
        {
            if (refreshInProgress)
            {
                throw new InvalidOperationException(
                    "Card content module synchronization is already in progress.");
            }

            refreshInProgress = true;
            try
            {
                StageOnePresentationCatalog catalog =
                    AssetDatabase.LoadAssetAtPath<
                        StageOnePresentationCatalog>(
                        StageOneGameplaySceneInstaller.CatalogPath);
                if (catalog == null)
                {
                    catalog = StageOneGameplaySceneInstaller
                        .EnsurePresentationCatalog();
                }

                TextAsset[] modules = DiscoverTextAssets();
                StageOneGameplaySceneInstaller
                    .ValidatePresentationContent(
                        catalog.ContentJson,
                        catalog.LocalizationJson,
                        catalog.UiFont,
                        modules);
                if (catalog.ConfigureCardContentModules(modules))
                {
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        internal static bool IsModuleAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                path.StartsWith(
                    ModuleRoot + "/",
                    StringComparison.Ordinal) &&
                string.Equals(
                    Path.GetExtension(path),
                    ".json",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ExecuteScheduledRefresh()
        {
            refreshScheduled = false;
            try
            {
                SynchronizeCatalogNow();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    internal sealed class CardContentModuleAssetPostprocessor :
        AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsModulePath(importedAssets) ||
                ContainsModulePath(deletedAssets) ||
                ContainsModulePath(movedAssets) ||
                ContainsModulePath(movedFromAssetPaths))
            {
                CardContentModuleCatalogDiscovery.ScheduleRefresh();
            }
        }

        private static bool ContainsModulePath(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                if (CardContentModuleCatalogDiscovery
                    .IsModuleAssetPath(paths[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class CardContentModulePrebuildValidator :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                CardContentModuleCatalogDiscovery
                    .SynchronizeCatalogNow();
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    "Card content module validation failed: " +
                    exception.Message);
            }
        }
    }
}
#endif
