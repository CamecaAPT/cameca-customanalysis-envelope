using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using Prism.Ioc;
using Prism.Modularity;

namespace Cameca.CustomAnalysis.Envelope.Core;

/// <summary>
/// Public <see cref="IModule"/> implementation is the entry point for AP Suite to discover and configure the custom analysis
/// </summary>
public class EnvelopeModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        containerRegistry.AddCustomAnalysisUtilities(options => options.UseLegacy = true);
#pragma warning restore CS0618 // Type or member is obsolete

        containerRegistry.Register<EnvelopeAnalysis>();
        containerRegistry.Register<object, EnvelopeNode>(EnvelopeNode.UniqueId);
        containerRegistry.RegisterInstance(EnvelopeNode.DisplayInfo, EnvelopeNode.UniqueId);
        containerRegistry.Register<IAnalysisMenuFactory, EnvelopeNodeMenuFactory>(nameof(EnvelopeNodeMenuFactory));
        containerRegistry.Register<object, EnvelopeViewModel>(EnvelopeViewModel.UniqueId);
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        var extensionRegistry = containerProvider.Resolve<IExtensionRegistry>();
        extensionRegistry.RegisterAnalysisView<LegacyCustomAnalysisView, EnvelopeViewModel>(AnalysisViewLocation.Top);
    }
}
