using System.Runtime.InteropServices;
using Scriber.Core.Audio;

namespace Scriber.Spikes.WasapiLoopback;

/// <summary>
/// Native WASAPI system-audio loopback capture without requiring third-party audio drivers.
/// Implements PH1.5-REC-005 feasibility spike on Windows Desktop.
/// </summary>
public sealed class WasapiLoopbackCapture : IDisposable
{
    private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const long REFTIMES_PER_SEC = 10000000;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;

    private WaveFormatEx _mixFormat;
    private int _channels;
    private int _sampleRate;
    private int _bitsPerSample;
    private bool _isFloat;

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private readonly MemoryStream _recordedPcm16Stream = new();

    public int SourceSampleRate => _sampleRate;
    public int SourceChannels => _channels;
    public bool IsFloat => _isFloat;
    public int TargetSampleRate => 16000;
    public int TargetChannels => 1;

    public void Initialize()
    {
        // 1. Create MMDeviceEnumerator
        var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))
            ?? throw new PlatformNotSupportedException("MMDeviceEnumerator COM class not found.");
        
        _deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;

        // 2. Get default audio render endpoint (Speakers / Headphones)
        int hr = _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _device);
        Marshal.ThrowExceptionForHR(hr);

        // 3. Activate IAudioClient
        var audioClientGuid = typeof(IAudioClient).GUID;
        hr = _device.Activate(ref audioClientGuid, 1 /* CLSCTX_ALL */, IntPtr.Zero, out object audioClientObj);
        Marshal.ThrowExceptionForHR(hr);
        _audioClient = (IAudioClient)audioClientObj;

        // 4. Query mix format
        hr = _audioClient.GetMixFormat(out IntPtr formatPtr);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            _mixFormat = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
            _channels = _mixFormat.nChannels;
            _sampleRate = (int)_mixFormat.nSamplesPerSec;
            _bitsPerSample = _mixFormat.wBitsPerSample;

            if (_mixFormat.wFormatTag == 3 /* WAVE_FORMAT_IEEE_FLOAT */)
            {
                _isFloat = true;
            }
            else if (_mixFormat.wFormatTag == 0xFFFE /* WAVE_FORMAT_EXTENSIBLE */)
            {
                var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(formatPtr);
                _isFloat = extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            }
            else
            {
                _isFloat = false;
            }

            // 5. Initialize loopback stream (1 second buffer, shared mode, loopback flag)
            long bufferDuration = REFTIMES_PER_SEC; // 1 second
            hr = _audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK,
                bufferDuration,
                0,
                formatPtr,
                IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);

            // 6. Get IAudioCaptureClient
            var captureGuid = typeof(IAudioCaptureClient).GUID;
            hr = _audioClient.GetService(ref captureGuid, out object captureObj);
            Marshal.ThrowExceptionForHR(hr);
            _captureClient = (IAudioCaptureClient)captureObj;
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPtr);
        }
    }

    public void Start(Action<float>? onRmsMeter = null)
    {
        if (_audioClient == null || _captureClient == null)
            throw new InvalidOperationException("Not initialized.");

        _cts = new CancellationTokenSource();
        int hr = _audioClient.Start();
        Marshal.ThrowExceptionForHR(hr);

        _captureTask = Task.Run(() => CaptureLoop(_cts.Token, onRmsMeter));
    }

    public async Task<byte[]> StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_captureTask != null)
            {
                try { await _captureTask; } catch { }
            }
            _cts.Dispose();
            _cts = null;
        }

        _audioClient?.Stop();
        return _recordedPcm16Stream.ToArray();
    }

    private void CaptureLoop(CancellationToken ct, Action<float>? onRmsMeter)
    {
        while (!ct.IsCancellationRequested)
        {
            Thread.Sleep(20); // Poll every 20ms

            int hr = _captureClient!.GetNextPacketSize(out int packetLength);
            if (hr < 0 || packetLength == 0)
                continue;

            while (packetLength > 0 && !ct.IsCancellationRequested)
            {
                hr = _captureClient.GetBuffer(
                    out IntPtr dataPtr,
                    out int numFramesRead,
                    out AudioClientBufferFlags flags,
                    out ulong _,
                    out ulong _);

                if (hr < 0) break;

                bool isSilent = (flags & AudioClientBufferFlags.AUDCLNT_BUFFERFLAGS_SILENT) != 0;

                if (numFramesRead > 0)
                {
                    ProcessFrames(dataPtr, numFramesRead, isSilent, onRmsMeter);
                }

                _captureClient.ReleaseBuffer(numFramesRead);

                _captureClient.GetNextPacketSize(out packetLength);
            }
        }
    }

    private unsafe void ProcessFrames(IntPtr dataPtr, int numFrames, bool isSilent, Action<float>? onRmsMeter)
    {
        if (numFrames == 0) return;

        // 1. Extract frames into mono float buffer
        float[] monoSource = new float[numFrames];

        if (isSilent || dataPtr == IntPtr.Zero)
        {
            Array.Clear(monoSource, 0, monoSource.Length);
        }
        else if (_isFloat && _bitsPerSample == 32)
        {
            float* pSample = (float*)dataPtr.ToPointer();
            for (int i = 0; i < numFrames; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _channels; ch++)
                {
                    sum += *pSample++;
                }
                monoSource[i] = sum / _channels;
            }
        }
        else if (!_isFloat && _bitsPerSample == 16)
        {
            short* pSample = (short*)dataPtr.ToPointer();
            for (int i = 0; i < numFrames; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _channels; ch++)
                {
                    sum += (*pSample++) / 32768.0f;
                }
                monoSource[i] = sum / _channels;
            }
        }

        // 2. Resample from _sampleRate to TargetSampleRate (16,000 Hz)
        float[] resampled = ResampleLinear(monoSource, _sampleRate, TargetSampleRate);

        // 3. Convert float[-1.0, 1.0] to 16-bit PCM bytes
        byte[] pcmBytes = new byte[resampled.Length * sizeof(short)];
        fixed (byte* pOutBytes = pcmBytes)
        {
            short* pOutShort = (short*)pOutBytes;
            for (int i = 0; i < resampled.Length; i++)
            {
                float clamped = Math.Clamp(resampled[i], -1.0f, 1.0f);
                pOutShort[i] = (short)(clamped * 32767f);
            }
        }

        lock (_recordedPcm16Stream)
        {
            _recordedPcm16Stream.Write(pcmBytes, 0, pcmBytes.Length);
        }

        // 4. Compute RMS metering
        if (onRmsMeter != null && pcmBytes.Length > 0)
        {
            float rms = WavHelper.CalculateRmsLevel(pcmBytes);
            onRmsMeter(rms);
        }
    }

    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || input.Length == 0) return input;

        double ratio = (double)sourceRate / targetRate;
        int outputLength = (int)Math.Floor(input.Length / ratio);
        float[] output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcIdx = i * ratio;
            int idxFloor = (int)srcIdx;
            int idxCeil = Math.Min(idxFloor + 1, input.Length - 1);
            float frac = (float)(srcIdx - idxFloor);

            output[i] = input[idxFloor] * (1.0f - frac) + input[idxCeil] * frac;
        }

        return output;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        if (_captureClient != null)
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }
        if (_audioClient != null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }
        if (_device != null)
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }
        if (_deviceEnumerator != null)
        {
            Marshal.ReleaseComObject(_deviceEnumerator);
            _deviceEnumerator = null;
        }

        _recordedPcm16Stream.Dispose();
    }
}

#region Native COM Definitions

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice(string pwstrId, out IMMDevice endpoint);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    [PreserveSig] int GetState(out int pdwState);
}

[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
    [PreserveSig] int GetStreamLatency(out long phnsLatency);
    [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr ppData, out int pNumFramesToRead, out AudioClientBufferFlags pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
    [PreserveSig] int ReleaseBuffer(int numFramesRead);
    [PreserveSig] int GetNextPacketSize(out int pNumFramesInNextPacket);
}

[Flags]
internal enum AudioClientBufferFlags
{
    None = 0,
    AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1,
    AUDCLNT_BUFFERFLAGS_SILENT = 0x2,
    AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4
}

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatExtensible
{
    public WaveFormatEx Format;
    public ushort wValidBitsPerSample;
    public uint dwChannelMask;
    public Guid SubFormat;
}

#endregion
