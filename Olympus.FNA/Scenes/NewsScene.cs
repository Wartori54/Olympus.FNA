using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using Olympus.API;
using Olympus.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Olympus; 

public class NewsScene : Scene {
    public override bool Alert => true;

    // This scene is not built in the "normal" way since it is fully re-generated each time, so it doesn't make
    // any sense to follow the "intended" format, and this removes some indentation sooooo
    public override Element Generate()
        => new Group() {
            Layout = {
                Layouts.Fill(1,1),
                Layouts.Column(),
            },
            Style = {
                { Group.StyleKeys.Spacing, 8 },
            },
            Children = { 
                new HeaderBig("News"),
                // Group with a single element to make the scrollbox clip before the header
                new Group() {
                    Layout = {
                        Layouts.Fill(1,1),
                    },
                    Clip = true,
                    Children = {
                        new ScrollBox() {
                            Layout = {
                                Layouts.Fill(1, 1),
                            },
                            Clip = true,
                            Content = new Group() {
                                Layout = {
                                    Layouts.Fill(1, 0),
                                    Layouts.Row(),
                                },
                                Style = {
                                    { Group.StyleKeys.Spacing, 8 },
                                },
                                Init = RegisterRefresh<Group>(GenerateNews),
                            }
                        },
                    }
                }
            }
        };

    public async Task GenerateNews(Group el) {
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
        
        IEnumerable<INewsEntry> news;
        try {
            news = App.NewsManager.GetDefault()
                .PollAll();
        } catch (Exception ex) {
            AppLogger.Log.Error("Failed to obtain news");
            AppLogger.Log.Error(ex, ex.Message);
            await UI.Run(() => {
                el.Add(new Group() {
                    Layout = { Layouts.Fill(1, 0), },
                    Children = {
                        new HeaderMedium("Failed to obtain news!") { Layout = { Layouts.Left(0.5f, -0.5f), } }
                    }
                });
            });
            return;
        }

        INewsEntry[] newsArray = news.ToArray();

        // TODO: config for this
        const int columnCount = 3;
        Group[] columns = new Group[columnCount];
        for (int i = 0; i < columnCount; i++) {
            columns[i] = new Group() {
                Layout = {
                    Layouts.Fill(1f/columnCount, 0, 8),
                    Layouts.Column(),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 8 },
                }
            };
        }
        
        await UI.Run(() => {
            el.DisposeChildren();
            foreach (Group group in columns) {
                el.Add(group);
            }
        });

        Task[] imageTasks = new Task[newsArray.Length];
        for (int i = 0; i < newsArray.Length; i++) {
            (Panel newsPanel, Task imageTask) = CreateNewsPanel(newsArray[i]);
            imageTasks[i] = imageTask;

            Group currentGroup = columns[i % columnCount];
            await UI.Run(() => currentGroup.Add(newsPanel));
        }

        await Task.WhenAll(imageTasks);
    }

    public static (Panel, Task) CreateNewsPanel(INewsEntry newsEntry) {
        Panel newsPanel = new Panel() {
            Layout = {
                Layouts.Fill(1f, 0), 
                Layouts.Column(), 
            },
            Children = {
                new HeaderSmall(newsEntry.Title) { Wrap = true },
                new Group() { ID = "ImageGroup", 
                    Layout = { Layouts.Fill(1, 0) },
                    Children = {
                        new Group() {
                            ForceWH = new Point(100, 100)
                        }
                    }
                },
                new Label(newsEntry.Text) { Wrap = true, },
            },
            Modifiers = {
                // new FadeInAnimation(0.09f).WithDelay(0.05f).With(Ease.SineInOut),
                new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f).With(Ease.SineIn),
                // new ScaleInAnimation(0.9f, 0.125f).WithDelay(0.05f).With(Ease.SineOut),
            }
        };

        foreach (INewsEntry.ILink link in newsEntry.Links) {
            newsPanel.Add(new HomeScene.IconButton("icons/browser",
                link.Text,
                _ => URIHelper.OpenInBrowser(link.Url)));
        }
        
        Element group = newsPanel["ImageGroup"];
        Task imageTask = Task.Run(async () => {
            IReloadable<Texture2D, Texture2DMeta>? tex = null;
            foreach (string img in newsEntry.Images) {
                tex = await App.Instance.Web.GetTextureUnmipped(img);
                if (tex != null) break;
            }

            if (tex == null) return;

            await UI.Run(() => {
                group.DisposeChildren();
                group.Add(new Image(tex) {
                    Modifiers = {
                        new FadeInAnimation(0.6f).With(Ease.QuadOut),
                        // new ScaleInAnimation(1.05f, 0.5f).With(
                        //     Ease.QuadOut)
                    },
                    Layout = {
                        ev => {
                            Image img = (Image) ev.Element;
                            img.AutoW = Math.Min(group.W, 400);
                            img.X = group.W / 2 - img.W / 2;
                        },
                    }
                });

            });
        });

        return (newsPanel, imageTask);

    }
    
    

}