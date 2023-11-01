using System;

using Newtonsoft.Json;

using uSync.Complete.Exporter.Extensions.Model;
using uSync.Expansions.Core.Queue.Models;
using uSync.Expansions.Core.Queue.Services;
using uSync.Exporter;
using uSync.Exporter.Extensions.Services;

namespace uSync.Complete.Exporter.Extensions.Extensions;
internal static class ExporterQueueExtensions
{
    public static QueuedItem Enqueue(this ISyncQueueService queueService, ExportMode exportMode, ExporterRequest request)
        => Enqueue(queueService, exportMode, request, DateTime.Now, true);

    public static QueuedItem Enqueue(this ISyncQueueService queueService, ExportMode exportMode, ExporterRequest request, DateTime scheduled, bool interactive)
        => queueService.Enqueue(request.ToQueuedItem(exportMode, scheduled, interactive));

    public static QueuedItem ToQueuedItem(this ExporterRequest request, ExportMode exportMode, DateTime scheduled, bool interactive)
    {
        var queuedRequest = new QueuedExporterRequest
        {
            Request = request,
            Mode = exportMode,
        };

        return new QueuedItem
        {
            ReferenceKey = request.Id,
            Action = SyncExporterStepService.Action,
            Data = JsonConvert.SerializeObject(queuedRequest),
            Name = request.Name,
            Priority = 1,
            Submitted = scheduled,
            Scheduled = scheduled,
            User = "",
            Interactive = interactive          
        };
    }

    public static void QueueExportJob(this ISyncLogEntryService queueEntryService, ExportMode mode, ExporterRequest request)
    {
        queueEntryService.AddAndQueue(
            request.ToQueuedItem(mode, DateTime.Now, true), LogEntrtyStatus.New, true);
    }

}
