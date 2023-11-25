using Microsoft.Xna.Framework;
using MonoMod.Utils;
using Newtonsoft.Json;
using OlympUI;
using Olympus.API;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Olympus;

public class LoennScene : Scene {
    private record struct LoennData {
        public string LatestVersion;
        public string DownloadURL;
        public string Changelog;

        public class LoennVersion {
            [JsonProperty("tag_name")]
            public string TagName = "";
            [JsonProperty("assets")]
            public List<LoennVersionAsset> Assets = new();
            [JsonProperty("body")]
            public string Body = "";

            public class LoennVersionAsset {
                [JsonProperty("browser_download_url")]
                public string BrowserDownloadUrl = "";
            }
        }
        
        public static async Task<LoennData?> Fetch(UrlManager urlManager) {
            try {
                await using Stream res = urlManager.TryHttpGetDataStream("loenn_latest");
                using StreamReader reader = new(res);
                await using JsonTextReader json = new(reader);

                LoennVersion? loennVersion = JsonHelper.Serializer.Deserialize<LoennVersion>(json);
                if (loennVersion == null) {
                    return null;
                }

                return new LoennData {
                   LatestVersion = loennVersion.TagName,
                   DownloadURL = GetDownloadURL(loennVersion.Assets),
                   Changelog = loennVersion.Body
               };
            } catch (Exception ex) {
                AppLogger.Log.Error($"Failed to check for Lönn version: {ex}");
                MetaNotificationScene.PushNotification(new Notification{ Message = $"Failed to check for Lönn version", Level = Notification.SeverityLevel.Warning });
                return null;
            }
        }
        
        private static string GetDownloadURL(List<LoennVersion.LoennVersionAsset> assets) {
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

            foreach (LoennVersion.LoennVersionAsset asset in assets) {
                string url = asset.BrowserDownloadUrl;
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

                    var groupEl = el.Add(new Group {
                        Layout = { 
                            Layouts.Column(4), 
                        },
                    });
                    
                    groupEl.Add(data != null
                        ? new Label($"Latest version: {data.Value.LatestVersion}")
                        : new Group {
                            Layout = {
                                Layouts.Row(4),
                            },
                            Children = {
                                new Label($"Latest version:"),
                                new Image(OlympUI.Assets.GetTexture("icons/close")) {
                                    AutoH = OlympUI.Assets.Font.Value.LineHeight,
                                    Style = {
                                        {ImageBase.StyleKeys.Color, Color.Red}
                                    },
                                },
                                new Label("Failed to fetch latest Lönn version!\n") {
                                    Style = {
                                        Color.Red
                                    }
                                },
                            }
                        });
                    groupEl.Add(Config.Instance.CurrentLoennVersion != null
                        ? new Label($"Current version: {Config.Instance.CurrentLoennVersion}")
                        : new Label("Current version: Not installed"));

                    string? home = Environment.GetEnvironmentVariable("HOME");
                    if (string.IsNullOrEmpty(home)) {
                        home = "";
                    }
                    if (Config.Instance.LoennInstallDirectory != null) {
                        groupEl.Add(new Label($"Install directory: {Config.Instance.LoennInstallDirectory?.Replace(home, "~")}"));
                    }
                })),
            },
            
            new Group {
                Layout = {
                    Layouts.Fill(1.0f, 0.0f), 
                    Layouts.Column(false),
                },
                Init = RegisterRefresh<Group>(async el => await UI.Run(() => {
                    el.DisposeChildren();
                    if (fetched && data == null) {
                        // ReSharper disable once AsyncVoidLambda
                        el.Add(new HomeScene.IconButton("icons/retry", "Retry fetching Lönn data", async _ => {
                            fetched = false;
                            Refresh();
                            data = await LoennData.Fetch(urlManager);
                            // If for some reason Github fails, the request would probably error quickly.
                            // We add an artificially delay to make the user think Olympus is actually doing something.
                            // This also has the benefit of rate-limiting the user since Github has a pretty bad limit of 60 requests/hours
                            // for non-authorized users and spamming this button could hit that limit quickly.
                            await Task.Delay(2000);
                            fetched = true;
                            Refresh();
                        }));
                    }
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
                                        if (!fetched || data == null) {
                                            el.Add(new HeaderMedium("Changelog") {
                                                Layout = {
                                                    Layouts.Left(0.5f, -0.5f),
                                                }
                                            });
                                            return;
                                        }

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
                                                    new Label("Failed to fetch Lönn changelog!\n") {
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
            if (data == null) {
                AppLogger.Log.Error("Tried to install Lönn while data isn't fetched yet!");
                yield return new EverestInstaller.Status("ERROR: Tried to install Lönn while data isn't fetched yet!", 0f, EverestInstaller.Status.Stage.Fail);
                yield break;
            }
            
            if (Config.Instance.LoennInstallDirectory != null && !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                AppLogger.Log.Warning("Config / Filesystem desync: Lönn install no longer exists");
                yield return new EverestInstaller.Status("WARN: Config / Filesystem desync: Lönn install no longer exists", 0f, EverestInstaller.Status.Stage.InProgress);
                Config.Instance.LoennInstallDirectory = null;
                Config.Instance.Save();
            } else if (Config.Instance.LoennInstallDirectory == null && Directory.Exists(Path.Combine(Config.GetDefaultDir(), "Lönn"))) {
                AppLogger.Log.Warning("Config / Filesystem desync: Lönn wasn't correctly uninstalled");
                yield return new EverestInstaller.Status("WARN: Config / Filesystem desync: Lönn install no longer exists", 0f, EverestInstaller.Status.Stage.InProgress);
                
                string? error = null;
                try {
                    Directory.Delete(Path.Combine(Config.GetDefaultDir(), "Lönn"), recursive: true);
                } catch (Exception e) {
                    AppLogger.Log.Error(e, "Couldn't delete Lönn directory");
                    error = e.ToString();
                    error += "\nERROR: Couldn't delete Lönn directory";
                }
                if (error != null) {
                    yield return new EverestInstaller.Status(error, 0f, EverestInstaller.Status.Stage.Fail);
                    yield break;
                }
            }
            
            string zipPath = Path.Combine(Config.GetCacheDir(), "Lönn.zip");
            string installPath = Path.Combine(Config.GetDefaultDir(), "Lönn");

            Channel<(string, float)> chan = Channel.CreateUnbounded<(string, float)>();
#pragma warning disable CS4014 // This is awaited while reading the channel
            Task.Run<Task>(async () => {
#pragma warning restore CS4014
                try {
                    var lastUpdate = DateTime.Now;
                    AppLogger.Log.Information($"Trying {data.Value.DownloadURL}");
                    await Web.DownloadFileWithProgress(data.Value.DownloadURL, zipPath, (pos, length, speed) => {
                        if (lastUpdate.Add(TimeSpan.FromSeconds(1)).CompareTo(DateTime.Now) < 0) {
                            chan.Writer.TryWrite(($"Downloading... {pos*100F/length}% {speed} Kib/s {pos}", ((float) pos / length) * 0.9f)); // 0% -> 90%
                            lastUpdate = DateTime.Now;
                        }
                        return true;
                    });
                } catch (Exception e) {
                    chan.Writer.TryWrite((e.ToString(), -1));
                    chan.Writer.TryWrite(("Failed to download Lönn!", -1f));
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
            
            ZipFile.ExtractToDirectory(zipPath, installPath, overwriteFiles: true);
            
            if (PlatformHelper.Is(Platform.MacOS)) {
                // Make Lönn actually executable
                string targetDir = Path.Combine(installPath, "Lönn.app", "Contents", "MacOS");
                if (EverestInstaller.chmod(Path.Combine(targetDir, "love"), 0755) != 0)
                    AppLogger.Log.Error($"Failed to chmod file {Path.Combine(targetDir, "love")}");
                if (EverestInstaller.chmod(Path.Combine(targetDir, "Lönn.sh"), 0755) != 0) 
                    AppLogger.Log.Error($"Failed to chmod file {Path.Combine(targetDir, "Lönn.sh")}");
            }
             
            yield return new EverestInstaller.Status("Successfully installed Lönn", 1f, EverestInstaller.Status.Stage.Success);

            Config.Instance.CurrentLoennVersion = data.Value.LatestVersion;
            Config.Instance.LoennInstallDirectory = installPath;
            Config.Instance.Save();
            Refresh();
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
#pragma warning disable CS1998 // Can't use IAsyncEnumerable without async 
        async IAsyncEnumerable<EverestInstaller.Status> UninstallFunc() {
#pragma warning restore CS1998
            if (string.IsNullOrWhiteSpace(Config.Instance.LoennInstallDirectory) || !Directory.Exists(Config.Instance.LoennInstallDirectory)) {
                AppLogger.Log.Error($"Install directory {Config.Instance.LoennInstallDirectory} doesnt exist");
                yield return new EverestInstaller.Status("ERROR: Lönn was never installed!", 0f, EverestInstaller.Status.Stage.Fail);
                yield break;
            }
            
            if (PlatformHelper.Is(Platform.Linux)) {
                if (!string.IsNullOrEmpty(Config.Instance.LoennLinuxDesktopEntry) && File.Exists(Config.Instance.LoennLinuxDesktopEntry)) {
                    File.Delete(Config.Instance.LoennLinuxDesktopEntry);
                    yield return new EverestInstaller.Status("Uninstalled Lönn desktop entry", 0.25f, EverestInstaller.Status.Stage.InProgress);
                }
                if (!string.IsNullOrEmpty(Config.Instance.LoennLinuxDesktopIcon) && File.Exists(Config.Instance.LoennLinuxDesktopIcon)) {
                    File.Delete(Config.Instance.LoennLinuxDesktopIcon);
                    yield return new EverestInstaller.Status("Uninstalled Lönn desktop icon", 0.5f, EverestInstaller.Status.Stage.InProgress);
                }

                Config.Instance.LoennLinuxDesktopEntry = null;
                Config.Instance.LoennLinuxDesktopIcon = null;
            }
            
            string? error = null;
            try {
                Directory.Delete(Config.Instance.LoennInstallDirectory, recursive: true);
            } catch (Exception e) {
                AppLogger.Log.Error(e, "Couldn't delete Lönn directory");
                error = e.ToString();
                error += "\nERROR: Couldn't delete Lönn directory";
            }
            if (error != null) {
                yield return new EverestInstaller.Status(error, 0f, EverestInstaller.Status.Stage.Fail);
                yield break;
            }
            yield return new EverestInstaller.Status("Successfully uninstalled Lönn", 1f, EverestInstaller.Status.Stage.Success);
            
            Config.Instance.CurrentLoennVersion = null;
            Config.Instance.LoennInstallDirectory = null;
            Config.Instance.Save();
            Refresh();
        }

        Scener.Set<WorkingOnItScene>(new WorkingOnItScene.Job(UninstallFunc, "download_rot"), "download_rot");
    }

    public class PlayButton : HomeScene.IconButton {
        // ReSharper disable once UnusedMember.Global
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