using Microsoft.Extensions.DependencyInjection;

using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

using uSync.Exporter;
using uSync.Exporter.Extensions.Services;

namespace uSync.Complete.Exporter.Extensions;

[ComposeAfter(typeof(uSyncExportComposer))]
internal class ExporterExtensionsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<ISyncExporterStepService, SyncExporterStepService>();
    }
}
