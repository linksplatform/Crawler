﻿using System;
using System.Diagnostics;

namespace Platform.Helpers.Disposal
{
    /// <summary>
    /// Представляет базовый класс реализующий основную логику необходимую для повышения вероятности корректного высвобождения памяти.
    /// </summary>
    /// <remarks>
    /// TODO: Попробовать реализовать компилируемый автоматический вариант DisposeCore (находить все типы IDisposable, IDisposal и автоматически вычищать их).
    /// </remarks>
    public abstract class DisposalBase : IDisposal
    {
        private readonly Process _currentProcess;
        private readonly AppDomain _currentDomain;
        private bool _disposed;

        public bool Disposed { get { return _disposed; } }

        protected DisposalBase()
        {
            _disposed = false;
            (_currentProcess = Process.GetCurrentProcess()).Exited += OnProcessExit;
            (_currentDomain = AppDomain.CurrentDomain).ProcessExit += OnProcessExit;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Destruct()
        {
            Dispose(false);
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            Dispose(false);
        }

        ~DisposalBase()
        {
            Destruct();
        }

        private void Dispose(bool manual)
        {
            if (!_disposed) lock (this) if (!_disposed)
                    {
                        try
                        {
                            DisposeCore(manual);
                        }
                        catch
                        {
                            if (manual) throw;
                            // else TODO: Log exception
                        }
                        finally
                        {
                            _currentProcess.Exited -= OnProcessExit;
                            _currentDomain.ProcessExit -= OnProcessExit;
                        }

                        _disposed = true;
                    }
        }

        protected abstract void DisposeCore(bool manual);

        protected void EnsureNotDisposed(string objectName)
        {
            if (_disposed) throw new ObjectDisposedException(objectName);
        }

        protected virtual void EnsureNotDisposed()
        {
            EnsureNotDisposed(GetType().Name);
        }
    }
}