﻿using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBarcodeScanner.ZXing.Cpp;

public partial class BarcodeReader : ComponentBase, IDisposable, IAsyncDisposable
{
    [Parameter]
    public string TextWithoutDevices { get; set; } = "looking for devices";

    [Parameter]
    public string LabelVideoDeviceListText { get; set; } = "Change video source:";

    [Parameter]
    public string ButtonStartText { get; set; } = "Start";

    [Parameter]
    public string ButtonResetText { get; set; } = "Reset";

    [Parameter]
    public string ButtonStopText { get; set; } = "Stop";

    [Parameter]
    public string ButtonToggleTorchText { get; set; } = "Toggle Torch";

    [Parameter]
    public bool DecodedPictureCapture { get; set; }

    [Parameter]
    public string Title { get; set; } = "Scan Barcode from Camera";

    [Parameter]
    public bool StartCameraAutomatically { get; set; }

    [Parameter]
    public bool ShowStart { get; set; } = true;

    [Parameter]
    public bool ShowStop { get; set; } = true;

    [Parameter]
    public bool ShowReset { get; set; } = true;

    [Parameter]
    public bool ShowToggleTorch { get; set; } = true;

    [Parameter]
    public bool ShowResult { get; set; } = true;

    [Parameter]
    public bool ShowVideoDeviceList { get; set; } = true;

    [Parameter]
    public int VideoWidth { get; set; } = 300;

    [Parameter]
    public int VideoHeight { get; set; } = 200;

    [Parameter]
    public bool FullWidthVideo { get; set; }

    [Parameter]
    public int? StreamHeight { get; set; }

    [Parameter]
    public int? StreamWidth { get; set; }

    [Parameter]
    public EventCallback<BarcodeReceivedEventArgs> OnBarcodeReceived { get; set; }

    [Parameter]
    public EventCallback<ErrorReceivedEventArgs> OnErrorReceived { get; set; }

    [Parameter]
    public EventCallback<DecodingChangedArgs> OnDecodingChanged { get; set; }

    private bool _isDecoding = false;
    private DotNetObjectReference<BarcodeReaderInterop>? _dotNetHelper;
    public bool IsDecoding
    {
        get => _isDecoding;
        protected set
        {
            var hasChanged = _isDecoding != value;

            _isDecoding = value;
            if (hasChanged)
            {
                var args = new DecodingChangedArgs()
                {
                    Sender = this,
                    IsDecoding = _isDecoding,
                };
                OnDecodingChanged.InvokeAsync(args);
            }
        }
    }

    private string BarcodeText { get; set; } = string.Empty;
    private string? ErrorMessage { get; set; } = string.Empty;

    public IEnumerable<VideoInputDevice> VideoInputDevices { get; private set; } = new List<VideoInputDevice>();

    [Parameter]
    public EventCallback<IEnumerable<VideoInputDevice>> VideoInputDevicesChanged { get; set; }

    private string? _selectedVideoInputId = string.Empty;
    [Parameter]
    public EventCallback<string> SelectedVideoInputIdChanged { get; set; }

    public string? SelectedVideoInputId
    {
        get => _selectedVideoInputId;
        protected set
        {
            _selectedVideoInputId = value;
            SelectedVideoInputIdChanged.InvokeAsync(value);
        }
    }
        
    [Inject]
    protected IJSRuntime? JsRuntime { get; set; }

    private BarcodeReaderInterop? _backend;
    private ElementReference _video;
    private ElementReference _canvas;

    private bool _decodedPictureCapture;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (JsRuntime == null)
            throw new NullReferenceException("JsRuntime is null");
            
        if (firstRender) {
            _backend = new BarcodeReaderInterop(JsRuntime);
            _dotNetHelper = DotNetObjectReference.Create(_backend);
            await JsRuntime.InvokeVoidAsync("Helpers.setDotNetHelper", _dotNetHelper);
            try
            {
                _decodedPictureCapture = DecodedPictureCapture;
                await _backend.SetLastDecodedPictureFormat(DecodedPictureCapture ? "image/jpeg" : null);

                await GetVideoInputDevicesAsync();

                _backend.BarcodeReceived += ReceivedBarcodeText;
                _backend.ErrorReceived += ReceivedErrorMessage;
                _backend.DecodingStarted += DecodingStarted;
                _backend.DecodingStopped += DecodingStopped;

                if (StartCameraAutomatically && VideoInputDevices.Any())
                {
                    await _backend.SetVideoInputDevice(SelectedVideoInputId);
                    await StartDecoding();
                }
            }
            catch (Exception ex)
            {
                await ReceivedErrorMessage(new ErrorReceivedEventArgs { Message = ex.Message });
            }
        }
    }
        
    protected override async Task OnParametersSetAsync()
    {
        if (_decodedPictureCapture != DecodedPictureCapture)
        {
            _decodedPictureCapture = DecodedPictureCapture;
            if (_backend != null)
                await _backend.SetLastDecodedPictureFormat(DecodedPictureCapture ? "image/jpeg" : null);
        }
    }
        
    [Obsolete("Please use DisposeAsync")]
    public void Dispose()
    {
        _ = DisposeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopDecoding();

            if (_backend != null)
            {
                _backend.BarcodeReceived -= ReceivedBarcodeText;
                _backend.ErrorReceived -= ReceivedErrorMessage;
                _backend.DecodingStarted -= DecodingStarted;
                _backend.DecodingStopped -= DecodingStopped;
            }
        }
        catch (Exception ex)
        {
            // Too late to do anything about it, but at least fail gracefully
            Console.WriteLine(ex.ToString());
        }
    }

    private async Task GetVideoInputDevicesAsync()
    {
        if (_backend != null)
            VideoInputDevices = await _backend.GetVideoInputDevices("get");
        await VideoInputDevicesChanged.InvokeAsync(VideoInputDevices);
    }

    private async Task RestartDecoding()
    {
        await StopDecoding();
        await StartDecoding();
    }

    public async Task StartDecoding()
    {
        ErrorMessage = null;
        var width = StreamWidth ?? 0;
        var height = StreamHeight ?? 0;
            
        if (_backend != null)
        {
            await _backend.StartDecoding(_video, width, height);
            SelectedVideoInputId = await _backend.GetVideoInputDevice();
        }

        StateHasChanged();
    }

    private async Task StartDecodingSafe()
    {
        try
        {
            await StartDecoding();
        }
        catch (Exception ex)
        {
            await OnErrorReceived.InvokeAsync(new ErrorReceivedEventArgs { Message = ex.Message });
        }
    }

    public async Task<string> Capture()
    {
        if (_backend != null) 
            return await _backend.Capture(_canvas);

        throw new NullReferenceException("Backend is null");
    }

    public async Task<string> CaptureLastDecodedPicture()
    {
        if (_backend != null) 
            return await _backend.GetLastDecodedPicture();

        throw new NullReferenceException("Backend is null");
    }

    public async Task StopDecoding()
    {
        if (_backend != null)
        {
            _backend.OnBarcodeReceived(string.Empty);
            await _backend.StopDecoding();
            StateHasChanged();
        }
    }

    private async Task StopDecodingSafe()
    {
        try
        {
            await StopDecoding();
        }
        catch (Exception ex)
        {
            await OnErrorReceived.InvokeAsync(new ErrorReceivedEventArgs { Message = ex.Message });
        }
    }

    private async Task RestartDecodingSafe()
    {
        await StopDecodingSafe();
        await StartDecodingSafe();
    }

    public async Task UpdateResolution()
    {
        await RestartDecoding();
    }

    public async Task ToggleTorch()
    {
        if (_backend != null)
            await _backend.ToggleTorch();
    }

    private async Task ToggleTorchSafe()
    {
        try
        {
            await ToggleTorch();
        }
        catch (Exception ex)
        {
            await OnErrorReceived.InvokeAsync(new ErrorReceivedEventArgs { Message = ex.Message });
        }
    }

    public async Task TorchOn()
    {
        if (_backend != null) 
            await _backend.SetTorchOn();
    }

    public async Task TorchOff()
    {
        if (_backend != null) 
            await _backend.SetTorchOff();
    }

    public async Task SelectVideoInput(VideoInputDevice device)
    {
        await ChangeVideoInputSource(device.DeviceId);
    }

    private async Task ReceivedBarcodeText(BarcodeReceivedEventArgs args)
    {
        BarcodeText = args.BarcodeText;
        await OnBarcodeReceived.InvokeAsync(args);
        StateHasChanged();
    }
    private async Task ReceivedErrorMessage(ErrorReceivedEventArgs args)
    {
        ErrorMessage = args.Message;
        await OnErrorReceived.InvokeAsync(args);
        StateHasChanged();
    }

    private Task DecodingStarted(DecodingActionEventArgs _)
    {
        IsDecoding = true;
        return Task.CompletedTask;
    }
    private Task DecodingStopped(DecodingActionEventArgs _)
    {
        IsDecoding = false;
        return Task.CompletedTask;
    }

    private async Task ChangeVideoInputSource(string? deviceId)
    {
        SelectedVideoInputId = deviceId;
        if (_backend != null) 
            await _backend.SetVideoInputDevice(deviceId);
        await RestartDecoding();
    }

    private async Task OnVideoInputSourceChanged(ChangeEventArgs args)
    {
        try
        {
            await ChangeVideoInputSource(args.Value?.ToString());
        }
        catch (Exception ex)
        {
            await OnErrorReceived.InvokeAsync(new ErrorReceivedEventArgs { Message = ex.Message });
        }
    }
}