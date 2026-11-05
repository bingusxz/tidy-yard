using Velopack;

namespace TidyYard;

public static class Program
{
    /// <summary>
    /// Velopack must run first: the installer and updater start the app with special arguments
    /// (install, update, uninstall) that it handles and then exits.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
