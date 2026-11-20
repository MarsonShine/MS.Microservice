using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MS.Microservice.Documents.Pdf;

namespace MS.Microservice.Documents.Rendering;

internal static class WordRenderWorker
{
    internal static async Task RunAsync()
    {
        var request = await JsonSerializer.DeserializeAsync<WordWorkerRequest>(Console.OpenStandardInput()).ConfigureAwait(false)
            ?? throw new InvalidDataException("Missing DOCX worker request.");
        var options = WordPdfConverter.Validate(request.Options);
        _ = new WordPdfConverter(request.Office);
        if (request.Docx.Length > options.Limits.MaximumInputBytes) throw new InvalidDataException("DOCX exceeds the input byte limit.");
        await WordRenderPolicy.ValidateAsync(request.Docx, options.Limits).ConfigureAwait(false);
        var directory = Directory.GetCurrentDirectory();
        var input = Path.Combine(directory, "source.docx");
        var profile = Path.Combine(directory, "profile");
        var output = Path.Combine(directory, "output");
        Directory.CreateDirectory(Path.Combine(profile, "user"));
        Directory.CreateDirectory(output);
        await using (var stream = new FileStream(input, FileMode.CreateNew, FileAccess.Write))
            await stream.WriteAsync(request.Docx).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(profile, "user", "registrymodifications.xcu"), Profile).ConfigureAwait(false);
        using var process = new Process
        {
            StartInfo = new(request.Office.LibreOfficeExecutablePath)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        // The inherited job already owns this worker; no native child can run before the parent's stdin gate.
        process.StartInfo.Environment.Remove("CRASH_DUMP_ENABLE");
        foreach (var argument in new[] { "-env:UserInstallation=" + new Uri(profile).AbsoluteUri, "-env:CrashDumpEnable=false",
            "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to", ExportFilter(options.Limits.MaximumPages), "--outdir", output, input })
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("LibreOffice could not be started.");
        var diagnostics = new StringBuilder();
        var errors = new StringBuilder();
        await Task.WhenAll(RenderWorkerSupervisor.ReadDiagnosticsAsync(process.StandardOutput, diagnostics, CancellationToken.None),
            RenderWorkerSupervisor.ReadDiagnosticsAsync(process.StandardError, errors, CancellationToken.None), process.WaitForExitAsync()).ConfigureAwait(false);
        var pdfPath = Path.Combine(output, "source.pdf");
        if (process.ExitCode != 0 || !File.Exists(pdfPath))
            throw new InvalidOperationException($"LibreOffice did not produce a PDF (exit {process.ExitCode}): {diagnostics} {errors}");
        byte[] pdf;
        await using (var stream = File.OpenRead(pdfPath))
            pdf = await WordPdfConverter.ReadAsync(stream, options.Limits.MaximumPdfBytes, "Rendered PDF").ConfigureAwait(false);
        using var content = new MemoryStream(pdf, writable: false);
        var info = await new PdfProcessor().InspectAsync(content, new()
        {
            Limits = new() { MaximumInputBytes = options.Limits.MaximumPdfBytes, MaximumPages = options.Limits.MaximumPages }
        }).ConfigureAwait(false);
        var destination = Console.OpenStandardOutput();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, info.Pages.Count);
        await destination.WriteAsync(header).ConfigureAwait(false);
        await destination.WriteAsync(pdf).ConfigureAwait(false);
        await destination.FlushAsync().ConfigureAwait(false);
    }

    internal static string ExportFilter(int maximumPages) => "pdf:writer_pdf_Export:" + JsonSerializer.Serialize(new
    {
        PageRange = new { type = "string", value = $"1-{maximumPages + 1}" },
        IsSkipEmptyPages = new { type = "boolean", value = false }
    });

    private const string Profile = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry">
          <item oor:path="/org.openoffice.Office.Common/Security/Scripting">
            <prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop>
            <prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop>
          </item>
          <item oor:path="/org.openoffice.Office.Writer/Content/Update">
            <prop oor:name="Link" oor:op="fuse"><value>2</value></prop>
            <prop oor:name="Field" oor:op="fuse"><value>false</value></prop>
            <prop oor:name="Chart" oor:op="fuse"><value>false</value></prop>
          </item>
        </oor:items>
        """;
}
