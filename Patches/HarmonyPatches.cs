using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JDMemory.Core;

namespace JDMemory.Patches
{
    /// <summary>
    /// ソロ・練習のプレイ開始（プレイボタン、練習のプレイボタン、リザルトからのリトライ）。
    /// リプレイ再生はここを通らないので、記録の対象外になる。
    /// </summary>
    [HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator), nameof(SinglePlayerLevelSelectionFlowCoordinator.StartLevel))]
    internal static class StartLevelPatch
    {
        private static void Prefix(bool practice)
        {
            try
            {
                PlayState.BeginPlay(practice);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }

    /// <summary>ソロ・練習のレベル準備。譜面が一致するか確かめ、曲の速さを記録する。</summary>
    [HarmonyPatch]
    internal static class StandardLevelInitPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(StandardLevelScenesTransitionSetupDataSO))
                .Where(m => m.Name == nameof(StandardLevelScenesTransitionSetupDataSO.Init));
        }

        private static void Postfix(StandardLevelScenesTransitionSetupDataSO __instance)
        {
            try
            {
                PlayState.OnStandardLevelInit(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }

    /// <summary>マルチ・キャンペーンが始まったら対象外にする</summary>
    [HarmonyPatch]
    internal static class OtherLevelInitPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(MultiplayerLevelScenesTransitionSetupDataSO))
                .Concat(AccessTools.GetDeclaredMethods(typeof(MissionLevelScenesTransitionSetupDataSO)))
                .Where(m => m.Name == "Init");
        }

        private static void Postfix()
        {
            PlayState.OnOtherLevelInit();
        }
    }

    /// <summary>
    /// ノーツの出現位置を決める処理。ここでオフセットを書き換えて JD を変える。
    /// 他のMOD（JDFixer など）より後に動くようにして、JDMemory の値を優先する。
    /// </summary>
    [HarmonyPatch(typeof(VariableMovementDataProvider), nameof(VariableMovementDataProvider.Init))]
    internal static class VariableMovementDataProviderPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            float startHalfJumpDurationInBeats,
            float maxHalfJumpDistance,
            float noteJumpMovementSpeed,
            float bpm,
            ref BeatmapObjectSpawnMovementData.NoteJumpValueType noteJumpValueType,
            ref float noteJumpValue)
        {
            try
            {
                var current = PlayState.Current;
                if (current == null || !current.Initialized || current.DesiredJD == null)
                {
                    return;
                }
                if (noteJumpMovementSpeed <= 0.01f || bpm <= 0f)
                {
                    Plugin.Log.Warn($"Unexpected NJS/BPM ({noteJumpMovementSpeed}/{bpm}); leaving JD unchanged");
                    return;
                }

                float offset = JumpMath.OffsetForJumpDistance(bpm, noteJumpMovementSpeed, current.DesiredJD.Value,
                    startHalfJumpDurationInBeats, maxHalfJumpDistance);

                noteJumpValueType = BeatmapObjectSpawnMovementData.NoteJumpValueType.BeatOffset;
                noteJumpValue = offset;
                current.Applied = true;
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        private static void Postfix(VariableMovementDataProvider __instance, float noteJumpMovementSpeed)
        {
            try
            {
                var current = PlayState.Current;
                if (current == null || !current.Initialized)
                {
                    return;
                }
                current.ActualNJS = noteJumpMovementSpeed;
                current.ActualJD = __instance.jumpDistance;
                Plugin.Log.Info($"JD desired={current.DesiredJD?.ToString("0.00") ?? "-"} actual={current.ActualJD:0.00} NJS={noteJumpMovementSpeed} applied={current.Applied}");
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }

    /// <summary>
    /// ソロ・練習のレベル終了（クリア・失敗・中断・やり直し）。
    /// 終了後の処理でリトライが始まる前に記録したいので Prefix にする。
    /// </summary>
    [HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), nameof(StandardLevelScenesTransitionSetupDataSO.Finish))]
    internal static class StandardLevelFinishPatch
    {
        private static void Prefix(LevelCompletionResults levelCompletionResults)
        {
            try
            {
                PlayState.OnLevelFinished(levelCompletionResults);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }
}
