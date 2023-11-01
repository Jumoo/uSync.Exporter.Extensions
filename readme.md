# uSync.Exporter... Extensions

uSync.Exporter.Extensions is an experimental library, looking at how we might API'fy the Exporter libraries in uSync.

It is delivered AS with no Warranty

# Why No Exporter API?

We often get asked if there is an API for uSync.Exporter, and its not as simple as it may seem.

Exporter (and publisher) do things in chunks, so an export or import might actually be 10s or 100s of requests to the server as each step of an export is a separate process and within a step there might be paged requests. 

this Extensions library is an attempt to wrap some of the complexity and make it simpler to call the exporter via API calls.

# Extensions API
The extensions API consists mainly of a ExporterStepService, which allows you to call teh inner workings of an export with one command.

