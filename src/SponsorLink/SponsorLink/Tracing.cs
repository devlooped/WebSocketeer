﻿// <autogenerated />
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Devlooped.Sponsors;

static class Tracing
{
    public static void Trace([CallerMemberName] string? message = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        var trace = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPONSORLINK_TRACE"));
#if DEBUG
        trace = true;
#endif

        if (!trace)
            return;

        var line = new StringBuilder()
            .Append($"[{DateTime.Now:O}]")
            .Append($"[{Process.GetCurrentProcess().ProcessName}:{Process.GetCurrentProcess().Id}]")
            .Append($" {message} ")
            .AppendLine($" -> {filePath}({lineNumber})")
            .ToString();

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sponsorlink");
        Directory.CreateDirectory(dir);

        var tries = 0;
        // Best-effort only
        while (tries < 10)
        {
            try
            {
                File.AppendAllText(Path.Combine(dir, "trace.log"), line);
                Debugger.Log(0, "SponsorLink", line);
                return;
            }
            catch (IOException) 
            { 
                tries++; 
            }
        }
    }
}
