using System;
using DropSendTo.Models;

namespace DropSendTo.Services;

public sealed class MacroExecutionContext
{
    public MacroExecutionContext(SlotExecutionMode slotMode, Func<string?, LaunchResult>? commandInvoker, string slotTitle, string? commandPath)
    {
        SlotMode = slotMode;
        CommandInvoker = commandInvoker;
        SlotTitle = slotTitle;
        CommandPath = commandPath ?? string.Empty;
    }

    public SlotExecutionMode SlotMode { get; }
    public Func<string?, LaunchResult>? CommandInvoker { get; }
    public string SlotTitle { get; }
    public string CommandPath { get; }
}
