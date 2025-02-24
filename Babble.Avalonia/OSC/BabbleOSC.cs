using Babble.Avalonia.OSC;
using Babble.Avalonia.Scripts;
using Babble.Core;
using Babble.Core.Settings;
using OscCore;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using VRCFaceTracking;
using VRCFaceTracking.BabbleNative;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFTReceiver;

namespace Babble.OSC;

public class BabbleOSC
{
    private static readonly string[] Prefixes = ["/", "/v2/", "/FT/", "/FT/v2/"];
    private List<OscMessage> messages = new List<OscMessage>();
    private Socket? _oscSocket = null;
    private IPEndPoint? _oscRemoteEndpoint = null;
    private OSCQuery _query;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _sendTask;

    private const string DEFAULT_HOST = "127.0.0.1";

    private const int DEFAULT_LOCAL_PORT = 44444;

    private const int DEFAULT_REMOTE_PORT = 8888;

    private const int TIMEOUT_MS = 10000;

    private const int MAX_RETRIES = 5;

    public BabbleOSC(string? host = null, int? remotePort = null)
    {
        var settings = BabbleCore.Instance.Settings;
        
        _query = new OSCQuery(IPAddress.Loopback);

        OnBabbleSettingsChanged(settings);

        var ip = IPAddress.Parse(host ?? DEFAULT_HOST);
        ConfigureReceiver(
            ip, 
            remotePort ?? DEFAULT_REMOTE_PORT);

        _cancellationTokenSource = new CancellationTokenSource();
        _sendTask = Task.Run(() => SendLoopAsync(_cancellationTokenSource.Token));
    }

    private void OnBabbleSettingsChanged(BabbleSettings settings)
    {
        var guiOSCAddress = nameof(settings.GeneralSettings.GuiOscAddress);
        var guiOSCPort= nameof(settings.GeneralSettings.GuiOscPort);

        settings.OnUpdate += async (setting) =>
        {
            if (setting == guiOSCAddress || setting == guiOSCPort)
            {
                // Close and dispose the current sender
                _oscSocket?.Close();

                // Delay before attempting to reconnect
                await Task.Delay(1000);

                // Attempt to reconfigure the receiver and reconnect
                ConfigureReceiver(
                    IPAddress.Parse(settings.GeneralSettings.GuiOscAddress),
                    settings.GeneralSettings.GuiOscPort);
            }
        };
    }

    private void ConfigureReceiver(IPAddress host, int remotePort)
    {
        if(_oscSocket is not null)
        {
            _oscSocket.Close(1000);
        }
        _oscSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        _oscSocket.SendTimeout = TIMEOUT_MS;
        _oscSocket.ReceiveTimeout = TIMEOUT_MS;
        _oscRemoteEndpoint = new IPEndPoint(host, remotePort);
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!BabbleCore.Instance.IsRunning)
                continue;

            try
            {
                if (BabbleCore.Instance.Settings.GeneralSettings.GuiForceRelevancy)
                    await SendMobileParameters(cancellationToken);
                else
                    await SendDesktopParameters(cancellationToken);

                await Task.Delay(100);
            }
            catch (Exception ex) { Console.WriteLine(ex); }
        }
         Console.WriteLine($"CR: {cancellationToken.IsCancellationRequested}");
    }

    private async Task SendMobileParameters(CancellationToken cancellationToken)
    {
        var mul = BabbleCore.Instance.Settings.GeneralSettings.GuiMultiply;

        foreach (var element in TheGrandLookupTable.Table)
        {
            foreach (var prefix in Prefixes)
            {
                messages.Clear();
                float val = element.Value.Invoke() * (float)mul;
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}", val));

                var bools = Float8Converter.ConvertFloatToBinaryParameter(val);
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}Negative", bools.Negative));
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}1", bools.Parameter1));
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}2", bools.Parameter2));
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}4", bools.Parameter4));
                messages.Add(new OscMessage($"/avatar/parameters{prefix}{element.Key}8", bools.Parameter8));
                var bundle = new OscBundle(DateTime.Now, messages.ToArray());

                if(_oscSocket is not null && _oscRemoteEndpoint is not null)
                await _oscSocket.SendToAsync(bundle.ToByteArray(), SocketFlags.None, _oscRemoteEndpoint, _cancellationTokenSource.Token);
            }
        }
    }

    private async Task SendDesktopParameters(CancellationToken cancellationToken)
    {
        var mul = BabbleCore.Instance.Settings.GeneralSettings.GuiMultiply;
        var prefix = BabbleCore.Instance.Settings.GeneralSettings.GuiOscLocation;

        messages.Clear();
        foreach (var exp in BabbleMapping.Mapping)
        {
            // Don't send the UE copies of pucker/funnel
            var address = BabbleAddresses.Addresses[exp.Key];
            var value = UnifiedTracking.Data.Shapes[(int)exp.Value].Weight;
            if ( value == 0 ||
                exp.Value == UnifiedExpressions.LipFunnelLowerRight ||
                exp.Value == UnifiedExpressions.LipFunnelUpperLeft ||
                exp.Value == UnifiedExpressions.LipFunnelUpperRight ||
                exp.Value == UnifiedExpressions.LipPuckerLowerRight ||
                exp.Value == UnifiedExpressions.LipPuckerUpperLeft ||
                exp.Value == UnifiedExpressions.LipPuckerUpperRight)
                continue;

            messages.Add(new OscMessage($"{prefix}{address}", value * (float) mul));
        }
        //var bundle = new OscBundle(DateTime.Now, [.. messages]);
        if(_oscSocket is not null && _oscRemoteEndpoint is not null)
        {
           foreach(OscMessage message in messages)
           {
             await _oscSocket.SendToAsync(message.ToByteArray(), SocketFlags.None, _oscRemoteEndpoint, _cancellationTokenSource.Token);
           }
        }

    }

    private async Task PollConnectionStatus(CancellationToken cancellationToken)
    {
        /*if (_sender.State != OscSocketState.Closed)
        {
            return;
        }

        // Close and dispose the current sender
        _sender.Close();
        _sender.Dispose();

        // Delay before attempting to reconnect
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        // Attempt to reconfigure the receiver and reconnect
        ConfigureReceiver(_sender.RemoteAddress, _sender.Port);*/
    }

    public void Teardown()
    {
        _cancellationTokenSource.Cancel();
        _oscSocket?.Close();
        _cancellationTokenSource.Dispose();
    }
}