using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.UI;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Coordinates pool-safe enemy selection, model creation, the read-only
    /// information panel, and camera composition. The selected entity ID is
    /// authoritative; pooled Transform references are never treated as
    /// identity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneEnemyInspectionController :
        MonoBehaviour
    {
        private sealed class RetainedCameraFocusTarget :
            IStageOneCameraFocusTarget
        {
            public bool IsCameraFocusValid { get; private set; }
            public Vector3 CameraFocusPosition { get; private set; }

            public void Retain(Vector3 position)
            {
                CameraFocusPosition = position;
                IsCameraFocusValid = true;
            }

            public void Clear()
            {
                IsCameraFocusValid = false;
                CameraFocusPosition = Vector3.zero;
            }
        }

        private readonly Dictionary<int, EnemySelectionView>
            selections =
                new Dictionary<int, EnemySelectionView>();
        private readonly RetainedCameraFocusTarget cameraFocusTarget =
            new RetainedCameraFocusTarget();

        private CompiledContent content;
        private StageOneUiTextCatalog textCatalog;
        private StageOneCameraController cameraController;
        private StageOneEnemyInspectionView inspectionView;
        private SimulationSnapshot latestSnapshot;
        private StageOneEnemyInspectionModel retainedModel;
        private int selectedEnemyId = -1;
        private ulong lastPresentationFingerprint;
        private bool selectionDefeated;
        private bool configured;

        public event Action<int> EnemySelected;

        public int SelectedEnemyId => selectedEnemyId;
        public bool HasSelection => selectedEnemyId >= 0;
        public bool IsShowingDefeatedEnemy =>
            HasSelection &&
            selectionDefeated &&
            retainedModel != null &&
            !retainedModel.IsAlive;
        public StageOneEnemyInspectionView InspectionView =>
            inspectionView;
        public EnemySelectionView SelectedEnemySelectionView =>
            selections.TryGetValue(
                selectedEnemyId,
                out EnemySelectionView selection)
                ? selection
                : null;

        public void Configure(
            CompiledContent compiledContent,
            StageOneUiTextCatalog catalog,
            Font font,
            StageOneCameraController camera,
            Transform uiParent)
        {
            if (compiledContent == null)
            {
                throw new ArgumentNullException(
                    nameof(compiledContent));
            }

            UnhookSharedInput();
            content = compiledContent;
            textCatalog = catalog ??
                StageOneUiTextCatalog.FromJson(null);
            cameraController = camera;
            if (inspectionView == null)
            {
                inspectionView =
                    StageOneEnemyInspectionView.CreateRuntime(
                        textCatalog,
                        font,
                        uiParent);
            }

            inspectionView.CloseRequested +=
                HandleCloseRequested;
            if (cameraController != null)
            {
                cameraController.BackgroundClicked +=
                    HandleBackgroundClicked;
            }

            configured = true;
        }

        public void Register(StageOneEnemyView enemyView)
        {
            if (!configured ||
                enemyView == null ||
                enemyView.EntityId < 0)
            {
                return;
            }

            EnemySelectionView selection =
                enemyView.SelectionView;
            if (selection == null)
            {
                selection =
                    enemyView.gameObject.AddComponent<
                        EnemySelectionView>();
                selection.Configure(enemyView.EntityId);
            }

            if (selections.TryGetValue(
                    enemyView.EntityId,
                    out EnemySelectionView previous) &&
                previous != null &&
                !ReferenceEquals(previous, selection))
            {
                previous.Clicked -=
                    HandleEnemyClicked;
                previous.SetSelected(false);
            }

            selection.Clicked -= HandleEnemyClicked;
            selection.Clicked += HandleEnemyClicked;
            selections[enemyView.EntityId] = selection;
            selection.SetSelected(
                enemyView.EntityId == selectedEnemyId);
        }

        public void Unregister(StageOneEnemyView enemyView)
        {
            if (enemyView == null)
            {
                return;
            }

            int entityId = enemyView.EntityId;
            EnemySelectionView selection =
                enemyView.SelectionView;
            if (selection != null)
            {
                selection.Clicked -= HandleEnemyClicked;
                selection.SetSelected(false);
            }

            if (selections.TryGetValue(
                    entityId,
                    out EnemySelectionView registered) &&
                ReferenceEquals(registered, selection))
            {
                selections.Remove(entityId);
            }

            if (selectedEnemyId == entityId)
            {
                if (selectionDefeated &&
                    retainedModel != null)
                {
                    if (selection != null)
                    {
                        cameraFocusTarget.Retain(
                            selection.CameraFocusPosition);
                    }

                    RefreshRetainedPresentation();
                }
                else
                {
                    ClearSelection();
                }
            }
        }

        public bool SelectEnemy(int entityId)
        {
            if (!configured ||
                latestSnapshot == null ||
                !selections.TryGetValue(
                    entityId,
                    out EnemySelectionView selection) ||
                selection == null ||
                !TryFindAliveEnemy(
                    latestSnapshot,
                    entityId,
                    out _))
            {
                return false;
            }

            bool selectionChanged =
                selectedEnemyId != entityId;
            if (selectedEnemyId != entityId)
            {
                SetSelectionIndicators(-1);
                selectedEnemyId = entityId;
                lastPresentationFingerprint = 0UL;
                retainedModel = null;
                selectionDefeated = false;
                cameraFocusTarget.Clear();
            }

            selection.SetSelected(true);
            RefreshSelectedModel();
            if (selectionChanged)
            {
                EnemySelected?.Invoke(entityId);
            }

            return true;
        }

        public void ClearSelection()
        {
            EnemySelectionView selected =
                SelectedEnemySelectionView;
            if (selected != null)
            {
                selected.SetSelected(false);
            }

            if (cameraController != null)
            {
                cameraController.ReleaseFocus(
                    cameraFocusTarget);
            }

            cameraFocusTarget.Clear();
            selectedEnemyId = -1;
            lastPresentationFingerprint = 0UL;
            retainedModel = null;
            selectionDefeated = false;
            if (inspectionView != null)
            {
                inspectionView.Hide();
            }
        }

        public void ApplySnapshot(
            SimulationSnapshot snapshot)
        {
            latestSnapshot = snapshot;
            if (selectedEnemyId < 0)
            {
                return;
            }

            if (snapshot == null ||
                !TryFindAliveEnemy(
                    snapshot,
                    selectedEnemyId,
                    out _))
            {
                if (selectionDefeated &&
                    retainedModel != null)
                {
                    RefreshRetainedPresentation();
                }
                else
                {
                    ClearSelection();
                }

                return;
            }

            selectionDefeated = false;
            RefreshSelectedModel();
        }

        /// <summary>
        /// Captures the selected enemy's terminal reader state before the
        /// simulation removes it. The retained model and camera point are
        /// independent of the pooled view, so they remain valid until the
        /// player closes the panel or selects another target.
        /// </summary>
        public bool NotifyEnemyDied(int entityId)
        {
            if (entityId < 0 ||
                selectedEnemyId != entityId)
            {
                return false;
            }

            if (retainedModel == null)
            {
                RefreshSelectedModel();
            }

            if (retainedModel == null ||
                selectedEnemyId != entityId)
            {
                return false;
            }

            EnemySelectionView selection =
                SelectedEnemySelectionView;
            if (selection != null)
            {
                cameraFocusTarget.Retain(
                    selection.CameraFocusPosition);
            }

            selectionDefeated = true;
            retainedModel =
                retainedModel.AsDefeated();
            lastPresentationFingerprint = 0UL;
            RefreshRetainedPresentation();
            return true;
        }

        private void RefreshSelectedModel()
        {
            if (selectedEnemyId < 0 ||
                latestSnapshot == null ||
                !TryFindAliveEnemy(
                    latestSnapshot,
                    selectedEnemyId,
                    out EnemySnapshot enemy) ||
                !content.TryGetEnemyId(
                    enemy.DefinitionId,
                    out EnemyDefinitionId definitionId))
            {
                ClearSelection();
                return;
            }

            if (!selections.TryGetValue(
                    selectedEnemyId,
                    out EnemySelectionView selection) ||
                selection == null)
            {
                ClearSelection();
                return;
            }

            ulong fingerprint =
                ComputeFingerprint(enemy);
            if (fingerprint !=
                    lastPresentationFingerprint ||
                inspectionView == null ||
                !inspectionView.IsVisible)
            {
                CompiledEnemyDefinition definition =
                    content.GetEnemy(definitionId);
                StageOneEnemyInspectionModel model =
                    StageOneEnemyInspectionModelFactory.Create(
                        enemy,
                        definition,
                        content,
                        textCatalog);
                retainedModel = model;
                inspectionView.Show(model);
                lastPresentationFingerprint =
                    fingerprint;
            }

            selection.SetSelected(true);
            cameraFocusTarget.Retain(
                selection.CameraFocusPosition);
            ApplyCameraFocus();
        }

        private void RefreshRetainedPresentation()
        {
            if (selectedEnemyId < 0 ||
                retainedModel == null ||
                inspectionView == null)
            {
                ClearSelection();
                return;
            }

            if (!inspectionView.IsVisible ||
                !ReferenceEquals(
                    inspectionView.CurrentModel,
                    retainedModel))
            {
                inspectionView.Show(retainedModel);
            }

            EnemySelectionView selection =
                SelectedEnemySelectionView;
            if (selection != null)
            {
                selection.SetSelected(true);
            }

            ApplyCameraFocus();
        }

        private void ApplyCameraFocus()
        {
            if (inspectionView == null ||
                !cameraFocusTarget.IsCameraFocusValid)
            {
                return;
            }

            Vector2 focusAnchor =
                inspectionView.FocusViewportAnchor;
            if (cameraController != null &&
                (!ReferenceEquals(
                     cameraController.FocusTarget,
                     cameraFocusTarget) ||
                 (cameraController.FocusViewportAnchor -
                  focusAnchor).sqrMagnitude > 0.000001f))
            {
                cameraController.SetFocusTarget(
                    cameraFocusTarget,
                    focusAnchor);
            }
        }

        private void HandleEnemyClicked(
            EnemySelectionView selection)
        {
            if (selection == null ||
                !SelectEnemy(selection.EntityId))
            {
                return;
            }
        }

        private void HandleCloseRequested()
        {
            ClearSelection();
        }

        private void HandleBackgroundClicked(
            Vector2 screenPosition)
        {
            ClearSelection();
        }

        private void SetSelectionIndicators(int entityId)
        {
            foreach (
                KeyValuePair<int, EnemySelectionView> pair in
                selections)
            {
                if (pair.Value != null)
                {
                    pair.Value.SetSelected(
                        pair.Key == entityId);
                }
            }
        }

        private static bool TryFindAliveEnemy(
            SimulationSnapshot snapshot,
            int entityId,
            out EnemySnapshot enemy)
        {
            if (snapshot != null)
            {
                EnemySnapshot[] enemies = snapshot.Enemies;
                for (int i = 0; i < enemies.Length; i++)
                {
                    if (enemies[i].Id == entityId &&
                        enemies[i].Alive)
                    {
                        enemy = enemies[i];
                        return true;
                    }
                }
            }

            enemy = default(EnemySnapshot);
            return false;
        }

        private ulong ComputeFingerprint(
            in EnemySnapshot enemy)
        {
            ulong hash = 1469598103934665603UL;
            Add(ref hash, enemy.Id);
            Add(ref hash, enemy.DefinitionId);
            Add(ref hash, enemy.HealthMilli);
            Add(ref hash, enemy.MaxHealthMilli);
            Add(ref hash, enemy.ShieldMilli);
            Add(ref hash, enemy.Armor);
            Add(ref hash, enemy.SlowBps);
            Add(ref hash, enemy.SpeedMultiplierBps);
            Add(ref hash, enemy.SizeMultiplierBps);
            Add(ref hash, enemy.EliteRenderScaleBps);
            Add(ref hash, enemy.BaseSpeedMilliPerTick);
            Add(ref hash, enemy.ControlGauge);
            Add(ref hash, enemy.ControlThreshold);
            Add(ref hash, enemy.RewardBudget);
            Add(ref hash, enemy.WaveProgressBudget);
            Add(ref hash, enemy.Generation);
            Add(ref hash, enemy.DeathBindingCount);
            string[] eliteTraits = enemy.EliteTraitIds;
            Add(ref hash, eliteTraits == null ? 0 : eliteTraits.Length);
            if (eliteTraits != null)
            {
                for (int i = 0; i < eliteTraits.Length; i++)
                {
                    Add(ref hash, eliteTraits[i]);
                }
            }
            StatusSnapshot[] statuses =
                enemy.StatusDetails;
            Add(
                ref hash,
                statuses == null ? 0 : statuses.Length);
            if (statuses != null)
            {
                for (int i = 0; i < statuses.Length; i++)
                {
                    StatusSnapshot status = statuses[i];
                    Add(ref hash, status.InstanceId);
                    Add(ref hash, (int)status.Type);
                    Add(ref hash, status.SourceEntityId);
                    Add(ref hash, status.SourceTowerId);
                    Add(ref hash, status.SourceCardId.Value);
                    Add(ref hash, status.Stacks);
                    Add(ref hash, status.Intensity);
                    Add(
                        ref hash,
                        ToVisibleRemainingTimeBucket(
                            status.RemainingTicks));
                    Add(ref hash, status.MaxStacks);
                    Add(ref hash, status.TickInterval);
                    Add(ref hash, status.ArmorIgnoreBps);
                }
            }

            return hash;
        }

        private int ToVisibleRemainingTimeBucket(
            int remainingTicks)
        {
            int tickRate =
                content == null
                    ? 1
                    : Math.Max(1, content.Run.TickRate);
            long tenths =
                ((long)Math.Max(0, remainingTicks) * 10L +
                 tickRate - 1L) /
                tickRate;
            return tenths >= int.MaxValue
                ? int.MaxValue
                : (int)tenths;
        }

        private static void Add(ref ulong hash, int value)
        {
            Add(ref hash, unchecked((ulong)(uint)value));
        }

        private static void Add(ref ulong hash, long value)
        {
            Add(ref hash, unchecked((ulong)value));
        }

        private static void Add(
            ref ulong hash,
            string value)
        {
            if (value == null)
            {
                Add(ref hash, 0UL);
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                Add(ref hash, value[i]);
            }
        }

        private static void Add(
            ref ulong hash,
            ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }

        private void UnhookSharedInput()
        {
            if (inspectionView != null)
            {
                inspectionView.CloseRequested -=
                    HandleCloseRequested;
            }

            if (cameraController != null)
            {
                cameraController.BackgroundClicked -=
                    HandleBackgroundClicked;
            }
        }

        private void OnDestroy()
        {
            UnhookSharedInput();
            foreach (
                EnemySelectionView selection in
                selections.Values)
            {
                if (selection != null)
                {
                    selection.Clicked -=
                        HandleEnemyClicked;
                }
            }

            selections.Clear();
        }
    }
}
