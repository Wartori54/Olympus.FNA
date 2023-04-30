using OlympUI;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeFileDialogSharp;

namespace Olympus {
    public class InstallManagerScene : Scene {

        public override bool Alert => true;

#pragma warning disable CS8618 // Generate runs before anything else.
        private Group InstallsManaged;
        private Group InstallsFound;
        private Group InstallsManual;
#pragma warning restore CS8618

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
                                        Layouts.Fill(1, 0),
                                        Layouts.Column()
                                    },
                                    Children = {

                                        new Group() {
                                            Style = {
                                                { Group.StyleKeys.Spacing, 8 },
                                            },
                                            Layout = {
                                                Layouts.Fill(1, 0, 0, 8),
                                                Layouts.Column(),
                                            },
                                            Children = {
                                                new HeaderSmall("Managed by Olympus"),
                                                new Label("TODO"),
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
            App.Instance.FinderManager.Updated += UpdateInstallList;
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

        private void AddManualInstallation() { // TODO: Save to disk

            string filter;

            if (PlatformHelper.Is(Platform.Linux)) {
                filter = "exe,bin.x86,bin.x86_64";
            } else if (PlatformHelper.Is(Platform.MacOS)) {
                filter = "app,exe,bin.osx";
            } else { // default to windows
                filter = "exe";
            }

            DialogResult result = Dialog.FileOpen(filter);

            if (!result.IsOk) {
                if (result.IsError) {
                    Console.WriteLine("Error while choosing file: " + result.ErrorMessage);
                }
                return;
            }

            Installation newInstall = new("manual", "Manual installation", result.Path);

            if (!newInstall.FixPath()) { // Ignore for now
                Console.WriteLine("Bad path: " + newInstall.Root);
                return; // TODO: Give user a warning for invalid installation
            }
            
            App.Instance.FinderManager.AddManualInstall(newInstall);
            UpdateInstallList(FinderUpdateState.Manual, App.Instance.FinderManager.Added, InstallList.Added);
        }

        private Panel CreateEntry(Installation install) {
            Label labelVersion;
            Panel panel = new() {
                Data = {
                    { "Installation", install },
                },
                Clip = true,
                Layout = {
                    Layouts.Fill(1, 0),
                    Layouts.Column(),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 0 },
                },
                Children = {
                    new HeaderSmall(install.Name) {
                        Wrap = true,
                    },
                    (labelVersion = new Label("Scanning...")),
                    new LabelSmall(install.Root),
                }
            };

            Task.Run(() => {
                (bool Modifiable, string Full, Version? Version, string? Framework, string? ModName, Version? ModVersion) version = install.ScanVersion(true);
                UI.Run(() => {
                    labelVersion.Text = version.Full;
                });
            });

            return panel;
        }

    }

}
