using System;
using System.Collections.Generic;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using RuleforgeTD.Rendering;
using UnityEngine;
using UnityEngine.Serialization;

namespace RuleforgeTD.Towers.Testing
{
    [DisallowMultipleComponent]
    public sealed class ArcherEnemyCombatSystem : MonoBehaviour
    {
        public const float SplitScaleMultiplier = 0.9f;

        private const int InitialPooledChildrenPerRoot = 2;
        private const int BasisPointScale = 10000;
        private const float SplitOffsetDistance = 0.24f;
        private const float GoldenAngleDegrees = 137.50776f;

        [FormerlySerializedAs("enemies")]
        [SerializeField] private EnemyHealth[] roots =
            Array.Empty<EnemyHealth>();
        [SerializeField] private EnemyHealth[] pooledChildren =
            Array.Empty<EnemyHealth>();
        [SerializeField] private EnemyTestMovementSystem movementSystem;
        [SerializeField] private Vector3[] routeCenters =
            Array.Empty<Vector3>();
        [SerializeField] private int[] rootMaximumHealth =
            Array.Empty<int>();
        [SerializeField] private Vector3[] rootBaseScales =
            Array.Empty<Vector3>();
        [SerializeField, Min(0.25f)] private float respawnDelay = 1.8f;

        private readonly List<EnemyHealth> runtimeChildren =
            new List<EnemyHealth>();
        private readonly List<int> childLineageIndices =
            new List<int>();
        private readonly List<int> childGenerations =
            new List<int>();
        private readonly List<float> childReturnTimers =
            new List<float>();
        private readonly List<bool> waitingForChildReturn =
            new List<bool>();

        private int[] rootGenerations = Array.Empty<int>();
        private float[] lineageRespawnTimers = Array.Empty<float>();
        private bool[] waitingForLineageRespawn = Array.Empty<bool>();
        private int totalSuccessfulSplits;

        public int EnemyCount => RootEnemyCount;
        public int RootEnemyCount => CountValid(roots);
        public int PooledEnemyCount => CountValid(runtimeChildren);
        public int TargetCount => RootEnemyCount + PooledEnemyCount;
        public int TotalSuccessfulSplits => totalSuccessfulSplits;

        public int ActiveSplitEnemyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < runtimeChildren.Count; i++)
                {
                    EnemyHealth child = runtimeChildren[i];
                    if (child != null &&
                        child.gameObject.activeInHierarchy)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int LivingEnemyCount =>
            CountLiving(roots, requireActive: true) +
            CountLiving(runtimeChildren, requireActive: true);

        private void Awake()
        {
            EnsureAuthoringState();
            RebuildRuntimeState(
                resetTotalSplits: true,
                rebuildChildPool: true);
            DeactivateAllPooledChildren();
        }

        private void Update()
        {
            EnsureRuntimeState();
            float deltaTime = Time.deltaTime;
            UpdateRootRespawns(deltaTime);
            UpdateChildReturns(deltaTime);
        }

        public void Configure(
            EnemyHealth[] combatRoots,
            float enemyRespawnDelay)
        {
            Configure(
                combatRoots,
                Array.Empty<EnemyHealth>(),
                null,
                enemyRespawnDelay);
        }

        public void Configure(
            EnemyHealth[] combatRoots,
            EnemyHealth[] splitEnemyPool,
            EnemyTestMovementSystem targetMovementSystem,
            float enemyRespawnDelay)
        {
            roots = CopyArray(combatRoots);
            pooledChildren = CopyArray(splitEnemyPool);
            movementSystem = targetMovementSystem;
            respawnDelay = Mathf.Max(0.25f, enemyRespawnDelay);

            routeCenters = new Vector3[roots.Length];
            rootMaximumHealth = new int[roots.Length];
            rootBaseScales = new Vector3[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                EnemyHealth root = roots[i];
                if (root == null)
                {
                    rootBaseScales[i] = Vector3.one;
                    continue;
                }

                routeCenters[i] = root.transform.position;
                rootMaximumHealth[i] = Mathf.Max(1, root.MaxHealth);
                rootBaseScales[i] = root.transform.localScale;
            }

            RebuildRuntimeState(
                resetTotalSplits: true,
                rebuildChildPool: true);
            DeactivateAllPooledChildren();
            ConfigureMovementActors();
        }

        // Kept as a source-compatible bridge for older generated scenes.
        // The lineage limit argument is intentionally ignored: health is now
        // the only split terminator.
        public void Configure(
            EnemyHealth[] combatRoots,
            EnemyHealth[] splitEnemyPool,
            EnemyTestMovementSystem targetMovementSystem,
            float enemyRespawnDelay,
            int ignoredMaximumSplitsPerLineage)
        {
            Configure(
                combatRoots,
                splitEnemyPool,
                targetMovementSystem,
                enemyRespawnDelay);
        }

        public void Configure(
            EnemyHealth[] combatRoots,
            EnemyHealth[] splitEnemyPool,
            float enemyRespawnDelay,
            EnemyTestMovementSystem targetMovementSystem)
        {
            Configure(
                combatRoots,
                splitEnemyPool,
                targetMovementSystem,
                enemyRespawnDelay);
        }

        public EnemyHealth GetTargetAt(int index)
        {
            if (index < 0)
            {
                return null;
            }

            int current = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                if (current++ == index)
                {
                    return roots[i];
                }
            }

            for (int i = 0; i < runtimeChildren.Count; i++)
            {
                if (runtimeChildren[i] == null)
                {
                    continue;
                }

                if (current++ == index)
                {
                    return runtimeChildren[i];
                }
            }

            return null;
        }

        public EnemyHealth[] GetAllTargets()
        {
            var targets = new EnemyHealth[TargetCount];
            int next = 0;
            CopyValid(roots, targets, ref next);
            CopyValid(runtimeChildren, targets, ref next);
            return targets;
        }

        public void CopyTargetsTo(List<EnemyHealth> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            for (int i = 0; i < roots.Length; i++)
            {
                EnemyHealth root = roots[i];
                if (root != null)
                {
                    destination.Add(root);
                }
            }

            for (int i = 0; i < runtimeChildren.Count; i++)
            {
                EnemyHealth child = runtimeChildren[i];
                if (child != null)
                {
                    destination.Add(child);
                }
            }
        }

        public int GetGeneration(EnemyHealth member)
        {
            int rootIndex = IndexOf(roots, member);
            if (rootIndex >= 0)
            {
                return rootIndex < rootGenerations.Length
                    ? rootGenerations[rootIndex]
                    : 0;
            }

            int childIndex = runtimeChildren.IndexOf(member);
            return childIndex >= 0 &&
                   childIndex < childGenerations.Count
                ? childGenerations[childIndex]
                : -1;
        }

        public EnemyHealth ApplyEnemyProgram(
            EnemyHealth target,
            ArcherShowcaseCardProgram cardProgram)
        {
            if (target == null ||
                target.IsDead ||
                !target.gameObject.activeInHierarchy ||
                cardProgram == null ||
                !cardProgram.IsReady)
            {
                return null;
            }

            EnemyHealth child = null;
            int lineageIndex = FindLineageIndex(target);
            if (lineageIndex >= 0 &&
                cardProgram.SplitEnemyCount == 2)
            {
                child = TrySplitEnemy(
                    target,
                    lineageIndex,
                    cardProgram.SplitHealthBasisPoints);
                if (child != null)
                {
                    cardProgram.PlaySplitBurst(
                        target.transform.position);
                }
            }

            ApplyProgramStatuses(target, cardProgram);
            if (child != null)
            {
                ApplyProgramStatuses(child, cardProgram);
            }

            return child;
        }

        public void ResetAllLineages()
        {
            EnsureRuntimeState();
            for (int lineageIndex = 0;
                 lineageIndex < roots.Length;
                 lineageIndex++)
            {
                ResetLineage(lineageIndex);
            }

            totalSuccessfulSplits = 0;
        }

        public EnemyHealth[] GetActiveLineageMembers()
        {
            var members = new List<EnemyHealth>(LivingEnemyCount);
            for (int lineageIndex = 0;
                 lineageIndex < roots.Length;
                 lineageIndex++)
            {
                AddActiveLineageMembers(lineageIndex, members);
            }

            return members.ToArray();
        }

        public EnemyHealth[] GetActiveLineageMembers(
            EnemyHealth lineageMember)
        {
            int lineageIndex = FindLineageIndex(lineageMember);
            if (lineageIndex < 0)
            {
                return Array.Empty<EnemyHealth>();
            }

            var members = new List<EnemyHealth>();
            AddActiveLineageMembers(lineageIndex, members);
            return members.ToArray();
        }

        private EnemyHealth TrySplitEnemy(
            EnemyHealth source,
            int lineageIndex,
            int healthBasisPoints)
        {
            int multiplier = Mathf.Clamp(
                healthBasisPoints,
                1,
                BasisPointScale);
            int splitMaximumHealth = MultiplyBasisPointsAllowZero(
                source.MaxHealth,
                multiplier);
            int splitCurrentHealth = Math.Min(
                splitMaximumHealth,
                MultiplyBasisPointsAllowZero(
                    source.CurrentHealth,
                    multiplier));

            // Calculate without a minimum clamp. A result below one health is
            // the natural, and only, split terminator.
            if (splitMaximumHealth < 1 ||
                splitCurrentHealth < 1)
            {
                return null;
            }

            int childIndex = FindAvailableChildIndex(lineageIndex);
            if (childIndex < 0)
            {
                childIndex = CreatePooledChild(lineageIndex);
            }

            if (childIndex < 0 ||
                childIndex >= runtimeChildren.Count)
            {
                return null;
            }

            EnemyHealth child = runtimeChildren[childIndex];
            if (child == null)
            {
                return null;
            }

            ArcherEnemyCardStatusView sourceStatus =
                source.GetComponent<ArcherEnemyCardStatusView>();
            ArcherEnemyCardStatusView childStatus =
                child.GetComponent<ArcherEnemyCardStatusView>();
            EnemyTestActor sourceActor =
                source.GetComponent<EnemyTestActor>();
            EnemyTestActor childActor =
                child.GetComponent<EnemyTestActor>();
            DirectionalEnemyAnimator sourceAnimator =
                source.GetComponent<DirectionalEnemyAnimator>();
            DirectionalEnemyAnimator childAnimator =
                child.GetComponent<DirectionalEnemyAnimator>();
            if (sourceAnimator == null ||
                childAnimator == null ||
                sourceActor == null ||
                childActor == null ||
                childStatus == null)
            {
                return null;
            }

            int nextGeneration = GetGeneration(source) + 1;
            Vector3 nextScale =
                source.transform.localScale * SplitScaleMultiplier;

            child.gameObject.SetActive(true);
            childStatus.ClearAll();
            source.Configure(
                splitMaximumHealth,
                splitCurrentHealth,
                sourceAnimator);
            child.Configure(
                splitMaximumHealth,
                splitCurrentHealth,
                childAnimator);
            source.transform.localScale = nextScale;
            child.transform.localScale = nextScale;

            // Split copies the complete status runtime before cards to the
            // right of Split run on both branches.
            if (sourceStatus != null)
            {
                childStatus.CopyAllFrom(sourceStatus);
            }

            int childOrdinal =
                GetLineageChildOrdinal(lineageIndex, childIndex);
            childActor.CopyRuntimeRouteFrom(
                sourceActor,
                GetSplitOffset(lineageIndex, childOrdinal));
            childActor.SetMovementEnabled(true);

            SetGeneration(source, nextGeneration);
            childGenerations[childIndex] = nextGeneration;
            waitingForChildReturn[childIndex] = false;
            childReturnTimers[childIndex] = 0f;
            totalSuccessfulSplits++;
            return child;
        }

        private int CreatePooledChild(int lineageIndex)
        {
            int templateIndex = FindTemplateChildIndex(lineageIndex);
            EnemyHealth template = templateIndex >= 0
                ? runtimeChildren[templateIndex]
                : lineageIndex >= 0 && lineageIndex < roots.Length
                    ? roots[lineageIndex]
                    : null;
            if (template == null)
            {
                return -1;
            }

            Transform parent = template.transform.parent;
            GameObject cloneObject = Instantiate(
                template.gameObject,
                parent);
            int ordinal = CountChildrenForLineage(lineageIndex);
            cloneObject.name =
                "Dynamic Split Pool " +
                roots[lineageIndex].name +
                " " +
                (ordinal + 1).ToString("000");
            cloneObject.SetActive(false);

            EnemyHealth child =
                cloneObject.GetComponent<EnemyHealth>();
            EnemyTestActor actor =
                cloneObject.GetComponent<EnemyTestActor>();
            if (child == null || actor == null)
            {
                Destroy(cloneObject);
                return -1;
            }

            runtimeChildren.Add(child);
            childLineageIndices.Add(lineageIndex);
            childGenerations.Add(0);
            childReturnTimers.Add(0f);
            waitingForChildReturn.Add(false);
            movementSystem?.RegisterActor(actor);
            return runtimeChildren.Count - 1;
        }

        private void ApplyProgramStatuses(
            EnemyHealth target,
            ArcherShowcaseCardProgram cardProgram)
        {
            if (target == null || target.IsDead)
            {
                return;
            }

            ArcherEnemyCardStatusView status =
                target.GetComponent<ArcherEnemyCardStatusView>();
            if (status != null)
            {
                status.ApplyBurn(cardProgram.BurnDefinition);
                status.ApplyPoison(cardProgram.PoisonDefinition);
            }
        }

        private void UpdateRootRespawns(float deltaTime)
        {
            for (int lineageIndex = 0;
                 lineageIndex < roots.Length;
                 lineageIndex++)
            {
                EnemyHealth root = roots[lineageIndex];
                if (root == null)
                {
                    continue;
                }

                if (!waitingForLineageRespawn[lineageIndex])
                {
                    if (!root.IsDead)
                    {
                        continue;
                    }

                    waitingForLineageRespawn[lineageIndex] = true;
                    lineageRespawnTimers[lineageIndex] = respawnDelay;
                    SetMovementEnabled(root, false);
                }

                lineageRespawnTimers[lineageIndex] -= deltaTime;
                if (lineageRespawnTimers[lineageIndex] <= 0f)
                {
                    ResetLineage(lineageIndex);
                }
            }
        }

        private void UpdateChildReturns(float deltaTime)
        {
            for (int childIndex = 0;
                 childIndex < runtimeChildren.Count;
                 childIndex++)
            {
                EnemyHealth child = runtimeChildren[childIndex];
                if (child == null ||
                    !child.gameObject.activeSelf)
                {
                    waitingForChildReturn[childIndex] = false;
                    childReturnTimers[childIndex] = 0f;
                    continue;
                }

                int lineageIndex =
                    childLineageIndices[childIndex];
                if (lineageIndex <
                        waitingForLineageRespawn.Length &&
                    waitingForLineageRespawn[lineageIndex])
                {
                    continue;
                }

                if (!waitingForChildReturn[childIndex])
                {
                    if (!child.IsDead)
                    {
                        continue;
                    }

                    waitingForChildReturn[childIndex] = true;
                    childReturnTimers[childIndex] = respawnDelay;
                    SetMovementEnabled(child, false);
                }

                childReturnTimers[childIndex] -= deltaTime;
                if (childReturnTimers[childIndex] <= 0f)
                {
                    ReturnChildToPool(childIndex);
                }
            }
        }

        private void ResetLineage(int lineageIndex)
        {
            if (lineageIndex < 0 ||
                lineageIndex >= roots.Length)
            {
                return;
            }

            for (int childIndex = 0;
                 childIndex < runtimeChildren.Count;
                 childIndex++)
            {
                if (childLineageIndices[childIndex] == lineageIndex)
                {
                    ReturnChildToPool(childIndex);
                }
            }

            EnemyHealth root = roots[lineageIndex];
            if (root != null)
            {
                root.gameObject.SetActive(true);
                root.transform.position = routeCenters[lineageIndex];
                root.transform.localScale =
                    rootBaseScales[lineageIndex];
                ClearStatuses(root);
                DirectionalEnemyAnimator animator =
                    root.GetComponent<DirectionalEnemyAnimator>();
                int maximumHealth = Mathf.Max(
                    1,
                    rootMaximumHealth[lineageIndex]);
                root.Configure(
                    maximumHealth,
                    maximumHealth,
                    animator);

                EnemyTestActor actor =
                    root.GetComponent<EnemyTestActor>();
                if (actor != null)
                {
                    actor.InitializeRoute();
                    actor.SetMovementEnabled(true);
                }
            }

            rootGenerations[lineageIndex] = 0;
            waitingForLineageRespawn[lineageIndex] = false;
            lineageRespawnTimers[lineageIndex] = 0f;
        }

        private void ReturnChildToPool(int childIndex)
        {
            if (childIndex < 0 ||
                childIndex >= runtimeChildren.Count)
            {
                return;
            }

            EnemyHealth child = runtimeChildren[childIndex];
            int lineageIndex = childLineageIndices[childIndex];
            if (child != null)
            {
                ClearStatuses(child);
                SetMovementEnabled(child, false);
                if (lineageIndex >= 0 &&
                    lineageIndex < rootBaseScales.Length)
                {
                    child.transform.localScale =
                        rootBaseScales[lineageIndex];
                }

                if (lineageIndex >= 0 &&
                    lineageIndex < rootMaximumHealth.Length)
                {
                    int maximumHealth = Mathf.Max(
                        1,
                        rootMaximumHealth[lineageIndex]);
                    child.Configure(
                        maximumHealth,
                        maximumHealth,
                        child.GetComponent<DirectionalEnemyAnimator>());
                }

                child.gameObject.SetActive(false);
            }

            childGenerations[childIndex] = 0;
            waitingForChildReturn[childIndex] = false;
            childReturnTimers[childIndex] = 0f;
        }

        private int FindAvailableChildIndex(int lineageIndex)
        {
            for (int childIndex = 0;
                 childIndex < runtimeChildren.Count;
                 childIndex++)
            {
                EnemyHealth child = runtimeChildren[childIndex];
                if (childLineageIndices[childIndex] == lineageIndex &&
                    child != null &&
                    !child.gameObject.activeSelf)
                {
                    return childIndex;
                }
            }

            return -1;
        }

        private int FindTemplateChildIndex(int lineageIndex)
        {
            for (int childIndex = 0;
                 childIndex < runtimeChildren.Count;
                 childIndex++)
            {
                if (childLineageIndices[childIndex] == lineageIndex &&
                    runtimeChildren[childIndex] != null)
                {
                    return childIndex;
                }
            }

            return -1;
        }

        private int FindLineageIndex(EnemyHealth member)
        {
            int rootIndex = IndexOf(roots, member);
            if (rootIndex >= 0)
            {
                return rootIndex;
            }

            int childIndex = runtimeChildren.IndexOf(member);
            return childIndex >= 0
                ? childLineageIndices[childIndex]
                : -1;
        }

        private void SetGeneration(
            EnemyHealth member,
            int generation)
        {
            int rootIndex = IndexOf(roots, member);
            if (rootIndex >= 0)
            {
                rootGenerations[rootIndex] = generation;
                return;
            }

            int childIndex = runtimeChildren.IndexOf(member);
            if (childIndex >= 0)
            {
                childGenerations[childIndex] = generation;
            }
        }

        private void AddActiveLineageMembers(
            int lineageIndex,
            List<EnemyHealth> members)
        {
            EnemyHealth root = roots[lineageIndex];
            if (IsLivingAndActive(root))
            {
                members.Add(root);
            }

            for (int childIndex = 0;
                 childIndex < runtimeChildren.Count;
                 childIndex++)
            {
                if (childLineageIndices[childIndex] != lineageIndex)
                {
                    continue;
                }

                EnemyHealth child = runtimeChildren[childIndex];
                if (IsLivingAndActive(child))
                {
                    members.Add(child);
                }
            }
        }

        private int CountChildrenForLineage(int lineageIndex)
        {
            int count = 0;
            for (int i = 0; i < childLineageIndices.Count; i++)
            {
                if (childLineageIndices[i] == lineageIndex)
                {
                    count++;
                }
            }

            return count;
        }

        private int GetLineageChildOrdinal(
            int lineageIndex,
            int childIndex)
        {
            int ordinal = 0;
            for (int i = 0;
                 i < childIndex &&
                 i < childLineageIndices.Count;
                 i++)
            {
                if (childLineageIndices[i] == lineageIndex)
                {
                    ordinal++;
                }
            }

            return ordinal;
        }

        private void EnsureAuthoringState()
        {
            roots = roots ?? Array.Empty<EnemyHealth>();
            pooledChildren =
                pooledChildren ?? Array.Empty<EnemyHealth>();

            if (routeCenters == null ||
                routeCenters.Length != roots.Length)
            {
                routeCenters = new Vector3[roots.Length];
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null)
                    {
                        routeCenters[i] =
                            roots[i].transform.position;
                    }
                }
            }

            if (rootMaximumHealth == null ||
                rootMaximumHealth.Length != roots.Length)
            {
                rootMaximumHealth = new int[roots.Length];
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null)
                    {
                        rootMaximumHealth[i] =
                            Mathf.Max(1, roots[i].MaxHealth);
                    }
                }
            }

            if (rootBaseScales == null ||
                rootBaseScales.Length != roots.Length)
            {
                rootBaseScales = new Vector3[roots.Length];
                for (int i = 0; i < roots.Length; i++)
                {
                    rootBaseScales[i] = roots[i] == null
                        ? Vector3.one
                        : roots[i].transform.localScale;
                }
            }
        }

        private void EnsureRuntimeState()
        {
            EnsureAuthoringState();
            if (rootGenerations.Length != roots.Length ||
                childReturnTimers.Count != runtimeChildren.Count)
            {
                RebuildRuntimeState(
                    resetTotalSplits: false,
                    rebuildChildPool:
                        runtimeChildren.Count == 0);
            }
        }

        private void RebuildRuntimeState(
            bool resetTotalSplits,
            bool rebuildChildPool)
        {
            rootGenerations = new int[roots.Length];
            lineageRespawnTimers = new float[roots.Length];
            waitingForLineageRespawn = new bool[roots.Length];

            if (rebuildChildPool)
            {
                runtimeChildren.Clear();
                childLineageIndices.Clear();
                childGenerations.Clear();
                childReturnTimers.Clear();
                waitingForChildReturn.Clear();

                for (int i = 0; i < pooledChildren.Length; i++)
                {
                    EnemyHealth child = pooledChildren[i];
                    if (child == null || roots.Length == 0)
                    {
                        continue;
                    }

                    int lineageIndex = Mathf.Min(
                        roots.Length - 1,
                        i / InitialPooledChildrenPerRoot);
                    runtimeChildren.Add(child);
                    childLineageIndices.Add(lineageIndex);
                    childGenerations.Add(0);
                    childReturnTimers.Add(0f);
                    waitingForChildReturn.Add(false);
                }
            }
            else
            {
                ResizeRuntimeChildState();
            }

            if (resetTotalSplits)
            {
                totalSuccessfulSplits = 0;
            }
        }

        private void ResizeRuntimeChildState()
        {
            while (childGenerations.Count < runtimeChildren.Count)
            {
                childGenerations.Add(0);
            }

            while (childReturnTimers.Count < runtimeChildren.Count)
            {
                childReturnTimers.Add(0f);
            }

            while (waitingForChildReturn.Count < runtimeChildren.Count)
            {
                waitingForChildReturn.Add(false);
            }
        }

        private void DeactivateAllPooledChildren()
        {
            for (int i = 0; i < runtimeChildren.Count; i++)
            {
                ReturnChildToPool(i);
            }
        }

        private void ConfigureMovementActors()
        {
            if (movementSystem == null)
            {
                return;
            }

            EnemyHealth[] targets = GetAllTargets();
            var actors = new EnemyTestActor[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                actors[i] = targets[i] == null
                    ? null
                    : targets[i].GetComponent<EnemyTestActor>();
            }

            movementSystem.Configure(actors);
        }

        private static Vector2 GetSplitOffset(
            int lineageIndex,
            int childOrdinal)
        {
            float angle =
                (childOrdinal * GoldenAngleDegrees +
                 lineageIndex * 31f) *
                Mathf.Deg2Rad;
            float radius =
                SplitOffsetDistance *
                (1f + 0.12f * (childOrdinal % 4));
            return new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * radius;
        }

        private static int MultiplyBasisPointsAllowZero(
            int value,
            int basisPoints)
        {
            long scaled =
                (long)Math.Max(0, value) *
                Math.Max(0, basisPoints) /
                BasisPointScale;
            return (int)Math.Min(int.MaxValue, scaled);
        }

        private static void ClearStatuses(EnemyHealth enemy)
        {
            ArcherEnemyCardStatusView status =
                enemy == null
                    ? null
                    : enemy.GetComponent<ArcherEnemyCardStatusView>();
            status?.ClearAll();
        }

        private static void SetMovementEnabled(
            EnemyHealth enemy,
            bool enabled)
        {
            EnemyTestActor actor =
                enemy == null
                    ? null
                    : enemy.GetComponent<EnemyTestActor>();
            actor?.SetMovementEnabled(enabled);
        }

        private static int IndexOf(
            EnemyHealth[] values,
            EnemyHealth target)
        {
            if (values == null || target == null)
            {
                return -1;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountValid(EnemyHealth[] values)
        {
            int count = 0;
            if (values == null)
            {
                return count;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountValid(List<EnemyHealth> values)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountLiving(
            EnemyHealth[] values,
            bool requireActive)
        {
            int count = 0;
            if (values == null)
            {
                return count;
            }

            for (int i = 0; i < values.Length; i++)
            {
                EnemyHealth enemy = values[i];
                if (enemy != null &&
                    !enemy.IsDead &&
                    (!requireActive ||
                     enemy.gameObject.activeInHierarchy))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountLiving(
            List<EnemyHealth> values,
            bool requireActive)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                EnemyHealth enemy = values[i];
                if (enemy != null &&
                    !enemy.IsDead &&
                    (!requireActive ||
                     enemy.gameObject.activeInHierarchy))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsLivingAndActive(EnemyHealth enemy)
        {
            return enemy != null &&
                   enemy.gameObject.activeInHierarchy &&
                   !enemy.IsDead;
        }

        private static EnemyHealth[] CopyArray(EnemyHealth[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<EnemyHealth>();
            }

            var copy = new EnemyHealth[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static void CopyValid(
            EnemyHealth[] source,
            EnemyHealth[] destination,
            ref int next)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null)
                {
                    destination[next++] = source[i];
                }
            }
        }

        private static void CopyValid(
            List<EnemyHealth> source,
            EnemyHealth[] destination,
            ref int next)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    destination[next++] = source[i];
                }
            }
        }
    }
}
