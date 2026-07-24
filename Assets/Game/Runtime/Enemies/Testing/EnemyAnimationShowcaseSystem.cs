using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Enemies.Testing
{
    [DisallowMultipleComponent]
    public sealed class EnemyAnimationShowcaseSystem : MonoBehaviour
    {
        private static readonly EnemyAnimationBehaviour[] Sequence =
        {
            EnemyAnimationBehaviour.Walk,
            EnemyAnimationBehaviour.Attack,
            EnemyAnimationBehaviour.Special,
            EnemyAnimationBehaviour.Walk2,
            EnemyAnimationBehaviour.Death,
            EnemyAnimationBehaviour.Death2
        };

        [SerializeField, Min(0.5f)] private float phaseDuration = 1.25f;
        [SerializeField] private TextMesh statusText;

        private EnemyTestActor[] actors;
        private float phaseElapsed;
        private int phaseIndex;

        public EnemyAnimationBehaviour CurrentBehaviour => Sequence[phaseIndex];

        private void Start()
        {
            actors = FindObjectsOfType<EnemyTestActor>();
            ShowBehaviour(Sequence[0]);
        }

        private void Update()
        {
            phaseElapsed += Time.unscaledDeltaTime;
            if (phaseElapsed < phaseDuration)
            {
                return;
            }

            phaseElapsed -= phaseDuration;
            phaseIndex = (phaseIndex + 1) % Sequence.Length;
            ShowBehaviour(Sequence[phaseIndex]);
        }

        public void Configure(float duration, TextMesh targetStatusText)
        {
            phaseDuration = Mathf.Max(0.5f, duration);
            statusText = targetStatusText;
        }

        public void ShowBehaviour(EnemyAnimationBehaviour behaviour)
        {
            if (actors == null)
            {
                actors = FindObjectsOfType<EnemyTestActor>();
            }

            for (int i = 0; i < actors.Length; i++)
            {
                EnemyTestActor actor = actors[i];
                DirectionalEnemyAnimator directionalAnimator =
                    actor.GetComponent<DirectionalEnemyAnimator>();
                EnemyHealth health = actor.GetComponent<EnemyHealth>();

                if (health.IsDead)
                {
                    health.ResetHealth();
                }

                bool supported = directionalAnimator.Supports(behaviour);
                bool isDeath = behaviour == EnemyAnimationBehaviour.Death ||
                               behaviour == EnemyAnimationBehaviour.Death2;
                bool isMovement = behaviour == EnemyAnimationBehaviour.Walk ||
                                  behaviour == EnemyAnimationBehaviour.Walk2;

                if (supported && isDeath)
                {
                    actor.SetMovementEnabled(false);
                    health.Kill(behaviour);
                }
                else if (supported)
                {
                    actor.SetMovementEnabled(isMovement);
                    directionalAnimator.PlayBehaviour(behaviour);
                }
                else
                {
                    actor.SetMovementEnabled(true);
                    directionalAnimator.PlayBehaviour(EnemyAnimationBehaviour.Walk);
                }
            }

            if (statusText != null)
            {
                statusText.text =
                    "NOW: " + behaviour.ToString().ToUpperInvariant() +
                    "  |  UNSUPPORTED MONSTERS KEEP WALKING";
            }
        }
    }
}
