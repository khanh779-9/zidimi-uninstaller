using System.Windows;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LanguageManager.Instance.Initialize();
        }
    }
}
