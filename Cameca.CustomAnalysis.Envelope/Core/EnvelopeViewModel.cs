using System;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;

namespace Cameca.CustomAnalysis.Envelope.Core;

internal class EnvelopeViewModel
    : LegacyCustomAnalysisViewModelBase<EnvelopeNode, EnvelopeAnalysis, EnvelopeOptions>
{
    public const string UniqueId = "Cameca.CustomAnalysis.Envelope.EnvelopeViewModel";

    public EnvelopeViewModel(IAnalysisViewModelBaseServices services, Func<IViewBuilder> viewBuilderFactory)
        : base(services, viewBuilderFactory)
    {
    }
}