// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

// Allows the test project to exercise the internal probing seams (per-workspace probe results,
// retry behavior) directly instead of only through the public timer-triggered entry point.
[assembly: InternalsVisibleTo("Microsoft.DncEng.GrafanaWatchdog.Tests")]
