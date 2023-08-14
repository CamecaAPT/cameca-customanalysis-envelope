using Cameca.CustomAnalysis.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cameca.CustomAnalysis.Envelope;

public static class ExtensionUtils
{
    public static Dictionary<string, byte> GetIonTypes(IIonData ionData, bool startAt0 = true)
    {
        Dictionary<string, byte> ionTypes = new();

        byte count = startAt0 ? (byte)0 : (byte)1;
        foreach (var ionType in ionData.Ions)
            ionTypes.Add(ionType.Name, count++);

        return ionTypes;
    }


}
