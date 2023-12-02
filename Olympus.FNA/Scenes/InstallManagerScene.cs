using OlympUI;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeFileDialogSharp;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Olympus {
    public class InstallManagerScene : Scene {
        public override bool Alert => true;

        // Generate runs before anything else.
        private Group InstallsFound = null!;
        private Group InstallsManual = null!;

        public static Installation? SelectedInstall;

        private readonly Group InstallsFoundLoading = new() {
            Layout = {
                Layouts.Left(0.5f, 0),
                Layouts.Top(0.5f, 0),
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
        };

        private readonly List<Installation> InstallsFoundAdded = new();
        private readonly HashSet<Installation> RenamingInstalls = new();

        public enum InstallList {
            Found,
            Added
        }

        public override Element Generate()
            => new Group {
                Style = {
                    { Group.StyleKeys.Spacing, 16 },
                },
                Layout = {
                    Layouts.Fill(),
                    Layouts.Column()
                },
                Children = {
                    new HeaderBig("Celeste Installations"),
                    new Group {
                        Clip = true,
                        ClipExtend = 16,
                        Layout = {
                            Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                        },
                        Children = {
                            new ScrollBox {
                                Clip = false,
                                Layout = {
                                    Layouts.Fill(),
                                },
                                Content = new Group {
                                    Style = {
                                        { Group.StyleKeys.Spacing, 16 },
                                    },
                                    Layout = {
                                        // Slight offset to avoid colliding with the scrollbar
                                        Layouts.Fill(1, 0, 20, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new Group {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                                Layouts.Column(),
                                            },
                                            Children = {
                                                new HeaderSmall("Found on this PC"),
                                                new Group {
                                                    Clip = true,
                                                    ClipExtend = 8,
                                                    Style = {
                                                        { Group.StyleKeys.Spacing, 8 },
                                                    },
                                                    Layout = {
                                                        Layouts.Fill(1, 0),
                                                        Layouts.Column(),
                                                    },
                                                    Init = el => InstallsFound = (Group) el,
                                                },
                                            }
                                        },

                                        new Group {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                                Layouts.Column(),
                                            },
                                            Children = {
                                                new HeaderSmall("Manually added"),
                                                new Button("Add installation", b => AddManualInstallation()) {
                                                    Style = {
                                                        { Group.StyleKeys.Spacing, 8 },
                                                    },
                                                    Layout = {
                                                        Layouts.Column(),
                                                        Layouts.Fill(1, 0),
                                                    },
                                                },
                                                new Group {
                                                    Clip = true,
                                                    ClipExtend = 8,
                                                    Style = {
                                                        { Group.StyleKeys.Spacing, 8 },
                                                    },
                                                    Layout = {
                                                        Layouts.Fill(1, 0),
                                                        Layouts.Column(),
                                                    },
                                                    Init = el => InstallsManual = (Group) el,
                                                },
                                            }
                                        },

                                    }
                                }
                            }
                        }
                    }

                }
            };

        public override Element PostGenerate(Element root) {
            SelectedInstall ??= Config.Instance.Installation;
            App.Instance.FinderManager.Updated += UpdateInstallList; // TODO: save to disk found installs
            UpdateInstallList();
            return root;
        }

        private void UpdateInstallList() {
            UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Found, InstallList.Found);
            UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
        }

        private void UpdateInstallList(FinderUpdateState state, List<Installation> found, InstallList listType) {
            Group targetGroup = new();
            if (listType == InstallList.Found) {
                targetGroup = InstallsFound;
            } else if (listType == InstallList.Added) {
                targetGroup = InstallsManual;
            }
            if (state == FinderUpdateState.Manual || state == FinderUpdateState.Start) {
                InstallsFoundAdded.Clear();
                targetGroup.DisposeChildren();
            }

            if (state == FinderUpdateState.Start) {
                InstallsFound.Add(InstallsFoundLoading);
            }

            if (state == FinderUpdateState.Manual || state == FinderUpdateState.Add) {
                for (int i = InstallsFoundAdded.Count; i < found.Count; i++) {
                    Installation install = found[i];
                    InstallsFoundAdded.Add(install);
                    targetGroup.Add(CreateEntry(install));
                }
            }

            if (state == FinderUpdateState.End) {
                InstallsFound.Remove(InstallsFoundLoading);
            }
        }

        private void AddManualInstallation() {
            string filter;

            if (PlatformHelper.Is(Platform.Linux)) {
                filter = "exe,bin.x86,bin.x86_64,dll";
            } else if (PlatformHelper.Is(Platform.MacOS)) {
                filter = "app,exe,bin.osx,dll";
            } else { // default to windows
                filter = "exe,dll";
            }

            DialogResult result = Dialog.FileOpen(filter);

            if (!result.IsOk) {
                if (result.IsError) {
                    AppLogger.Log.Error("Error while choosing file: " + result.ErrorMessage);
                }
                return;
            }

            Installation newInstall = new(Installation.InstallationType.Manual, "", result.Path);

            if (!newInstall.FixPath()) { // Ignore for now
                AppLogger.Log.Warning($"Bad path: {newInstall.Root}");
                MetaNotificationScene.PushNotification(new Notification { Message = $"Invalid Celeste install: {newInstall.Root}", Level = Notification.SeverityLevel.Warning});
                return;
            }
            
            App.Instance.FinderManager.AddManualInstall(newInstall);
            UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
        }

        private Panel CreateEntry(Installation install) {
            Label? labelVersion = null;
            
            void GeneratePanelContent(InstallerSelectablePanel panel) => UI.Run(() => {
                panel.DisposeChildren();
                
                panel.Add(new Icon(OlympUI.Assets.GetTexture($"icons/{install.Icon}")) {
                    AutoW = 64,
                    Layout = {
                        Layouts.Top(0.5f, -0.5f),
                    },
                });

                var textGroup = panel.Add(new Group() {
                    Clip = true,
                    Layout = {
                        Layouts.Fill(1, 0), 
                        Layouts.Column(),
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 4 },
                    },
                });
                if (RenamingInstalls.Contains(install)) {
                    textGroup.Add(new TextInput(new HeaderSmall(install.Name), new HeaderSmall("")) {
                        MaxLength = 50,
                        Placeholder = "Manual Installation",
                        ClickCallback = _ => panel.PreventNextClick(),
                        TextCallback = _ => panel.InvalidateFullDown(),
                        Style = {
                            { TextInput.StyleKeys.Placeholder, Color.Yellow}
                        }
                    });
                } else {
                    textGroup.Add(new HeaderSmall(string.IsNullOrWhiteSpace(install.Name) ? "Manual Installation" : install.Name) {
                        Wrap = true,
                    });
                }
                textGroup.Add(labelVersion ??= new Label("Scanning..."));
                textGroup.Add(new LabelSmall(install.Root));

                // if (install.Type != Installation.InstallationType.Manual) return;

                var buttonGroup = panel.Add(new Group {
                    Layout = {
                        Layouts.Top(0.5f, -0.5f),
                        Layouts.Right(8),
                        Layouts.Row(8, resize: false),
                    },
                });
                if (RenamingInstalls.Contains(install)) {
                    //TODO: Use a proper done icon
                    buttonGroup.Add(new RenameButton("search", "Done", b => {
                        panel.PreventNextClick();
                        // Make sure to trim it
                        install.Name = panel.FindChild<TextInput>()!.Text.Trim();
                        RenamingInstalls.Remove(install);
                        GeneratePanelContent(panel);
                    }));
                    buttonGroup.Add(new RenameButton("close", "Cancel", b => {
                        panel.PreventNextClick();
                        RenamingInstalls.Remove(install);
                        GeneratePanelContent(panel);
                    })); 
                } else {
                    //TODO: Use a proper edit icon
                    buttonGroup.Add(new RenameButton("search", "Rename", b => {
                        panel.PreventNextClick();
                        RenamingInstalls.Add(install);
                        GeneratePanelContent(panel);
                        UI.Run(() => UI.Focusing = panel.FindChild<TextInput>());
                    }));                    
                }

                buttonGroup.Add(new RemoveButton("delete", "Delete", b => {
                    panel.PreventNextClick();
                    App.FinderManager.RemoveInstallation(install);
                    GeneratePanelContent(panel);
                }));
            });
            
            InstallerSelectablePanel panel = null!;
            panel = new(install) {
                Clip = false,
                Layout = {
                    Layouts.Fill(1, 0),
                    Layouts.Row(resize: false),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 4 },
                },
                Init = el => GeneratePanelContent((InstallerSelectablePanel)el), 
            };

            Task.Run(() => {
                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) version = install.ScanVersion(true);
                UI.Run(() => {
                    if (labelVersion == null) return;
                    labelVersion.Text = version.Full;
                });
            });

            return panel;
        }

        private class RemoveButton : MetaMainScene.SidebarButton {
            public new static readonly Style DefaultStyle = new() {
                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0xff, 0x30, 0x30, 0xff) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0xc3, 0x00, 0x00, 0xc0) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
            };

            public RemoveButton(string icon, string text, Action<Button> cb)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text, cb) { }
            public RemoveButton(IReloadable<Texture2D, Texture2DMeta> icon, string text, Action<Button> cb)
                : base(icon, text) {
                Callback += cb;
                WH = new(64, 64);
            }
        }

        private class RenameButton : MetaMainScene.SidebarButton {
            public new static readonly Style DefaultStyle = new() {
                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0xff, 0x30, 0x30, 0xff) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0xc3, 0x00, 0x00, 0xc0) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
            };

            public RenameButton(string icon, string text, Action<Button> cb)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text, cb) { }
            public RenameButton(IReloadable<Texture2D, Texture2DMeta> icon, string text, Action<Button> cb)
                : base(icon, text) {
                Callback += cb;
                WH = new(64, 64);
            }
        }

        private class InstallerSelectablePanel : SelectablePanel {
            public new static readonly Style DefaultStyle = new() { // TODO: selected on dark mode looks awful
                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x08, 0x08, 0x22, 0xd0) },
                        { Panel.StyleKeys.Border, new Color(0x08, 0x08, 0x08, 0xd0) },
                    }
                },
                {
                    StyleKeys.Selected,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x08, 0x08, 0x48, 0xd0) },
                        { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0xd0) },
                    }
                },
            };

            private bool preventNextClick = false;
            private readonly Installation install;
            
            public InstallerSelectablePanel(Installation install) : base(_ => false) {
                this.install = install;
            }

            public override void Update(float dt) {
                Selected = Equals(install, SelectedInstall);
                base.Update(dt);
            }

            private void OnClick(MouseEvent.Click e) {
                if (preventNextClick) {
                    preventNextClick = false;
                    return;
                }

                SelectedInstall = install;
                Config.Instance.Installation = install;
                Config.Instance.Save();
            }

            public void PreventNextClick() {
                preventNextClick = true;
            }
        }
    }
}
