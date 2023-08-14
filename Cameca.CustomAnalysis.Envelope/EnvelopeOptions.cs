using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Prism.Mvvm;

namespace Cameca.CustomAnalysis.Envelope;

public class EnvelopeOptions : BindableBase
{
    private HashSet<string> ionsOfInterest = new();
    [Display(Name = "Ions of Interest", Description = "Ions to cluster and form envelopes around")]
    public HashSet<string> IonsOfInterest
    {
        get => ionsOfInterest;
        set => SetProperty(ref ionsOfInterest, value);
    }

    private float atomSeparation;
    [Display(Name = "Max Atom Separation", Description = "[.2 - 5.0] nm (dmax)")]
    public float AtomSeparation
    {
        get => atomSeparation;
        set => SetProperty(ref atomSeparation, value);
    }

    private int minAtomsPerCluster;
    [Display(Name = "Minimum atoms per cluster")]
    public int MinAtomsPerCluster
    {
        get => minAtomsPerCluster;
        set => SetProperty(ref minAtomsPerCluster, value);
    }

    private float gridResolution;
    [Display(Name = "Grid resoltuion", Description = "[.05 - 5.0] nm")]
    public float GridResolution
    {
        get => gridResolution;
        set => SetProperty(ref gridResolution, value);
    }

    private bool fillInGrid;
    [Display(Name = "Fill in Grid?")]
    public bool FillInGrid
    {
        get => fillInGrid;
        set => SetProperty(ref fillInGrid, value);
    }
}