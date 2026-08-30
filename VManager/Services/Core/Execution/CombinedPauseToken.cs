// Services/Core/Execution/CombinedPauseToken.cs
using System;
using System.Linq;

namespace VManager.Services.Core.Execution;

/// <summary>
/// Combina varios PauseToken en uno solo (lógica OR: pausado si CUALQUIERA
/// de los tokens de origen está pausado). Debe disposearse para desuscribirse
/// de los tokens de origen y evitar leaks, especialmente si alguno de esos
/// tokens de origen vive más tiempo que esta instancia (ej. un token global).
/// </summary>
public sealed class CombinedPauseToken : IDisposable
{
    private readonly PauseTokenSource _linkedSource = new();
    private readonly PauseToken[] _sourceTokens;
    private readonly Action<bool> _handler;
    private bool _disposed;

    public PauseToken Token => _linkedSource.Token;

    public CombinedPauseToken(params PauseToken[] sourceTokens)
    {
        _sourceTokens = sourceTokens;
        _handler = _ => Recompute();

        foreach (var token in _sourceTokens)
            token.PauseChanged += _handler;

        Recompute(); // sincronizar estado inicial
    }

    private void Recompute()
    {
        if (_sourceTokens.Any(t => t.IsPaused))
            _linkedSource.Pause();
        else
            _linkedSource.Resume();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var token in _sourceTokens)
            token.PauseChanged -= _handler;
    }
}