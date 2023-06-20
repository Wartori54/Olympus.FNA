using OlympUI;
using OlympUI.Modifiers;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Olympus {

    public class EverestInstallScene : Scene {
        
        public override bool Alert => true;
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
                new HeaderBig("Versions"),
                new Group() {
                    Layout = {
                        Layouts.Fill(1, 1, 0, (20 + 8)*2*2),
                    },
                    Children = {
                        new SelectionBox() {
                            Layout = {
                                Layouts.Fill(),
                            }, Clip = true, Cached = true, //TODO: (maybe find better solution?) This removes the shadow from the element but it is required for clip to work
                            Init = RegisterRefresh<SelectionBox>(async el => {
                                ICollection<EverestInstaller.EverestVersion>? versions = EverestInstaller.QueryEverestVersions();
                                if (versions == null) return;
                                await UI.Run(() => {
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
                                        el.Content.Add(new VersionEntry($"{version.Branch}: {version.version}",
                                            version.date.ToShortDateString() + " " + version.date.ToShortTimeString(),
                                            desc, 
                                            b => {}));
                                    }
                                });

                            })
                        },
                    }
                },
                new Group() {
                    Clip = false,
                    Layout = {
                        Layouts.Row(),
                        Layouts.Fill(1,0),
                    },
                    Style = {
                        { Group.StyleKeys.Spacing, 16 },
                    },
                    Children = {
                        // new Group() {
                        //     Clip = false,
                        //     Layout = {
                        //         Layouts.Fill(0.5f, 0f),
                        //     },
                        //     Children = {
                        //         new Button("Install", button => {
                        //         }) {
                        //             Layout = {
                        //                                             Layouts.Fill(0.5f, 0f),
                        //                                         },
                        //         },
                        //     }
                        // },
                        new Button("Install", button => {
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

        public override Element PostGenerate(Element root) {
            EverestInstaller.QueryEverestVersions();
            return base.PostGenerate(root);
        }

        private class VersionEntry : ISelectionBoxEntry {
            private readonly string title;
            private readonly string subTitle;
            private readonly string description;
            private readonly Action<bool> click;

            public VersionEntry(string title, string subTitle, string description, Action<bool> click) {
                this.title = title;
                this.subTitle = subTitle;
                this.description = description; 
                this.click = click;
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