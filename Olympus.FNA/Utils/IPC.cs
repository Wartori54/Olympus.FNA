using OlympUI;
using Olympus.NativeImpls;
using Olympus.Utils.IPCCommands;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Olympus.Utils {
    /// <summary>
    /// This class holds the logic for inter-process-communication for tasks such as the 1 click installer
    /// </summary>
    public class IPC : IDisposable {
        private CancellationTokenSource tokenSource = new();
        private CancellationToken ct;

        public IPC() {
            ct = tokenSource.Token;
        }

        public async void Start() {
            try {
                while (!ct.IsCancellationRequested) {
                    await using NamedPipeServerStream server = new(App.Name, PipeDirection.In, 1);
                    await server.WaitForConnectionAsync(ct);
                    using (StreamReader sr = new(server)) {
                        string buf = await sr.ReadToEndAsync(ct);
                        Console.WriteLine($"Received IPC text: {buf}");
                        IPCCommandsManager.RunCommand(buf);
                    }
                }
            } catch (OperationCanceledException ex) {
                // NOOP, as its intended
            }
        }

        public void Dispose() {
            tokenSource.Cancel();
        }

        public static void SendText(string text) {
            using NamedPipeClientStream client = new NamedPipeClientStream(".", App.Name, PipeDirection.Out);
            client.Connect();
            using (StreamWriter sw = new StreamWriter(client)) {
                sw.Write(text);
                sw.Close();
            }
            client.Close();
        }
        

    }

    namespace IPCCommands {
        public interface IPCCommand {
            public bool Match(string s);
            public void Run(string cmd);
        }

        public class GBOneClickInstall : IPCCommand {
            public static readonly GBOneClickInstall Instance = new GBOneClickInstall();
            private GBOneClickInstall() {}
            
            public bool Match(string s) {
                return s.StartsWith("everest:https://gamebanana.com/mmdl/");
            }

            public void Run(string cmd) {
                int modId;
                try {
                    string modIdString = cmd.Split(",")[0].Split("/")[^1];
                    modId = int.Parse(modIdString);
                } catch (Exception e) {
                    AppLogger.Log.Error(e, $"Could not parse one click install! {cmd}");
                    return;
                }

                ModAPI.RemoteModInfoAPI.RemoteModFileInfo? fileInfo = 
                    ((ModAPI.MaddieModInfoAPI) ModAPI.RemoteAPIManager.APIRegister.MaddieAPI.ApiInstance)
                    .GetModFileInfoFromFileId(modId);

                if (fileInfo == null) {
                    AppLogger.Log.Error($"Could not find mod id: {modId}!");
                    return;
                }
                
                // Request focus when we're sure that the download will start
                SingleInstance.RequestFocus();

                UI.Run(() => {
                    if (Scener.Front?.Locked ?? false) return;
                    Scener.PopFront();
                    WorkingOnItScene.Job job = ModUpdater.Jobs.GetInstallModJob(fileInfo);
                    Scener.Set<WorkingOnItScene>(job, "download_rot");
                });
            }
        }

        public static class IPCCommandsManager {
            private static readonly List<IPCCommand> Instances = new() {
                GBOneClickInstall.Instance,
            };

            public static void RunCommand(string text) {
                foreach (IPCCommand ipcCommand in Instances) {
                    if (ipcCommand.Match(text)) {
                        ipcCommand.Run(text);
                        return;
                    }
                }
            }
        }
    }
}