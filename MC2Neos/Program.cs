using System;
using System.IO;
using System.Text;
using Substrate;
using BaseX;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FrooxEngine;
using CommandLine;

namespace MC2Neos
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (!Parser.TryParse<Options>(args, out var options))
                return;

            UniLog.OnLog += msg => { }; // just ignore regular messages for now
            UniLog.OnError += msg => Console.WriteLine(msg);

            await StandaloneFrooxEngineRunner.RunEngineTask(async runner =>
            {
                bool loggedIn;

                if (string.IsNullOrWhiteSpace(options.Login) || string.IsNullOrWhiteSpace(options.Password))
                    loggedIn = await runner.InteractiveLogin();
                else
                    loggedIn = await runner.Login(options.Login, options.Password, options.TwoFactorToken);

                if (!loggedIn)
                {
                    Console.WriteLine("Failed to login, aborting");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(options.Directory))
                {
                    // check whether the directory contains a single minecraft level or multiple
                    if (File.Exists(Path.Combine(options.Directory, "level.dat")))
                        await Import(runner, options.Directory);
                    else
                    {
                        foreach (var subdir in Directory.EnumerateDirectories(options.Directory))
                        {
                            if (File.Exists(Path.Combine(subdir, "level.dat")))
                                await Import(runner, subdir);
                        }
                    }
                }

                var recordManager = runner.Engine.RecordManager;

                while (recordManager.SyncingRecordsCount > 0)
                {
                    Console.WriteLine($"Syncing {recordManager.SyncingRecordsCount} Records -> {recordManager.CurrentUploadTask.Progress * 100:F2} %");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                while (recordManager.UploadingVariantsCount > 0)
                {
                    Console.WriteLine($"Uploading {recordManager.UploadingVariantsCount} Variants");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            });
        }

        static async Task Import(StandaloneFrooxEngineRunner runner, string path)
        {
            var name = Path.GetFileName(path);

            Console.WriteLine($"Importing: {name}");

            await runner.CreateWorldOrUpdate(new List<string>()
            {
                "mc2neos",
                name
            },
            async w =>
            {
                // make sure the world has the same name as the Minecraft level
                w.Name = name;

                var indicator = new ConsoleProgressIndicator();

                await MinecraftNeos.MinecraftImporter.SyncMinecraftWorld(w, path, progress: indicator);
            });
        }
    }
}
