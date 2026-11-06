using System;

namespace Sundries;

internal interface IModule : IDisposable
{
    string Name { get; }

    string[] Description { get; }

    bool Enabled { get; set; }

    void DrawSettings() { }

    void Draw() { }

    void IDisposable.Dispose() { }
}
