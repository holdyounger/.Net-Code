using System.Diagnostics;

class Program
{

    static void createProcess()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo { };
        string strDnrsp = "/C echo [360DNRSP] Request blocked.";

        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = strDnrsp;
        // startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }
    static void Main()
    {
        bool skipReport = false;
        string content = "696";
        long contentLen = content?.Length ?? 0;
        if (contentLen < 1024)
            skipReport = true;
        else
            skipReport = false;

        createProcess();

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        foreach (var window in shell.Windows())
        {
            try
            {
                Console.WriteLine(window.Document.Folder.Self.Path);
            }
            catch
            {
                // 忽略非 Explorer 窗口
            }
        }
    }
}
