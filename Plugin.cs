using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using JDMemory.Core;
using JDMemory.Installers;
using SiraUtil.Zenject;
using IPALogger = IPA.Logging.Logger;

namespace JDMemory
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal const string HarmonyId = "io.github.jdmemory";

        internal static IPALogger Log { get; private set; }

        private Harmony _harmony;

        [Init]
        public Plugin(IPALogger logger, Config config, Zenjector zenjector)
        {
            Log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();
            HistoryStore.Instance = HistoryStore.Load();

            zenjector.Install<MenuInstaller>(Location.Menu);
            zenjector.Install<PlayerInstaller>(Location.StandardPlayer);
        }

        [OnStart]
        public void OnStart()
        {
            _harmony = new Harmony(HarmonyId);
            // 1つの割り込みが失敗しても、他の割り込み（JD の変更・記録など）は止めない
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    Log.Error($"Harmony patch failed: {type.FullName}: {e}");
                }
            }
            Log.Info("JDMemory started");
        }

        [OnExit]
        public void OnExit()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
