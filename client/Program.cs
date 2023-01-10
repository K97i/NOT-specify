﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace specify_client;

public class Program
{
    public const string SpecifyVersion = "v1.0.0";
    public static Stopwatch Time;
    
    static async Task Main()
    {
        try
        {
            data.Cache.Issues = new List<string>();
            var initialConsoleFg = Console.ForegroundColor;
            var initialConsoleBg = Console.BackgroundColor;
            Console.WriteLine($"Specify {SpecifyVersion}");
            Console.WriteLine("This tool gathers information about your computer.  It does not collect any sensitive information.");

            // TODO: make settings better
            while (true)
            {
                var settingsLine1 = Console.CursorTop;
                Console.Write("[Enter] - continue, [q] - quit ");
                if (Settings.RedactUsername)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                Console.Write("[1] - Toggle Redact Username");
                Console.ForegroundColor = initialConsoleFg;
                Console.BackgroundColor = initialConsoleBg;
                Console.Write(" ");
                if (Settings.RedactOneDriveCommercial)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                Console.Write("[2] - Toggle Redact Commercial OneDrive");
                // If the next message is used, the output is repeated every time the user presses a key
                /*Console.Write("[2] - Toggle Redact Commercial OneDrive name");*/
                Console.ForegroundColor = initialConsoleFg;
                Console.BackgroundColor = initialConsoleBg;
                Console.WriteLine();
                if (Settings.DontUpload)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                Console.Write("[3] - Don't Upload");
                Console.ForegroundColor = initialConsoleFg;
                Console.BackgroundColor = initialConsoleBg;
                Console.Write(" ");
                if (Settings.EnableDebug)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }
                Console.Write("[4] - Enable Debug Logging");
                Console.ForegroundColor = initialConsoleFg;
                Console.BackgroundColor = initialConsoleBg;

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine("\nGoodbye!");
                    return;
                }
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key is ConsoleKey.D1 or ConsoleKey.NumPad1)
                    Settings.RedactUsername = !Settings.RedactUsername;
                if (key.Key is ConsoleKey.D2 or ConsoleKey.NumPad2)
                    Settings.RedactOneDriveCommercial = !Settings.RedactOneDriveCommercial;
                if (key.Key is ConsoleKey.D3 or ConsoleKey.NumPad3)
                    Settings.DontUpload = !Settings.DontUpload;
                if(key.Key is ConsoleKey.D4 or ConsoleKey.NumPad4)
                    Settings.EnableDebug = !Settings.EnableDebug;

                Console.SetCursorPosition(0, settingsLine1);
            }

            Console.WriteLine("\n");

            Time = new Stopwatch();
            Time.Start();
            await DebugLog.StartDebugLog();
            var pList = new ProgressList();
            pList.PrintStatuses();
            pList.RunItem("MainData");
            pList.RunItem("SystemData");
            pList.RunItem("Security");
            pList.RunItem("Network");
            pList.RunItem("Hardware");
            pList.RunItem(ProgressList.Specificializing);
        }
        catch (Exception ex)
        {
            Settings.EnableDebug = true;
            await DebugLog.StartDebugLog();
            await DebugLog.LogEventAsync("FATAL EXCEPTION", DebugLog.Region.Misc, DebugLog.EventType.ERROR);
            await DebugLog.LogEventAsync(ex.ToString());
            Console.WriteLine($"A fatal exception has occurred. Please see {DebugLog.LogFilePath} for details.");
            Console.WriteLine("Press any key to close this window.");
            Console.ReadKey(true);
        }
    }
    
        
    public static void PrettyPrintObject(object o)
    {
        var jsonString = JsonConvert.SerializeObject(o, Formatting.Indented);

        Console.WriteLine(jsonString);
    }

    public static void PrettyPrintWmiResults(List<Dictionary<string, object>> wmi)
    {
        foreach (var instance in wmi)
        {
            foreach (var pair in instance)
            {
                Console.WriteLine("{0} = {1}", pair.Key, pair.Value);
            }

            if (!instance.Equals(wmi.Last()))
            {
                Console.WriteLine("---------------");
            }
        }
    }
}