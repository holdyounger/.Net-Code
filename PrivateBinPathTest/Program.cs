using System;

class Program
{
    static void Main()
    {
        AppDomainSetup setup = new AppDomainSetup();
        // 包含空格的目录路径
        setup.PrivateBinPath = @"C:\My Files\Assemblies;C:\Another Folder\Libs";

        AppDomain newDomain = AppDomain.CreateDomain("NewAppDomain", null, setup);

        try
        {
            newDomain.DoCallBack(() =>
            {
                Console.WriteLine($"PrivateBinPath: {AppDomain.CurrentDomain.SetupInformation.PrivateBinPath}");
            });
        }
        finally
        {
            AppDomain.Unload(newDomain);
        }
    }
}