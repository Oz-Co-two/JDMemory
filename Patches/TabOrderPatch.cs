using System;
using System.Reflection;
using BeatSaberMarkupLanguage.GameplaySetup;
using HarmonyLib;
using JDMemory.UI;

namespace JDMemory.Patches
{
    /// <summary>
    /// BSML は MODS タブをタブ名の辞書順に並べる（GameplaySetup のコンストラクタ内の比較ラムダ）。
    /// その比較に割り込み、JDMemory を常に先頭にする。設定でオフにできる。
    /// BSML の作りが変わって比較処理が見つからない場合は、何もしない（普通の辞書順になる）。
    /// </summary>
    [HarmonyPatch]
    internal static class TabOrderPatch
    {
        private static Type _menuType;
        private static PropertyInfo _nameProperty;

        private static bool Prepare()
        {
            bool found = FindTarget() != null;
            if (!found)
            {
                Plugin.Log.Warn("BSML のタブ並べ替え処理が見つからないため、JDMemory を先頭にできません");
            }
            return found;
        }

        private static MethodBase TargetMethod() => FindTarget();

        private static MethodBase FindTarget()
        {
            _menuType ??= AccessTools.TypeByName("BeatSaberMarkupLanguage.GameplaySetup.GameplaySetupMenu");
            if (_menuType == null)
            {
                return null;
            }
            _nameProperty ??= AccessTools.Property(_menuType, "Name");

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var nested in typeof(GameplaySetup).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var method in nested.GetMethods(flags))
                {
                    var parameters = method.GetParameters();
                    if (method.ReturnType == typeof(int) && parameters.Length == 2 &&
                        parameters[0].ParameterType == _menuType && parameters[1].ParameterType == _menuType)
                    {
                        return method;
                    }
                }
            }
            return null;
        }

        private static void Postfix(object __0, object __1, ref int __result)
        {
            try
            {
                if (PluginConfig.Instance == null || !PluginConfig.Instance.PinTabFirst || _nameProperty == null)
                {
                    return;
                }
                bool aIsOurs = _nameProperty.GetValue(__0) as string == JDMemoryTab.TabName;
                bool bIsOurs = _nameProperty.GetValue(__1) as string == JDMemoryTab.TabName;
                if (aIsOurs && !bIsOurs)
                {
                    __result = -1;
                }
                else if (bIsOurs && !aIsOurs)
                {
                    __result = 1;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }
}
