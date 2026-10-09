using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace KeepInMind;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<KeptItem> Items { get; set; } = [];

    public List<CustomGroup> Groups { get; set; } = [];

    public List<string> GroupOrder { get; set; } = [];

    public bool GroupBySource { get; set; } = true;

    public SortOrder Sort { get; set; } = SortOrder.Manual;

    public CompletedMode Completed { get; set; } = CompletedMode.InPlace;

    public Vector4 StorageColor { get; set; } = Sources.DefaultStorageColor;

    public Vector4 StorageEnoughColor { get; set; } = Sources.DefaultStorageEnoughColor;

    public Dictionary<Source, Vector4> SourceColors { get; set; } = new(Sources.DefaultColors);
}

public sealed class KeptItem
{
    public uint ItemId { get; set; }

    public int Needed { get; set; } = 1;

    public bool Done { get; set; }

    public Vector4? Color { get; set; }

    public Guid? GroupId { get; set; }
}

public sealed class CustomGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f);
}

public enum SortOrder
{
    Manual,
    Name,
    Remaining,
}

public enum CompletedMode
{
    InPlace,
    Section,
    Hidden,
}
