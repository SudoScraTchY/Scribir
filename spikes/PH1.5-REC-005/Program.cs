using Scriber.Core.Audio;
using Scriber.Spikes.WasapiLoopback;

Console.WriteLine("=================================================");
Console.WriteLine("  Scribir PH1.5-REC-005: WASAPI Loopback Capture  ");
Console.WriteLine("=================================================");

try
{
    using var loopback = new WasapiLoopbackCapture();
    Console.WriteLine("[1/4] Initializing WASAPI loopback client...");
    loopback.Initialize();

    Console.WriteLine($"      Default Render Format: {loopback.SourceSampleRate} Hz, {loopback.SourceChannels} ch, Float={loopback.IsFloat}");
    Console.WriteLine($"      Target Pipeline Format: {loopback.TargetSampleRate} Hz, {loopback.TargetChannels} ch, 16-bit PCM");

    Console.WriteLine("[2/4] Starting live loopback capture (capturing 3 seconds of system audio)...");
    float maxRms = 0f;
    loopback.Start(rms =>
    {
        if (rms > maxRms) maxRms = rms;
        // Print live meter in console
        int barLength = (int)(rms * 40);
        string bar = new string('#', Math.Clamp(barLength, 0, 40)).PadRight(40, '-');
        Console.Write($"\r      Level: [{bar}] RMS: {rms:F4}   ");
    });

    await Task.Delay(3000);
    Console.WriteLine();

    Console.WriteLine("[3/4] Stopping capture and finalizing buffer...");
    byte[] rawPcm16 = await loopback.StopAsync();
    Console.WriteLine($"      Captured {rawPcm16.Length} raw PCM16 bytes (~{rawPcm16.Length / (16000 * 2.0):F2} seconds of audio). Peak RMS={maxRms:F4}");

    string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "loopback_test.wav");
    Console.WriteLine($"[4/4] Writing standard 44-byte WAV header to: {outputPath}");

    using (var fs = File.Create(outputPath))
    {
        WavHelper.WriteHeader(fs, rawPcm16.Length);
        fs.Write(rawPcm16);
    }

    Console.WriteLine($"      SUCCESS! Valid WAV written ({new FileInfo(outputPath).Length} bytes).");
    Console.WriteLine("      Loopback audio can feed directly into LiveTranscriptionSession and PipelineFactory!");
    Console.WriteLine("=================================================");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
