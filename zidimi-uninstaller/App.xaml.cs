using System.Windows;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LanguageManager.Instance.Initialize();
        }
    }
}
