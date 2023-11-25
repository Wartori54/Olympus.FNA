using OlympUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Olympus;

public abstract class SetupLoennShortcutSceneBase : Scene {
    public override bool Alert => true;
    public override bool Locked => true;

    private readonly string title;
    private readonly IEnumerable<string> descriptions;
    
    protected SetupLoennShortcutSceneBase(string title, IEnumerable<string> descriptions) {
        this.title = title;
        this.descriptions = descriptions;
    }
    
    public override Element Generate() 
        => new Group() {
            ID = "SetupLoennShortcutScene",
            Style = {
                { Group.StyleKeys.Spacing, 16 },
            },
            Layout = {
                Layouts.Fill(),
            },
            Children = {
                new Panel {
                    Layout = {
                        Layouts.Fill(0.5f, 0.4f),
                        Layouts.Left(0.5f, -0.5f),
                        Layouts.Top(0.5f, -0.5f),
                        Layouts.Column(16, false),
                    },
                    Style = {
                        { Panel.StyleKeys.Padding, 16 }
                    },
                    Init = el => UI.Run(() => {
                        el.DisposeChildren();
                        el.Add(new HeaderMedium(title) {
                            Layout = {
                                Layouts.Left(0.5f, -0.5f),
                            },
                        });
                        var descEle = new Group {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                                Layouts.Column(),
                            }
                        };
                        foreach (string desc in descriptions) {
                            descEle.Add(new Label(desc) {
                                Wrap = true, 
                                Layout = {
                                    Layouts.Left(0.5f, -0.5f),
                                },
                            });
                        }
                        el.Add(descEle);
                        el.Add(new Group {
                            Layout = {
                                Layouts.Fill(1.0f, 0.0f),
                                Layouts.Bottom(8),
                                Layouts.Row()
                            },
                            Style = {
                                { Group.StyleKeys.Spacing, 16 }
                            },
                            Children = {
                                new CenteredButton("Yes", b => { 
                                    CreateShortcut();
                                    Scener.PopAlert();
                                }) {
                                    Layout = {
                                        Layouts.Fill(0.5f, 0.0f, 16 / 2, 0),
                                    },
                                },
                                new CenteredButton("No", b => Scener.PopAlert()) {
                                    Layout = {
                                        Layouts.Fill(0.5f, 0.0f, 16 / 2, 0),
                                    },
                                },
                            }
                        });
                    })
                },
            }
        };

    protected abstract void CreateShortcut();
    
    public partial class CenteredButton : Button {
        public CenteredButton(string text, Action<Button> cb) : base(text, cb) {
            GetChild<Label>().Layout.Add(Layouts.Left(0.5f, -0.5f));
        }
    }
}

//TODO: Windows desktop shortcuts / start menu entries
public class SetupLoennShortcutSceneWindows : SetupLoennShortcutSceneBase {
    public SetupLoennShortcutSceneWindows() 
        : base("TODO", new[] { "TODO" }) 
    {
        throw new NotImplementedException();
    }
    protected override void CreateShortcut() {
        throw new NotImplementedException();
    }
}

//TODO: MacOS shortcuts
public class SetupLoennShortcutSceneMacOS : SetupLoennShortcutSceneBase {
    public SetupLoennShortcutSceneMacOS() 
        : base("TODO", new[] { "TODO" }) 
    {
        throw new NotImplementedException();
    }
    protected override void CreateShortcut() {
        throw new NotImplementedException();
    }
}

public class SetupLoennShortcutSceneLinux : SetupLoennShortcutSceneBase {
    public SetupLoennShortcutSceneLinux() 
        : base("Setup Desktop Entry", new[] { "Would you like to create a Desktop Entry for Lönn?", "This allows you to start Lönn from your application launcher." })
    { }

    protected override void CreateShortcut() {
        if (Config.Instance.LoennInstallDirectory == null) {
            AppLogger.Log.Warning("Tried to create desktop entry while install location is null");
            return;
        }
        
        string desktopEntryContent =
            $"""
            [Desktop Entry]
            Type=Application
            Path={OlympUI.Assets.GetPath("loenn")}
            Exec=./find-love.sh {Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.love")}
            Name=Lönn
            Keywords=Loenn;Lonn
            GenericName=Celeste Map Editor
            Categories=Utility;
            Icon={App.Name}_Lönn
            Comment=A fast Celeste map editor.
            """;
        
        string desktopName = $"applications/{App.Name}_Lönn.desktop";
        string desktopPath;
        string iconName = $"icons/hicolor/256x256/apps/{App.Name}_Lönn.png";
        string iconPath;

        string? data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(data)) {
            desktopPath = Path.Combine(data, desktopName);
            iconPath = Path.Combine(data, iconName);
        } else if (!string.IsNullOrEmpty(home)) {
            desktopPath = Path.Combine(home, ".local", "share", desktopName);
            iconPath = Path.Combine(home, ".local", "share", iconName);
        } else {
            return;
        }

        if (File.Exists(desktopPath)) {
            AppLogger.Log.Error($"Lönn Desktop Entry: {desktopPath} already exists!");
            MetaNotificationScene.PushNotification(new Notification{ Message = $"Lönn Desktop Entry: {desktopPath} already exists!", Level = Notification.SeverityLevel.Warning });
            return;
        }
        if (File.Exists(iconPath)) {
            AppLogger.Log.Error($"Lönn Desktop Entry: {iconPath} already exists!");
            MetaNotificationScene.PushNotification(new Notification{ Message = $"Lönn Desktop Entry: {iconPath} already exists!", Level = Notification.SeverityLevel.Warning });
            return;
        }
        
        string? dir = Path.GetDirectoryName(desktopPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        dir = Path.GetDirectoryName(iconPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        
        File.WriteAllText(desktopPath, desktopEntryContent);
        File.Copy(OlympUI.Assets.GetPath($"loenn/Loenn.png")!, iconPath);
        
        // Let desktop environments know that desktop entries were changed
        try {
            Process.Start("update-desktop-database", Path.GetDirectoryName(desktopPath)!);
        } catch {
            // ignored
        }

        Config.Instance.LoennLinuxDesktopEntry = desktopPath;
        Config.Instance.LoennLinuxDesktopIcon = iconPath;
        Config.Instance.Save();
    }
}
