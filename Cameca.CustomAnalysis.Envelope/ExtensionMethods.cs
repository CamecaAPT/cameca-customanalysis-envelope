using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Cameca.CustomAnalysis.Envelope
{
    public static class ExtensionMethods
    {
        public static Color NextColor(this Random random)
        {
            Color returnColor = Color.FromRgb((byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255));
            return returnColor;
        }
    }
}
