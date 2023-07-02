using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using OlympUI.Modifiers;
using Olympus.ColorThief;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Olympus {
    public class HomeScene : Scene {

        private LockedAction<Installation?>? refreshModList;

        public HomeScene() {
            Config.Instance.SubscribeInstallUpdateNotify(i => {
                refreshModList?.TryRun(i);
            });
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

                                                refreshModList = new LockedAction<Installation?>(_ => {
                                                    try {
                                                        if (Config.Instance.Installation == null) return;
                                                        (bool Modifiable, string Full, Version? Version,
                                                                string? Framework, string? ModName,
                                                                Version? ModVersion)
                                                            = Config.Instance.Installation.ScanVersion(false);
                                                        if (ModName == null || ModVersion == null) return;
                                                        UI.Run(() => { // remove old and add loading screen
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
                                                        (Version? everestVersion, List<ModList.ModInfo> installedMods) = GenerateModList();
                                                        UI.Run(() => {
                                                            el.DisposeChildren();
                                                            el.Children = GenerateModListPanels(everestVersion, installedMods);
                                                            UI.Root.InvalidateForce(); // TODO: find a real fix for panels with buttons not resizing properly the first time
                                                        });
                                                    } catch (Exception e) {
                                                        Console.WriteLine("refreshModList crashed with exception {0}", e);
                                                        Console.WriteLine("Stacktrace: {0}", e.StackTrace);
                                                    }
                                                    
                                                }); 
                                                refreshModList.TryRun(null); // pass null because i is ignored
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
            if (Config.Instance.Installation != null) return Config.Instance.Installation.Name;
            Console.WriteLine("GetInstallationName called before config was loaded!");
            return "No install selected";

        }

        private static string GetInstallationInfo() {
            if (Config.Instance.Installation == null) {
                Console.WriteLine("GetInstallationInfo called before config was loaded!");
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

        // Returns a the mods installed, to be ran async
        private (Version? everestVersion, List<ModList.ModInfo> installedMods) GenerateModList() {
            if (Config.Instance.Installation == null) {
                Console.WriteLine("GenerateModList called before config was loaded!");
                return new ValueTuple<Version?, List<ModList.ModInfo>>(); // shouldn't ever happen
            }
            (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
            = Config.Instance.Installation.ScanVersion(false);

            Console.WriteLine("Gathering Mod List");
            List<ModList.ModInfo> installedMods = ModList.GatherModList(true, false, false, false);

            return (ModVersion, installedMods);
        }

        // Builds the panel list from the installed mods, to be run on thread UI
        private static ObservableCollection<Element> GenerateModListPanels(Version? everestVersion, List<ModList.ModInfo> mods) {
            if (Config.Instance.Installation == null) {
                 Console.WriteLine("GenerateModList called before config was loaded!");
                 return new ObservableCollection<Element>(); // shouldn't ever happen
            }
            Console.WriteLine("Generating mod panels");
            EverestInstaller.EverestVersion? everestUpdate = null;
            EverestInstaller.EverestBranch? branch = EverestInstaller.DeduceBranch(Config.Instance.Installation);
            if (branch != null) {
                EverestInstaller.EverestVersion? version = EverestInstaller.GetLatestForBranch(branch);
                if (everestVersion != null && version != null && version.version > everestVersion.Minor) {
                    everestUpdate = version;
                }
            }
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
                        Layout = {
                            Layouts.Fill(1, 0),
                            Layouts.Row(),
                        },
                        Children = {
                            new Group() {
                                Style = {
                                    { Group.StyleKeys.Spacing, 0 },
                                },
                                Layout = {
                                    Layouts.Fill(0.8f, 0),
                                    Layouts.Column()
                                },
                                Children = {
                                    new LabelSmall(everestVersion == null ? "Unknown version" : 
                                        "Installed version: " + everestVersion),
                                    new LabelSmall(everestUpdate == null ? "Up to date" : $"Update available: 1.{everestUpdate.version}.0"),
                                }
                            },
                            new Button("Change version", _ => Scener.Push<EverestInstallScene>()),
                        }
                    }
                }
            };


            ObservableCollection<Element> panels = new() {
                everestPanel,
            };

            foreach (ModList.ModInfo mod in mods) {
                if (Config.Instance.Installation == null) continue;
                ModPanel modPanel = new(mod) {
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column(),
                    },
                    Clip = false,
                    Children = {
                        new HeaderSmall(mod.Name),
                        new Label(mod.Description == "" ? "Description could not be loaded or empty" : mod.Description) {
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
                                new LabelSmall("Path: " + Path.GetRelativePath(Config.Instance.Installation.Root, mod.Path)),
                                new LabelSmall("Installed Version: " + mod.Version),
                                new LabelSmall(mod.NewVersion == null || mod.NewVersion.Equals(mod.Version) 
                                    ? "Up to date" : "Update available: " + mod.NewVersion),
                                new LabelSmall("") {
                                    Data = {
                                        {"subscribe_click",
                                            (bool disabled, Element label) => 
                                            { (label as LabelSmall)!.Text = disabled ? "Disabled" : "";}}
                                    },
                                    Modifiers = {
                                        new OpacityModifier(0.7f),
                                    }
                                },
                            }
                        },
                        
                    }
                };

                if (mod.NewVersion == null || mod.NewVersion.Equals(mod.Version)) {
                    panels.Add(modPanel);
                    continue;
                }
                
                modPanel.Children.Add( 
                    new Group() {
                        Layout = {
                            Layouts.Fill(1, 0),
                            Layouts.Column()
                        },
                        Children = {
                            new Button("update", b => {
                                Group? parent = b.Parent as Group; // Should never be null
                                if (parent == null) {
                                    Console.WriteLine("ModPanel button parent was null!!!!");
                                    b.Text = "Error!";
                                    return;
                                }

                                ModPanel? panelParent = parent.Parent as ModPanel;
                                if (panelParent == null) {
                                    Console.WriteLine("ModPanel button parent was null!!!!");
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
                                    UI.Run(() => {
                                        b.Text =
                                            $"Press to Cancel | {(int) Math.Floor(100D * (position / (double) length))}% @ {speed} KiB/s";
                                    });
                                    bool exists = b.Data.TryGet("cancel", out bool cancel);
                                    Console.WriteLine(exists);
                                    if (exists) {
                                        return !cancel;
                                    }

                                    return true;
                                }, (success, isDone) => {
                                    UI.Run(() => { 
                                        if (isDone) {
                                            b.Text = success ? "Mod updated!" : "Mod update failed! Press to retry";
                                            b.Data.Add("updating", false);
                                            b.Data.Add("cancel", false);
                                            if (success)
                                                b.Enabled = false;
                                        } else if (!success) {
                                            b.Text = "Retrying in 3 seconds...";
                                        } else { // !isDone && success
                                            b.Text = "Update canceled";
                                            b.Data.Add("updating", false);
                                            b.Data.Add("cancel", false);
                                        }
                                    });
                                });
                            }) {
                                Enabled = !mod.IsUpdaterBlacklisted,
                                Layout = {
                                    Layouts.Fill(1, 0),
                                },
                                Data = {
                                    {"updating", false},
                                }
                            }
                        },
                    });

                panels.Add(modPanel);
            }
            return panels;
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

            public readonly ModList.ModInfo Mod;

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

            public ModPanel(ModList.ModInfo mod)
            : base() {
                this.Mod = mod;
                this.Disabled = this.Mod.IsBlacklisted;
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
                Mod.IsBlacklisted = Disabled;
                ModList.BlackListUpdate(Mod);
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

    }
}
