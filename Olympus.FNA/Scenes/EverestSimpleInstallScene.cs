using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using Olympus.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Olympus; 

public class EverestSimpleInstallScene : Scene {
    public override bool Alert => true;

    private EverestInstaller.EverestBranch? selectedBranch = null;

    public override Element Generate()
        => new Group() {
            ID = "base",
            Style = {
                { Group.StyleKeys.Spacing, 16 },
            },
            Layout = {
                Layouts.Fill(),
                Layouts.Column(),
            },
            Children = {
                new HeaderBig("Install Everest"),
                new HeaderSmall("Latest version"),
                new Panel() {
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column()
                    },
                    Children = {
                        new HeaderMedium("") {
                            Init = RegisterRefresh<Label>(async el => {
                                if (Config.Instance.Installation == null) {
                                    // Refuse to load
                                    return;
                                }

                                EverestInstaller.EverestVersion? newest = GetLatest();

                                await UI.Run(() => {
                                    if (newest != null)
                                        el.Text = $"{newest.Branch}: {newest.version}{(newest.Branch.IsNonNative ? " (.NET Framework)" : "")}";
                                    else {
                                        el.Text = "Not found!";
                                    }
                                });

                            })
                        },
                        new Label("There's a new version of Everest\nUpdate now to get all the new features!") {
                            Init = RegisterRefresh<Label>(async el=> {
                                if (Config.Instance.Installation == null) {
                                    // Refuse to load
                                    return;
                                }
                                
                                EverestInstaller.EverestVersion? newest = GetLatest();
                                
                                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                                                                    = Config.Instance.Installation.ScanVersion(false);

                                await UI.Run(() => {
                                    if (newest != null)
                                        el.Text = ModVersion?.Minor == newest.version
                                            ? "Up to date!\nYou still can reinstall if some funkiness is going on..."
                                            : "There's a new version of Everest\nUpdate now to get all the new features!";
                                    else {
                                        el.Text = "Couldn't find any everest version matching your criteria, try again";
                                    }
                                });

                            })
                        },
                    }
                },
                new UpdateButton("download", "Update", async b => {
                    if (Config.Instance.Installation == null) return;
                    EverestInstaller.EverestVersion? version = GetLatest();
                    if (version == null) return;
                    Scener.PopFront();

                    WorkingOnItScene.Job job = new WorkingOnItScene.Job(() => 
                        EverestInstaller.InstallVersion(version, Config.Instance.Installation), 
                        "monomod2");
                    Scener.Set<WorkingOnItScene>(job, "monomod2");
                }) {
                    Init = RegisterRefresh<UpdateButton>(async el => {
                        if (Config.Instance.Installation == null) {
                            // Refuse to load
                            return;
                        }
                        EverestInstaller.EverestVersion? newest = GetLatest();
                        if (newest == null) {
                            el.Enabled = false;
                            return;
                        }

                        el.Enabled = true;

                        EverestInstaller.EverestBranch? branch =
                            CurrentInstallBranch();
                        if (branch == null) {
                            el.IsUpdate = true;
                            el.Text = "Install";
                            return;
                        } else if (!Equals(branch, selectedBranch ?? branch)) {
                            el.IsUpdate = true;
                            el.Text = "Switch";
                            return;
                        }
                        
                        
                        
                        (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                                                            = Config.Instance.Installation.ScanVersion(false);

                        if (newest.version == ModVersion?.Minor) {
                            el.IsUpdate = false;
                            el.Text = "Reinstall";
                        } else {
                            el.IsUpdate = true;
                            el.Text = "Update";
                        }
                        
                    })
                },
                new HeaderSmall("Your Everest"),
                new Panel() {
                    Layout = {
                        Layouts.Fill(1, 0),
                        Layouts.Column()
                    },
                    Children = {
                        new HeaderMedium("") {
                            Init = RegisterRefresh<Label>(async el => {
                                if (Config.Instance.Installation == null) {
                                    // Refuse to load
                                    return;
                                }
                                
                                EverestInstaller.EverestBranch? branch =
                                    CurrentInstallBranch();

                                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) 
                                    = Config.Instance.Installation.ScanVersion(false);
                                
                                await UI.Run(() => {
                                    if (branch != null)
                                        el.Text = $"{branch}: {ModVersion?.Minor.ToString() ?? "Unknown"}{(branch.IsNonNative ? " (.NET Framework)" : "")}";
                                    else {
                                        el.Text = $"Everest not installed";
                                        el.GetParent().GetChild<Label>("desc").Text = // Too lazy to add an Init to the label right below
                                            "Seems like Everest is not yet installed on this copy of celeste\nInstall it now to enjoy mods!";
                                    }
                                });

                            })
                        },
                        new Label("This is your current Everest version.") {
                            ID = "desc",
                        },
                    }
                },
                new Group() {
                    ID = "bottomButtons",
                    Layout = {
                        Layouts.Row()
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 16 },
                    },
                    Init = RegisterRefresh<Group>(async el => {
                        if (Config.Instance.Installation == null) return;
                        EverestInstaller.EverestBranch branch =
                             CurrentInstallBranch() ?? new EverestInstaller.EverestBranch(EverestInstaller.EverestBranch.ReleaseType.Stable, true);
                        await UI.Run(() => {
                            el.DisposeChildren();
                            EverestInstaller.EverestBranch.ReleaseType[] allBranches = Enum.GetValues<EverestInstaller.EverestBranch.ReleaseType>();
                            foreach (EverestInstaller.EverestBranch.ReleaseType currBranch in allBranches) {
                                if ((selectedBranch ?? branch).type == currBranch) continue;
                                el.Children.Add(new Button($"{(branch.type == currBranch ? "Back" : "Switch")} to {currBranch}", b => {
                                    selectedBranch ??= branch;
                                    selectedBranch = new EverestInstaller.EverestBranch(currBranch, selectedBranch.IsNonNative);
                                    VersionCache.Invalidate();
                                    Refresh();
                                }) {
                                    ID = currBranch.ToString(),
                                });
                            }
                            // Notice how "Core" and "Framework" are swapped here, this represents what we can switch to, rather than what we're using
                            el.Children.Add(new Button($"Use .NET {((selectedBranch ?? branch).IsNonNative ? "Core" : "Framework")}", b => {
                                selectedBranch ??= branch;
                                selectedBranch = new EverestInstaller.EverestBranch(selectedBranch.type, !selectedBranch.IsNonNative);
                                VersionCache.Invalidate();
                                Refresh();
                            }));
                        });
                    }),
                },
                new Group() {
                    Layout = {
                        Layouts.Row()
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 16 },
                    },
                    Children = {
                        new Label("Not enough?"),
                        new Button("Open Advanced view", b => {
                            Scener.PopFront();
                            Scener.Push<EverestInstallScene>();
                        }),
                    }
                },

            }
        };

    public override Element PostGenerate(Element root) {
        Config.Instance.SubscribeInstallUpdateNotify(i => selectedBranch = null);
        return base.PostGenerate(root);
    }

    private TimedCache<EverestInstaller.EverestVersion?>? VersionCache; 

    private EverestInstaller.EverestVersion? GetLatest() {
        if (VersionCache != null) return VersionCache.Value;
        VersionCache = new TimedCache<EverestInstaller.EverestVersion?>(
            new(0, 5, 0),
            sender => {
                EverestSimpleInstallScene scene = (EverestSimpleInstallScene)
                    (sender ?? throw new Exception("Cache returned null sender"));
                if (Config.Instance.Installation == null) return null;
                EverestInstaller.EverestBranch? branch = scene.selectedBranch ??
                                                         CurrentInstallBranch() ?? 
                                                         new EverestInstaller.EverestBranch(EverestInstaller.EverestBranch.ReleaseType.Stable, true); // Default to stable


                ICollection<EverestInstaller.EverestVersion> versions =
                    EverestInstaller.QueryEverestVersions();
                EverestInstaller.EverestVersion newest =
                    new EverestInstaller.EverestVersion() { version = 0 };

                foreach (EverestInstaller.EverestVersion version in versions) {
                    if (version.Branch.Equals(branch) && version.version > newest.version) {
                        newest = version;
                    }
                }

                if (newest.version == 0) {
                    return null;
                }

                return newest;
            }, this);
        Config.Instance.SubscribeInstallUpdateNotify(i => VersionCache.Invalidate());

        return VersionCache.Value;
    }

    private ManualCache<EverestInstaller.EverestBranch?>? CurrentBranchCache;


    private EverestInstaller.EverestBranch? CurrentInstallBranch() {
        if (CurrentBranchCache != null) return CurrentBranchCache.Value;
        CurrentBranchCache = new ManualCache<EverestInstaller.EverestBranch?>(
            sender => {
                if (Config.Instance.Installation == null) return null;
                return EverestInstaller.DeduceBranch(Config.Instance.Installation);
            }, this);
            
        Config.Instance.SubscribeInstallUpdateNotify(i => CurrentBranchCache.Invalidate());

        return CurrentBranchCache.Value;
    }
    
    public partial class UpdateButton : Button {
    
        public static readonly new Style DefaultStyle = new() {
            {
                StyleKeys.Update,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x00, 0x60, 0x10, 0x30) },
                    { Button.StyleKeys.Foreground, new Color(0xf0, 0x50, 0x50, 0x50) },
                    { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0x80) },
                    { Panel.StyleKeys.Shadow, 0.5f },
                }
            },
            {
                StyleKeys.HoveredUpdate,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x37, 0x6e, 0x40, 0x70) },
                    { Button.StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x28, 0x50, 0x2f, 0x70) },
                    { Panel.StyleKeys.Shadow, 0 },
                }
            },

            {
                StyleKeys.PressedUpdate,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x00, 0x60, 0x10, 0x50) },
                    { Button.StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0x60) },
                    { Panel.StyleKeys.Shadow, 0.35f },
                }
            },
    
            { Group.StyleKeys.Padding, 0 },
        };

        public Icon Icon;
        public Label Label;

        public bool IsUpdate;

        public UpdateButton(string icon, string text, Action<Button> cb)
            : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text) {
            Callback += cb;
        }

        public UpdateButton(IReloadable<Texture2D, Texture2DMeta> icon, string text)
            : base() {
            WH = new(72, 64);
            Cached = true;
            Icon iconi = new(icon) {
                ID = "icon",
                Style = {
                    { ImageBase.StyleKeys.Color, Style.GetLink(Button.StyleKeys.Foreground) },
                },
                Layout = {
                    Layouts.Left(0.5f, -0.5f),
                    Layouts.Top(8),
                }
            };
            Texture2DMeta icont = icon.Meta;
            if (icont.Width > icont.Height) {
                iconi.AutoW = 32;
            } else {
                iconi.AutoH = 32;
            }
            Icon = Add(iconi);

            Label = Add(new LabelSmall(text) {
                ID = "label",
                Style = {
                    { Label.StyleKeys.Color, Style.GetLink(Button.StyleKeys.Foreground) },
                },
                Layout = {
                    Layouts.Left(0.5f, -0.5f),
                    Layouts.Bottom(4),
                }
            });
        }
        
        public override Style.Key StyleState { 
            get {
                if (IsUpdate) {
                    return !Enabled ? StyleKeys.Disabled :
                        Pressed ? StyleKeys.PressedUpdate :
                        Hovered ? StyleKeys.HoveredUpdate :
                        StyleKeys.Update;
                } else {
                    return !Enabled ? StyleKeys.Disabled :
                        Pressed ? StyleKeys.Pressed :
                        Hovered ? StyleKeys.Hovered :
                        StyleKeys.Normal;
                }
            }
            
        }
        
        public new abstract partial class StyleKeys : Button.StyleKeys {
            protected StyleKeys(Secret secret) : base(secret) { }

            public static readonly Style.Key Update = new("Update");
            public static readonly Style.Key PressedUpdate = new("PressedUpdate");
            public static readonly Style.Key HoveredUpdate = new("HoveredUpdate");
        }    
    }
}