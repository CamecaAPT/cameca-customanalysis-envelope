using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;

namespace Cameca.CustomAnalysis.Envelope.Core;

[DefaultView(EnvelopeViewModel.UniqueId, typeof(EnvelopeViewModel))]
internal class EnvelopeNode : LegacyCustomAnalysisNodeBase<EnvelopeAnalysis, EnvelopeOptions>
{
    public const string UniqueId = "Cameca.CustomAnalysis.Envelope.EnvelopeNode";
    
    public static INodeDisplayInfo DisplayInfo { get; } = new NodeDisplayInfo("Envelope");

    public EnvelopeNode(IStandardAnalysisNodeBaseServices services, EnvelopeAnalysis analysis)
        : base(services, analysis)
    {
    }

    protected override void OnAdded(NodeAddedEventArgs eventArgs)
    {
        base.OnAdded(eventArgs);
        Analysis.ID = eventArgs.NodeId;
    }
}