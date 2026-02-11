// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Discussion thoroughness level — detected automatically by the Head agent
/// from the user's prompt, or overridden manually in settings.
/// Controls turn count, convergence threshold, and commentary verbosity.
/// </summary>
public enum DiscussionDepth
{
    /// <summary>Automatic: Head agent detects depth from the user's prompt (default).</summary>
    Auto,

    /// <summary>Fast exploration: 10 turns, threshold 60, brief commentary.</summary>
    Quick,

    /// <summary>Balanced discussion: uses configured settings (default 30 turns, threshold 80).</summary>
    Standard,

    /// <summary>Deep analysis: 50 turns, threshold 90, detailed commentary.</summary>
    Deep
}
