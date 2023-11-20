using OlympUI;
using System;
using System.IO;

namespace Olympus; 

public class SetupLoennShortcutLinuxScene : Scene {
    public override bool Alert => true;

    public override Element Generate() 
        => new Group() {
            ID = "SetupLoennShortcutLinuxScene",
            Style = {
                { Group.StyleKeys.Spacing, 16 },
            },
            Layout = {
                Layouts.Fill(),
            },
            Children = {
                // new HeaderBig("Setup Desktop Entry"),
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
                    Children = {
                        new HeaderMedium("Setup Desktop Entry") {
                            Layout = {
                                Layouts.Left(0.5f, -0.5f),
                            },
                        },
                        new Label("Would you like to create a Desktop Entry for Lönn?") {
                            Wrap = true,
                            Layout = {
                                Layouts.Left(0.5f, -0.5f),
                            },
                        },
                        new Group {
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
                                    CreateDesktopEntry();
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
                        }
                    }
                }
            }
        };

    private static void CreateDesktopEntry() {
        string desktopEntryContent =
            $"""
            [Desktop Entry]
            Type=Application
            Path={OlympUI.Assets.GetPath("love")}
            Exec={OlympUI.Assets.GetPath("love/find-love.sh")} {Path.Combine(Config.Instance.LoennInstallDirectory, "Lönn.love")}
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
            desktopPath = Path.Combine(home, ".local/share", desktopName);
            iconPath = Path.Combine(home, ".local/share", iconName);
        } else {
            return;
        }

        if (File.Exists(desktopPath)) {
            AppLogger.Log.Error($"Lönn Desktop Entry: {desktopPath} already exists!");
            return;
        }
        if (File.Exists(iconPath)) {
            AppLogger.Log.Error($"Lönn Desktop Entry: {iconPath} already exists!");
            return;
        }
        
        string? dir = Path.GetDirectoryName(desktopPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        dir = Path.GetDirectoryName(iconPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        
        File.WriteAllText(desktopPath, desktopEntryContent);
        File.Copy(OlympUI.Assets.GetPath($"desktop/Lönn.png"), iconPath);
        Config.Instance.LoennLinuxDesktopEntry = desktopPath;
        Config.Instance.LoennLinuxDesktopIcon = iconPath;
        Config.Instance.Save();
    }
}

public partial class CenteredButton : Button {
    public CenteredButton(string text, Action<Button> cb) : base(text, cb) {
        GetChild<Label>().Layout.Add(Layouts.Left(0.5f, -0.5f));
    }
}