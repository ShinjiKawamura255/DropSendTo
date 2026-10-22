using System.Diagnostics;
using System.IO;
using DropSendTo.Services;

internal static partial class Program
{
    private static void VerifyStartupCommands(string output, string dotnet)
    {
        var root = Path.Combine(output, "startup-command");
        var resultDirectory = Path.Combine(root, "different working directory");
        var binaries = Path.Combine(root, "日本語 folder");
        Directory.CreateDirectory(resultDirectory);
        Directory.CreateDirectory(binaries);
        foreach (var source in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(source, Path.Combine(binaries, Path.GetFileName(source)), overwrite: true);

        var appHost = Path.Combine(binaries, "DropSendTo.SettingsProbe.exe");
        var dll = Path.Combine(binaries, "DropSendTo.SettingsProbe.dll");
        var renamedDll = Path.Combine(binaries, "名前の違う launcher.dll");
        File.Copy(dll, renamedDll, overwrite: true);

        void Verify(string name, string executable, string[] arguments, string expected)
        {
            var resultPath = Path.Combine(resultDirectory, name + ".txt");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = binaries
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.ArgumentList.Add("--startup-command");
            start.ArgumentList.Add(resultPath);
            using var process = Process.Start(start)!;
            try
            {
                Assert(process.WaitForExit(15000), $"Startup command probe finishes: {name}");
                Assert(process.ExitCode == 0, $"Startup command probe succeeds: {name}");
                Assert(File.ReadAllText(resultPath) == expected, $"Startup command is preserved: {name}");
            }
            finally
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
            }
        }

        Verify("apphost", appHost, [], StartupCommandBuilder.Quote(appHost));
        var dotnetPath = Path.GetFullPath(dotnet);
        var hosted = $"{StartupCommandBuilder.Quote(dotnetPath)} {StartupCommandBuilder.Quote(dll)}";
        Verify("dotnet-absolute", dotnet, [dll], hosted);
        Verify("dotnet-relative", dotnet, [".\\DropSendTo.SettingsProbe.dll"], hosted);
        Verify("dotnet-renamed", dotnet,
            ["exec", "--runtimeconfig", Path.ChangeExtension(dll, ".runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(dll, ".deps.json"), renamedDll],
            $"{StartupCommandBuilder.Quote(dotnetPath)} {StartupCommandBuilder.Quote(renamedDll)}");
    }
}
