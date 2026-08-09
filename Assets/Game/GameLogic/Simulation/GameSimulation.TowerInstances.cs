using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 검증이 끝난 타워 정의와 건설 지점으로 권위 타워 상태를 만든다.
        /// 일반 런은 비용·해금 검사를 마친 뒤 호출하고, 테스트 샌드박스는
        /// 명시적인 우회 경계에서 같은 생성 코드를 재사용한다.
        /// </summary>
        private TowerState CreateTowerInstance(
            TowerDefinitionId definitionId,
            int buildPointIndex)
        {
            CompiledTowerDefinition definition =
                content.GetTower(definitionId);
            var tower = new TowerState
            {
                Id = new TowerId(nextTowerId++),
                DefinitionId = definitionId,
                BuildPointIndex = buildPointIndex,
                Position = run.BuildSpotsInternal[
                    buildPointIndex],
                Level = 1,
                SubjectType = definition.SubjectTypeMode ==
                    SubjectTypeMode.Enemy
                        ? SubjectType.Enemy
                        : SubjectType.Projectile,
                CardInstanceIds =
                    new int[definition.SlotCount],
                CardSubjectTypes =
                    new SubjectType[definition.SlotCount],
                Program = new CardId[0],
                ProgramInstances = new int[0],
                ProgramSubjectTypes = new SubjectType[0]
            };

            for (int slot = 0;
                 slot < tower.CardInstanceIds.Length;
                 slot++)
            {
                tower.CardInstanceIds[slot] = -1;
                tower.CardSubjectTypes[slot] =
                    tower.SubjectType;
            }

            towers.Add(tower);
            AddPresentation(
                PresentationEventType.TowerPlaced,
                tower.Id.Value,
                buildPointIndex,
                0,
                definition.StableId);
            return tower;
        }
    }
}
