using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.Infrastructure.PowerShell
{
    public class PtyProcessWrapper : IDisposable
    {
        private readonly ILogger<PtyProcessWrapper> _logger;
        private Process? _process;
        private bool _disposed;

        public event EventHandler? ProcessExited;

        public StreamWriter StandardInput => _process?.StandardInput ?? throw new InvalidOperationException("Process is not running.");
        public StreamReader StandardOutput => _process?.StandardOutput ?? throw new InvalidOperationException("Process is not running.");
        public StreamReader StandardError => _process?.StandardError ?? throw new InvalidOperationException("Process is not running.");

        public PtyProcessWrapper(ILogger<PtyProcessWrapper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PtyProcessWrapper));
            if (_process != null) throw new InvalidOperationException("Process is already running.");

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
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
                
                _logger.LogInformation("Started powershell.exe with PID: {ProcessId}", _process.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while starting powershell.exe.");
                CleanupProcess();
                throw;
            }
        }

        public void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PtyProcessWrapper));

            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000); // Wait up to 3 seconds for exit
                }
                _logger.LogInformation("Stopped powershell.exe process.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping powershell.exe process.");
            }
            finally
            {
                CleanupProcess();
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_process != null)
            {
                try 
                {
                    var exitCode = _process.ExitCode;
                    if (exitCode != 0)
                    {
                        _logger.LogWarning("powershell.exe process exited abnormally with code {ExitCode}.", exitCode);
                    }
                    else
                    {
                        _logger.LogInformation("powershell.exe process exited normally.");
                    }
                }
                catch (Exception ex)
                {
                     _logger.LogWarning(ex, "Could not retrieve exit code for powershell.exe.");
                }
            }
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }

        private void CleanupProcess()
        {
            if (_process != null)
            {
                _process.Exited -= OnProcessExited;
                _process.Dispose();
                _process = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
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
            
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
