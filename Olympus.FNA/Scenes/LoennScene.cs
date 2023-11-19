using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OlympUI;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
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

                    await UI.Run(() => {
                        el.DisposeChildren();
                        el.Add(new Group {
                            Layout = {
                                Layouts.Column(4),
                            }, 
                            Children = {
                                new LabelSmall($"Latest version: {data.Value.LatestVersion}"),
                                new LabelSmall($"Current version: {Config.Instance.CurrentLoennVersion}"),
                                new LabelSmall($"Install directory: {Config.Instance.LoennInstallDirectory}"),
                            }
                        });
                    });
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
                    new PlayButton("icons/play", "Launch", b => {}) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    },
                    new HomeScene.IconButton("icons/update", "Update", b => { }) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    },
                    new HomeScene.IconButton("icons/delete", "Uninstall", b => { }) {
                        Layout = {
                            Layouts.Fill(1.0f, 0.0f),
                        },
                    },
                }
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