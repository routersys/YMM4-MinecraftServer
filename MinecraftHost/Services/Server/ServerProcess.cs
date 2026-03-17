using MinecraftHost.Localization;
using MinecraftHost.Models.Logging;
using MinecraftHost.Models.Server;
using MinecraftHost.Services.Authorization;
using MinecraftHost.Services.Interfaces.Logging;
using MinecraftHost.Services.Interfaces.Server;
using MinecraftHost.Services.Jobs;
using MinecraftHost.Services.Logging;
using MinecraftHost.Settings.Configuration;
using Open.Nat;
using System.Diagnostics;
using System.Text;

namespace MinecraftHost.Services.Server;

public sealed class ServerProcess : IServerProcess
{
    private readonly Process _process;
    private readonly CancellationTokenSource _cts;
    private readonly IStructuredLogService _structuredLogService;
    private readonly int _port;
    private readonly string _ruleName;
    private readonly string _correlationId;
    private int _closePortStarted;
    private WindowsJobHandle? _jobHandle;
    private NatDevice? _natDevice;
    private Mapping? _portMapping;
    private Mapping? _portMappingUdp;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler? Exited;

    public bool IsRunning
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (Exception ex)
            {
                LogBoundaryException("IsRunning", ex, StructuredLogLevel.Debug);
                return false;
            }
        }
    }

    public ServerProcess(string javaPath, string jarPath, int maxMemoryMB, string workingDirectory, int port)
    {
        _structuredLogService = StructuredLogServiceProvider.Instance;
        _correlationId = Guid.NewGuid().ToString("N");
        _port = port;
        _ruleName = $"MinecraftServer_{_port}_{Guid.NewGuid():N}";
        _cts = new CancellationTokenSource();

        string fileName;
        string arguments;
        var isExe = jarPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (isExe)
        {
            fileName = jarPath;
            arguments = string.Empty;
        }
        else
        {
            fileName = javaPath;
            arguments = $"-Xmx{maxMemoryMB}M -Xms{maxMemoryMB / 2}M -Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -jar \"{jarPath}\" nogui";
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        if (!isExe)
        {
            _process.StartInfo.Environment["JAVA_TOOL_OPTIONS"] = "-Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8";
        }

        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data is not null)
                OutputReceived?.Invoke(this, e.Data);
        };

        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data is not null)
                ErrorReceived?.Invoke(this, e.Data);
        };

        _process.Exited += (s, e) =>
        {
            _ = ClosePortAsync();
            _structuredLogService.Log(StructuredLogLevel.Warning, nameof(ServerProcess), "サーバープロセスが終了しました。", "ProcessExited", correlationId: _correlationId);
            Exited?.Invoke(this, EventArgs.Empty);
            try { _cts.Cancel(); } catch (Exception ex) { LogBoundaryException("ProcessExited.Cancel", ex); }
        };
    }

    public void Start()
    {
        try
        {
            _process.Start();
            _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerProcess), $"サーバープロセスを開始しました。PID={_process.Id}", "Start", correlationId: _correlationId);
            _jobHandle ??= WindowsJobHandle.CreateForChildProcess(_process);

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            if (MinecraftHostSettings.Default.EnablePortForwarding)
                _ = OpenPortAsync();
        }
        catch (Exception ex)
        {
            var wrapped = BoundaryExceptionPolicy.Wrap(nameof(Start), ex);
            _structuredLogService.Log(StructuredLogLevel.Error, nameof(ServerProcess), wrapped.Message, "Start", exception: wrapped, correlationId: _correlationId);
            throw wrapped;
        }
    }

    private async Task OpenPortAsync()
    {
        try
        {
            var psiTcp = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{_ruleName}\" dir=in action=allow protocol=TCP localport={_port}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var procTcp = Process.Start(psiTcp);
            if (procTcp is not null) await procTcp.WaitForExitAsync();

            var psiUdp = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{_ruleName}_UDP\" dir=in action=allow protocol=UDP localport={_port}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var procUdp = Process.Start(psiUdp);
            if (procUdp is not null) await procUdp.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            LogBoundaryException("OpenPort.Firewall", ex);
        }

        try
        {
            var discoverer = new NatDiscoverer();
            using var cts = new CancellationTokenSource(5000);
            _natDevice = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
            if (_natDevice is not null)
            {
                _portMapping = new Mapping(Protocol.Tcp, _port, _port, "Minecraft Server");
                await _natDevice.CreatePortMapAsync(_portMapping);
                try
                {
                    _portMappingUdp = new Mapping(Protocol.Udp, _port, _port, "Minecraft Server UDP");
                    await _natDevice.CreatePortMapAsync(_portMappingUdp);
                }
                catch (Exception exUdP)
                {
                    LogBoundaryException("OpenPort.Upnp.Udp", exUdP);
                }
            }
        }
        catch (Exception ex)
        {
            LogBoundaryException("OpenPort.Upnp", ex);
        }
    }

    private async Task ClosePortAsync()
    {
        if (!MinecraftHostSettings.Default.EnablePortForwarding) return;
        if (Interlocked.Exchange(ref _closePortStarted, 1) == 1) return;

        try
        {
            var psiTcp = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall delete rule name=\"{_ruleName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var procTcp = Process.Start(psiTcp);
            if (procTcp is not null) await procTcp.WaitForExitAsync();

            var psiUdp = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall delete rule name=\"{_ruleName}_UDP\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var procUdp = Process.Start(psiUdp);
            if (procUdp is not null) await procUdp.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            LogBoundaryException("ClosePort.Firewall", ex);
        }

        try
        {
            if (_natDevice is not null)
            {
                if (_portMapping is not null)
                    await _natDevice.DeletePortMapAsync(_portMapping);
                if (_portMappingUdp is not null)
                    await _natDevice.DeletePortMapAsync(_portMappingUdp);
                _natDevice = null;
                _portMapping = null;
                _portMappingUdp = null;
            }
        }
        catch (MappingException ex)
        {
            _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerProcess), ex.Message, "ClosePort.Upnp", correlationId: _correlationId);
            _natDevice = null;
            _portMapping = null;
            _portMappingUdp = null;
        }
        catch (Exception ex)
        {
            LogBoundaryException("ClosePort.Upnp", ex);
        }
    }

    public async Task SendCommandAsync(string command)
    {
        try
        {
            bool hasExited;
            try { hasExited = _process.HasExited; }
            catch (Exception ex)
            {
                LogBoundaryException("SendCommand.CheckExited", ex, StructuredLogLevel.Debug);
                hasExited = true;
            }

            if (!hasExited && _process.StandardInput is not null)
            {
                await _process.StandardInput.WriteLineAsync(command);
                await _process.StandardInput.FlushAsync();
                _structuredLogService.Log(StructuredLogLevel.Debug, nameof(ServerProcess), string.Format(Texts.ServerProcess_LogSendCommandFormat, command), "SendCommand", correlationId: _correlationId);
            }
        }
        catch (Exception ex)
        {
            var wrapped = BoundaryExceptionPolicy.Wrap(nameof(SendCommandAsync), ex);
            _structuredLogService.Log(StructuredLogLevel.Warning, nameof(ServerProcess), wrapped.Message, "SendCommand", exception: wrapped, correlationId: _correlationId);
            throw wrapped;
        }
    }

    public void Stop()
    {
        _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerProcess), Texts.ServerProcess_LogStopBegin, "Stop", correlationId: _correlationId);
        try
        {
            bool hasExited;
            try { hasExited = _process.HasExited; }
            catch (Exception ex)
            {
                LogBoundaryException("Stop.CheckExited", ex, StructuredLogLevel.Debug);
                hasExited = true;
            }

            if (!hasExited)
            {
                if (_process.StandardInput is not null)
                {
                    try
                    {
                        _process.StandardInput.WriteLine("stop");
                        _process.StandardInput.Flush();
                    }
                    catch (Exception ex)
                    {
                        LogBoundaryException("Stop.WriteStop", ex);
                    }
                }

                if (!_process.WaitForExit(10000))
                {
                    try
                    {
                        _process.Kill(true);
                        _process.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        LogBoundaryException("Stop.Kill", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogBoundaryException(nameof(Stop), ex);
        }
    }

    public async Task StopAsync()
    {
        _structuredLogService.Log(StructuredLogLevel.Information, nameof(ServerProcess), Texts.ServerProcess_LogStopAsyncBegin, "StopAsync", correlationId: _correlationId);
        try
        {
            bool hasExited;
            try { hasExited = _process.HasExited; }
            catch (Exception ex)
            {
                LogBoundaryException("StopAsync.CheckExited", ex, StructuredLogLevel.Debug);
                hasExited = true;
            }

            if (!hasExited)
            {
                if (_process.StandardInput is not null)
                {
                    try
                    {
                        await _process.StandardInput.WriteLineAsync("stop");
                        await _process.StandardInput.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        LogBoundaryException("StopAsync.WriteStop", ex);
                    }
                }

                using var cts = new CancellationTokenSource(10000);
                try
                {
                    await _process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        _process.Kill(true);
                        await _process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        LogBoundaryException("StopAsync.Kill", ex);
                    }
                }
                catch (Exception ex)
                {
                    LogBoundaryException("StopAsync.WaitForExit", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogBoundaryException(nameof(StopAsync), ex);
        }
    }

    public bool TryGetMetricsSnapshot(out ServerProcessMetricsSnapshot snapshot)
    {
        try
        {
            if (_process.HasExited)
            {
                snapshot = default;
                return false;
            }

            _process.Refresh();
            snapshot = new ServerProcessMetricsSnapshot(_process.TotalProcessorTime, _process.WorkingSet64, DateTime.UtcNow);
            return true;
        }
        catch (Exception ex)
        {
            LogBoundaryException(nameof(TryGetMetricsSnapshot), ex, StructuredLogLevel.Debug);
            snapshot = default;
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        try { _ = ClosePortAsync(); } catch (Exception ex) { LogBoundaryException("Dispose.ClosePort", ex); }
        try { _cts.Cancel(); } catch (Exception ex) { LogBoundaryException("Dispose.CancelToken", ex, StructuredLogLevel.Debug); }
        _cts.Dispose();
        _jobHandle?.Dispose();
        try { _process.Dispose(); } catch (Exception ex) { LogBoundaryException("Dispose.Process", ex, StructuredLogLevel.Debug); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        try { await ClosePortAsync(); } catch (Exception ex) { LogBoundaryException("DisposeAsync.ClosePort", ex); }
        try { _cts.Cancel(); } catch (Exception ex) { LogBoundaryException("DisposeAsync.CancelToken", ex, StructuredLogLevel.Debug); }
        _cts.Dispose();
        _jobHandle?.Dispose();
        try { _process.Dispose(); } catch (Exception ex) { LogBoundaryException("DisposeAsync.Process", ex, StructuredLogLevel.Debug); }
    }

    private void LogBoundaryException(string operation, Exception exception, StructuredLogLevel level = StructuredLogLevel.Warning)
    {
        var wrapped = BoundaryExceptionPolicy.Wrap(operation, exception);
        _structuredLogService.Log(level, nameof(ServerProcess), wrapped.Message, operation, exception: wrapped, correlationId: _correlationId);
    }
}