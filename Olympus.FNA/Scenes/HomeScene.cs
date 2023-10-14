using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using OlympUI.Modifiers;
using Olympus.API;
using Olympus.ColorThief;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Olympus.ModAPI;

namespace Olympus {
    public class HomeScene : Scene {

        private LockedAction<Installation?>? refreshModList;
        private LockedAction<object?>? loadMoreMods;
        private LockedAction<Installation?>? refreshModsHeader;

        private CancellationTokenSource cancellationToken;
        private CancellationToken? ct;

        // If updates take more than one second between writes the InstallDirty event may be fired, void those
        // only allow them back once updating is done
        public bool UpdateInProgress { get; private set; }

        public HomeScene() {
            cancellationToken = new CancellationTokenSource();

            if (Config.Instance.Installation != null)
                Config.Instance.Installation.InstallDirty += SubAction;
            Config.Instance.SubscribeInstallUpdateNotify((i, oldI) => {
                if (oldI != null)
                    oldI.InstallDirty -= SubAction;
                if (i != null)
                    i.InstallDirty += SubAction;
                cancellationToken.Cancel();
                refreshModList?.TryRun(i);
            });
            
            Config.Instance.SubscribeInstallUpdateNotify(i => refreshModsHeader?.TryRun(i));
            return;
            
            void SubAction(Installation? i) {
                if (!UpdateInProgress)
                    refreshModList?.TryRun(i);
            }
        }

        public override Element Generate()
            => new Group() {
                ID = "HomeScene",
                Style = {
                    { Group.StyleKeys.Padding, 8 },
                },
                Layout = {
                    Layouts.Fill(1, 1),
                },
                Children = {

                    new Group() {
                        Style = {
                            { Group.StyleKeys.Spacing, 16 },
                        },
                        Layout = {
                            Layouts.Fill(1f, 0.4f, 0, 32),
                            Layouts.Top(),
                            Layouts.Left(),
                            Layouts.Column(false),
                        },
                        Children = {
                            new Group() {
                                Layout = {
                                    Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                    Layouts.Row(false),
                                },
                                Init = RegisterRefresh<Group>(async el => {
                                    await UI.Run(() => {
                                        el.DisposeChildren();
                                        el.Add(new Group() {
                                            Layout = {
                                                Layouts.Fill(1, 1),
                                            },
                                            Children = {
                                                new Group() {
                                                    Layout = {
                                                        Layouts.Left(0.5f, -0.5f),
                                                        Layouts.Top(0.5f, -0.5f),
                                                        Layouts.Column(8),
                                                    },
                                                    Children = {
                                                        new Spinner() {
                                                            Layout = { Layouts.Left(0.5f, -0.5f) },
                                                        },
                                                        new Label("Loading") {
                                                            Layout = { Layouts.Left(0.5f, -0.5f) },
                                                        },
                                                    }
                                                }
                                            }
                                        });
                                    });

                                    List<RemoteModInfoAPI.RemoteModInfo> mods;
                                    try {
                                        mods = App.APIManager.DefaultAPI().GetFeaturedEntries().ToList();
                                    } catch (Exception e) {
                                        AppLogger.Log.Error("Failed downloading featured entries:");
                                        AppLogger.Log.Error(e, e.Message);
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new Group() {
                                                Layout = {
                                                    Layouts.Fill(1, 1),
                                                },
                                                Children = {
                                                    new Group() {
                                                        Layout = {
                                                            Layouts.Left(0.5f, -0.5f),
                                                            Layouts.Top(0.5f, -0.5f),
                                                            Layouts.Column(8),
                                                        },
                                                        Children = {
                                                            new Label("Failed downloading featured mods list.") {
                                                                Layout = { Layouts.Left(0.5f, -0.5f) },
                                                            },
                                                        }
                                                    }
                                                }
                                            });
                                        });
                                        return;
                                    }

                                    if (mods.Count == 0) {
                                        await UI.Run(() => {
                                            el.DisposeChildren();
                                            el.Add(new Group() {
                                                Layout = {
                                                    Layouts.Fill(1, 1),
                                                },
                                                Children = {
                                                    new Group() {
                                                        Layout = {
                                                            Layouts.Left(0.5f, -0.5f),
                                                            Layouts.Top(0.5f, -0.5f),
                                                            Layouts.Column(8),
                                                        },
                                                        Children = {
                                                            new Label("No featured mods found.") {
                                                                Layout = { Layouts.Left(0.5f, -0.5f) },
                                                            },
                                                        }
                                                    }
                                                }
                                            });
                                        });
                                        return;
                                    }

                                    int max = Math.Min(mods.Count, 3);

                                    HashSet<int> randomized = new(max);
                                    int[] randomMap = new int[max];
                                    Random random = new();

                                    for (int i = 0; i < max; i++) {
                                        int modi;
                                        do {
                                            modi = random.Next(mods.Count);
                                        } while (!randomized.Add(modi));
                                        randomMap[i] = modi;
                                    }

                                    Panel[] panels = new Panel[max];

                                    await UI.Run(() => {
                                        el.DisposeChildren();
                                        for (int i = 0; i < max; i++) {
                                            RemoteModInfoAPI.RemoteModInfo mod = mods[randomMap[i]];

                                            panels[i] = el.Add(new Panel() {
                                                ID = $"FeaturedMod:{i}",
                                                Clip = true,
                                                Layout = {
                                                    Layouts.Fill(1f / max, 1, i == 0 || i == max - 1 ? 8 / 2 : 8, 0),
                                                    Layouts.Move(8 * i, 0),
                                                },
                                                Modifiers = {
                                                    new FadeInAnimation(0.09f).WithDelay(0.05f * i).With(Ease.SineInOut),
                                                    new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f * i).With(Ease.SineIn),
                                                    new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f * i).With(Ease.SineOut),
                                                },
                                                Children = {
                                                    new Group() {
                                                        ID = "Images",
                                                        Layout = {
                                                            Layouts.FillFull(),
                                                        },
                                                        Children = {
                                                            new Spinner() {
                                                                Layout = {
                                                                    Layouts.Left(0.5f, -0.5f),
                                                                    Layouts.Top(0.5f, -0.5f),
                                                                },
                                                            },
                                                        }
                                                    },
                                                    new Group() {
                                                        ID = "Tints",
                                                        Layout = {
                                                            Layouts.FillFull(),
                                                        },
                                                    },
                                                    new Group() {
                                                        ID = "Content",
                                                        Layout = {
                                                            Layouts.Fill(),
                                                            Layouts.Column(false),
                                                        },
                                                        Children = {
                                                            new HeaderSmall(mod.Name) {
                                                                ID = "Header",
                                                                Wrap = true,
                                                            },
                                                            new HeaderSmaller(mod.Description) {
                                                                ID = "Description",
                                                                Wrap = true,
                                                            },
                                                        }
                                                    },
                                                }
                                            });
                                        }
                                    });

                                    Task[] imageTasks = new Task[max];
                                    for (int i = 0; i < max; i++) {
                                        RemoteModInfoAPI.RemoteModInfo mod = mods[randomMap[i]];
                                        Panel panel = panels[i];

                                        imageTasks[i] = Task.Run(async () => {
                                            IReloadable<Texture2D, Texture2DMeta>? tex = await App.Web.GetTextureUnmipped(mod.Screenshots[0]);
                                            await UI.Run(() => {
                                                Element imgs = panel["Images"];
                                                Element tints = panel["Tints"];
                                                imgs.DisposeChildren();

                                                if (tex is null)
                                                    return;

                                                imgs.Add<Image>(new(tex) {
                                                    DisposeTexture = true,
                                                    Style = {
                                                        { Color.White * 0.3f }
                                                    },
                                                    Modifiers = {
                                                        new FadeInAnimation(0.6f).With(Ease.QuadOut),
                                                        new ScaleInAnimation(1.05f, 0.5f).With(Ease.QuadOut)
                                                    },
                                                    Layout = {
                                                        ev => {
                                                            Image img = (Image) ev.Element;
                                                            if (imgs.W > imgs.H) {
                                                                img.AutoW = imgs.W;
                                                                if (img.H < imgs.H) {
                                                                    img.AutoH = imgs.H;
                                                                }
                                                            } else {
                                                                img.AutoH = imgs.H;
                                                                if (img.W < imgs.W) {
                                                                    img.AutoW = imgs.W;
                                                                }
                                                            }
                                                        },
                                                        Layouts.Left(0.5f, -0.5f),
                                                        Layouts.Top(0.5f, -0.5f),
                                                    },
                                                });

                                                tints.Add<Image>(new(OlympUI.Assets.White) {
                                                    Style = { Color.Transparent },
                                                    Layout = { Layouts.FillFull() },
                                                });
                                                tints.Add<Image>(new(OlympUI.Assets.GradientQuadYInv) {
                                                    Style = { Color.Transparent },
                                                    Layout = { Layouts.FillFull() },
                                                });
                                                tints.Add<Image>(new(OlympUI.Assets.GradientQuadY) {
                                                    Style = { Color.Transparent },
                                                    Layout = { Layouts.FillFull() },
                                                });

                                                UI.RunLate(() => {
                                                    int bgi = 0;
                                                    List<QuantizedColor> colors = tex.GetPalette(6);
                                                    Color fg =
                                                        colors[0].IsDark ?
                                                        colors.Where(c => !c.IsDark).OrderByDescending(c => c.Color.ToHsl().S).FirstOrDefault().Color :
                                                        colors.Where(c => c.IsDark).OrderByDescending(c => c.Color.ToHsl().S).FirstOrDefault().Color;
                                                    Color[] bgs =
                                                        colors[0].IsDark ?
                                                        colors.Where(c => c.IsDark).OrderByDescending(c => c.Color.ToHsl().S).ThenBy(c => c.Color.ToHsl().L).Select(c => c.Color).ToArray() :
                                                        colors.Where(c => !c.IsDark).OrderByDescending(c => c.Color.ToHsl().S).ThenByDescending(c => c.Color.ToHsl().L).Select(c => c.Color).ToArray();
                                                    if (fg == default)
                                                        fg = colors[0].IsDark ? Color.White : Color.Black;
                                                    if (bgs.Length == 0)
                                                        bgs = colors[0].IsDark ? new Color[] { Color.Black, Color.White } : new Color[] { Color.White, Color.Black };
                                                    fg =
                                                        colors[0].IsDark ? new(
                                                            fg.R / 255f + 0.3f,
                                                            fg.G / 255f + 0.3f,
                                                            fg.B / 255f + 0.3f
                                                        ) :
                                                        new(
                                                            fg.R / 255f * 0.3f,
                                                            fg.G / 255f * 0.3f,
                                                            fg.B / 255f * 0.3f
                                                        );
                                                    panel.Style.Add(Panel.StyleKeys.Background, colors[0].Color);
                                                    foreach (Element child in tints) {
                                                        child.Style.Update(0f); // Force faders to be non-fresh.
                                                        child.Style.Add(bgs[bgi++ % bgs.Length] * (0.3f + bgi * 0.1f));
                                                    }
                                                    foreach (Element child in panel["Content"]) {
                                                        child.Style.Update(0f); // Force faders to be non-fresh.
                                                        child.Style.Add(fg);
                                                    }
                                                });
                                            });
                                        });
                                    }

                                    await Task.WhenAll(imageTasks);
                                })
                            },
                        }
                    },

                    new Group() {
                        Style = {
                            { Group.StyleKeys.Spacing, 8 },
                        },
                        Layout = {
                            Layouts.Fill(0.7f, 0.6f, 32, 0),
                            Layouts.Bottom(),
                            Layouts.Left(),
                            Layouts.Column(false),
                        },
                        Children = {
                            new Group() {
                                Style = {
                                    { Group.StyleKeys.Spacing, 16 },
                                },
                                Layout = {
                                    Layouts.Fill(1, 0),
                                    Layouts.Row(),
                                },
                                Children = {
                                    new HeaderMedium("Your mods") {
                                        Init = RegisterRefresh<HeaderMedium>(el => {
                                            refreshModsHeader =
                                                new LockedAction<Installation?>(i => UI.Run(() => NameGen(i, el)));

                                            refreshModsHeader.TryRun(Config.Instance.Installation);
                                            return Task.CompletedTask; // Cheese it this way since it only schedules functions to run
                                        }),
                                    },
                                    new Group() {
                                        Layout = {
                                            Layouts.Fill(1, 0, LayoutConsts.Prev, 0),
                                        },
                                        Children = {
                                            new Group() {
                                                Style = {
                                                    { Group.StyleKeys.Spacing, -4 },
                                                },
                                                Layout = {
                                                    Layouts.Column(),
                                                },
                                                Children = GetCelesteVersionLabels(),
                                            },
                                            new Button("Manage Installs", _ => Scener.Push<InstallManagerScene>()) {
                                                Style = {
                                                    { Group.StyleKeys.Padding, new Padding(8, 4) },
                                                },
                                                Layout = {
                                                    Layouts.Right(),
                                                    Layouts.Fill(0, 1),
                                                },
                                            },
                                        }
                                    },
                                }
                            },
                            new Group() {
                                Clip = true,
                                ClipExtend = 8,
                                Style = {
                                    { Group.StyleKeys.Spacing, 16 },
                                },
                                Layout = {
                                    Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                    Layouts.Column(),
                                },
                                Children = {
                                    new CallbackScrollBox(el => {
                                        if (loadMoreMods == null || loadMoreMods.IsRunning) return;
                                        Task.Run(() => loadMoreMods?.TryRun(null, false));
                                    }) {
                                        Layout = {
                                            Layouts.Fill(1, 1, 0, LayoutConsts.Prev)
                                        },
                                        Content = new Group() {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 0),
                                                Layouts.Column(),
                                            },
                                            Init = RegisterRefresh<Group>(async el => {
                                                await UI.Run(() => {
                                                    el.DisposeChildren();
                                                    el.Add(new Group() {
                                                        Layout = {
                                                            Layouts.Fill(1, 0),
                                                        },
                                                        Children = {
                                                            new Group() {
                                                                Layout = {
                                                                    Layouts.Left(0.5f, -0.5f),
                                                                    Layouts.Row(8),
                                                                },
                                                                Children = {
                                                                    new Spinner() {
                                                                        Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                    },
                                                                    new Label("Loading") {
                                                                        Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                    },
                                                                }
                                                            }
                                                        }
                                                    });
                                                });

                                                refreshModList = new LockedAction<Installation?>(async install => {
                                                    loadMoreMods = null;
                                                    Panel firstPanel = new Panel() {
                                                        Layout = {
                                                            Layouts.Fill(1, 0),
                                                            Layouts.Column(),
                                                        },
                                                        Modifiers = {
                                                            new FadeInAnimation(0.09f).WithDelay(0.05f).With(Ease.SineInOut),
                                                            new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f).With(Ease.SineIn),
                                                            new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f).With(Ease.SineOut),
                                                        }
                                                       
                                                    };
                                                    if (install == null) {
                                                        UI.Run(() => {
                                                            el.DisposeChildren();
                                                            firstPanel.Children = new() {
                                                                new HeaderSmall("Mods start here"),
                                                                new Label("You have yet to select your celeste installation.\nDo so by pressing on the \"Manage Installs\" button above.") {
                                                                    Wrap = true,
                                                                },
                                                            };
                                                            el.Add(firstPanel);
                                                            UI.Root.InvalidateForce();
                                                        });
                                                        return;
                                                    }
                                                    (bool Modifiable, string Full, Version? Version,
                                                            string? Framework, string? ModName,
                                                            Version? ModVersion)
                                                        = install.ScanVersion(false);
                                                    if (!Modifiable) {
                                                        UI.Run(() => {
                                                            el.DisposeChildren();
                                                            firstPanel.Children = new() {
                                                                new HeaderSmall("*Confusion noises*"),
                                                                new Label(
                                                                    "Seems like your currently selected celeste install is malformed or unreadable, try revising it.\nOr choose another one by pressing on the \"Manage Installs\" button above.") {
                                                                    Wrap = true,
                                                                },
                                                            };
                                                            el.Add(firstPanel);
                                                            UI.Root.InvalidateForce();
                                                        });
                                                        return;
                                                    }
                                                    if (ModName == null || ModVersion == null) {
                                                        UI.Run(() => {
                                                            el.DisposeChildren();
                                                            firstPanel.Children = new() {
                                                                new HeaderSmall("Vanilla Celeste"),
                                                                new Label(
                                                                    "The currently selected celeste installation is not yet modded.\nInstall Everest now to play with mods") {
                                                                    Wrap = true,
                                                                },
                                                                new Group() {
                                                                    Layout = {
                                                                        Layouts.Fill(1, 0, 0, 0),
                                                                        Layouts.Row(),
                                                                        Layouts.Right(),
                                                                    },
                                                                    Children = {
                                                                        new Button("Install Everest",
                                                                            b =>
                                                                                Scener
                                                                                    .Push<EverestSimpleInstallScene>()),
                                                                    }
                                                                }
                                                            };
                                                            el.Add(firstPanel);
                                                            UI.Root.InvalidateForce();
                                                        });
                                                        return;
                                                    }
                                                    try {
                                                        await UI.Run(() => { // remove old and add loading screen
                                                            el.DisposeChildren();
                                                            el.Children = new ObservableCollection<Element>() {
                                                                new Group() {
                                                                    Layout = {
                                                                        Layouts.Left(0.5f, -0.5f),
                                                                        Layouts.Row(8),
                                                                    },
                                                                    Children = {
                                                                        new Spinner() {
                                                                            Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                        },
                                                                        new Label("Loading") {
                                                                            Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                        },
                                                                    }
                                                                }
                                                            };
                                                        });
                                                        
                                                        (Version? everestVersion, IEnumerable<IModFileInfo> installedMods) = GenerateModList();
                                                        Panel everestPanel = GenerateEverestPanel(everestVersion);
                                                        IEnumerator<IModFileInfo> modsEnumeration = installedMods.GetEnumerator();
                                                        
                                                        await UI.Run(() => {
                                                            el.DisposeChildren();
                                                            el.Add(everestPanel);
                                                        });
                                                        
                                                        // neat hack to force a single call on every update to prevent freezes
                                                        loadMoreMods = new LockedAction<object>(async _ => {
                                                            IEnumerable<Element> generatedModListPanels = GenerateModListPanels(modsEnumeration, 1, true);
                                                            foreach (Element panel in generatedModListPanels) {
                                                                if (!ReferenceEquals(Config.Instance.Installation, install))
                                                                    break;
                                                                await UI.Run(() => el.Add(panel));
                                                            }
                                                        });
                                                        loadMoreMods.TryRun(null, false);
                                                    } catch (Exception e) {
                                                        AppLogger.Log.Error("refreshModList crashed with exception {0}", e);
                                                        AppLogger.Log.Error("Stacktrace: {0}", e.StackTrace);
                                                    }
                                                    
                                                }); 
                                                refreshModList.TryRun(Config.Instance.Installation); // pass null because i is ignored
                                                // the correct install will get picked through Config.Instance.Install
                                            })
                                        },
                                    },
                                },
                            },
                        }
                    },

                    new Group() {
                        Style = {
                            { Group.StyleKeys.Spacing, 16 },
                        },
                        Layout = {
                            Layouts.Fill(0.3f, 0.6f, 0, 0),
                            Layouts.Bottom(),
                            Layouts.Right(),
                            Layouts.Column(false),
                        },
                        Children = {
                            new HeaderMedium("News"),
                            new Group() {
                                Clip = true,
                                ClipExtend = 8,
                                Layout = {
                                    Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                },
                                Children = {
                                    new ScrollBox() {
                                        Layout = {
                                            Layouts.Fill(1, 1)
                                        },
                                        Content = new Group() {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 0),
                                                Layouts.Column(),
                                            },
                                            Init = RegisterRefresh<Group>(async el => {
                                                await UI.Run(() => {
                                                    el.DisposeChildren();
                                                    el.Add(new Group() {
                                                        Layout = {
                                                            Layouts.Fill(1, 0),
                                                        },
                                                        Children = {
                                                            new Group() {
                                                                Layout = {
                                                                    Layouts.Left(0.5f, -0.5f),
                                                                    Layouts.Row(8),
                                                                },
                                                                Children = {
                                                                    new Spinner() {
                                                                        Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                    },
                                                                    new Label("Loading") {
                                                                        Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                    },
                                                                }
                                                            }
                                                        }
                                                    });
                                                });

                                                IEnumerable<INewsEntry> news;
                                                try {
                                                    news = App.NewsManager.GetDefault()
                                                        .PollLast(3);
                                                } catch (Exception e) {
                                                    AppLogger.Log.Error("Failed to obtain news");
                                                    AppLogger.Log.Error(e, e.Message);
                                                    UI.Run(() => {
                                                        el.DisposeChildren();
                                                        el.Add(new Group() {
                                                            Layout = { Layouts.Fill(1, 0), },
                                                            Children = {
                                                                new Group() {
                                                                    Layout = {
                                                                        Layouts.Left(0.5f, -0.5f), Layouts.Row(8),
                                                                    },
                                                                    Children = {
                                                                        new Label("Failed to obtain news!") {
                                                                            Layout = { Layouts.Top(0.5f, -0.5f) },
                                                                        },
                                                                    }
                                                                }
                                                            }
                                                        });
                                                    });
                                                    return;
                                                }
                                                
                                                INewsEntry[] newsArray = news.ToArray();
                                                
                                                Task[] imageTasks = new Task[newsArray.Length];
                                                await UI.Run(() => el.DisposeChildren());
                                                for (int i = 0; i < newsArray.Length; i++) {
                                                    (Panel newsPanel, Task imageTask) = NewsScene.CreateNewsPanel(newsArray[i]);
                                                    imageTasks[i] = imageTask;
                                        
                                                    await UI.Run(() => el.Add(newsPanel));
                                                }
                                                
                                                await UI.Run(() => 
                                                    el.Add(new Button("See all", b => Scener.Push<NewsScene>()) {
                                                        Layout = {
                                                            Layouts.FillFull(0, 0), 
                                                            Layouts.Left(0.5f, -0.5f)
                                                        },
                                                    })
                                                );
                                        
                                                await Task.WhenAll(imageTasks);
                                                // Panel[] panels = new Panel[newsArray.Length];
                                                //
                                                // await UI.Run(() => {
                                                //     el.DisposeChildren();
                                                //     for (int i = 0; i < newsArray.Length; i++) {
                                                //         INewsEntry newsEntry = newsArray[i];
                                                //         Panel panel = new() {
                                                //             Layout = { Layouts.Fill(1, 0), Layouts.Column(), },
                                                //             Modifiers = {
                                                //                 new FadeInAnimation(0.09f).WithDelay(0.05f)
                                                //                     .With(Ease.SineInOut),
                                                //                 new OffsetInAnimation(new Vector2(0f, 10f), 0.15f)
                                                //                     .WithDelay(0.05f).With(Ease.SineIn),
                                                //                 new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f)
                                                //                     .With(Ease.SineOut),
                                                //             },
                                                //             Children = {
                                                //                 new HeaderSmall(newsEntry.Title) {
                                                //                     Wrap = true,
                                                //                 },
                                                //                 new Group() {
                                                //                     ID = "ImageGroup", 
                                                //                     Layout = {
                                                //                         Layouts.Fill(1, 0)
                                                //                     }
                                                //                 },
                                                //                 new Label(newsEntry.Text) { Wrap = true, },
                                                //             }
                                                //         };
                                                //
                                                //         foreach (INewsEntry.ILink link in newsEntry.Links) {
                                                //             panel.Add(new IconButton("icons/browser",
                                                //                 link.Text,
                                                //                 _ => URIHelper.OpenInBrowser(link.Url)));
                                                //         }
                                                //
                                                //         panels[i] = el.Add(panel);
                                                //     }
                                                //
                                                //     el.Add(new Button("See all", b => Scener.Push<NewsScene>()) {
                                                //         Layout = { Layouts.FillFull(0, 0), Layouts.Left(0.5f, -0.5f) },
                                                //     });
                                                // });
                                                // Task[] newsTasks = new Task[newsArray.Length];
                                                // for (int i = 0; i < newsArray.Length; i++) {
                                                //     INewsEntry newsEntry = newsArray[i];
                                                //     Panel panel = panels[i];
                                                //     newsTasks[i] = Task.Run(async () => {
                                                //         IReloadable<Texture2D, Texture2DMeta>? tex = null;
                                                //         foreach (string img in newsEntry.Images) {
                                                //             tex = await App.Web.GetTextureUnmipped(img);
                                                //             if (tex != null) break;
                                                //         }
                                                //
                                                //         if (tex == null) return;
                                                //
                                                //         Element group = panel["ImageGroup"];
                                                //
                                                //         await UI.Run(() => {
                                                //             group.DisposeChildren();
                                                //             group.Add(new Image(tex) {
                                                //                 Modifiers = {
                                                //                     new FadeInAnimation(0.6f).With(Ease.QuadOut),
                                                //                     new ScaleInAnimation(1.05f, 0.5f).With(
                                                //                         Ease.QuadOut)
                                                //                 },
                                                //                 Layout = {
                                                //                     ev => {
                                                //                         Image img = (Image) ev.Element;
                                                //                         img.AutoW = Math.Min(group.W, 400);
                                                //                         img.X = group.W / 2 - img.W/2;
                                                //                     },
                                                //                 }
                                                //             });
                                                //         });
                                                //     });
                                                // }
                                                //
                                                // await Task.WhenAll(newsTasks);
                                            })
                                        }
                                    },
                                },
                            },
                        }
                    },

                },
            };
        
        public static void NameGen(Installation? i, Label el) {
            if (i == null) {
                el.Text = "No storby";
                return;
            }

            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                = i.ScanVersion(false);
            if (!Modifiable) {
                el.Text = "Wrong game";
            } else if (ModName == null || ModVersion == null) {
                el.Text = "No Mods :(";
            } else {
                el.Text = "Your Mods";
            }
            
            el.GetParent().GetChild<Group>().Layout.Clear();
            el.GetParent().GetChild<Group>().Layout.Add(Layouts.Fill(1, 1, LayoutConsts.Prev, 0));
        }
        
        private static string GetInstallationName() {
            if (Config.Instance.Installation != null) return Config.Instance.Installation.Name;
            // AppLogger.Log.LogLine("GetInstallationName called before config was loaded!");
            return "No install selected";

        }

        private static string GetInstallationInfo() {
            if (Config.Instance.Installation == null) {
                // AppLogger.Log.LogLine("GetInstallationInfo called before config was loaded!");
                return "No install selected";
            }
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
            = Config.Instance.Installation.ScanVersion(false);
            return Full;
        }
        
        private Action<Installation?>? updateEvent = null;

        private ObservableCollection<Element> GetCelesteVersionLabels() {
            LabelSmall installName = new("Celeste Installation: " + GetInstallationName());
            LabelSmall installVersion = new("Version: " + GetInstallationInfo());

            if (updateEvent == null) {
                updateEvent = i => {};
                Config.Instance.SubscribeInstallUpdateNotify(i => updateEvent(i)); // wrapper so we can update the lambda
            }

            updateEvent = i => {
                UI.Run(() => {
                    installName.Text = "Celeste Installation: " + GetInstallationName();
                    installVersion.Text = "Version: " + GetInstallationInfo();
                });
            };

            return new() {
                installName,
                installVersion
            };
        }

        // Returns all the mods installed, to be ran async
        private (Version? everestVersion, IEnumerable<IModFileInfo> installedMods) GenerateModList() {
            if (Config.Instance.Installation == null) {
                AppLogger.Log.Error("GenerateModList called before config was loaded!");
                return new ValueTuple<Version?, List<LocalInfoAPI.LocalModFileInfo>>(); // shouldn't ever happen
            }
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
            = Config.Instance.Installation.ScanVersion(false);

            AppLogger.Log.Information("Gathering Mod List");
            IEnumerable<IModFileInfo> installedMods = Config.Instance.Installation.LocalInfoAPI.CreateAllModFileInfo();

            return (ModVersion, installedMods);
        }

        private Panel GenerateEverestPanel(Version? everestVersion) {
            if (Config.Instance.Installation == null) {
                AppLogger.Log.Error("GenerateEverestPanel called before config was loaded!");
                return new Panel();
            }
            AppLogger.Log.Information("Creating everest panel");
            EverestInstaller.EverestVersion? everestUpdate = null;
            EverestInstaller.EverestBranch? branch = EverestInstaller.DeduceBranch(Config.Instance.Installation);
            if (branch != null) {
                EverestInstaller.EverestVersion? version = EverestInstaller.GetLatestForBranch(branch);
                if (everestVersion != null && version != null && version.version > everestVersion.Minor) {
                    everestUpdate = version;
                }
            }

            Button versionButton = new Button("Change version", _ => Scener.Push<EverestSimpleInstallScene>()) {
                Layout = {
                    Layouts.Right(),
                },
            };
            int versionButtonWidth = versionButton.GetChild<Label>().W + versionButton.Padding.W;
            // TODO: make use of LayoutConsts.Prev instead of this hack
            Panel everestPanel = new Panel() {
                ID = "everestPanel",
                Layout = {
                    Layouts.Fill(1, 0),
                    Layouts.Column(),
                },
                Children = {
                    new HeaderSmall("Everest"),
                    new Label("Everest is the mod loader. It's installed like a game patch.\nYou need to have Everest installed for all other mods to be loaded.") {
                        Wrap = true,
                    },
                    new Group() {
                        Layout = {
                            Layouts.Fill(1, 0, versionButtonWidth, 0),
                            Layouts.Row(),
                        },
                        Children = {
                            new Group() {
                                Layout = {
                                    Layouts.Fill(1f, 0, 0, 0),
                                    Layouts.Column(),
                                },
                                Children = {
                                    new LabelSmall(everestVersion == null ? "Unknown version" : 
                                        "Installed version: " + everestVersion),
                                    new LabelSmall(everestUpdate == null ? "Up to date" : $"Update available: 1.{everestUpdate.version}.0"),
                                }
                            },
                            versionButton,
                        }
                    },
                },
                Modifiers = {
                    new FadeInAnimation(0.09f).WithDelay(0.05f).With(Ease.SineInOut),
                    new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f).With(Ease.SineIn),
                    new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f).With(Ease.SineOut),
                }
            };

            return everestPanel;
        }

        // Builds the panel list from the installed mods, shouldn't be run on UI
        private IEnumerable<Element> GenerateModListPanels(IEnumerator<IModFileInfo> mods, int amount = -1, bool withAnimation = false) {
            if (Config.Instance.Installation == null) {
                 AppLogger.Log.Error("GenerateModList called before config was loaded!");
                 return new ObservableCollection<Element>(); // shouldn't ever happen
            }
            
            List<Element> panels = new();
            int i = 0;
            while (mods.MoveNext()) {
                LocalInfoAPI.LocalModFileInfo localMod = (LocalInfoAPI.LocalModFileInfo) mods.Current;
                ModPanel modPanel = new(localMod) {
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column(),
                    },
                    Clip = false,
                    Children = {
                        new Group() {
                            Layout = {
                                Layouts.Row(8),
                            },
                            Children = {
                                new HeaderSmall(localMod.Name),
                                new LabelSmall("") {
                                    Data = {
                                        {"subscribe_click",
                                            (bool disabled, Element label) => 
                                            { (label as LabelSmall)!.Text = disabled ? "(Disabled)" : "";}}
                                    },
                                    Modifiers = {
                                        new OpacityModifier(0.7f),
                                    },
                                    Layout = {
                                        Layouts.Bottom(),
                                    }
                                },
                            }
                        },
                        new Label("Loading...") {
                            ID = "Description",
                            Wrap = true,
                        },
                        new Group() {
                            ID = "BottomPart",
                            Layout = {
                                Layouts.Fill(1, 0),
                                Layouts.Row(),
                            },
                            Children = {
                                new Group() {
                                    ID = "Info",
                                    Style = {
                                        { Group.StyleKeys.Spacing, 0 },
                                    },
                                    Layout = {
                                        Layouts.Fill(0, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new LabelSmall("Path: " + Path.GetRelativePath(Config.Instance.Installation.Root, localMod.Path!)),
                                        new LabelSmall("Installed Version: " + localMod.Version) {
                                            ID = "VersionLabel"
                                        },
                                        new LabelSmall("Loading...") {
                                            ID = "UpdateLabel",
                                        },
                                        
                                    }
                                },
                                new Group() {
                                    ID = "UpdateGroup",
                                    Layout = {
                                        Layouts.FillFull(1f, 1f, LayoutConsts.Prev),
                                    }
                                }
                            }
                        }
                        
                    },
                };

                // This is a bad idea, since it will overload the thread pool, effectively freezing all other tasks of the app
                // Task.Run(() => FinishModPanels(modPanel));
                if (withAnimation) {
                    modPanel.Modifiers = new ObservableCollection<Modifier> {
                        new FadeInAnimation(0.09f).WithDelay(0.05f).With(Ease.SineInOut),
                        new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f).With(Ease.SineIn),
                        new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f).With(Ease.SineOut),
                    };
                }
                
                panels.Add(modPanel);
                i++;
                if (amount > 0 && amount <= i) break;
            }

            // If nothing was generated, skip the task below
            if (panels.Count == 0) return panels;

            cancellationToken.Dispose();
            cancellationToken = new CancellationTokenSource();
            ct = cancellationToken.Token;

            Task.Run(async () => {
                // neat hack to force a single call on every update to prevent freezes
                uint currGUID = UI.GlobalUpdateID;
                for (int i = 0; i < panels.Count; i++) {
                    while (currGUID == UI.GlobalUpdateID) await Task.Delay(1);
                    currGUID = UI.GlobalUpdateID;
                    if (ct.Value.IsCancellationRequested) break;
                    try {
                        FinishModPanels((ModPanel) panels[i]);
                    } catch (Exception ex) {
                        // FinishModPanels may crash if the panel is no longer valid, just take it and ignore it
                        break;
                    }
                }
            }, ct.Value);
            
            return panels;
        }

        private void FinishModPanels(ModPanel panel) {
            IModInfo? modInfo = null;
            try {
                modInfo = App.Instance.APIManager.TryAll<IModInfo>(api => api.GetModInfoFromFileInfo(panel.Mod));
            } catch (Exception ex) {
                AppLogger.Log.Error("Couldn't finish mod panels!");
                AppLogger.Log.Error(ex, ex.Message);
            }


            IModFileInfo? remoteFileInfo = null;

            if (modInfo != null) {
                foreach (IModFileInfo file in modInfo.Files) {
                    if (file.Name.Equals(panel.Mod.Name)) {
                        remoteFileInfo = file;
                        break;
                    }
                }
            }

            string modHash = panel.Mod.Hash; // This can be expensive, so do it while async

            UI.Run(() => {
                try {
                    string? desc = modInfo?.Description;
                    if (desc == "") desc = null;
                    panel.GetChild<Label>("Description").Text = desc ?? "No description available";
                    Group infoGroup = panel.GetChild<Group>("BottomPart").GetChild<Group>("Info");
                    infoGroup.GetChild<LabelSmall>("VersionLabel").Text = "Installed Version: " + panel.Mod.Version;
                    if (remoteFileInfo == null || remoteFileInfo.Hash == modHash) {

                        infoGroup.GetChild<LabelSmall>("UpdateLabel").Text = "Up to date!";
                        return;
                    }

                    infoGroup.GetChild<LabelSmall>("UpdateLabel").Text =
                        "New version available: " + remoteFileInfo.Version;

                    Element? oldUpdateButton = panel.GetChild("UpdateButton");
                    if (oldUpdateButton != null)
                        panel.Children.Remove(oldUpdateButton);

                    panel.GetChild<Group>("BottomPart").GetChild<Group>("UpdateGroup").Add(
                        new Button("Update", b => {
                            using DisposeEvent disposeEvent = new DisposeEvent(() => UpdateInProgress = false);
                            UpdateInProgress = true;
                            Group? parent = b.Parent?.Parent as Group; // Should never be null
                            if (parent == null) {
                                AppLogger.Log.Error("ModPanel button parent was null!!!!");
                                b.Text = "Error!";
                                return;
                            }

                            ModPanel? panelParent = parent.Parent as ModPanel;
                            if (panelParent == null) {
                                AppLogger.Log.Error("ModPanel button parent was null!!!!");
                                b.Text = "Error!";
                                return;
                            }

                            panelParent.PreventNextClick();
                            if (b.Data.TryGet("updating", out bool updating) && updating) {
                                b.Data.Add("cancel", true);
                                b.Text = "Canceling...";
                                return;
                            }

                            b.Data.Add("cancel", false);
                            b.Data.Add("updating", true); // Add will replace existing values


                            b.Text = "Starting download...";

                            ModUpdater.UpdateMod(panelParent.Mod, (position, length, speed) => {
                                bool exists = b.Data.TryGet("cancel", out bool cancel);
                                if (exists && cancel) {
                                    return false;
                                }

                                UI.Run(() => {
                                    b.Text =
                                        $"Press to Cancel | {(int) Math.Floor(100D * (position / (double) length))}% @ {speed} KiB/s";
                                });
                                return true;
                            }, (success, isDone) => {
                                UI.Run(() => {
                                    if (isDone) {
                                        b.Text = success ? "Mod updated!" : "Mod update failed! Press to retry";
                                        b.Data.Add("updating", false);
                                        b.Data.Add("cancel", false);
                                        if (success) {
                                            b.Enabled = false;
                                            if (Config.Instance.Installation == null || panel.Mod.Path == null) return;
                                            IModFileInfo? newModInfo =
                                                Config.Instance.Installation.LocalInfoAPI.CreateModFileInfo(
                                                    panel.Mod.Path);
                                            if (newModInfo == null) return;
                                            panel.Mod = (LocalInfoAPI.LocalModFileInfo) newModInfo;
                                            FinishModPanels(panel);
                                        }
                                    } else if (!success) {
                                        b.Text = "Retrying in 3 seconds...";
                                    } else {
                                        // !isDone && success
                                        b.Text = "Update canceled";
                                        b.Data.Add("updating", false);
                                        b.Data.Add("cancel", false);
                                    }
                                });
                            });
                        }) {
                            Enabled = !panel.Mod.IsUpdaterBlacklisted ?? true,
                            Layout = {
                                // Layouts.Fill(1, 0),
                                Layouts.Right(), Layouts.Bottom(),
                            },
                            Data = { { "updating", false }, }
                        }
                    );
                } catch (Exception ex) {
                    // No children means we're dealing with a disposed panel, ignore that
                    if (panel.Children.Count == 0) return;
                    AppLogger.Log.Warning(ex, "Could not late populate mod panel info");
                }

            });
        }

        private partial class ModPanel : Panel {

            public new static readonly Style DefaultStyle = new() { // TODO: selected on dark mode looks awful
                {
                    StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x08, 0x08, 0x08, 0xd0) },
                        { Panel.StyleKeys.Border, new Color(0x08, 0x08, 0x08, 0xd0) },
                    }
                },

                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x22, 0x22, 0x22, 0xd0) },
                        { Panel.StyleKeys.Border, new Color(0x08, 0x08, 0x08, 0xd0) },
                    }
                },

                {
                    StyleKeys.Selected,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x48, 0x48, 0x48, 0xd0) },
                        { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0xd0) },
                    }
                },
            };

            private bool Disabled;

            public LocalInfoAPI.LocalModFileInfo Mod;

            private List<Tuple<Element, Action<bool, Element>>> subscribedClicks = new();

            private bool preventNextClick = false;

            public void PreventNextClick() => preventNextClick = true;

            private void ParseChilds(IEnumerable<Element> elements) {
                foreach (Element el in elements) {
                    if (!el.Data.TryGet("subscribe_click", out Action<bool, Element>? act)) {
                        if (el.Children.Count == 0) continue;
                        ParseChilds(el.Children);
                        continue;
                    }

                    if (act != null) {
                        subscribedClicks.Add(new Tuple<Element, Action<bool, Element>>(el, act));
                        act.Invoke(this.Disabled, el); // Invoke on init as well, on purpose
                    }

                }
            }

            public ModPanel(LocalInfoAPI.LocalModFileInfo mod)
            : base() {
                this.Mod = mod;
                this.Disabled = Mod.IsBlacklisted ?? false;
                this.Children.CollectionChanged += (sender, args) => {
                    if (args.NewItems == null) return;
                    ParseChilds(args.NewItems.Cast<Element>());
                };
            }

            public override void Update(float dt) {
                Style.Apply(Disabled ? StyleKeys.Selected : 
                            Hovered  ? StyleKeys.Hovered :
                                       StyleKeys.Normal);

                base.Update(dt);
            }

            private void OnClick(MouseEvent.Click e) {
                if (preventNextClick) {
                    preventNextClick = false;
                    return;
                }
                Disabled = !Disabled;
                Config.Instance.Installation?.MainBlacklist.Update(Mod, Disabled);
                foreach (Tuple<Element, Action<bool, Element>> subbed in subscribedClicks) {
                    subbed.Item2.Invoke(this.Disabled, subbed.Item1);
                }
            }

            public  new abstract partial class StyleKeys {

                public static readonly Style.Key Normal = new("Normal");
                public static readonly Style.Key Selected = new("Selected");
                public static readonly Style.Key Hovered = new("Hovered");
            }
        }

        public partial class IconButton : Button {

            public readonly Icon Icon;
            public readonly Label Label;

            public IconButton(string iconPath, string text, Action<Button> cb) : base() {
                Cached = true;
                
                Layout.Add(Layouts.Row(false));

                Icon = Add(new Icon(OlympUI.Assets.GetTexture(iconPath)) {
                    ID = "icon",
                    Style = {
                        { ImageBase.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                    },
                    Layout = {
                        Layouts.Top(0.5f, -0.5f),
                    },
                    AutoH = 24,
                });

                Label = Add(new Label(text) {
                    ID = "label",
                    Style = {
                        { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                    },
                });

                Callback = cb;
            }
        }

        /// <summary>
        /// A scrollbox that generates new elements on scrolling.
        /// Only considers the vertical direction.
        /// </summary>
        public partial class CallbackScrollBox : ScrollBox {
            private readonly Action<Element> action;

            public CallbackScrollBox(Action<Element> action) {
                this.action = action;
            }


            public override void Update(float dt) {
                base.Update(dt);
                
                Vector2 xy = -Content.XY;
                Vector2 wh = Content.WH.ToVector2();
                Vector2 boxWH = WH.ToVector2();
                float entrySize = 0;
                if (Content.Children.Count > 0) {
                    entrySize = Content.Children[0].H;
                }

                if (wh.Y <= xy.Y + boxWH.Y + entrySize) {
                    action.Invoke(this);
                }
                
            }
            
        }

        /// <summary>
        /// Runs an action when disposed
        /// </summary>
        private class DisposeEvent : IDisposable {
            private Action? target;
            public DisposeEvent(Action target) {
                this.target = target;
            }

            public void Dispose() {
                target?.Invoke();
                target = null;
            }
        }

    }
}
