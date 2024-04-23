using OlympUI;

namespace Olympus;

public class UnderConstructionScene : Scene {
    public override Element Generate()
        => new Group() {
            Layout = {
                Layouts.Column(),
            },
            Children = {
                new HeaderMedium("UNDER CONSTRUCTION!"),
                new Image(OlympUI.Assets.GetTexture("important.png")),
            },
        };
}