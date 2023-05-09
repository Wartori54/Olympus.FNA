using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using Olympus.ColorThief;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Olympus {
    public class HomeScene : Scene {

        private Action<Installation?>? refreshModList = null;

        public HomeScene() : base() {
            Config.Instance.SubscribeInstallUpdateNotify(i => (refreshModList ?? (i => {}))(i));
            // TODO: make refreshModList async, currently if install has a lot of mods, little lag can be noticed
        }

        public override Element Generate()
            => new Group() {
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

                                    IWebAPI.IEntry[] mods;
                                    try {
                                        mods = await App.WebAPI.GetFeaturedEntries();
                                    } catch (Exception e) {
                                        Console.WriteLine("Failed downloading featured entries:");
                                        Console.WriteLine(e);
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

                                    if (mods.Length == 0) {
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

                                    int max = Math.Min(mods.Count(mod => mod.CanBeFeatured), 3);

                                    HashSet<int> randomized = new(max);
                                    int[] randomMap = new int[max];
                                    Random random = new();

                                    for (int i = 0; i < max; i++) {
                                        int modi;
                                        do {
                                            modi = random.Next(mods.Length);
                                        } while (!randomized.Add(modi) || !mods[modi].CanBeFeatured);
                                        randomMap[i] = modi;
                                    }

                                    Panel[] panels = new Panel[max];

                                    await UI.Run(() => {
                                        el.DisposeChildren();
                                        for (int i = 0; i < max; i++) {
                                            IWebAPI.IEntry mod = mods[randomMap[i]];

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
                                                            new HeaderSmaller(mod.ShortDescription) {
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
                                        IWebAPI.IEntry mod = mods[randomMap[i]];
                                        Panel panel = panels[i];

                                        imageTasks[i] = Task.Run(async () => {
                                            IReloadable<Texture2D, Texture2DMeta>? tex = await App.Web.GetTextureUnmipped(mod.Images[0]);
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
                            { Group.StyleKeys.Spacing, 4 },
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
                                    new HeaderMedium("Your Mods"),
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
                                    new Group() {
                                        Layout = {
                                            Layouts.Fill(1, 0),
                                        },
                                        Children = {
                                            /*
                                            new Label("Pinned info here?"),
                                            */
                                        }
                                    },
                                    new ScrollBox() {
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

                                                await Task.Delay(1000);

                                                refreshModList = i => {
                                                    (Version? everestVersion, List<ModList.ModInfo> installedMods) = GenerateModList();
                                                    UI.Run(() => {
                                                        el.DisposeChildren();
                                                        el.Children = GenerateModListPanels(everestVersion, installedMods);
                                                    });
                                                };
                                                await Task.Run(() => refreshModList(null)); // pass null because i is ignored
                                                // the correct install will get picked through Config.Instance.Install
                                            })
                                        }
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

                                                await Task.Delay(3000);

                                                await UI.Run(() => {
                                                    el.DisposeChildren();
                                                    el.Add(new Panel() {
                                                        Layout = {
                                                            Layouts.Fill(1, 0),
                                                            Layouts.Column(),
                                                        },
                                                        Children = {
                                                            new HeaderSmall("Awesome News"),
                                                            new Label("TODO"),
                                                        }
                                                    });
                                                    el.Add(new Panel() {
                                                        Layout = {
                                                            Layouts.Fill(1, 0),
                                                            Layouts.Column(),
                                                        },
                                                        Children = {
                                                            new HeaderSmall("Bad News"),
                                                            new Label("TODO"),
                                                        }
                                                    });
                                                });
                                            })
                                        }
                                    },
                                },
                            },
                        }
                    },

                },
            };
        
        private static string GetInstallationName() {
            if (Config.Instance == null || Config.Instance.Installation == null) {
                Console.WriteLine("GetInstallationName called before config was loaded!");
                return "Loading...";
            }

            return Config.Instance.Installation.Name;
        }

        private static string GetInstallationInfo() {
            if (Config.Instance == null || Config.Instance.Installation == null) {
                Console.WriteLine("GetInstallationInfo called before config was loaded!");
                return "Loading...";
            }
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
            = Config.Instance.Installation.ScanVersion(false);
            return Full;
        }
        
        private Action<Installation?>? updateEvent = null;

        private ObservableCollection<Element> GetCelesteVersionLabels() {
            LabelSmall InstallName = new("Celeste Installation: " + GetInstallationName());
            LabelSmall InstallVersion = new("Version: " + GetInstallationInfo());

            if (updateEvent == null) {
                updateEvent = i => {};
                Config.Instance.SubscribeInstallUpdateNotify(i => updateEvent(i)); // wrapper so we can update the lambda
            }

            updateEvent = i => {
                UI.Run(() => {
                    InstallName.Text = "Celeste Installation: " + GetInstallationName();
                    InstallVersion.Text = "Version: " + GetInstallationInfo();
                });
            };

            return new() {
                InstallName,
                InstallVersion
            };
        }

        // Returns a the mods installed, to be ran async
        private (Version? everestVersion, List<ModList.ModInfo> installedMods) GenerateModList() {
            if (Config.Instance == null || Config.Instance.Installation == null) {
                Console.WriteLine("GenerateModList called before config was loaded!");
                return new(); // shouldn't ever happen
            }
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
            = Config.Instance.Installation.ScanVersion(false);

            List<ModList.ModInfo> installedMods = ModList.GatherModList(true, false, false, false);

            return (ModVersion, installedMods);
        }

        // Builds the pannel list from the installed mods, to be run on UI
        private ObservableCollection<Element> GenerateModListPanels(Version? everestVersion, List<ModList.ModInfo> mods) {
            Panel everestPanel = new Panel() {
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
                        Style = {
                            { Group.StyleKeys.Spacing, 0 },
                        },
                        Layout = {
                            Layouts.Fill(1, 0),
                            Layouts.Column()
                        },
                        Children = {
                            new LabelSmall("Installed Version: " + everestVersion ?? ""),
                            new LabelSmall("Update Available: TODO"),
                        }
                    },
                }
            };

            ObservableCollection<Element> panels = new() {
                everestPanel,
            };

            foreach (ModList.ModInfo mod in mods) {
                Panel modPanel = new Panel() {
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column(),
                    },
                    Children = {
                        new HeaderSmall(mod.Name),
                        new Label("This is a mod.") {
                            Wrap = true,
                        },
                        new Group() {
                            Style = {
                                { Group.StyleKeys.Spacing, 0 },
                            },
                            Layout = {
                                Layouts.Fill(1, 0),
                                Layouts.Column()
                            },
                            Children = {
                                new LabelSmall("Installed Version: " + mod.Version),
                                new LabelSmall("Update Available: TODO"),
                            }
                        },
                    }
                };

                panels.Add(modPanel);
            }
            return panels;
        }

    }
}
