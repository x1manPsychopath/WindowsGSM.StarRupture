using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Plugins
{
    public class StarRupture : SteamCMDAgent
    {
        // Plugin metadata
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.StarRupture",
            author = "x1manPsychopath",
            description = "WindowsGSM plugin for StarRupture Dedicated Server",
            version = "1.0.0",
            url = "https://github.com/x1manPsychopath/WindowsGSM.StarRupture",
            color = "#FF6600"
        };

        public StarRupture(ServerConfig serverData) : base(serverData)
            => base.serverData = _serverData = serverData;

        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // SteamCMD settings
        public override bool loginAnonymous => false; // requires Steam login
        public override string AppId => "3809400";

        // Game server fixed values
        public override string StartPath => "StarRuptureServerEOS.exe";
        public string FullName = "StarRupture Dedicated Server";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 0;
        public object QueryMethod = null; // StarRupture does not expose a query protocol

        // Defaults (mostly for WGSM GUI; game is CLI-port-only)
        public string ServerName = "StarRupture Server";
        public string Port = "7777";
        public string Additional = string.Empty;

        // StarRupture currently does not use config files for basic server params
        public async void CreateServerCFG()
        {
            // Intentionally left blank
        }

        // Build command-line arguments
        private string BuildArguments()
        {
            // Base port from WindowsGSM, fallback to default
            string port =
                !string.IsNullOrWhiteSpace(_serverData.ServerPort)
                    ? _serverData.ServerPort
                    : Port;

            // Environment override (optional convenience)
            string envPort = Environment.GetEnvironmentVariable("STARRUPTURE_PORT");
            if (!string.IsNullOrWhiteSpace(envPort))
            {
                port = envPort;
            }

            // Required by official docs
            string args = $"-Log -port={port}";

            // Append any extra params from WGSM if present
            if (!string.IsNullOrWhiteSpace(_serverData.ServerParam))
            {
                args += " " + _serverData.ServerParam;
            }

            return args;
        }

        // Start server
        public async Task<Process> Start()
        {
            string exePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{Path.GetFileName(exePath)} not found ({exePath})";
                return null;
            }

            string param = BuildArguments();

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = exePath,
                    Arguments = param,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                p.Start();

                if (AllowsEmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        // Stop server
        public async Task Stop(Process p)
        {
            if (p == null || p.HasExited)
            {
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    ServerConsole.SetMainWindow(p.MainWindowHandle);
                    ServerConsole.SendWaitToMainWindow("^c");
                });

                await Task.Delay(2000);

                if (!p.HasExited)
                {
                    p.Kill();
                }
            }
            catch (Exception e)
            {
                Error = $"Failed to stop server: {e.Message}";
            }
        }

        // Update server (SteamCMD)
        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            // Force non-anonymous login to ensure StarRupture can be installed
            var (p, error) = await Installer.SteamCMD.UpdateEx(
                serverData.ServerID,
                AppId,
                validate,
                custom: custom,
                loginAnonymous: false
            );

            Error = error;

            if (p != null)
            {
                await Task.Run(() => p.WaitForExit());
            }

            return p;
        }

        // Relaxed: consider install valid if folder exists
        public bool IsInstallValid()
        {
            string folder = ServerPath.GetServersServerFiles(_serverData.ServerID);
            if (!Directory.Exists(folder))
            {
                return false;
            }

            // Executable may not exist yet if SteamCMD hasn't completed, but folder presence is enough
            return true;
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {Path.GetFileName(exePath)}";
            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}
