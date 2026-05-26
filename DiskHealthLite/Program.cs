namespace DiskHealthLite;

static class Program
{
    [STAThread]
    static void Main()
    {
        if (!AdminLauncher.EnsureElevated())
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
