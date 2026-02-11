// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// PanelSettings lives in CopilotAgent.Core.Models (following the same pattern as
// MultiAgentSettings and OfficeSettings) so that AppSettings can reference it
// without a circular dependency. This file provides a convenient type alias
// for use within the Panel project.

global using PanelSettings = CopilotAgent.Core.Models.PanelSettings;