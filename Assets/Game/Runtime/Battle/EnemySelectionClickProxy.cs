using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Routes clicks from either root or child colliders to the owning enemy
    /// selection view. This keeps future enemy prefab hierarchy choices out
    /// of the inspection controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySelectionClickProxy : MonoBehaviour
    {
        private EnemySelectionView owner;

        public void Configure(EnemySelectionView selectionOwner)
        {
            owner = selectionOwner;
        }

        private void OnMouseUpAsButton()
        {
            if (owner == null ||
                StageOneCameraController.ShouldSuppressWorldClick)
            {
                return;
            }

            owner.RequestSelection();
        }
    }
}
