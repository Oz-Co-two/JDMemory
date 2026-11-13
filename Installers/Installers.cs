using JDMemory.Managers;
using JDMemory.UI;
using Zenject;

namespace JDMemory.Installers
{
    internal sealed class MenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesTo<SelectionWatcher>().AsSingle();
            Container.BindInterfacesTo<JDMemoryTab>().AsSingle();
            Container.BindInterfacesTo<UpdateChecker>().AsSingle();
        }
    }

    internal sealed class PlayerInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesTo<ScoreTracker>().AsSingle();
        }
    }
}
