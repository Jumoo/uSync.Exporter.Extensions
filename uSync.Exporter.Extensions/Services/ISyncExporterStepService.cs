using uSync.Complete.Exporter.Extensions;
using uSync.Exporter;

namespace uSync.Exporter.Extensions.Services;

public interface ISyncExporterStepService
{
    IEnumerable<string> ListPacks();
    ExporterResponse Process(ExportMode mode, ExporterRequest request);
    ExporterResponse ProcessBatch(ExportMode mode, ExporterRequest request);
    void UpdateRequest(ExporterRequest request, ExporterResponse response);
}