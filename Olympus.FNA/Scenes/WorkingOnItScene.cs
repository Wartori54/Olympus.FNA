using OlympUI;
using System.Threading.Tasks;

namespace Olympus {

    public class WorkingOnItScene : Scene {
        public override bool Locked => true;

        public override Element Generate()
            => new Group() {
                ID = "base",
                Init = RegisterRefresh<Group>(async el => {
                    await Task.Delay(5000);
                    await UI.Run(() => {
                        Scener.PopFront();
                        Scener.Set<HomeScene>();
                    });
                })
            };
        
    }
}