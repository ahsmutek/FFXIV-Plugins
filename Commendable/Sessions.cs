using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using ContentFinderCondition = Lumina.Excel.Sheets.ContentFinderCondition;
using TerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace Commendable;

internal sealed class Sessions : IDisposable
{
    public const string ChatTag = "Commendable";
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(5);
    // A plugin reload or reconnect picks its duty back up; an older unfinished file is a run that crashed.
    private static readonly TimeSpan ResumeWithin = TimeSpan.FromMinutes(15);

    private readonly IClientState clientState;
    private readonly IDutyState dutyState;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly IDataManager data;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Journal journal;

    private Interpreter? interpreter;
    private uint? entered;
    private DateTime flushAt;

    public Sessions(IClientState clientState, IDutyState dutyState, IFramework framework, IPlayerState playerState,
                    IDataManager data, IChatGui chat, IPluginLog log, Configuration config, string journalFolder)
    {
        this.clientState = clientState;
        this.dutyState = dutyState;
        this.framework = framework;
        this.playerState = playerState;
        this.data = data;
        this.chat = chat;
        this.log = log;
        this.config = config;
        journal = new Journal(journalFolder);
        entered = clientState.TerritoryType;
        clientState.TerritoryChanged += OnTerritoryChanged;
        dutyState.DutyCompleted += OnDutyCompleted;
        framework.Update += OnUpdate;
    }

    public event Action<DutySession>? Completed;

    public event Action<DutySession>? Left;

    public DutySession? Current => interpreter?.Session;

    public DutySession? Last { get; private set; }

    public bool Recording => Current is { IsCompleted: false };

    public uint SelfId => interpreter?.SelfId ?? 0;

    public string JournalFolder => journal.Folder;

    public long Ms => Current is { } session ? (long)(DateTime.UtcNow - session.StartedAt).TotalMilliseconds : 0;

    public bool Knows(uint entityId) => interpreter?.Knows(entityId) ?? false;

    public bool KnowsPet(uint entityId) => interpreter?.KnowsPet(entityId) ?? false;

    public void Observe(Observation observation)
    {
        if (interpreter == null)
            return;
        journal.Append(observation);
        interpreter.Apply(observation);
    }

    public void Flush() => Save(journal.Flush);

    // Leaves the duty's file open-ended, so a reload in the duty carries on with it.
    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        dutyState.DutyCompleted -= OnDutyCompleted;
        framework.Update -= OnUpdate;
        Save(journal.Stop);
    }

    private void OnTerritoryChanged(uint territory) => entered = territory;

    private void OnUpdate(IFramework _)
    {
        // A session starts with who you are, which isn't known yet while logging in.
        if (entered is { } territory && playerState.IsLoaded)
        {
            entered = null;
            Enter(territory);
        }

        if (DateTime.UtcNow < flushAt)
            return;
        flushAt = DateTime.UtcNow + FlushEvery;
        Flush();
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (Current is not { IsCompleted: false } session)
            return;
        Observe(new DutyComplete(Ms));
        Flush();
        Completed?.Invoke(session);
    }

    private void Enter(uint territory)
    {
        // Logging out isn't leaving the duty; a reconnect lands back in it.
        if (territory == 0)
            return;

        var dutyId = DutyIdOf(territory);
        if (Current?.DutyId == dutyId)
            return;

        End();
        if (dutyId != 0 && !Resume(dutyId))
            Start(territory, dutyId);
    }

    private void Start(uint territory, uint dutyId)
    {
        var start = new DutyStart(0, Journal.Format, DateTime.UtcNow, territory, dutyId, DutyName(dutyId),
                                  playerState.EntityId, playerState.CharacterName, playerState.HomeWorld.RowId,
                                  playerState.ClassJob.RowId);
        Begin(new Interpreter());
        Save(() => journal.Start(start, config.SavedDuties));
        Observe(start);
    }

    private bool Resume(uint dutyId)
    {
        if (config.SavedDuties <= 0 || Journal.Saved(journal.Folder).FirstOrDefault() is not { } latest)
            return false;

        List<Observation> saved;
        try
        {
            if (File.ReadLines(latest).FirstOrDefault() is not { } line ||
                ObservationJson.Read(line) is not DutyStart first || first.DutyId != dutyId)
                return false;
            saved = Journal.Read(latest).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning(e, "Couldn't read the last duty's saved data.");
            return false;
        }

        if (saved is not [DutyStart start, .., var last] || start.DutyId != dutyId || saved.Any(o => o is DutyEnd) ||
            DateTime.UtcNow - start.StartedAt.AddMilliseconds(last.Ms) > ResumeWithin)
            return false;

        var resumed = new Interpreter();
        foreach (var observation in saved)
            resumed.Apply(observation);
        Begin(resumed);
        journal.Continue(latest);
        Observe(new Resumed(Ms));
        log.Information($"Carrying on with {start.DutyName} from its saved data.");
        return true;
    }

    // Subscribed after any replay, so a resumed duty doesn't print its old events to chat again.
    private void Begin(Interpreter next)
    {
        interpreter = next;
        interpreter.Recorded += Notify;
    }

    private void End()
    {
        if (interpreter?.Session is not { } session)
            return;

        Observe(new DutyEnd(Ms));
        Save(journal.Stop);
        interpreter.Recorded -= Notify;
        interpreter = null;
        if (!config.KeepLastDuty)
            Last = null;
        else if (session.IsCompleted)
            Last = session;
        Left?.Invoke(session);
    }

    private void Notify(PlayerRecord player, Interaction interaction)
    {
        if (config.ChatNotifications && !config.DisabledCategories.Contains(interaction.Category) &&
            Lines.Chat(player, interaction) is { } text)
            chat.Print(text, ChatTag);
    }

    private void Save(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning(e, "Couldn't save the duty's data.");
        }
    }

    private uint DutyIdOf(uint territory) =>
        data.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var row) ? row.ContentFinderCondition.RowId : 0;

    private string DutyName(uint dutyId)
    {
        var name = data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(dutyId)?.Name.ExtractText() ?? "";
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : $"Duty {dutyId}";
    }
}
