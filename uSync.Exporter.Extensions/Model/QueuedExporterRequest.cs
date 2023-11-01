using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using uSync.Exporter;

namespace uSync.Complete.Exporter.Extensions.Model;
internal class QueuedExporterRequest
{
    public ExportMode Mode { get; set; }
    public ExporterRequest Request { get; set; }

}
