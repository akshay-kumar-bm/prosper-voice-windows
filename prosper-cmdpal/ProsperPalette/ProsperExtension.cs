using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace ProsperPalette;

// Must match the com:Class Id and CreateInstance ClassId in Package.appxmanifest.
[Guid("138DF00F-BC92-457E-B75A-774F7C38D6BC")]
public sealed partial class ProsperExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly ProsperCommandsProvider _provider = new();

    public ProsperExtension(ManualResetEvent disposed) => _disposed = disposed;

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        _provider.Dispose();
        Prosper.Engine.Shutdown();
        _disposed.Set();
    }
}
