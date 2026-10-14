using Microsoft.Extensions.DependencyInjection;

namespace DrGymGirly;

public partial class App : Application
{

    public App(AppShell shell)
    {
        InitializeComponent();
        MainPage = shell;
    }
}
