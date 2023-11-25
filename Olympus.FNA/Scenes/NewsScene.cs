using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using OlympUI.Modifiers;
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
        // 400 here is just arbitrary, it looks good enough
        int maxImageWidth = 400;
        int maxImageHeight = (int) (maxImageWidth * (9f / 16f)); // default to 16:9 aspect ratio since most imgs are shaped like this
        Panel newsPanel = new Panel() {
            Layout = {
                Layouts.Fill(1f, 0), 
                Layouts.Column(), 
            },
            Modifiers = {
                new OffsetInAnimation(new Vector2(0f, 10f), 0.15f).WithDelay(0.05f).With(Ease.SineIn),
            }
        };
        // Adding children after creation is mandatory here because of the need to access `newsPanel` in its children
        newsPanel.Children.Add(new HeaderSmall(newsEntry.Title) { Wrap = true });
        newsPanel.Children.Add(new Panel() { 
            ID = "ImagePanel", 
            Clip = true,
            Layout = {
                Layouts.Left(0.5f, -0.5f),
            },
            Style = { { Group.StyleKeys.Padding, 0 }, { Panel.StyleKeys.Shadow, 0 }, },
            MinWH = new Point(10, 10),
            Children = {
                new Group() { // Fake group to make an empty space
                    Layout = {
                        ev => {
                            int width = Math.Min(newsPanel.W-newsPanel.Padding*2, maxImageWidth);
                            ev.Element.W = width;
                            ev.Element.H = maxImageHeight;
                        }
                    },
                    Children = {
                        new Label("Loading...") { // TODO: some animation for when an image is loading
                            Layout = {
                                Layouts.Top(0.5f, -0.5f),
                                Layouts.Left(0.5f, -0.5f),
                            },
                            Modifiers = {
                                new OpacityModifier(0.5f),
                            }
                        }
                    }
                }
            }
        });
        newsPanel.Children.Add(new Label(newsEntry.Text) { Wrap = true, });

        foreach (INewsEntry.ILink link in newsEntry.Links) {
            newsPanel.Add(new HomeScene.IconButton("icons/browser",
                link.Text,
                _ => URIHelper.OpenInBrowser(link.Url)));
        }
        
        Element imagePanel = newsPanel["ImagePanel"];
        Task imageTask = Task.Run(async () => {
            IReloadable<Texture2D, Texture2DMeta>? tex = null;
            foreach (string img in newsEntry.Images) {
                tex = await App.Instance.Web.GetTextureUnmipped(img);
                if (tex != null) break;
            }

            if (tex == null) return;

            await UI.Run(() => {
                Image image = new Image(tex) {
                    Modifiers = {
                        new FadeInAnimation(0.6f).With(Ease.QuadOut),
                    },
                    Layout = {
                        ev => {
                            Image img = (Image) ev.Element;
                            img.AutoH = maxImageHeight;
                            img.X = imagePanel.W/2 - img.AutoW/2;

                        },
                    }
                };
                imagePanel.DisposeChildren();
                imagePanel.Layout.Add(ev => {
                    int width = Math.Min(newsPanel.W-newsPanel.Padding*2, image.W);
                    ev.Element.W = width; 
                    ev.Element.H = maxImageHeight;
                });
                // alright let me be clear here
                // WHYY THE FRICK DOES A HECKING GROUP NEED TO BE HERE IN ORDER FOR THE IMG TO BE CENTERED
                imagePanel.Add(new Group() {image});

            });
        });

        return (newsPanel, imageTask);

    }
    
    

}