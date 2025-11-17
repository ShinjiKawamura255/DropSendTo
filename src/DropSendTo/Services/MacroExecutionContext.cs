using System;
using System.Collections.Generic;
using DropSendTo.Models;

namespace DropSendTo.Services;

public sealed class MacroExecutionContext
{
    public MacroExecutionContext(
        SlotExecutionMode slotMode,
        Func<string?, LaunchResult>? commandInvoker,
        string slotTitle,
        string? commandPath,
        IReadOnlyList<string>? droppedPaths = null)
    {
        SlotMode = slotMode;
        CommandInvoker = commandInvoker;
        SlotTitle = slotTitle;
        CommandPath = commandPath ?? string.Empty;
        DroppedPaths = droppedPaths ?? Array.Empty<string>();
    }

    public SlotExecutionMode SlotMode { get; }
    public Func<string?, LaunchResult>? CommandInvoker { get; }
    public string SlotTitle { get; }
    public string CommandPath { get; }
    public IReadOnlyList<string> DroppedPaths { get; }
}
