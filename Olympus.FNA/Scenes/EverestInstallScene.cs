using OlympUI;
using OlympUI.Modifiers;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Olympus {

    public class EverestInstallScene : Scene {
        
        public override bool Alert => true;

        public bool ShowAllEntries = false;

        private Func<SelectionBox, Task> selectionBoxGenerate = el => Task.CompletedTask;
        public override Element Generate() 
            => new Group() {
                ID = "base",
                Style = {
                    { Group.StyleKeys.Spacing, 16 },
                },
                Layout = {
                    Layouts.Fill(),
                    Layouts.Column()
                },
                Init = RegisterRefresh<Group>(el => {
                    if (Config.Instance.Installation == null) {
                        for (int i = 0; i < el.Children.Count; i++) {
                            Element child = el.Children[i];
                            if (child.ID == "boxGroup") {
                                el.Children.RemoveAt(i);
                                el.Children.Add(new Label("No installation selected!"));
                            }
                        }
                    }

                    return Task.CompletedTask;
                }),
                Children = {
                    new HeaderBig("Versions"),
                    new Group() {
                        ID = "boxGroup",
                        Layout = {
                            Layouts.Fill(1, 1, 0,166),
                        },
                        Children = {
                            new SelectionBox() {
                                ID = "box",
                                Layout = {
                                    Layouts.Fill(),
                                }, 
                                Clip = true, Cached = true, // TODO: (maybe find better solution?) This removes the shadow from the element but it is required for clip to work
                                Init = RegisterRefresh<SelectionBox>(async el1 => {
                                    selectionBoxGenerate = async el => {
                                        if (Config.Instance.Installation == null) {
                                            // Refuse to load
                                            return;
                                        }

                                        await UI.Run(() => {
                                            // remove old and add loading screen
                                            el.Content.Clear();
                                            el.Children.Add(
                                                new Group() {
                                                    Layout = { Layouts.Left(0.5f, -0.5f), Layouts.Row(8), },
                                                    Children = {
                                                        new Spinner() { Layout = { Layouts.Top(0.5f, -0.5f) }, },
                                                        new Label("Loading") { Layout = { Layouts.Top(0.5f, -0.5f) }, },
                                                    }
                                                });
                                        });
                                        ICollection<EverestInstaller.EverestVersion> versions =
                                            EverestInstaller.QueryEverestVersions();
                                        EverestInstaller.EverestBranch? branch =
                                            EverestInstaller.DeduceBranch(Config.Instance.Installation);
                                        if (branch != null && !ShowAllEntries) {
                                            List<EverestInstaller.EverestVersion> filteredVersions
                                                = versions.Where(version => version.Branch.IsImportant(branch))
                                                    .ToList();
                                            versions = filteredVersions;
                                        }

                                        (bool Modifiable, string Full, Version? Version, string? Framework,
                                                string? ModName,
                                                Version? ModVersion)
                                            = Config.Instance.Installation.ScanVersion(false);

                                        await UI.Run(() => {
                                                // Its needed to be careful when removing children since we only want to remove loading elements
                                                for (int i = 0; i < el.Children.Count; i++) {
                                                    Element child = el.Children[i];
                                                    if (child is not Group group) continue;
                                                    if (group.Children.Count == 0) continue;
                                                    if (group.Children[0] is not Spinner) continue;
                                                    el.Children.RemoveAt(i);
                                                    break;
                                                }

                                                foreach (EverestInstaller.EverestVersion version in versions) {
                                                    string desc = "";
                                                    if (version.description != "") {
                                                        desc += version.description;
                                                    }

                                                    if (version.author != "") {
                                                        desc += (version.description != "" ? " (by " : "")
                                                                + version.author
                                                                + (version.description != "" ? ")" : "");
                                                    }

                                                    el.Content.Add(new VersionEntry(
                                                        $"{version.Branch}: {version.version}" +
                                                        (version.version == ModVersion?.Minor ? " (Current)" : ""),
                                                        version.date.ToShortDateString() + " " +
                                                        version.date.ToShortTimeString(),
                                                        desc,
                                                        b => { },
                                                        version));
                                                }
                                            }
                                        );
                                    };
                                    await selectionBoxGenerate(el1);
                                })
                            },
                        }
                    },
                    new Group() {
                        Layout = {
                            Layouts.Row(),
                            Layouts.Fill(0, 0),
                        },
                        Children = {
                            new Button("Broken text", b => {
                                ShowAllEntries = !ShowAllEntries;
                                if (ShowAllEntries) {
                                    b.Text = "Reduce view";
                                } else {
                                    b.Text = "Show all entries";
                                }

                                SelectionBox box = b.GetParent().GetParent().GetChild<Group>("boxGroup").GetChild<SelectionBox>("box");
                                Task.Run(() => selectionBoxGenerate(box));
                            }) {
                                Init = RegisterRefresh<Button>(async el => {
                                    await UI.Run(() => {
                                        if (ShowAllEntries) {
                                            el.Text = "Reduce view";
                                        } else {
                                            el.Text = "Show all entries";
                                        }
                                    });
                                }),
                            }
                        }
                        
                    },
                    new Group() {
                        ID = "buttons",
                        Clip = false,
                        Layout = {
                            Layouts.Row(),
                            Layouts.Fill(1,0),
                        },
                        Style = {
                            { Group.StyleKeys.Spacing, 16 },
                        },
                        Children = {
                            new Button("Install", async button => {
                                ISelectionBoxEntry? boxEntry = button.GetParent()
                                    .GetParent()
                                    .GetChild("boxGroup")!
                                    .GetChild<SelectionBox>("box")
                                    .Selected;
                                if (boxEntry == null) return; // TODO: warn maybe?

                                if (Config.Instance.Installation == null) return; // TODO: make it impossible to happen

                                await foreach (var status in 
                                               EverestInstaller.InstallVersion(((VersionEntry) boxEntry).EverestVersion,
                                                   Config.Instance.Installation)) {
                                    Console.WriteLine(status.Text + " | " + status.Progress + " | " + status.CurrentStage);
                                }
                            }) {
                                Layout = {
                                    Layouts.Fill(0.5f, 0f, 8),
                                },
                            },
                            new Button("Uninstall", button => {
                            }) {
                                Layout = {
                                    Layouts.Fill(0.5f, 0f, 8),
                                },
                            },
                            // new Group() {
                            //     Clip = false,
                            //     Layout = {
                            //         Layouts.Fill(0.5f, 0f),
                            //     },
                            //     Children = {
                            //         new Button("Uninstall", button => {
                            //             
                            //         }),
                            //     }
                            // }
                        }
                    }
                }
            };

        private class VersionEntry : ISelectionBoxEntry {
            private readonly string title;
            private readonly string subTitle;
            private readonly string description;
            private readonly Action<bool> click;

            public readonly EverestInstaller.EverestVersion EverestVersion;

            public VersionEntry(string title, string subTitle, string description, Action<bool> click, EverestInstaller.EverestVersion everestVersion) {
                this.title = title;
                this.subTitle = subTitle;
                this.description = description; 
                this.click = click;
                this.EverestVersion = everestVersion;
            }
            public string GetTitle() {
                return title;
            }

            public IEnumerable<Element> GetContents() {
                if (subTitle != "") {
                    yield return new LabelSmall(subTitle) {
                        Modifiers = {
                            new OpacityModifier(0.5f)
                        }
                    }; 
                }
                if (description != "") {
                    yield return new LabelSmall(description);
                }
            }

            public void OnUpdate(bool b) 
                => click.Invoke(b);
            
        }
    }
}