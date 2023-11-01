using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Umbraco.Extensions;
using uSync.Complete.Exporter.Extensions;
using uSync.Expansions.Core;
using uSync.Expansions.Core.Models;
using uSync.Exporter;
using uSync.Exporter.Options;
using uSync.Exporter.Services;

namespace uSync.Exporter.Extensions.Services;

internal class SyncExporterStepService : ISyncExporterStepService
{
    public const string Action = "Exporter_Queued_Action";

    private readonly SyncExporterService _exporterService;

    private readonly ExporterStep[] _exportSteps;
    private readonly ExporterStep[] _reportSteps;
    private readonly ExporterStep[] _importSteps;

    private uSyncExporterOptions _options;
    private string _root;

    public SyncExporterStepService(
        SyncExporterService exporterService,
        IHostEnvironment hostEnvironment,
        IOptions<uSyncExporterOptions> options)
    {
        _root = Path.GetFullPath(hostEnvironment.ContentRootPath);
        _options = options.Value;

        _exporterService = exporterService;

        _exportSteps = new[]
        {
            new ExporterStep("Calculate", "icon-settings usync-cogs", _exporterService.GetAllItems),
            new ExporterStep("Dependencies",  _exporterService.GetDependencies),
            new ExporterStep("Export", "icon-box", _exporterService.ExportItems),
            new ExporterStep("Files", _exporterService.ExportFiles),
            new ExporterStep("System", _exporterService.ExportSystemFiles),
            new ExporterStep("Media", "icon-pictures-alt-2", _exporterService.ExportMedia),
            new ExporterStep("Zip", "icon-zip", ArchiveExport)
        };

        _reportSteps = new[]
        {
            new ExporterStep("Fetch", "icon-box", RetrieveExport),
            new ExporterStep("Validate", "icon-plugin", _exporterService.ValidatePack),
            new ExporterStep("Items", "icon-script-alt", _exporterService.Report),
            new ExporterStep("Files", "icon-script", _exporterService.ReportFiles),
            new ExporterStep("Check", "icon-slideshow", _exporterService.GetReport),
            new ExporterStep("Clean", "icon-brush-alt-2", _exporterService.CleanReport)
        };

        _importSteps = new[]
        {
            new ExporterStep("Fetch", "icon-box", RetrieveExport),
            new ExporterStep("Validate", "icon-plugin", _exporterService.ValidatePack),
            new ExporterStep("RestorePoint", "icon-pushpin", _exporterService.CreateRestorePoint),
            new ExporterStep("Files", "icon-script-alt", _exporterService.ImportFiles),
            new ExporterStep("Media", "icon-pictures-alt-2", _exporterService.ImportMedia),
            new ExporterStep("Import", "icon-box", _exporterService.Import),
            new ExporterStep("Finalize", "icon-box", _exporterService.ImportFinalize),
            new ExporterStep("Report", "icon-slideshow", _exporterService.ImportResults),
            new ExporterStep("Clean", "icon-brush-alt-2", _exporterService.Cleaning),
        };
    }

    public ExporterResponse Process(ExportMode mode, ExporterRequest request)
    {
        return mode switch
        {
            ExportMode.Export => ExportPack(request),
            ExportMode.Import => ImportPack(request),
            ExportMode.Report => ReportPack(request),
            _ => new ExporterResponse { ExportComplete = true },
        };
    }

    public ExporterResponse ProcessBatch(ExportMode mode, ExporterRequest request)
    {
        ExporterResponse response;
        do
        {
            response = Process(mode, request);

            if (!response.ExportComplete)
                UpdateRequest(request, response);


        } while (!response.ExportComplete);

        return response;
    }

    public void UpdateRequest(ExporterRequest request,  ExporterResponse response)
    {
        
        // move next. 
        request.StepIndex = response.StepIndex;
        request.Request.PageNumber = response.NextPage;
        request.Request.HandlerFolder = response.NextFolder;
        request.Request.AdditionalData = response.Response.AdditionalData;
    }

    private ExporterResponse ExportPack(ExporterRequest request)
        => ProcessExporterSteps(_exportSteps, request);

    private ExporterResponse ImportPack(ExporterRequest request)
        => ProcessExporterSteps(_importSteps, request);

    private ExporterResponse ReportPack(ExporterRequest request)
        => ProcessExporterSteps(_reportSteps, request);

    private ExporterResponse ProcessExporterSteps(ExporterStep[] steps, ExporterRequest request)
    {
        if (request.StepIndex >= steps.Length)
            return new ExporterResponse { ExportComplete = true };

        if (request.Id == Guid.Empty) request.Id = Guid.NewGuid();

        UpdateRequestFromConfig(request.Request);

        request.Request.AdditionalData["name"] = request.Name ?? "sync-pack.file";

        var step = steps[request.StepIndex];

        var response = new ExporterResponse
        {
            StepIndex = request.StepIndex
        };

        // perform the work. 
        response.Response = step.Step(request.Id, request.Request);

        // handler the return
        if (response.Response.Items?.Any() == false)
            response.Response.Items = request.Request.Items;

        response.Id = request.Id;
        response.NextFolder = response.Response.NextFolder;

        // increment the page 
        IncrementPage(response, request.Request.PageNumber);

        // increment step 
        IncrementStep(steps, response, request.CreateRestore);

        // only show steps we are going to run.
        var visibleSteps = steps.Where(x => request.CreateRestore ||
            x.Name != "RestorePoint");

        response.Progress = SyncStepProgressHelper.UpdateProgress(visibleSteps, response.StepIndex);

        return response;
    }

    private static void IncrementPage(ExporterResponse response, int currentPage)
    {
        if (response.Response.AllPagesProcessed ||
            response.Response.ResetPaging)
        {
            response.NextPage = 0;
            return;
        }

        response.NextPage = currentPage + 1;
    }

    private static void IncrementStep(ExporterStep[] steps, ExporterResponse response, bool createRestore)
    {
        if (response.Response.AllPagesProcessed == true)
        {
            do
            {
                response.StepIndex++;
                response.NextPage = 0;
            } while (
                response.StepIndex < steps.Length &&
                !createRestore && steps[response.StepIndex].Name == "RestorePoint");
        }

        if (response.StepIndex >= steps.Length)
            response.ExportComplete = true;
    }

    private void UpdateRequestFromConfig(SyncPackRequest request)
    {
        request.HandlerSet = GetSetName();

        request.PageSize = _options.PageSize;

        if (request.Options == null) request.Options = new SyncPackOptions();
        if (request.AdditionalData == null) request.AdditionalData = new Dictionary<string, object>();

        if (!_options.NoFolder)
        {

            var folders = _options.AdditionalFolders.ToDelimitedList();

            var exclusions = _options.Exclusions.ToDelimitedList();

            var replacements = _options.Replacements.ToDelimitedList();

            // add config folders..
            request.Options.Folders.MergeAndClean(folders, false);

            request.Options.SystemExclusions.MergeAndClean(exclusions);

            request.Options.FileReplacements = replacements;
        }
        else
        {
            // when 'NoFolders' setting is present we blank the folder list
            request.Options.Folders = new List<string>();
        }
    }

    private string GetSetName()
    => _options.HandlerSet;

    ////

    /// <summary>
    ///  saves the export to a folder on disk, (so it can be later retrieved).
    /// </summary>
    private SyncPackResponse ArchiveExport(Guid id, SyncPackRequest request)
    {
        using (var stream = _exporterService.PackExport(id, request))
        {
            if (stream == null) return SyncPackResponseHelper.Fail("Cannot pack export");

            var path = GetExportArchiveFile(id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (var fileStream = File.Create(path))
            {
                stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(fileStream);
            }
        }

        return SyncPackResponseHelper.Succeed(true);
    }

    /// <summary>
    ///  takes an export from our 'export' folder and puts it into the temp location 
    ///  ready to be acted upon.
    /// </summary>
    private SyncPackResponse RetrieveExport(Guid id, SyncPackRequest request)
    {
        var sourceFile = GetExportArchiveFile(id);
        if (!File.Exists(sourceFile)) return SyncPackResponseHelper.Fail($"Cannot find sync pack with id {id}");

        var targetFolder = _exporterService.CreateImportFolder(id);
        var targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));    
        File.Copy(sourceFile, targetFile, true);

        _exporterService.UnpackExport(id);

        return SyncPackResponseHelper.Succeed(true);
    }


    public IEnumerable<string> ListPacks()
    {
        var folder = GetExportFolder();

        foreach (var file in Directory.GetFiles(folder, "*.usync"))
        {
            yield return Path.GetFileNameWithoutExtension(file);
        }
    }

    private string GetExportFolder()
        => Path.Combine(_root, "uSync", "Exports");

    private string GetExportArchiveFile(Guid id)
        => Path.Combine(GetExportFolder(), id.ToString() + ".usync");
}
