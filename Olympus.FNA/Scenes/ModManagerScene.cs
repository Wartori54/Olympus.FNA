using FontStashSharp;
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
using System.Threading.Tasks;

namespace Olympus {
    public class ModManagerScene : Scene {

        private SceneContainer? sceneContainer;

        private ManualCache<Dictionary<ModAPI.IModInfo, EntryModInfo>> modLinkCache = new(_ => {
            if (Config.Instance.Installation == null) return new Dictionary<ModAPI.IModInfo, EntryModInfo>();
            Dictionary<ModAPI.IModInfo, EntryModInfo> ret = new();
            foreach (ModAPI.IModFileInfo iModFileInfo in Config.Instance.Installation.LocalInfoAPI
                         .CreateAllModFileInfo()) {
                ModAPI.LocalInfoAPI.LocalModFileInfo localModFileInfo = (ModAPI.LocalInfoAPI.LocalModFileInfo) iModFileInfo;
                // We can get into trouble if a cache regeneration happens while doing this, so freeze it is
                ModAPI.IModInfo? modInfo = App.Instance.APIManager.TryAll(api => api.GetModInfoFromFileInfo(localModFileInfo), true);
                if (modInfo == null) continue;
                if (ret.TryGetValue(modInfo, out EntryModInfo? modFiles)) {
                    modFiles.ContainedMods.Add(localModFileInfo);
                } else {
                    ret.Add(modInfo, new EntryModInfo(new List<ModAPI.LocalInfoAPI.LocalModFileInfo> { localModFileInfo }));
                }
            }

            return ret;
        },null);

        public ModManagerScene() {
            Config.Instance.SubscribeInstallUpdateNotify(_ => modLinkCache.Invalidate());
        }

        public override Element Generate()
            => new Group() {
                Style = { { Group.StyleKeys.Padding, 8 }, { Group.StyleKeys.Spacing, 8 }, },
                Layout = { Layouts.Column(), Layouts.Fill(1F, 1F), },
                Children = {
                    new Group() {
                        Layout = { Layouts.Fill(1F, 0F), Layouts.Row(), },
                        Children = {
                            new Group() {
                                Layout = { Layouts.Fill(1 / 4F, 0), },
                                Children = {
                                    new SectionButton(this, Scener.Get<ModManagerMapsScene>(), "Your Maps")
                                    {
                                        Layout = { Layouts.Left(0.5f, -0.5f), }
                                    },
                                }
                            },
                            new Group() {
                                Layout = { Layouts.Fill(1 / 4F, 0), },
                                Children = {
                                    new SectionButton(this, Scener.Get<ModManagerBagsScene>(), "Your Mod Bags") {
                                        Layout = { Layouts.Left(0.5f, -0.5f) }
                                    },
                                }
                            },
                            new Group() {
                                Layout = { Layouts.Fill(1 / 4F, 0), },
                                Children = {
                                    new SectionButton(this, Scener.Get<ModManagerToolsScene>(),"Your Tools") {
                                        Layout = { Layouts.Left(0.5f, -0.5f) }
                                    },
                                }
                            },
                            new Group() {
                                Layout = { Layouts.Fill(1 / 4F, 0), },
                                Children = {
                                    new SectionButton(this, Scener.Get<ModManagerAllScene>(), "All Mods") {
                                        Layout = { Layouts.Left(0.5f, -0.5f) }
                                    },
                                }
                            },
                        }
                    },
                    new Panel() {
                        ID = "Search Box",
                        Layout = {
                            Layouts.Fill(1, 0),
                        },
                        Children = {
                            new HeaderSmall("Search here..."),
                        }
                    },
                    (sceneContainer = new SceneContainer(Scener.Get<ModManagerMapsScene>()) {
                        Layout = {
                            Layouts.Fill(1F, 1F, 0, LayoutConsts.Prev), 
                        },
                    }),
                },
                Init = RegisterRefresh<Group>(_ => {
                    // TODO: Figure out how to automatically refresh SceneContainers 
                    sceneContainer.Scene.Refresh();
                    return Task.CompletedTask;
                })
            };

        public override void Enter(params object[] args) {
            sceneContainer?.Scene.Enter();
            base.Enter();
        }

        public override void Leave() {
            sceneContainer?.Scene.Leave();
            base.Leave();
        }

        public List<(ModAPI.IModInfo, EntryModInfo)> GetAllOfType(ModAPI.ModType type) {
            List<(ModAPI.IModInfo, EntryModInfo)> ret = new();
            foreach ((ModAPI.IModInfo modInfo, EntryModInfo modFileInfos) in modLinkCache.Value) {
                if (modInfo.ModType == type)
                    ret.Add((modInfo, modFileInfos));
            }

            return ret;
        }

        private class ModManagerMapsScene : Scene {
            private bool isVisible;
            
            // Two groups wrapping the scroll box are needed since one can only do the clipping, and the other one is
            // in charge of the padding
            public override Element Generate()
                => new Group() {
                    ID = "SceneRoot",
                    Layout = {
                        Layouts.Fill(1f, 1f),
                    },
                    Style = {
                        { Group.StyleKeys.Padding, new Padding(0, 8, 0, 0) },
                    },
                    Children = {
                        new Group() {
                            ID = "ScrollBoxClipper",
                            Layout = {
                                Layouts.Fill(1f, 1f),
                            },
                            Clip = true,
                            Children = {
                                new ScrollBox() {
                                    ID = "ScrollBox",
                                    Layout = {
                                        Layouts.Fill(1f, 1f)
                                    }, 
                                    Clip = true,
                                    Content = new Group() {
                                        ID = "ScrollBoxContents",
                                        Layout = {
                                            Layouts.Fill(1f, 0f),
                                            Layouts.Row(),
                                        },
                                        Style = {
                                            { Group.StyleKeys.Spacing, 8 },
                                            // { Group.StyleKeys.Padding, 8},
                                        },
                                        Init = RegisterRefresh<Group>(async el => {
                                            await UI.Run(() => {
                                                el.DisposeChildren();
                                                el.Add(new Group() {
                                                    Layout = {
                                                        Layouts.FillFull(1, 0, 8/* for some reason this group is 8px bigger than it should be */),
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
                                            // This is fine since scenes do not get re-instantiated
                                            List<(ModAPI.IModInfo, EntryModInfo)> allOfType = 
                                                Scener.Get<ModManagerScene>().GetAllOfType(ModAPI.ModType.Map);
                                            List<Panel> generatedPanels = new();
                                            foreach ((ModAPI.IModInfo modInfo, EntryModInfo modFileInfos) in allOfType) {
                                                // (int blacklistStatus, Task<bool> hasUpdates) = AnalyzeModFileInfos(modFileInfos);
                                                

                                                generatedPanels.Add(new ModManagerSceneEntry(modFileInfos, modInfo));
                                                // generatedPanels.Add(GeneratePanel(modInfo, 
                                                    // GetBlacklistString(blacklistStatus, modFileInfos.Count), 
                                                    // hasUpdates,
                                                     // GetImageForMod(modInfo)));
                                            }
                                            const int columns = 4;
                                            List<Group> columnList = new();
                                            for (int i = 0; i < columns; i++) {
                                                columnList.Add(new Group() {
                                                    Layout = {
                                                        Layouts.Fill(1F/columns, 0, 8*(columns-1)/columns),
                                                        Layouts.Column(),
                                                    },
                                                    Style = {
                                                        { Group.StyleKeys.Spacing, 8 },
                                                    },
                                                });
                                            }

                                            int currColumn = 0;
                                            foreach (Panel generatedPanel in generatedPanels) {
                                                columnList[currColumn].Children.Add(generatedPanel);
                                                
                                                currColumn = (currColumn + 1) % columns;
                                            }

                                            if (!isVisible) return;
                                            await UI.Run(() => {
                                                el.DisposeChildren();
                                                
                                                foreach (Group column in columnList) {
                                                    el.Children.Add(column);
                                                }
                                            });
                                        }),
                                    },
                                }
                            }
                        }
                    }
                };

            public override void Enter(params object[] args) {
                isVisible = true;
                base.Enter(args);
            }

            public override void Leave() {
                // Dispose the scene on Leave instead on the Refresh, so this way we make sure we don't get a draw with
                // the contents we're about to dispose (as that draw call *will* be really expensive)
                isVisible = false;
                UI.Run(Root.GetChild<Group>("ScrollBoxClipper").GetChild<ScrollBox>("ScrollBox").Content.DisposeChildren);
                base.Leave();
            }


            // private static (int blacklistStatus, Task<bool> hasUpdates) AnalyzeModFileInfos(List<ModAPI.IModFileInfo> modFileInfos) {
            //     int blacklistStatus = 0;
            //     bool hasUpdates = false;
            //     foreach (ModAPI.IModFileInfo modFileInfo in modFileInfos) {
            //         if (modFileInfo.IsBlacklisted ?? false) {
            //             blacklistStatus++;
            //         }
            //     }
            //
            //     return (blacklistStatus, Task.Run(() => {
            //         foreach (ModAPI.IModFileInfo modFileInfo in modFileInfos) {
            //             ModAPI.RemoteModInfoAPI.RemoteModFileInfo? remoteModFileInfo =
            //                 App.Instance.APIManager.TryAll(api => api.GetModFileInfoFromId(modFileInfo.Name));
            //             if (remoteModFileInfo == null) continue;
            //             if (!hasUpdates && remoteModFileInfo.Hash != modFileInfo.Hash) {
            //                 hasUpdates = true;
            //             }
            //         }
            //
            //         return hasUpdates;
            //     }));
            // }

        }
        
        private class ModManagerBagsScene : Scene {
            public override Element Generate()
                => new Group() {
                    Layout = {
                        Layouts.Column(),
                    }, Children = {
                        new Label("bagbagbagbagbagbagbagbagbagbagbabagbagbagbagbagbagbagbagbagbabagbagbagbagbagbagbagbagbagbag") {
                        }
                    }
                };
        }
        
        private class ModManagerToolsScene : Scene {
            public override Element Generate()
                => new Group() {
                    Layout = {
                        Layouts.Column(),
                    }, 
                    Clip = true,
                    Init = RegisterRefresh<Group>(async el => {
                        // This is fine since scenes do not get re-instantiated
                        List<(ModAPI.IModInfo, EntryModInfo)> allOfType = Scener.Get<ModManagerScene>().GetAllOfType(ModAPI.ModType.Tool);
                        await UI.Run(() => {
                            el.Children.Clear();
                            foreach ((ModAPI.IModInfo modInfo, EntryModInfo modFileInfos) in 
                                     allOfType) {
                                string text = modInfo.Name + $" ({modInfo.ModType})" + " -> ";
                                foreach (ModAPI.IModFileInfo modFileInfo in modFileInfos.ContainedMods) {
                                    text += modFileInfo.Name + ", ";
                                }

                                text = text[..^2];
                                if (modInfo.ModType == ModAPI.ModType.Tool)
                                    el.Children.Add(new Label(text));
                            }
                        });
                    }),
                    Children = {
                        new Label("tool 102"),
                        new Label("tool 203"),
                    }
                };
        }
        
        private class ModManagerAllScene : Scene {
            public override Element Generate()
                => new Group() {
                    Layout = {
                        Layouts.Column(),
                    }, Children = {
                       new Panel() {
                           Layout = {
                               Layouts.Column(),
                           },
                           Children = {
                               new Label("thing 1,"),
                               new Label("thing 2"),
                           }
                       },
                    }
                };
        }

        public sealed partial class SectionButton : Button {
            public new static readonly Style DefaultStyle = new() {
                {
                    StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x00) },
                        { StyleKeys.Foreground, new Color(0xf0, 0x50, 0x50, 0x50) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x50) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0x70) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
            };

            public SectionButton(ModManagerScene parentScene, Scene scene, string text) 
                : base(b => {
                    if (parentScene.sceneContainer != null) 
                        parentScene.sceneContainer.Scene = scene;
                }) {
                Layout.Add(Layouts.Row(false));
                
                Children.Add(new HeaderMedium(text) {
                    ID = "label",
                    Style = {
                        { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) }
                    }
                });
                
                Children.Add(new MetaMainScene.SidebarNavButtonIndicator(() => scene == parentScene.sceneContainer?.Scene) {
                    Y = 24+8,
                    H = 4,
                    IsHorizontal = true,
                    Layout = {
                        Layouts.Fill(1, 0, 0, 0),
                        Layouts.Left(0.5f, -0.5f),
                    },
                    Style = {
                        {
                            MetaMainScene.SidebarNavButtonIndicator.StyleKeys.Normal,
                            new Style() {
                                { new Color(0x00, 0x00, 0x00, 0x00) },
                                { MetaMainScene.SidebarNavButtonIndicator.StyleKeys.Scale, 0f },
                            }
                        },
        
                        {
                            MetaMainScene.SidebarNavButtonIndicator.StyleKeys.Active,
                            new Style() {
                                { new Color(0x90, 0x90, 0x90, 0xff) },
                                { MetaMainScene.SidebarNavButtonIndicator.StyleKeys.Scale, 1f },
                            }
                        },
                    },
                });
            }
            
            
        }
        
        // The panels used everywhere in this scene
        public sealed partial class ModManagerSceneEntry : Panel {
            private readonly ModAPI.IModFileInfo modFileInfo;
            private readonly ModAPI.IModInfo modInfo;

            private Task<GrayscalableImage?>? imageTask;
            private Task? setupImageTask;
            private Task setupUpdateTask;

            private GrayscalableImage? image;
            private Label? overlaidLabel;
            private Icon? enabledIcon;
            
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

            public ModManagerSceneEntry(ModAPI.IModFileInfo modFileInfo, ModAPI.IModInfo modInfo) {
                this.modFileInfo = modFileInfo;
                this.modInfo = modInfo;
                Disabled = this.modFileInfo.IsBlacklisted ?? false;
                Layout.Add(Layouts.Fill(1, 0));
                Layout.Add(Layouts.Column());
                Children = new ObservableCollection<Element> {
                    new Group() {
                        Layout = { Layouts.Column(8), Layouts.Fill(1, 0)},
                        Style = {
                            { Group.StyleKeys.Spacing, 0 },
                        },
                        Children = {
                            new Group() {
                                Layout = {
                                    Layouts.Row(),
                                    Layouts.Fill(1, 0),
                                },
                                Children = {
                                    new Group() {
                                        Layout = {
                                            Layouts.Fill(1, 0, LayoutConsts.Next),
                                        },
                                        Children = {
                                            new HeaderSmall(modInfo.Name) {
                                                Wrap = true,
                                            },
                                        }
                                    },
                                    (enabledIcon = new Icon(OlympUI.Assets.GetTexture("icons/play.png")) {
                                        AutoH = 32,
                                        Visible = !Disabled,
                                    }),
                                }
                            }
                            // new LabelSmall($"({
                            //     modFileInfo.IsBlacklisted switch {
                            //         false => "Enabled",
                            //         null => "Mixed",
                            //         true => "Disabled"
                            //      }
                            // })"),
                        }
                    },
                    new Group() {
                        ID = "BottomGroup",
                        Layout = {
                            Layouts.Row(),
                            Layouts.Fill(1f, 0),
                        },
                        Style = {
                            { Group.StyleKeys.Spacing, 8 } // TODO: this makes all the buttons move for a frame, whyy
                        },
                        Children = {
                            new Group() {
                                ID = "DescLabelCont",
                                Layout = {
                                    Layouts.Fill(1f, 0f, LayoutConsts.Next),
                                    // Layouts.FitChildren(false, true),
                                },
                                Children = {
                                    new Label(modInfo.Description != "" ? modInfo.Description : "No description available RANDOM DEBUGGING TEXT PLEASE REMOVE BEFORE PUSHING I DARE YOU IF YOU DONT ILL COMMIT redacted PLEASE OK YOU HEARD ME?") {
                                        ID = "DescriptionLabel",
                                        Wrap = true,
                                    },
                                }
                            },
                            new Button("Hashing...") {
                                ID = "UpdateStatus",
                                Enabled = false,
                                Layout = {
                                    // Layouts.Right()
                                }
                            },
                        }
                    },
                };
                Task.Run(async () => {
                    imageTask = GetImageForMod(modInfo); // Dont await this, the next call will add a placeholder space for the image
                    setupImageTask = SetupImage(); // TODO: config to disable the image
                    if (setupImageTask != null) await setupImageTask;
                });
                setupUpdateTask = Task.Run(SetupUpdates);
            }
            
            public static async Task<GrayscalableImage?>? GetImageForMod(ModAPI.IModInfo modInfo) {
                IReloadable<Texture2D, Texture2DMeta>? tex = null;
                foreach (string img in modInfo.Screenshots) {
                    tex = await App.Instance.Web.GetTextureUnmipped(img);
                    if (tex != null) break;
                }
    
                if (tex == null) return null;
    
                return new GrayscalableImage(tex) {
                    DisposeTexture = true,
                    Style = {
                        { GrayscalableImage.StyleKeys.Intensity, 1f },
                    },
                    Modifiers = {
                        new FadeInAnimation(0.6f).With(Ease.QuadOut),
                        // new ScaleInAnimation(1.05f, 0.5f).With(
                        //     Ease.QuadOut)
                    },
                };
            }

            // Get the image in place and hook eveything up after we got the task
            private async Task? SetupImage() {
                int maxImageWidth = 400;
                int maxImageHeight = (int) (maxImageWidth * (9f / 16f));
                Element descLabel = GetChild("BottomGroup") ??
                                                        throw new InvalidOperationException(
                                                            "Missing description label from mod panel!");
                // Set up the Loading screen asap
                await UI.Run(() => {
                    // Insert it before the Description label
                    Children.Insert(
                        Children.IndexOf(descLabel),
                        new Panel() {
                            ID = "ImagePanel",
                            Clip = true,
                            Layout = {
                                Layouts.Left(0.5f, -0.5f),
                            },
                            Style = { { Group.StyleKeys.Padding, 0 }, { Panel.StyleKeys.Shadow, 0 }, },
                            MinWH = new Point(10, 10),
                            Children = {
                                new Group() { // Fake group to make an empty space
                                    Layout = {
                                        ev => {
                                            int width = Math.Min(W-Padding*2, maxImageWidth);
                                            ev.Element.W = width;
                                            ev.Element.H = maxImageHeight;
                                        }
                                    },
                                    Children = { // Loading screen for now
                                        new Label("Loading...") { // TODO: some animation for when an image is loading
                                            Layout = {
                                                Layouts.Top(0.5f, -0.5f),
                                                Layouts.Left(0.5f, -0.5f),
                                            },
                                            Modifiers = {
                                                new OpacityModifier(0.5f),
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    );
                });
                // Wait for the actual task to be done
                if (imageTask != null)
                    image = await imageTask;
                else 
                    image = null;
                Panel imagePanel = GetChild<Panel>("ImagePanel");
                if (image == null) {
                    await UI.Run(() => {
                        Label label = imagePanel.GetChild<Group>().GetChild<Label>();
                        label.Text = "Image could not be loaded!";
                    });
                    return;
                }
                
                // Special layout to center the image
                image.Layout.Add(ev => {
                    Image img = (Image) ev.Element;
                    // int panelW = panel.W - panel.Padding*2;
                    // img.AutoW = Math.Min(panelW, 400);
                    img.AutoH = maxImageHeight;
                    img.X = imagePanel.W/2 - img.AutoW/2;
                });
                // image.Layout.Add(Layouts.Left(0.5f, -0.5f));

                overlaidLabel = new HeaderBig("") {
                    Layout = {
                        ev => {
                            ev.Element.X = imagePanel.W / 2 - ev.Element.W / 2;
                            ev.Element.Y = imagePanel.H / 2 - ev.Element.H / 2;
                        }
                    },
                    Style = { 
                        { Label.StyleKeys.Color, new Color(0, 0, 0, 255) },
                        { Label.StyleKeys.FontEffect, CustomGlyphRenderer.FontSystemEffectExtension.Outline.AsVanilla() },
                        { Label.StyleKeys.FontEffectAmount, 6 /* EffectAmount */}  
                    },
                    Visible = false,
                };
                overlaidLabel.Text = Disabled ? "Press to enable!" : "Press to disable!";
                
                await UI.Run(() => {
                    imagePanel.DisposeChildren();
                    imagePanel.Layout.Add(ev => { // Special layout for dynamic width
                        int width = Math.Min(W-Padding*2, image.W);
                        ev.Element.W = width; 
                        ev.Element.H = maxImageHeight;
                    });
                    imagePanel.Children.Add(new Group() {
                        Layout = {
                            // Layouts.Left(0.5f, -0.5f),
                            Layouts.FillFull()
                        },
                        Children = {
                            image,
                            overlaidLabel,
                        }
                    });
                });
            }

            private async Task SetupUpdates() {
                string hasUpdatesText;
                if (modFileInfo is EntryModInfo entryModInfo) { // Sad, we have to do special behavior
                    bool anyUpdate = false;
                    foreach (ModAPI.LocalInfoAPI.LocalModFileInfo containedMod in entryModInfo.ContainedMods) {
                        if (HasUpdate(containedMod)) {
                            anyUpdate = true;
                            break;
                        }
                    }
                    hasUpdatesText = anyUpdate ? "Updates available!" : "Up to date!";
                } else {
                    hasUpdatesText = HasUpdate(modFileInfo) ? "Update available!" : "Up to date!";
                }
                await UI.Run(() => {
                    Button button = GetChild<Group>("BottomGroup").GetChild<Button>("UpdateStatus");
                    button.Text = hasUpdatesText;
                    button.Enabled = hasUpdatesText != "Up to date!";
                });
            }

            private static bool HasUpdate(ModAPI.IModFileInfo modFileInfo) {
                ModAPI.RemoteModInfoAPI.RemoteModFileInfo? remoteModFileInfo =
                                        App.Instance.APIManager.TryAll(api => api.GetModFileInfoFromId(modFileInfo.Name));
                if (remoteModFileInfo == null) return false;
                return remoteModFileInfo.Hash != modFileInfo.Hash;
            }
            
            public override void Update(float dt) {
                Style.Apply(StyleKeys.Normal);
                // if (image != null)
                    // image.coolerIntensity = Hovered ? 0f : 1f;
                // Console.WriteLine(this + " " + Hovered);
                // image?.coolerIntensity.Update(dt);
                // image?.InvalidateCachedTexture();
                image?.Style.Apply(Disabled ? 
                    GrayscalableImage.StyleKeys.Grayed : 
                    GrayscalableImage.StyleKeys.Normal);
                if (overlaidLabel != null && overlaidLabel.Visible != Hovered) {
                    overlaidLabel.Visible = Hovered;
                    overlaidLabel.InvalidatePaint();
                }

                base.Update(dt);
            }
            
            private void OnClick(MouseEvent.Click e) {
                Disabled = !Disabled;
                if (overlaidLabel != null) 
                    overlaidLabel.Text = Disabled ? "Press to enable!" : "Press to disable!";
                if (enabledIcon != null) 
                    enabledIcon.Visible = !Disabled;
                // if (preventNextClick) {
                // preventNextClick = false;
                // return;
                // }
                // Disabled = !Disabled;
                // Config.Instance.Installation?.MainBlacklist.Update(Mod, Disabled);
                // foreach (Tuple<Element, Action<bool, Element>> subbed in subscribedClicks) {
                // subbed.Item2.Invoke(this.Disabled, subbed.Item1);
                // }
            }
            
            public  new abstract partial class StyleKeys {
            
                public static readonly Style.Key Normal = new("Normal");
                public static readonly Style.Key Selected = new("Selected");
                public static readonly Style.Key Hovered = new("Hovered");
            }
        }

        // This class holds a collection of mods and acts as a single one
        public class EntryModInfo : ModAPI.IModFileInfo {
            public List<ModAPI.LocalInfoAPI.LocalModFileInfo> ContainedMods { get; private set; }
            public string? Path { get; } = null;
            public string Name => throw new InvalidOperationException($"Cannot obtain {nameof(Name)} of {nameof(EntryModInfo)}");
            public string Hash 
                => throw new InvalidOperationException($"Cannot obtain {nameof(Hash)} of {nameof(EntryModInfo)}!");
            public bool IsLocal => true; // All mods are local

            public DateTime? LastUpdate =>
                throw new InvalidOperationException($"Cannot obtain {nameof(LastUpdate)} of {nameof(EntryModInfo)}");
            public string[] DownloadUrl =>
                throw new InvalidOperationException($"Cannot obtain {nameof(DownloadUrl)} of {nameof(EntryModInfo)}");

            public bool? IsBlacklisted {
                get {
                    bool anyBlacklisted = false;
                    foreach (ModAPI.LocalInfoAPI.LocalModFileInfo mod in ContainedMods) {
                        if (mod.IsBlacklisted ?? false) {
                            anyBlacklisted = true;
                        } else if (anyBlacklisted) { // If its some mod blacklisted but this one is
                            return null; // Will be interpreted as mixed
                        }
                    }

                    return anyBlacklisted;
                }
            }

            public bool? IsUpdaterBlacklisted {
                get {
                    bool anyUpdaterBlacklisted = false;
                    foreach (ModAPI.LocalInfoAPI.LocalModFileInfo mod in ContainedMods) {
                        if (mod.IsUpdaterBlacklisted ?? false) {
                            anyUpdaterBlacklisted = true;
                        } else if (anyUpdaterBlacklisted) { // If its some mod updaterblacklisted but this one is
                            return null; // Will be interpreted as mixed
                        }
                    }

                    return anyUpdaterBlacklisted;
                }
            }

            public Version? Version => ContainedMods[0].Version;
            public string[] DependencyIds => Array.Empty<string>();

            // `mods` are the ordered list of mods that should be grouped, the first mod is the "representative mod"
            public EntryModInfo(List<ModAPI.LocalInfoAPI.LocalModFileInfo> mods) {
                if (mods.Count == 0)
                    throw new InvalidOperationException($"Cannot create {nameof(EntryModInfo)} with 0 mods!");
                ContainedMods = mods;
            }

        }
    }
    
}
