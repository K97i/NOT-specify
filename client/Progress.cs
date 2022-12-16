﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace specify_client;

public enum ProgressType
{
    Queued,
    Processing,
    Complete,
    Failed
}

public class ProgressStatus
{
    public string Name { get; }
    public ProgressType Status { get; set; }
    public Action Action { get; }
    public List<string> Dependencies { get; }
    public bool SkipProgressWait { get; }

    public SolidColorBrush StatusColor => Status switch
    {
        ProgressType.Queued => (SolidColorBrush)new BrushConverter().ConvertFrom("#FFFFFF"),
        ProgressType.Processing => (SolidColorBrush)new BrushConverter().ConvertFrom("#FF7800"),
        ProgressType.Complete => (SolidColorBrush)new BrushConverter().ConvertFrom("#00ff00"),
        ProgressType.Failed => (SolidColorBrush)new BrushConverter().ConvertFrom("#FF0000"),
        _ => throw new Exception("Bad progress status!")
    };

    public ProgressStatus(string name, Action a, List<string> deps = null, bool skipProgressWait = false)
    {
        Name = name;
        Status = ProgressType.Queued;
        Action = a;
    }
}

/**
 * Things for progress, will be called by the GUI
 */
public class ProgressList
{
    public Dictionary<string, ProgressStatus> Items { get; set; }

    public ProgressList()
    {
        Items = new Dictionary<string, ProgressStatus>(){
        { "MainData", new ProgressStatus("Main Data", data.Cache.MakeMainData) },
        { "SystemData", new ProgressStatus("System Data", data.Cache.MakeSystemData) },
        // { "DummyTimer", new ProgressStatus("Dummy 5 second timer", data.Cache.DummyTimer) },
        { "Security", new ProgressStatus("Security Info", data.Cache.MakeSecurityData) },
        { "Network", new ProgressStatus("Network Info", data.Cache.MakeNetworkData) },
        { "Hardware", new ProgressStatus("Hardware Info", data.Cache.MakeHardwareData) },
        {
            "Assemble",
            new ProgressStatus("Monolith ... Assemble", MonolithCache.AssembleCache,
                new List<string>{"MainData", "SystemData", "Security", "Network", "Hardware"})
        },
        {
            "WriteFile",
            new ProgressStatus("Write the file", Monolith.WriteFile, new List<string>(){ "Assemble" })
        }
    };
    }

    public void RunItem(string key)
    {
        var item = Items.ContainsKey(key) ? Items[key] : throw new Exception($"Progress item {key} doesn't exist!");

        new Thread(() =>
        {
            foreach (var k in item.Dependencies)
            {
                var dep = Items.ContainsKey(k) ? Items[k] : throw new Exception($"Dependency {k} of {key} does not exist!");
                while (dep.Status != ProgressType.Complete)
                {
                    Thread.Sleep(0);
                }
            }

            item.Status = ProgressType.Processing;
            item.Action();
            item.Status = ProgressType.Complete;
        }).Start();
    }

    public void PrintStatuses()
    {
        new Thread(() =>
        {
            var allComplete = true;
            var cPos = new List<int>();
            var oldStatus = new List<ProgressType>();

            for (var i = 0; i < Items.Count; i++)
            {
                var item = Items.ElementAt(i).Value;
                Console.Write(item.Name + " - " + item.Status);
                cPos.Add(Console.CursorTop);
                oldStatus.Add(item.Status);
                Console.WriteLine();
            }

            do
            {
                allComplete = true;

                for (var i = 0; i < Items.Count; i++)
                {
                    var item = Items.ElementAt(i).Value;
                    if (!item.SkipProgressWait && item.Status != ProgressType.Complete)
                    {
                        allComplete = false;
                    }

                    if (item.Status == oldStatus[i]) continue;

                    Console.SetCursorPosition(0, cPos[i]);
                    Console.WriteLine((item.Name + " - " + item.Status).PadRight(Console.BufferWidth));
                    oldStatus[i] = item.Status;
                }

                Console.SetCursorPosition(0, cPos.Last() + 1);
                Thread.Sleep(100);
            } while (!allComplete);

            Console.SetCursorPosition(0, cPos.Last() + 1);
        }).Start();
    }

    /**
     * From https://stackoverflow.com/a/8946847 and a comment
     */
    public static void ClearCurrentConsoleLine()
    {
        var currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.BufferWidth));
        Console.SetCursorPosition(0, currentLineCursor);
    }
}