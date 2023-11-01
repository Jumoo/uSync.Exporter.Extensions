using Microsoft.AspNetCore.Mvc;

using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.BackOffice.Controllers;
using Umbraco.Cms.Web.Common.Attributes;
using Umbraco.Extensions;

using uSync.Complete.Exporter.Extensions.Extensions;
using uSync.Core.Dependency;
using uSync.Core.Sync;
using uSync.Expansions.Core;
using uSync.Expansions.Core.Queue.Services;
using uSync.Exporter;
using uSync.Exporter.Extensions.Services;

namespace uSync.Complete.Exporter.Extensions.Controller;


/// <summary>
///  Example API Endpoint commands.
/// </summary>
/// <remarks>
///   this controller shows you how you might call export/import for the uSyncExporter.
///   exports are stored in the "uSync/Exports" folder 
///    
///   umbraco/backoffice/usync/SyncExporterStepApi/CreateContentExport?contentId=#guid#
///   umbraco/backoffice/usync/SyncExporterStepApi/CreateExport?id=#guid#
///   umbraco/backoffice/usync/SyncExporterStepApi/ListPacks
///   umbraco/backoffice/usync/SyncExporterStepApi/Import?id=#guid#
/// </remarks>

[PluginController("uSync")]
public class SyncExporterStepApiController : UmbracoAuthorizedApiController
{
    private readonly IContentService _contentService;
    private readonly ISyncExporterStepService _syncExporterStepService;
    private readonly ISyncLogEntryService _logEntryService;

    public SyncExporterStepApiController(
        IContentService contentService,
        ISyncLogEntryService logEntryService,
        ISyncExporterStepService syncExporterStepService)
    {
        _contentService = contentService;
        _logEntryService = logEntryService;
        _syncExporterStepService = syncExporterStepService;
    }

    /// <summary>
    ///  a simple example of how to create an export programmatically. 
    /// </summary>
    [HttpGet]
    public bool CreateContentExport(Guid contentId, bool includeChildren = true, bool includeDependencies = true)
    {
        var contentItem = _contentService.GetById(contentId);
        if (contentItem == null) return false;

        //
        // the dependency flags, dictate what extra things get included 
        // when the dependencies are calculated for an item. 
        //
        // for example if you want media add - DependencyFlags.IncludeMedia
        //
        var flags = DependencyFlags.None;
        if (includeChildren) flags |= DependencyFlags.IncludeChildren;
        if (includeDependencies) flags |= DependencyFlags.IncludeDependencies;

        var baseSyncItem = new SyncItem()
        {
            Udi = contentItem.GetUdi(),
            Flags = flags,
            Name = contentItem.Name,
        };

        var request = new ExporterRequest
        {
            Id = Guid.NewGuid(),
            Name = contentItem.Name,
            Request = new SyncPackRequest
            {
                Items = new[] { baseSyncItem }
            }
        };

        //
        // an export happens over multiple requests, in v12, we can push
        // the request to the uSyncQueue and this will churn through the
        // process until is completed. 
        //

        // using the queue log service means the job appears in the log,
        // so you can see it happen. (you can use the QueueService if you
        // don't want anything appearing in the audit log. 
        // 
        _logEntryService.QueueExportJob(ExportMode.Export, request);

        return true;
    }

    /// <summary>
    ///  Create an export of a content page (and children) inline.
    /// </summary>
    /// <remarks>
    ///  This call doesn't send to the queue, so 
    ///  the process will happen within this single request.
    ///  
    ///  if the request is long running, then you run the risk
    ///  of the call timing out. 
    /// </remarks>
    /// <param name="id">Guid id for the content.</param>
    /// <returns></returns>
    [HttpGet]
    public Guid CreateExport(Guid id)
    {
        var contentItem = _contentService.GetById(id);
        if (contentItem == null) return Guid.Empty;

        var syncItem = new SyncItem
        {
            Name = contentItem.Name,
            Flags = DependencyFlags.IncludeChildren,
            Udi = contentItem.GetUdi()
        };

        var request = new ExporterRequest
        {
            Id = Guid.NewGuid(),
            Name = contentItem.Name,
            Request = new SyncPackRequest
            {
                Items = new[] { syncItem }
            }
        };

        var result = _syncExporterStepService.ProcessBatch(ExportMode.Export, request);
        return result.Id;

    }

    [HttpGet]
    public bool Import(Guid id)
    {
        var result = _syncExporterStepService.ProcessBatch(ExportMode.Import, 
            new ExporterRequest {
                Id = id,
                Request = new SyncPackRequest()
            });

        return result.ExportComplete; 
    }

    [HttpGet]
    public IEnumerable<string> ListPacks()
        => _syncExporterStepService.ListPacks();
}
