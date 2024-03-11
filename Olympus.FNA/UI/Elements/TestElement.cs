using Microsoft.Xna.Framework;

namespace OlympUI ;

    public partial class TestElement : Element {
        protected override bool IsComposited => false;

        protected Style.Entry StyleTests = new(0);

        public TestElement() {
            WH = new Point(100, 100);
        }

    }