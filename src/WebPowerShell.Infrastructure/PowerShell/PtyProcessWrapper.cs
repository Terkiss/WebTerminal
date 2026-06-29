using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.Infrastructure.PowerShell
{
    public class PtyProcessWrapper : IDisposable, IAsyncDisposable
    {
        private readonly ILogger<PtyProcessWrapper> _logger;
        private readonly string _fileName;
        private readonly string _arguments;
        private Process? _process;
        private int _isDisposed;
        private readonly object _syncRoot = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _outputReadTask;
        private Task? _errorReadTask;

        public event EventHandler? ProcessExited;
        public event EventHandler<string>? OutputDataReceived;
        public event EventHandler<string>? ErrorDataReceived;

        public StreamWriter StandardInput => _process?.StandardInput ?? throw new InvalidOperationException("Process is not running.");
        public StreamReader StandardOutput => _process?.StandardOutput ?? throw new InvalidOperationException("Process is not running.");
        public StreamReader StandardError => _process?.StandardError ?? throw new InvalidOperationException("Process is not running.");

        public async Task WriteInputAsync(string input, CancellationToken cancellationToken = default)
        {
            if (_process == null || _process.HasExited) throw new InvalidOperationException("Process is not running.");
            try
            {
                await _process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IOException while writing to process standard input (pipe may be broken).");
            }
        }

        public async Task WriteControlCharacterAsync(char controlChar, CancellationToken cancellationToken = default)
        {
            if (_process == null || _process.HasExited) throw new InvalidOperationException("Process is not running.");
            try
            {
                await _process.StandardInput.WriteAsync(controlChar.ToString().AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IOException while writing control character to process standard input.");
            }
        }

        public PtyProcessWrapper(ILogger<PtyProcessWrapper> logger, string fileName = "powershell.exe", string arguments = "")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileName = string.IsNullOrWhiteSpace(fileName) ? "powershell.exe" : fileName;
            _arguments = arguments ?? "";
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) == 1) throw new ObjectDisposedException(nameof(PtyProcessWrapper));
            lock (_syncRoot)
            {
                if (_process != null) throw new InvalidOperationException("Process is already running.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _fileName,
                Arguments = _arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            _process.Exited += OnProcessExited;

            try
            {
                if (!_process.Start())
                {
                    throw new InvalidOperationException("Failed to start powershell.exe process.");
                }
                
                _cancellationTokenSource = new CancellationTokenSource();
                _outputReadTask = Task.Run(() => ReadStreamAsync(_process.StandardOutput, data => OutputDataReceived?.Invoke(this, data), _cancellationTokenSource.Token));
                _errorReadTask = Task.Run(() => ReadStreamAsync(_process.StandardError, data => ErrorDataReceived?.Invoke(this, data), _cancellationTokenSource.Token));

                _logger.LogInformation("Started {FileName} with PID: {ProcessId}", _fileName, _process.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while starting {FileName}.", _fileName);
                CleanupProcessAsync().GetAwaiter().GetResult();
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) == 1) throw new ObjectDisposedException(nameof(PtyProcessWrapper));

            Process? localProcess;
            lock (_syncRoot)
            {
                localProcess = _process;
            }

            if (localProcess == null) return;

            try
            {
                _cancellationTokenSource?.Cancel();

                if (!localProcess.HasExited)
                {
                    try
                    {
                        localProcess.Kill(true);
                    }
                    catch (InvalidOperationException) { /* Process already exited */ }
                    catch (System.ComponentModel.Win32Exception) { /* Process already exited */ }
                    
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await localProcess.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Process did not exit within 3 seconds after Kill.");
                    }
                }
                _logger.LogInformation("Stopped {FileName} process.", _fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping {FileName} process.", _fileName);
            }
            finally
            {
                await CleanupProcessAsync();
            }
        }

        public void Stop()
        {
            // Fire-and-forget to avoid Sync-over-Async blocking (P1 fix)
            _ = Task.Run(async () => 
            {
                try { await StopAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error during fire-and-forget StopAsync."); }
            });
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            Process? localProcess;
            lock (_syncRoot)
            {
                localProcess = _process;
            }

            if (localProcess != null)
            {
                try 
                {
                    var exitCode = localProcess.ExitCode;
                    if (exitCode != 0)
                    {
                        _logger.LogWarning("{FileName} process exited abnormally with code {ExitCode}.", _fileName, exitCode);
                    }
                    else
                    {
                        _logger.LogInformation("{FileName} process exited normally.", _fileName);
                    }
                }
                catch (Exception ex)
                {
                     _logger.LogWarning(ex, "Could not retrieve exit code for {FileName}.", _fileName);
                }
            }
            ProcessExited?.Invoke(this, EventArgs.Empty);

            // Clean up resources asynchronously when the process exits by itself
            _ = CleanupProcessAsync();
        }

        private async Task CleanupProcessAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            Process? localProcess;
            lock (_syncRoot)
            {
                localProcess = _process;
                _process = null;
            }

            if (localProcess != null)
            {
                localProcess.Exited -= OnProcessExited;
                try { localProcess.Dispose(); } catch { /* ignore */ }
            }

            if (_outputReadTask != null || _errorReadTask != null)
            {
                try
                {
                    var tasks = new[] { _outputReadTask, _errorReadTask }.Where(t => t != null).ToArray();
                    if (tasks.Length > 0)
                    {
                        await Task.WhenAny(Task.WhenAll(tasks!), Task.Delay(1000));
                    }
                }
                catch (Exception) { /* ignore */ }
                
                _outputReadTask = null;
                _errorReadTask = null;
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task ReadStreamAsync(StreamReader reader, Action<string> onDataReceived, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var readCount = await reader.ReadAsync(buffer, cancellationToken);
                    if (readCount == 0)
                        break;

                    var data = new string(buffer, 0, readCount);
                    onDataReceived(data);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading from process stream.");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
            
            if (disposing)
            {
                try
                {
                    Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception ignored during dispose.");
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
            
            try
            {
                await StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception ignored during DisposeAsync.");
            }
            
            GC.SuppressFinalize(this);
        }
    }
}
