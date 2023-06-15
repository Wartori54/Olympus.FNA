using OlympUI;
using System;
using System.Collections.Generic;

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
                            },
                            Content = {
                                new VersionEntry("v1", "a cool everest version", b => 
                                    Console.WriteLine($"woo i clicked v1 {b}")),
                                 new VersionEntry("v2", "a cooler everest version", b => 
                                     Console.WriteLine($"woo i clicked v2 {b}")),                                   
                                new VersionEntry("v3", "a coolest everest version", b => 
                                    Console.WriteLine($"woo i clicked v3 {b}")),
                            }
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

        private class VersionEntry : ISelectionBoxEntry {
            private readonly string title;
            private readonly string description;
            private readonly Action<bool> click;

            public VersionEntry(string title, string description, Action<bool> click) {
                this.title = title;
                this.description = description; 
                this.click = click;
            }
            public string GetTitle() {
                return title;
            }

            public IEnumerable<Element> GetContents() {
                return new List<Element> {new LabelSmall(description)};
            }

            public void OnUpdate(bool b) 
               => click.Invoke(b);
            
        }
    }
}