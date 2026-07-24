using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Simulation
{
    public static class LogicContentJsonLoader
    {
        public static CompiledContent Load(TextAsset jsonAsset)
        {
            if (jsonAsset == null)
            {
                throw new ArgumentNullException(nameof(jsonAsset));
            }

            ContentCatalogDto dto = JsonUtility.FromJson<ContentCatalogDto>(jsonAsset.text);
            return ContentCompiler.Compile(dto, GameSimulation.IsEffectOperationSupported);
        }
    }
}
