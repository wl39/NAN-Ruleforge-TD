using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Ruleforge BGM의 플랫폼별 로드/압축 기본값을 재현 가능하게 유지한다.
    /// 긴 곡은 WebGL 메모리를 위해 압축 상태로 두고, 짧은 전투 인트로와
    /// 루프는 정확한 예약 재생을 위해 로드 시 압축 해제한다.
    /// </summary>
    public sealed class RuleforgeMusicAudioImporter : AssetPostprocessor
    {
        private const string MusicRoot =
            "Assets/Game/Resources/RuleforgeTD/Audio/Music/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(
                    MusicRoot,
                    System.StringComparison.Ordinal))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            bool isBattleSegment =
                assetPath.EndsWith(
                    "Battle_Intro.wav",
                    System.StringComparison.Ordinal) ||
                assetPath.EndsWith(
                    "Battle_Loop.wav",
                    System.StringComparison.Ordinal);

            importer.forceToMono = false;
            importer.ambisonic = false;
            importer.loadInBackground = !isBattleSegment;

            AudioImporterSampleSettings defaults =
                importer.defaultSampleSettings;
            defaults.loadType = isBattleSegment
                ? AudioClipLoadType.DecompressOnLoad
                : AudioClipLoadType.CompressedInMemory;
            defaults.compressionFormat = isBattleSegment
                ? AudioCompressionFormat.PCM
                : AudioCompressionFormat.Vorbis;
            defaults.quality = isBattleSegment ? 1f : 0.72f;
            defaults.preloadAudioData = true;
            defaults.sampleRateSetting =
                AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = defaults;

            // Unity 2022 WebGL 빌드가 브라우저 호환 AAC로 변환하더라도
            // 위 loadType은 유지되어 긴 BGM과 정밀 루프의 용도가 갈린다.
        }
    }
}
