using Microsoft.Xna.Framework;
using MonoMod.Utils;
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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus;

public class LoennScene : Scene {
    private record struct LoennData {
        public string LatestVersion;
        public string DownloadURL;
        public string Changelog;
        
        public static async Task<LoennData?> Fetch(UrlManager urlManager) {
            try {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Olympus");
                using var res = await client.GetAsync("https://api.github.com/repos/CelestialCartographers/Loenn/releases/latest");
                using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
                
                // await using var res = urlManager.TryHttpGetDataStream("loenn_latest");
                // using var reader = new StreamReader(res);
                await using var json = new JsonTextReader(reader);

                var obj = (JObject) await JToken.ReadFromAsync(json);
                return new LoennData {
                   LatestVersion = (string) obj["tag_name"]!,
                   DownloadURL = GetDownloadURL((JArray) obj["assets"]!),
                   Changelog = (string) obj["body"]!
               };
            } catch (Exception ex) {
                AppLogger.Log.Error($"Failed to check for Lönn version: {ex}");
                return null;
            }
        }
        
        private static string GetDownloadURL(JArray assets) {
            string wantedSuffix;
            if (PlatformHelper.Is(Platform.Windows)) {
                wantedSuffix = "-windows.zip";
            } else if (PlatformHelper.Is(Platform.Linux)) {
                wantedSuffix = "-linux.zip";
            } else if (PlatformHelper.Is(Platform.MacOS)) {
                wantedSuffix = "-macos.app.zip";
            } else {
                AppLogger.Log.Error("Unsupported platform");
                return "";
            }

            foreach (JToken artifact in assets) {
                string url = (string) (artifact as JObject)!["browser_download_url"]!;
                if (url.EndsWith(wantedSuffix)) {
                    return url;
                }
            }

            AppLogger.Log.Error("Lönn artifact not found");
            return "";
        }
    }
    
    private const string YamlPath = "metadata/urls/loenn.yaml";

    private readonly UrlManager urlManager = new(YamlPath);
    
    private static bool fetched;
    private static LoennData? data;

    public LoennScene() {
        data = null;
        fetched = false;
        if (data != null) {
            fetched = true;
            return;
        }

        Task.Run(async () => {
            data = await LoennData.Fetch(urlManager);
            Refresh();
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
                Init = RegisterRefresh<Group>(async el => await UI.Run(() => {
                    var play = new PlayButton("icons/play", "Launch", _ => Launch()) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    };
                    var update = new HomeScene.IconButton("icons/update", "Update", _ => Install()) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    };
                    var install = new HomeScene.IconButton("icons/download", "Install", _ => Install()) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    };
                    var uninstall = new HomeScene.IconButton("icons/delete", "Uninstall", _ => Uninstall()) {
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
                })),
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
                    new HomeScene.IconButton("icons/wiki", "Open README", _ => URIHelper.OpenInBrowser(urlManager.GetEntry("loenn_readme").Url)) {
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
                Init = RegisterRefresh<Group>(async el => await UI.Run(() => {
                    el.DisposeChildren();
                    if (!fetched) {
                        el.Add(new Group {
                            Layout = {
                                Layouts.Row(4),
                            }, 
                            Children = {
                                new Spinner(), 
                                new Label("Loading"),
                            }
                        });
                        return;
                    }
                    if (data == null) {
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
                        return;
                    }
                    if (Config.Instance.CurrentLoennVersion == null) {
                        el.Add(new Group {
                            Layout = { Layouts.Column(4), },
                            Children = {
                                new Label($"Latest version: {data.Value.LatestVersion}"),
                                new Label("Current version: Not installed"),
                            }
                        });
                        return;
                    }

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
                            new Label($"Install directory: {Config.Instance.LoennInstallDirectory?.Replace(home, "~")}"),
                        }
                    });
                })),
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
                                    Init = RegisterRefresh<Group>(async el => await UI.Run(() => {
                                        el.DisposeChildren();
                                        if (!fetched) {
                                            el.Add(new HeaderMedium("Changelog") {
                                                Layout = {
                                                    Layouts.Left(0.5f, -0.5f),
                                                }
                                            });
                                            return;
                                        }

                                        if (data == null) return;
                                        
                                        el.Add(new HeaderMedium($"Changelog - {data.Value.LatestVersion}") {
                                            Layout = {
                                                Layouts.Left(0.5f, -0.5f),
                                            }
                                        });
                                    })),
                                },
                                
                                new Group {
                                    Layout = {
                                        Layouts.Fill(1.0f, 0.0f),
                                    },
                                    Init = RegisterRefresh<Group>(async el => await UI.Run(() => {
                                        el.DisposeChildren();
                                        if (!fetched) {
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
                                            return;
                                        }
                                        
                                        if (data == null) {
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
                                            return;
                                        }
                                        
                                        el.Add(new Group {
                                            Layout = {
                                                Layouts.Fill(1.0f, 0.0f),
                                            },
                                            Children = {
                                                //TODO: Maybe implement a simple Markdown parser? 
                                                new LabelSmall(data.Value.Changelog) { Wrap = true },
                                            }
                                        });
                                    })),
                                }
                            } 
                        }
                    }
                }
            },
        };

    private void Launch() {
        if (Config.Instance.LoennInstallDirectory == null) {
            AppLogger.Log.Warning("Tried to launch Lönn while the install directory is null");
            return;
        }
        
        Process loenn = new();
        if (PlatformHelper.Is(Platform.Windows)) {
            loenn.StartInfo.FileName = Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.exe");
            loenn.StartInfo.WorkingDirectory = Config.Instance.LoennInstallDirectory;
        } else if (PlatformHelper.Is(Platform.Linux)) {
            // Use the find-love script
            loenn.StartInfo.FileName = OlympUI.Assets.GetPath("loenn/find-love.sh");
            loenn.StartInfo.Arguments = Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.love");
            loenn.StartInfo.UseShellExecute = true;
            loenn.StartInfo.WorkingDirectory = OlympUI.Assets.GetPath("loenn");
        } else if (PlatformHelper.Is(Platform.MacOS)) {
            // Run the app
            loenn.StartInfo.FileName = "open";
            loenn.StartInfo.Arguments = "Lönn.app";
            loenn.StartInfo.UseShellExecute = true;
            loenn.StartInfo.WorkingDirectory = Config.Instance.LoennInstallDirectory;
        }

        Console.Error.WriteLine($"Starting Loenn process: {loenn.StartInfo.FileName} {loenn.StartInfo.Arguments} (in {Config.Instance.LoennInstallDirectory})");

        loenn.Start();
    }
    
    private void Install() {
        async IAsyncEnumerable<EverestInstaller.Status> InstallFunc() {
            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
            Task.Run<Task>(async () => {
                try {
                    if (data == null) {
                        AppLogger.Log.Warning("Tried to install Lönn while data isn't fetched yet");
                        return;
                    }
                    
                    if (Config.Instance.LoennInstallDirectory != null && !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                        AppLogger.Log.Warning("Config / Filesystem desync: Lönn install no longer exists");
                        chan.Writer.TryWrite(("WARN: Config / Filesystem desync: Lönn install no longer exists", 0f));
                        Config.Instance.LoennInstallDirectory = null;
                        Config.Instance.Save();
                    } else if (Config.Instance.LoennInstallDirectory == null && Directory.Exists(Path.Combine(Config.GetDefaultDir(), "Lönn"))) {
                        AppLogger.Log.Warning("Config / Filesystem desync: Lönn wasn't correctly uninstalled");
                        chan.Writer.TryWrite(("WARN: Config / Filesystem desync: Lönn install no longer exists", 0f));
                        try {
                            Directory.Delete(Path.Combine(Config.GetDefaultDir(), "Lönn"), recursive: true);
                        } catch {
                            AppLogger.Log.Error("Couldn't delete Lönn directory");
                            chan.Writer.TryWrite(("ERROR: Couldn't delete Lönn directory", 0f));
                        }
                    }
                    
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
                    
                    ZipFile.ExtractToDirectory(zipPath, installPath, overwriteFiles: true);
                    
                    if (PlatformHelper.Is(Platform.MacOS)) {
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
                    Refresh();
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
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        item.Item2 != 1f
                            ? EverestInstaller.Status.Stage.InProgress
                            : EverestInstaller.Status.Stage.Success);
                } else {
                    yield return new EverestInstaller.Status(item.Item1, 1f, EverestInstaller.Status.Stage.Fail);
                }
            }
        }

        void HandlePopup(Scene? prev, Scene? next) {
            if (prev is WorkingOnItScene) {
                //TODO: Windows & macOS
                if (PlatformHelper.Is(Platform.Linux)) {
                    Scener.Push<SetupLoennShortcutSceneLinux>();
                }
            }
            Scener.FrontChanged -= HandlePopup;
        }
        Scener.Set<WorkingOnItScene>(new WorkingOnItScene.Job(InstallFunc, "download_rot"), "download_rot");
        Scener.FrontChanged += HandlePopup;
    }
    
    private void Uninstall() {
        async IAsyncEnumerable<EverestInstaller.Status> UninstallFunc() {
            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
            Task.Run<Task>(() => {
                try {
                    if (string.IsNullOrWhiteSpace(Config.Instance.LoennInstallDirectory) || !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                        AppLogger.Log.Error($"Install directory {Config.Instance.LoennInstallDirectory} doesnt exist");
                        chan.Writer.TryWrite(("Lönn was never installed!", -1f));
                        chan.Writer.Complete();
                        return Task.CompletedTask;
                    }

                    Directory.Delete(Config.Instance.LoennInstallDirectory, recursive: true);
                    
                    if (PlatformHelper.Is(Platform.Linux)) {
                        if (!string.IsNullOrEmpty(Config.Instance.LoennLinuxDesktopEntry) && File.Exists(Config.Instance.LoennLinuxDesktopEntry))
                            File.Delete(Config.Instance.LoennLinuxDesktopEntry);
                        if (!string.IsNullOrEmpty(Config.Instance.LoennLinuxDesktopIcon) && File.Exists(Config.Instance.LoennLinuxDesktopIcon))
                            File.Delete(Config.Instance.LoennLinuxDesktopIcon);

                        Config.Instance.LoennLinuxDesktopEntry = null;
                        Config.Instance.LoennLinuxDesktopIcon = null;
                    }
                    
                    chan.Writer.TryWrite(("Lönn successfully uninstalled!", 1f));

                    Config.Instance.CurrentLoennVersion = null;
                    Config.Instance.LoennInstallDirectory = null;
                    Config.Instance.Save();
                    Refresh();
                } catch (Exception e) {
                    chan.Writer.TryWrite((e.ToString(), -1));
                    chan.Writer.TryWrite(("Failed to uninstall Lönn!", -1f));
                    AppLogger.Log.Error(e, e.Message);
                }
                
                chan.Writer.Complete();
                return Task.CompletedTask;
            });
            
            while (await chan.Reader.WaitToReadAsync())
            while (chan.Reader.TryRead(out (string, float) item)) {
                if (item.Item2 >= 0) {
                    yield return new EverestInstaller.Status(item.Item1, item.Item2,
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
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
        public static readonly new Style DefaultStyle = new() {
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
                    { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0x70) },
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