using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace UwpMandelbrot {
    static class Extensions {
        public static int ToInt(this Color color) {
            return (color.A << 24) | (color.B << 16) | (color.G << 8) | color.R;
        }
    }
}
