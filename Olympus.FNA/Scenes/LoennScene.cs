using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OlympUI;
using Olympus.API;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus;

public class LoennScene : Scene {
    private record struct LoennData {
        public string LatestVersion;
        public string DownloadURL;

        public string ChangelogTitle;
        public string ChangelogBody;
        
        public static async Task<LoennData?> Fetch() {
            try {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Olympus");
                
                using var res = await client.GetAsync("https://api.github.com/repos/CelestialCartographers/Loenn/releases/latest");
                using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
                await using var json = new JsonTextReader(reader);

                var obj = (JObject) await JToken.ReadFromAsync(json);
                var data = new LoennData();
                data.LatestVersion = (string) obj["tag_name"]!;
                data.DownloadURL = GetDownloadURL((JArray) obj["assets"]!);
                data.ChangelogTitle = (string) obj["name"]!;
                data.ChangelogBody = (string) obj["body"]!;
                    

                return data;
            } catch (Exception ex) {
                AppLogger.Log.Error($"Failed to check for Lönn version: {ex}");
                return null;
            }
        }
        
        private static string GetDownloadURL(JArray assets) {
            string wantedSuffix;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                wantedSuffix = "-windows.zip";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                wantedSuffix = "-linux.zip";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                wantedSuffix = "-macos.app.zip";
            } else {
                AppLogger.Log.Error("Unsupported platform");
                return "";
            }

            foreach (JToken artifact in assets) {
                string url = (string) (artifact as JObject)["browser_download_url"];
                if (url.EndsWith(wantedSuffix)) {
                    return url;
                }
            }

            AppLogger.Log.Error("Lönn artifact not found");
            return "";
        }
    }

    private static bool fetched;
    private static LoennData? data;

    private Func<Task>? updateButtons;
    private Func<Task>? updateLabels;

    public LoennScene() {
        data = null;
        fetched = false;
        if (data != null) {
            fetched = true;
            return;
        }

        Task.Run(async () => {
            data = await LoennData.Fetch();
            await Task.Delay(1000);
            fetched = true;
        });
    }
    
    public override Element Generate()
        => new Group {
            ID = "LoennScene",
            Layout = {
                Layouts.Fill(1, 1), 
                Layouts.Row(),
            },
            Style = {
                { Group.StyleKeys.Spacing, 32 }, 
                { Group.StyleKeys.Padding, 32 },
            },
            Children = {
                new Group {
                    Layout = {
                        Layouts.Column(), 
                        Layouts.Fill(0.5f, 1.0f, 32 / 2, 0),
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 16 },
                    },
                    Children = GenerateButtonsPanel(),
                },
                new Group {
                    Layout = {
                        Layouts.Fill(0.5f, 1.0f, 32 / 2, 0),
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 16 },
                    },
                    Children = GenerateChangelogPanel(),
                },
            }
        };

    private ObservableCollection<Element> GenerateButtonsPanel()
        => new() {
            new Group {
                Layout = {
                    Layouts.Fill(1.0f, 0.0f),
                    Layouts.Column(),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 12 },
                },
                Init = RegisterRefresh<Group>(async el => {
                    updateButtons = async () => await UI.Run(() => {
                        var play = new PlayButton("icons/play", "Launch", b => Launch()) {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                            },
                        };
                        var update = new HomeScene.IconButton("icons/update", "Update", b => Update()) {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                            },
                        };
                        var install = new HomeScene.IconButton("icons/download", "Install", b => Install()) {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                            },
                        };
                        var uninstall = new HomeScene.IconButton("icons/delete", "Uninstall", b => Uninstall()) {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                            },
                        };
                        el.DisposeChildren();

                        if (data == null) {
                            if (Config.Instance.CurrentLoennVersion == null) {
                                play.Enabled = false;
                                install.Enabled = false;
                                uninstall.Enabled = false;
                                el.Add(play);
                                el.Add(install);
                                el.Add(uninstall);
                            } else {
                                update.Enabled = false;
                                el.Add(play);
                                el.Add(update);
                                el.Add(uninstall);
                            }
                            return;
                        }
                        if (Config.Instance.CurrentLoennVersion == null) {
                            play.Enabled = false;
                            uninstall.Enabled = false;
                            el.Add(play);
                            el.Add(install);
                            el.Add(uninstall);
                            return;
                        }
                        if (Config.Instance.CurrentLoennVersion != data.Value.LatestVersion) {
                            el.Add(play);
                            el.Add(update);
                            el.Add(uninstall);
                            return;
                        }

                        update.Enabled = false;
                        el.Add(play);
                        el.Add(update);
                        el.Add(uninstall);
                    });
                    
                    await updateButtons();
                    while (!fetched) { await Task.Delay(10); } 
                    await updateButtons();
                }),
            },
            
            new Group {
                Layout = {
                    Layouts.Fill(1.0f, 0.0f),
                    Layouts.Column(),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 12 },
                },
                Children = {
                    new Label("Check the README for usage instructions, keybinds, help and more") {
                        Wrap = true,  
                    },
                    new HomeScene.IconButton("icons/wiki", "Open README", b => { }) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    },
                }
            },
            
            new Group {
                H = 60, 
                Layout = {
                    Layouts.Fill(1.0f, 0.0f), 
                    Layouts.Column(false),
                },
                Init = RegisterRefresh<Group>(async el => {
                    await UI.Run(() => {
                        el.DisposeChildren();
                        el.Add(new Group {
                            Layout = {
                                Layouts.Row(4),
                            }, 
                            Children = {
                                new Spinner(), 
                                new Label("Loading"),
                            }
                        });
                    });

                    while (!fetched) { await Task.Delay(10); } 

                    if (data == null) {
                        await UI.Run(() => {
                            el.DisposeChildren();
                            el.Add(new Group {
                                Layout = {
                                    Layouts.Row(4),
                                }, 
                                Children = {
                                    new Image(OlympUI.Assets.GetTexture("icons/close")) {
                                        AutoH = OlympUI.Assets.Font.Value.LineHeight,
                                        Style = {
                                            {ImageBase.StyleKeys.Color, Color.Red}
                                        },
                                    },
                                    new Label("Failed to fetch Lönn status!\n") {
                                        Style = {
                                            Color.Red
                                        }
                                    },
                                }
                            });
                        });
                        return;
                    }

                    updateLabels = async () => await UI.Run(() => {
                        el.DisposeChildren();

                        if (Config.Instance.CurrentLoennVersion == null) {
                            el.Add(new Group {
                                Layout = { Layouts.Column(4), },
                                Children = {
                                    new Label($"Latest version: {data.Value.LatestVersion}"),
                                    new Label("Current version: Not installed"),
                                }
                            });
                        } else {
                            string? home = Environment.GetEnvironmentVariable("HOME");
                            if (string.IsNullOrEmpty(home)) {
                                home = "";
                            }
                            el.Add(new Group {
                                Layout = {
                                    Layouts.Column(4),
                                },
                                Children = {
                                    new Label($"Latest version: {data.Value.LatestVersion}"),
                                    new Label($"Current version: {Config.Instance.CurrentLoennVersion}"),
                                    new Label($"Install directory: {Config.Instance.LoennInstallDirectory.Replace(home, "~")}"),
                                }
                            });
                        }
                    });
                    await updateLabels();
                }),
            },
        };

    private ObservableCollection<Element> GenerateChangelogPanel()
        => new() {
            new Panel {
                Clip = true,
                Layout = {
                    Layouts.Fill(),
                },
                Children = {
                    new ScrollBox {
                        Layout = {
                            Layouts.Fill(),
                        },
                        Content = new Group {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                                Layouts.Column(8),
                            },
                            Style = {
                                { Group.StyleKeys.Padding, 14 },
                            },
                            Children = {
                                new Group {
                                    Layout = {
                                        Layouts.Fill(1.0f, 0.0f),
                                    },
                                    Init = RegisterRefresh<Group>(async el => {
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new HeaderMedium("Changelog") {
                                                Layout = {
                                                    Layouts.Left(0.5f, -0.5f),
                                                }
                                            });
                                        });
                                        
                                        while (!fetched) { await Task.Delay(10); }

                                        if (data == null) return;
                                        
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new HeaderMedium($"Changelog - {data.Value.LatestVersion}") {
                                                Layout = {
                                                    Layouts.Left(0.5f, -0.5f),
                                                }
                                            });
                                        });
                                    })
                                },
                                
                                new Group {
                                    Layout = {
                                        Layouts.Fill(1.0f, 0.0f),
                                    },
                                    Init = RegisterRefresh<Group>(async el => {
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new Group {
                                                Layout = {
                                                    Layouts.Top(0.5f, -0.5f),
                                                    Layouts.Left(0.5f, -0.5f),
                                                    Layouts.Row(4),
                                                }, 
                                                Children = {
                                                    new Spinner(), 
                                                    new Label("Loading"),
                                                }
                                            });
                                        });
                                        
                                        while (!fetched) { await Task.Delay(10); }

                                        if (data == null) {
                                            await UI.Run(() => {
                                                el.DisposeChildren();
                                                el.Add(new Group {
                                                    Layout = {
                                                        Layouts.Top(0.5f, -0.5f),
                                                        Layouts.Left(0.5f, -0.5f),
                                                        Layouts.Row(4),
                                                    }, 
                                                    Children = {
                                                        new Image(OlympUI.Assets.GetTexture("icons/close")) {
                                                            AutoH = OlympUI.Assets.Font.Value.LineHeight,
                                                            Style = {
                                                                {ImageBase.StyleKeys.Color, Color.Red}
                                                            },
                                                        },
                                                        new Label("Failed to fetch Lönn status!\n") {
                                                            Style = {
                                                                Color.Red
                                                            }
                                                        },
                                                    }
                                                });
                                            });
                                            return;
                                        }
                                        
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new Group {
                                                Layout = {
                                                    Layouts.Fill(1.0f, 0.0f),
                                                },
                                                Children = {
                                                    //TODO: Maybe implement a simple Markdown parser? 
                                                    new LabelSmall(data.Value.ChangelogBody) { Wrap = true },
                                                }
                                            });
                                        });
                                    }),
                                }
                            } 
                        }
                    }
                }
            },
        };

    private void Launch() {
        Process loenn = new();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            loenn.StartInfo.FileName = Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.exe");
            loenn.StartInfo.WorkingDirectory = Config.Instance.LoennInstallDirectory;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            // Use the find-love script
            loenn.StartInfo.FileName = OlympUI.Assets.GetPath("love/find-love.sh");
            loenn.StartInfo.Arguments = Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.love");
            loenn.StartInfo.UseShellExecute = true;
            loenn.StartInfo.WorkingDirectory = OlympUI.Assets.GetPath("love");
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            // Run the app
            loenn.StartInfo.FileName = "open";
            loenn.StartInfo.Arguments = "Lönn.app";
            loenn.StartInfo.UseShellExecute = true;
            loenn.StartInfo.WorkingDirectory = Config.Instance.LoennInstallDirectory;
        }

        Console.Error.WriteLine($"Starting Loenn process: {loenn.StartInfo.FileName} {loenn.StartInfo.Arguments} (in {Config.Instance.LoennInstallDirectory})");

        loenn.Start();
    }
    
    private void Update() {
        async IAsyncEnumerable<EverestInstaller.Status> UpdateFunc() {
            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
            Task.Run<Task>(async () => {
                try {
                    if (string.IsNullOrWhiteSpace(Config.Instance.LoennInstallDirectory) || !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                        AppLogger.Log.Error($"Install directory {Config.Instance.LoennInstallDirectory} doesnt exist");
                        chan.Writer.TryWrite(("Lönn was never installed!", -1f));
                        chan.Writer.Complete();
                        return;
                    }
                    
                    Directory.Delete(Config.Instance.LoennInstallDirectory, recursive: true);
                    chan.Writer.TryWrite(("Lönn successfully removed old version", 1f));

                    string zipPath = Path.Combine(Config.GetCacheDir(), "Lönn.zip");
                    
                    var lastUpdate = DateTime.Now;
                    AppLogger.Log.Information($"Trying {data.Value.DownloadURL}");
                    await Web.DownloadFileWithProgress(data.Value.DownloadURL, zipPath, (pos, length, speed) => {
                        if (lastUpdate.Add(TimeSpan.FromSeconds(1)).CompareTo(DateTime.Now) < 0) {
                            chan.Writer.TryWrite(($"Downloading... {pos*100F/length}% {speed} Kib/s {pos}", (float) pos / length));
                            lastUpdate = DateTime.Now;
                        }
                        return true;
                    });
                    
                    ZipFile.ExtractToDirectory(zipPath, Config.Instance.LoennInstallDirectory);
                    
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                        // Make Lönn actually executable
                        Process chmod = new() { 
                            StartInfo = {
                                FileName = "chmod", 
                                Arguments = "+x love Lönn.sh", 
                                UseShellExecute = true,
                                WorkingDirectory = Config.Instance.LoennInstallDirectory + "/Lönn.app/Contents/MacOS"
                            }
                        };
                        chmod.Start();
                        await chmod.WaitForExitAsync();
                    }
                    
                    chan.Writer.TryWrite(("Lönn successfully updated!", 1f));

                    Config.Instance.CurrentLoennVersion = data.Value.LatestVersion;
                    Config.Instance.Save();
                    if (updateButtons != null) await updateButtons();
                    if (updateLabels != null) await updateLabels();
                } catch (Exception e) {
                    chan.Writer.TryWrite((e.ToString(), -1));
                    chan.Writer.TryWrite(("Failed to install Lönn!", -1f));
                    AppLogger.Log.Error(e, e.Message);
                }
                
                chan.Writer.Complete();
            });
            
            while (await chan.Reader.WaitToReadAsync())
            while (chan.Reader.TryRead(out (string, float) item)) {
                if (item.Item2 >= 0) {
                    yield return new EverestInstaller.Status(item.Item1, item.Item2,
                        item.Item2 != 1f
                            ? EverestInstaller.Status.Stage.InProgress
                            : EverestInstaller.Status.Stage.Success);
                } else {
                    yield return new EverestInstaller.Status(item.Item1, 1f, EverestInstaller.Status.Stage.Fail);
                }
            }
        }

        Scener.Set<WorkingOnItScene>(new WorkingOnItScene.Job(UpdateFunc, "download_rot"), "download_rot");
    }
    
    private void Install() {
        async IAsyncEnumerable<EverestInstaller.Status> InstallFunc() {
            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
            Task.Run<Task>(async () => {
                try {
                    string zipPath = Path.Combine(Config.GetCacheDir(), "Lönn.zip");
                    string installPath = Path.Combine(Config.GetDefaultDir(), "Lönn"); 
                    
                    var lastUpdate = DateTime.Now;
                    AppLogger.Log.Information($"Trying {data.Value.DownloadURL}");
                    await Web.DownloadFileWithProgress(data.Value.DownloadURL, zipPath, (pos, length, speed) => {
                        if (lastUpdate.Add(TimeSpan.FromSeconds(1)).CompareTo(DateTime.Now) < 0) {
                            chan.Writer.TryWrite(($"Downloading... {pos*100F/length}% {speed} Kib/s {pos}", (float) pos / length));
                            lastUpdate = DateTime.Now;
                        }
                        return true;
                    });
                    
                    ZipFile.ExtractToDirectory(zipPath, installPath);
                    
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                        // Make Lönn actually executable
                        Process chmod = new() { 
                            StartInfo = {
                                FileName = "chmod", 
                                Arguments = "+x love Lönn.sh", 
                                UseShellExecute = true,
                                WorkingDirectory = installPath + "/Lönn.app/Contents/MacOS"
                            }
                        };
                        chmod.Start();
                        await chmod.WaitForExitAsync();
                    }
                    
                    chan.Writer.TryWrite(("Lönn successfully installed!", 1f));

                    Config.Instance.CurrentLoennVersion = data.Value.LatestVersion;
                    Config.Instance.LoennInstallDirectory = installPath;
                    Config.Instance.Save();
                    if (updateButtons != null) await updateButtons();
                    if (updateLabels != null) await updateLabels();
                } catch (Exception e) {
                    chan.Writer.TryWrite((e.ToString(), -1));
                    chan.Writer.TryWrite(("Failed to install Lönn!", -1f));
                    AppLogger.Log.Error(e, e.Message);
                }
                
                chan.Writer.Complete();
            });
            
            while (await chan.Reader.WaitToReadAsync())
            while (chan.Reader.TryRead(out (string, float) item)) {
                if (item.Item2 >= 0) {
                    yield return new EverestInstaller.Status(item.Item1, item.Item2,
                        item.Item2 != 1f
                            ? EverestInstaller.Status.Stage.InProgress
                            : EverestInstaller.Status.Stage.Success);
                } else {
                    yield return new EverestInstaller.Status(item.Item1, 1f, EverestInstaller.Status.Stage.Fail);
                }
            }
        }

        Scener.Set<WorkingOnItScene>(new WorkingOnItScene.Job(InstallFunc, "download_rot"), "download_rot");
    }
    
    private void Uninstall() {
        async IAsyncEnumerable<EverestInstaller.Status> UninstallFunc() {
            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
            Task.Run<Task>(async () => {
                try {
                    if (string.IsNullOrWhiteSpace(Config.Instance.LoennInstallDirectory) || !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                        AppLogger.Log.Error($"Install directory {Config.Instance.LoennInstallDirectory} doesnt exist");
                        chan.Writer.TryWrite(("Lönn was never installed!", -1f));
                        chan.Writer.Complete();
                        return;
                    }

                    Directory.Delete(Config.Instance.LoennInstallDirectory, recursive: true);
                    chan.Writer.TryWrite(("Lönn successfully uninstalled!", 1f));

                    Config.Instance.CurrentLoennVersion = null;
                    Config.Instance.LoennInstallDirectory = null;
                    Config.Instance.Save();
                    if (updateButtons != null) await updateButtons();
                    if (updateLabels != null) await updateLabels();
                } catch (Exception e) {
                    chan.Writer.TryWrite((e.ToString(), -1));
                    chan.Writer.TryWrite(("Failed to uninstall Lönn!", -1f));
                    AppLogger.Log.Error(e, e.Message);
                }
                
                chan.Writer.Complete();
            });
            
            while (await chan.Reader.WaitToReadAsync())
            while (chan.Reader.TryRead(out (string, float) item)) {
                if (item.Item2 >= 0) {
                    yield return new EverestInstaller.Status(item.Item1, item.Item2,
                        item.Item2 != 1f
                            ? EverestInstaller.Status.Stage.InProgress
                            : EverestInstaller.Status.Stage.Success);
                } else {
                    yield return new EverestInstaller.Status(item.Item1, 1f, EverestInstaller.Status.Stage.Fail);
                }
            }
        }

        Scener.Set<WorkingOnItScene>(new WorkingOnItScene.Job(UninstallFunc, "download_rot"), "download_rot");
    }

    public partial class PlayButton : HomeScene.IconButton {
        public new static readonly Style DefaultStyle = new() {
            {
                StyleKeys.Normal,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x00, 0x30, 0x10, 0x50) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },

            {
                StyleKeys.Disabled,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x05, 0x35, 0x1E, 0x70) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },

            {
                StyleKeys.Hovered,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x40, 0x70, 0x45, 0x70) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },

            {
                StyleKeys.Pressed,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x20, 0x50, 0x25, 0x70) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },
        };

        public PlayButton(string icon, string text, Action<PlayButton> cb)
            : base(icon, text, b => cb((PlayButton) b)) { }

    }
}