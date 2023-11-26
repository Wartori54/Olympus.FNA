using OlympUI;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeFileDialogSharp;
using Microsoft.Xna.Framework.Graphics;
using Olympus.NativeImpls;
using Microsoft.Xna.Framework;

namespace Olympus {
    public class InstallManagerScene : Scene {

        public override bool Alert => true;

#pragma warning disable CS8618 // Generate runs before anything else.
        private Group InstallsManaged;
        private Group InstallsFound;
        private Group InstallsManual;
#pragma warning restore CS8618

        public static Installation? SelectedInstall;

        private Group InstallsFoundLoading = new Group() {
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

        private List<Installation> InstallsFoundAdded = new();
        
        private HashSet<Installation> RenamingInstalls = new();

        public enum InstallList {
            Found,
            Added
        }

        public override Element Generate()
            => new Group() {
                Style = {
                    { Group.StyleKeys.Spacing, 16 },
                },
                Layout = {
                    Layouts.Fill(),
                    Layouts.Column()
                },
                Children = {
                    new HeaderBig("Celeste Installations"),
                    new Group() {
                        Clip = true,
                        ClipExtend = 16,
                        Layout = {
                            Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                        },
                        Children = {
                            new ScrollBox() {
                                Clip = false,
                                Layout = {
                                    Layouts.Fill(),
                                },
                                Content = new Group() {
                                    Style = {
                                        { Group.StyleKeys.Spacing, 16 },
                                    },
                                    Layout = {
                                        // Slight offset to avoid colliding with the scrollbar
                                        Layouts.Fill(1, 0, 20, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new Group() {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                                Layouts.Column(),
                                            },
                                            Children = {
                                                new HeaderSmall("Found on this PC"),
                                                new Group() {
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

                                        new Group() {
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
                                                new Group() {
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
                AppLogger.Log.Warning("Bad path: " + newInstall.Root);
                return; // TODO: Give user a warning for invalid installation
            }
            
            App.Instance.FinderManager.AddManualInstall(newInstall);
            UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
        }

        private Panel CreateEntry(Installation install) {
            Label? labelVersion = null;
            
            InstallerSelectablePanel panel = null!; // Required to reference it inside the body
            panel = new(install, b => {
                SelectedInstall = install;
                Config.Instance.Installation = install;
                Config.Instance.Save();
            }) {
                Data = {
                    { "Installation", install },
                },
                Clip = false,
                Layout = {
                    Layouts.Fill(1, 0),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 0 },
                },
                Children = {
                    new Group() {
                        Clip = true,
                        Layout = {
                            Layouts.Fill(1, 0),
                            Layouts.Column(),
                        },
                        Style = {
                            { Group.StyleKeys.Spacing, 4 },
                        },
                        Init = el => {
                            if (RenamingInstalls.Contains(install)) {
                                el.Add(new TextInput(install.Name) {
                                    MaxLength = 50,
                                    Placeholder = "Manual Installation",
                                    ClickCallback = _ => panel.PreventNextClick(),
                                    Style = {
                                        { TextInput.StyleKeys.Placeholder, Color.Yellow}
                                    }
                                });
                                el.Add(labelVersion = new Label("Scanning..."));
                                el.Add(new LabelSmall(install.Root));
                                return;
                            }
                            el.Add(new HeaderSmall(string.IsNullOrWhiteSpace(install.Name) ? "Manual Installation" : install.Name) {
                                Wrap = true,
                            });
                            el.Add(labelVersion = new Label("Scanning..."));
                            el.Add(new LabelSmall(install.Root));
                        },
                    },
                    
                }
            };
            if (install.Type == Installation.InstallationType.Manual) {
                var group = panel.Add(new Group {
                    Layout = {
                        Layouts.Top(0.5f, -0.5f),
                        Layouts.Right(8),
                        Layouts.Row(8, resize: false),
                    },
                });
                if (RenamingInstalls.Contains(install)) {
                    //TODO: Use a proper done icon
                    group.Add(new RenameButton("search", "Done", b => {
                        panel.PreventNextClick();
                        install.Name = panel.FindChild<TextInput>()!.Text;
                        RenamingInstalls.Remove(install);
                        UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
                    }));
                    group.Add(new RenameButton("close", "Cancel", b => {
                        panel.PreventNextClick();
                        RenamingInstalls.Remove(install);
                        UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
                    })); 
                } else {
                    //TODO: Use a proper edit icon
                    group.Add(new RenameButton("search", "Rename", b => {
                        panel.PreventNextClick();
                        RenamingInstalls.Add(install);
                        UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
                    }));                    
                }
                
                group.Add(new RemoveButton("delete", "Delete", b => {
                    panel.PreventNextClick();
                    App.FinderManager.RemoveInstallation(install);
                    UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
                }));
            }

            Task.Run(() => {
                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) version = install.ScanVersion(true);
                UI.Run(() => {
                    if (labelVersion == null) return;
                    labelVersion.Text = version.Full;
                });
            });

            return panel;
        }
        
        public class RemoveButton : MetaMainScene.SidebarButton {
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
        
        public class RenameButton : MetaMainScene.SidebarButton {
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

        public partial class InstallerSelectablePanel : Panel { // TODO: merge this with the other SelectablePanel

            public static readonly new Style DefaultStyle = new() { // TODO: selected on dark mode looks awful
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

            public bool Selected = false;

            private bool preventNextClick = false;

            private Action<InstallerSelectablePanel> callback;
            private Installation install;
            
            public InstallerSelectablePanel(Installation install, Action<InstallerSelectablePanel> cb)
            : base() {
                this.callback = cb;
                this.install = install;
            }

            public override void Update(float dt) {
                Selected = install.Equals(SelectedInstall);

                Style.Apply(Selected ? StyleKeys.Selected : 
                            Hovered  ? StyleKeys.Hovered :
                                       StyleKeys.Normal);

                base.Update(dt);
            }

            private void OnClick(MouseEvent.Click e) {
                if (preventNextClick) {
                    preventNextClick = false;
                    return;
                }
                callback.Invoke(this);
            }

            public void PreventNextClick() {
                preventNextClick = true;
            }

            public new abstract partial class StyleKeys {

                public static readonly Style.Key Normal = new("Normal");
                public static readonly Style.Key Selected = new("Selected");
                public static readonly Style.Key Hovered = new("Hovered");
            }
        }

    }

}
