using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using Prism.Events;
using Prism.Services.Dialogs;

namespace Cameca.CustomAnalysis.Envelope.Core;

internal class EnvelopeNodeMenuFactory : LegacyAnalysisMenuFactoryBase
{
    public EnvelopeNodeMenuFactory(IEventAggregator eventAggregator, IDialogService dialogService)
        : base(eventAggregator, dialogService)
    {
    }

    protected override INodeDisplayInfo DisplayInfo => EnvelopeNode.DisplayInfo;
    protected override string NodeUniqueId => EnvelopeNode.UniqueId;
    public override AnalysisMenuLocation Location { get; } = AnalysisMenuLocation.Analysis;
}