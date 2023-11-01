using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;

using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Extensions;

using uSync.BackOffice;
using uSync.BackOffice.Hubs;
using uSync.Complete.Exporter.Extensions.Extensions;
using uSync.Complete.Exporter.Extensions.Model;
using uSync.Expansions.Core.Queue.Models;
using uSync.Expansions.Core.Queue.Processors;
using uSync.Expansions.Core.Queue.Services;
using uSync.Exporter;
using uSync.Exporter.Extensions.Services;

namespace uSync.Complete.Exporter.Extensions;
internal class ExporterQueueProcessor : ISyncQueueProcessor
{
    public string Name => "Exporter queue processor";
    public string Action => SyncExporterStepService.Action;

    private readonly IHostingEnvironment _hostingEnvironment;
    private readonly ISyncExporterStepService _exporterStepService;
    private readonly IHubContext<SyncHub> _hubContext;
    private readonly ISyncQueueService _queueService;

    private GlobalSettings _globalSettings;

    public ExporterQueueProcessor(
        IOptionsMonitor<GlobalSettings> optionsMonitor,
        IHostingEnvironment hostingEnvironment,
        ISyncExporterStepService exporterStepService,
        IHubContext<SyncHub> hubContext,
        ISyncQueueService queueService)
    {
        _hostingEnvironment = hostingEnvironment;
        _exporterStepService = exporterStepService;
        _hubContext = hubContext;
        _queueService = queueService;

        _globalSettings = optionsMonitor.CurrentValue;
        optionsMonitor.OnChange(globalSettings =>
        {
            _globalSettings = globalSettings;
        });
    }

    public async Task<Attempt<QueueProcessingResult>> Process(QueuedItem item)
    {
        var queuedRequest = JsonConvert.DeserializeObject<QueuedExporterRequest>(item.Data);
        var serverUrl = GetUmbracoUrl();

        await using (var hub = await GetSignalRHub(serverUrl, item.ReferenceKey))
        {
            queuedRequest.Request.ClientId = hub.ConnectionId;
            queuedRequest.Request.Request.Callbacks = new HubClientService(_hubContext, hub.ConnectionId)?.Callbacks();

            var result = _exporterStepService.Process(queuedRequest.Mode, queuedRequest.Request);

            if (result.ExportComplete)
            {
                // end..
                return Attempt.Succeed(new QueueProcessingResult("")
                {
                    Complete = true
                });
            }
            else
            {
                var nextRequest = PrepareNextStep(queuedRequest.Request, result);
                _queueService.Enqueue(queuedRequest.Mode, nextRequest);
            }

            return Attempt.Succeed(new QueueProcessingResult(""));
        }
    }

    private ExporterRequest PrepareNextStep(ExporterRequest request, ExporterResponse response)
    {
        var next = new ExporterRequest
        {
            ClientId = request.ClientId,
            Id = request.Id,
            Name = request.Name,
            Request = request.Request,
            StepIndex = response.StepIndex,
        };

        _exporterStepService.UpdateRequest(request, response);

        return next;
    }

    private async Task<HubConnection> GetSignalRHub(string url, Guid requestId)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{url}/SyncHub")
            .Build();

        connection.Closed += async (error) =>
        {
            // if the connection closes, try to reconnect 
            await Task.Delay(new Random().Next(0, 5) * 1000);
            await connection.StartAsync();
        };

        connection.On<uSyncUpdateMessage>("Update", async msg =>
        {
            //await _hubContext.SendMessage("Update", 
            //  "queue",
            //  new QueueUpdateMessage
            //  {
            //      RequestId = requestId,
            //      Method = "update",
            //      Message = msg
            //  },
            //  string.Empty);
        });

        connection.On<SyncProgressSummary>("Add", async summary =>
        {
            //await _hubContext.SendMessage(
            //    "queue",
            //    new QueueUpdateMessage
            //    {
            //        RequestId = requestId,
            //        Method = "add",
            //        Message = summary
            //    },
            //    string.Empty);
        });

        await connection.StartAsync();
        return connection;
    }

    private string GetUmbracoUrl()
        => _hostingEnvironment.ApplicationMainUrl.AbsoluteUri.TrimEnd('/')
            + _globalSettings.GetBackOfficePath(_hostingEnvironment);
}
